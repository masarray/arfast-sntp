using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ARFastSNTP;

/// <summary>
/// One owner of Windows clock discipline, with a bounded multi-peer probing
/// loop. Mutations are applied on the next generation; no task-per-peer
/// lifetime, no unbounded queue and no second worker/process.
/// </summary>
internal sealed class MultiPeerWorker : BackgroundService
{
    private sealed class PeerRuntime
    {
        internal SntpSample? Sample;
        internal long SeenAt;
        internal long NextAt;
        internal int Streak;
        internal int Failures;
        internal string? Error;
        internal string? PendingEvent;
        internal string State = "Waiting";
        internal bool LastProbeInRound;
        internal bool WasEnabled;
        internal string? KissCode;
        internal int RateStrikes;
    }

    private readonly ClockEpochGuard _clockEpoch = new();
    private readonly SyncOptions _options;
    private readonly PeerCatalog _catalog;
    private readonly ILogger<MultiPeerWorker> _logger;
    private readonly Dictionary<string, PeerRuntime> _runtime = new(StringComparer.Ordinal);
    private readonly Queue<string> _history = new();
    private readonly Queue<NtpDecisionTrace> _diagnostics = new();
    private string? _lastDecisionKey;
    private long _lastDecisionStamp;
    private readonly string _statusPath;
    private readonly string _diagnosticPath;
    private string? _selected;
    private long _requests, _accepted, _rejected, _lastStepStamp, _lastLogStamp;
    private DateTimeOffset? _lastCorrection;
    private bool _pendingVerification;
    private string? _previousState;
    private long _lastCatalogGeneration = -1;
    private long _lastNetworkRearmStamp;
    private long _networkRecoveryStamp;
    private long _lastAdapterSurveyStamp;

    internal MultiPeerWorker(SyncOptions options, PeerCatalog catalog,
        ILogger<MultiPeerWorker> logger)
    {
        _options = options;
        _catalog = catalog;
        _logger = logger;
        _statusPath = options.StatusPath;
        _diagnosticPath = Path.ChangeExtension(_statusPath, ".diagnostics.json");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Multi-peer SNTP worker initialized; correction={Allowed}",
            _options.AllowStep);
        using var network = new NetworkChangeSignal(_catalog.Wake);
        var adapterFingerprint = new NetworkAdapterFingerprint();
        long lastHandledNetworkVersion = network.Version;
        while (!stoppingToken.IsCancellationRequested)
        {
            // The clock may be changed by Windows Settings, W32Time, or another
            // process while we are sleeping. A monotonic epoch guard prevents
            // stale pre-change offsets from ever authorizing a clock step.
            bool clockChanged = _clockEpoch.Observe(
                DateTimeOffset.UtcNow, Stopwatch.GetTimestamp());
            var (peers, generation) = _catalog.Snapshot();

            // Network callbacks only wake this thread. Multiple Windows NIC,
            // gateway and DHCP notifications are debounced into one recovery.
            // There is no task-per-event, polling timer or second clock owner.
            if (network.Version != lastHandledNetworkVersion)
            {
                TimeSpan quiet = network.QuietFor;
                if (quiet < NetworkRecoveryPolicy.Debounce)
                {
                    try
                    {
                        await Task.Delay(NetworkRecoveryPolicy.Debounce - quiet,
                            stoppingToken);
                    }
                    catch (OperationCanceledException) { break; }
                }
                long newest = network.Version;
                if (newest != lastHandledNetworkVersion)
                {
                    lastHandledNetworkVersion = newest;
                    long nowStamp = Stopwatch.GetTimestamp();
                    // NetworkChange callbacks can be noisy (virtual NICs,
                    // interface metrics and transient Windows notifications).
                    // Do not erase perfectly valid time samples without a
                    // material observed IPv4 adapter/address/gateway change.
                    // The low-frequency fallback uses the SAME fingerprint.
                    bool realAdapterChange = peers.Any(p => p.Enabled) &&
                        adapterFingerprint.Check();
                    if (realAdapterChange &&
                        (_lastNetworkRearmStamp == 0 ||
                         Stopwatch.GetElapsedTime(_lastNetworkRearmStamp, nowStamp) >=
                             NetworkRecoveryPolicy.MinimumRearm))
                    {
                        _lastNetworkRearmStamp = nowStamp;
                        _networkRecoveryStamp = nowStamp;
                        DiscardSamples(preserveVerification: true);
                        NoteOnce("IPv4 adapter/address/gateway changed; rechecking NTP sources.");
                    }
                }
            }
            if (_lastCatalogGeneration != generation)
            {
                if (_lastCatalogGeneration != -1)
                    DiscardSamples(preserveVerification: true);
                _lastCatalogGeneration = generation;
            }
            if (clockChanged)
            {
                DiscardSamples();
                Note("Windows clock changed outside the NTP worker; "
                    + "discarded previous samples and rechecking.");
            }
            var current = peers.Select(p => p.Address).ToHashSet(StringComparer.Ordinal);
            foreach (string orphan in _runtime.Keys.Where(ip => !current.Contains(ip)).ToArray())
                _runtime.Remove(orphan);
            foreach (var peer in peers)
            {
                if (!_runtime.TryGetValue(peer.Address, out var rt))
                    _runtime[peer.Address] = rt = new PeerRuntime();
                if (!peer.Enabled)
                {
                    rt.WasEnabled = false;
                    rt.State = "Off";
                    rt.Sample = null;
                    rt.Error = null;
                    rt.Streak = 0;
                    // No network request or Windows clock write in OFF.
                    // Preserve server-originated RATE / DENY / RSTR.
                }
                else if (!rt.WasEnabled)
                {
                    rt.WasEnabled = true;
                    if (NetworkRecoveryPolicy.CanRearm(rt.KissCode))
                    {
                        rt.NextAt = 0;
                        rt.Failures = 0;
                        rt.State = "Waiting";
                        rt.Error = null;
                    }
                    else
                        rt.State = NetworkRecoveryPolicy.IsPermanentDenial(rt.KissCode)
                            ? "Denied" : "Rate limited";
                }
                rt.LastProbeInRound = false;
            }

            var enabled = peers.Where(p => p.Enabled).ToArray();

            // Only with active peers, sample local IPv4 adapter/gateway state
            // every 8s as an inexpensive fallback for missed native events.
            // Never gate NTP correctness on the general network-online flag.
            if (enabled.Length != 0 &&
                (_lastAdapterSurveyStamp == 0 ||
                 Stopwatch.GetElapsedTime(_lastAdapterSurveyStamp) >= TimeSpan.FromSeconds(8)))
            {
                _lastAdapterSurveyStamp = Stopwatch.GetTimestamp();
                if (adapterFingerprint.Check())
                {
                    network.ReportAdapterChanged();
                    // Route/gateway fingerprint changed; allow the normal
                    // debounced supervisor path to clear old samples first.
                    continue;
                }
            }
            if (enabled.Length == 0)
            {
                _selected = null;
                // An unverified old correction cannot be 'verified' later
                // after the operator deliberately disables every source.
                _pendingVerification = false;
                TraceDecision(peers, "NTP off", null, "All NTP sources disabled.", null);
                Publish(peers, "NTP off", null, "All NTP sources disabled.");
                // Keep the status fresh and the Studio connected while idle,
                // without making a network request or toggling Windows services.
                try { await _catalog.WaitAsync(PollPolicy.IdleHeartbeatInterval, stoppingToken); }
                catch (OperationCanceledException) { break; }
                continue;
            }

            // Limit outstanding UDP probes and avoid process/thread proliferation.
            using (var slots = new SemaphoreSlim(4))
            {
                var due = enabled.Where(p => IsDue(_runtime[p.Address])).ToArray();
                await Task.WhenAll(due.Select(async peer =>
                {
                    await slots.WaitAsync(stoppingToken).ConfigureAwait(false);
                    try { await ProbeAsync(peer.Address, generation, stoppingToken).ConfigureAwait(false); }
                    finally { slots.Release(); }
                })).ConfigureAwait(false);
            }
            // A route change mid-flight invalidates even successfully
            // returned UDP samples. Handle the coalesced notification next.
            if (network.Version != lastHandledNetworkVersion)
            {
                DiscardSamples(preserveVerification: true);
                Publish(peers, "Verifying", null,
                    "Network changed during NTP probing; awaiting fresh replies.");
                continue;
            }
            // An external Windows clock adjustment between individual NTP
            // queries must not mix offsets measured in different epochs.
            if (_clockEpoch.Observe(DateTimeOffset.UtcNow, Stopwatch.GetTimestamp()))
            {
                DiscardSamples();
                Note("Windows clock changed during NTP polling; "
                    + "discarded measurements and retrying.");
                TraceDecision(peers, "Verifying", null,
                    "Clock epoch changed; fresh NTP measurements required.", null);
                Publish(peers, "Verifying", null,
                    "Clock epoch changed; fresh NTP measurements required.");
                continue;
            }
            // Drain per-peer transitions on the single worker thread. Probes
            // must not enqueue UI events concurrently.
            foreach (var peer in peers)
            {
                var rt = _runtime[peer.Address];
                if (rt.PendingEvent is string message)
                {
                    Note(message);
                    rt.PendingEvent = null;
                }
            }

            if (_catalog.Snapshot().Generation != generation)
                continue; // configuration changed during probing: discard selection

            var eligible = enabled.Where(p =>
            {
                var rt = _runtime[p.Address];
                return rt.Sample.HasValue && rt.SeenAt > 0 &&
                    Stopwatch.GetElapsedTime(rt.SeenAt) <= TimeSpan.FromSeconds(12);
            }).Select(p =>
            {
                var rt = _runtime[p.Address];
                var sample = rt.Sample!.Value;
                return new PeerElection.Candidate(p.Address,
                    sample.Offset.TotalMilliseconds, sample.RttMs, sample.Stratum);
            }).ToArray();

            bool oneConfirmed = eligible.Length == 1 &&
                _runtime[eligible[0].Address].Streak >= 2;
            var decision = PeerElection.Choose(eligible, _selected, oneConfirmed);
            string? chosen = decision.Selected;
            // Network silence, Windows socket errors and NTP validation
            // failures are different evidence. Preserve the distinction.
            var failedStates = enabled.Select(p => _runtime[p.Address].State).ToArray();
            string overallState = PeerStatusClassifier.Pending(
                eligible.Length, decision, failedStates);
            string? evt = null;
            SntpSample? primary = chosen is not null ? _runtime[chosen].Sample : null;
            if (chosen is not null && primary.HasValue)
            {
                bool sync = PollPolicy.IsSynchronized(primary.Value.Offset);
                overallState = sync ? "Synchronized" : "Offset detected";
                if (sync && _pendingVerification)
                {
                    _pendingVerification = false;
                    Note("Previous clock correction verified by fresh NTP measurements.");
                }
                if (_selected != chosen)
                {
                    Note("Time source selected: " + chosen);
                    _selected = chosen;
                }
                if (_options.AllowStep && !sync &&
                    _runtime[chosen].LastProbeInRound &&
                    (_lastStepStamp == 0 ||
                        Stopwatch.GetElapsedTime(_lastStepStamp) >= PollPolicy.StepCooldown))
                {
                    // Recheck immediately before the privileged clock write.
                    // Reject a sample whose UTC epoch changed after election.
                    if (_clockEpoch.Observe(DateTimeOffset.UtcNow, Stopwatch.GetTimestamp()))
                    {
                        DiscardSamples();
                        Note("Windows clock changed before correction; "
                            + "unsafe pre-change sample discarded.");
                        TraceDecision(peers, "Verifying", null,
                            "External clock change before step; correction held.", null);
                        Publish(peers, "Verifying", null,
                            "Clock changed before correction; retrying.");
                        continue;
                    }
                    // Selection is bounded to fresh, coherent burst data,
                    // with a generation barrier held through SetSystemTime.
                    if (_catalog.TryStep(chosen, generation,
                        DateTimeOffset.UtcNow + primary.Value.Offset, out var result))
                    {
                        evt = result.Success
                            ? $"PC time corrected by {primary.Value.Offset.TotalMilliseconds:+0.0;-0.0;0.0} ms."
                            : $"Windows clock correction failed (Win32 {result.Error}).";
                        Note(evt);
                        overallState = result.Success ? "Correcting" : "Correction failed";
                        if (result.Success)
                        {
                            // Expected local adjustment; do not mistake our own
                            // clock step for a second *external* discontinuity.
                            _clockEpoch.Reset(DateTimeOffset.UtcNow,
                                Stopwatch.GetTimestamp());
                            _lastStepStamp = Stopwatch.GetTimestamp();
                            _lastCorrection = DateTimeOffset.UtcNow;
                            _pendingVerification = true;
                            // All pre-step offsets have a different epoch.
                            // Preserve verification until a NEW post-step
                            // response proves the corrected Windows time.
                            DiscardSamples(preserveVerification: true);
                        }
                    }
                }
            }
            else
            {
                // No current consensus: never carry a stale selection into
                // the status snapshot or use its old offset for correction.
                _selected = null;
                // An unverified successful local step remains pending across
                // temporary network loss, not falsely marked as verified.
            }
            if (chosen is null && eligible.Length > 1)
                NoteOnce("NTP sources disagree; Windows correction held.");

            TraceDecision(peers, overallState, chosen, decision.Reason, evt);
            Publish(peers, overallState, primary, evt ?? decision.Reason,
                decision.Degraded);
            // A completed round can consume the whole 2s timeout of other
            // unreachable servers. An already-due confirmation must NOT wait
            // another unconditional second, nor inherit the stable 3s poll.
            // RATE/DENY backoff is preserved in each peer's NextAt.
            TimeSpan earliest = enabled
                .Select(p => _runtime[p.Address])
                .Where(rt => !NetworkRecoveryPolicy.IsPermanentDenial(rt.KissCode))
                .Select(rt => rt.NextAt == 0 ? TimeSpan.Zero :
                    Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), rt.NextAt))
                .DefaultIfEmpty(PollPolicy.StableInterval)
                .Min();
            TimeSpan wait = PollPolicy.NextSupervisorWait(
                earliest, chosen is not null && primary.HasValue &&
                PollPolicy.IsSynchronized(primary.Value.Offset));
            try { await _catalog.WaitAsync(wait, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>
    /// Only the single supervisor calls this after outstanding probes finish.
    /// Clear the election, verification and all per-peer timestamps together.
    /// Reset due times so recovery probes are scheduled without waiting for a
    /// stable-poll interval, while respecting per-query bounded concurrency.
    /// </summary>
    private void DiscardSamples(bool preserveVerification = false)
    {
        _selected = null;
        if (!preserveVerification) _pendingVerification = false;
        foreach (var rt in _runtime.Values)
        {
            rt.Sample = null;
            rt.SeenAt = 0;
            // An address change, clock jump or server toggle cannot erase
            // the NTP server's RATE backoff or DENY/RSTR prohibition.
            if (NetworkRecoveryPolicy.CanRearm(rt.KissCode))
            {
                rt.NextAt = 0;
                rt.Failures = 0;
                rt.State = "Waiting";
                rt.Error = null;
            }
            else
                rt.State = NetworkRecoveryPolicy.IsPermanentDenial(rt.KissCode)
                    ? "Denied" : "Rate limited";
            rt.Streak = 0;
            rt.PendingEvent = null;
            rt.LastProbeInRound = false;
        }
    }

    private static bool IsDue(PeerRuntime peer) =>
        !NetworkRecoveryPolicy.IsPermanentDenial(peer.KissCode) &&
        (peer.NextAt == 0 || Stopwatch.GetElapsedTime(peer.NextAt) >= TimeSpan.Zero);
    // NextAt uses a monotonic expiry timestamp, not PC wall-clock time.

    private async Task ProbeAsync(string address, long generation, CancellationToken token)
    {
        // A peer may have waited for one of the four bounded probe slots
        // while the operator disabled it. Recheck after slot acquisition so
        // OFF does not initiate a new UDP transaction from the old generation.
        if (!_catalog.IsCurrent(address, generation)) return;
        var rt = _runtime[address];
        try
        {
            Interlocked.Increment(ref _requests);
            var server = IPAddress.Parse(address);
            var first = await NtpProtocol.QueryAsync(server, _options.ServerYear, token)
                .ConfigureAwait(false);
            SntpSample sample = first;
            if (rt.Streak == 1 && rt.Sample is SntpSample prior)
            {
                // A new independently originated Mode-4 reply checks the
                // previous coherent three-reply burst. Keep the full NTP
                // parser, source binding and offset-spread validation.
                // Repeating all three packets for confirmation increases
                // load without adding a separate confirmation epoch.
                if (!PollPolicy.CoherentConfirmation(prior, first))
                    throw new InvalidOperationException(
                        "NTP confirmation offset differs from initial burst.");
                Interlocked.Increment(ref _accepted);
            }
            else if (rt.Streak == 0 || first.Offset.Duration() > PollPolicy.StepThreshold)
            {
                await Task.Delay(100, token).ConfigureAwait(false);
                if (!_catalog.IsCurrent(address, generation)) return;
                Interlocked.Increment(ref _requests);
                var second = await NtpProtocol.QueryAsync(server, _options.ServerYear, token)
                    .ConfigureAwait(false);
                await Task.Delay(100, token).ConfigureAwait(false);
                if (!_catalog.IsCurrent(address, generation)) return;
                Interlocked.Increment(ref _requests);
                var third = await NtpProtocol.QueryAsync(server, _options.ServerYear, token)
                    .ConfigureAwait(false);
                sample = PollPolicy.MedianCoherent(first, second, third);
                Interlocked.Add(ref _accepted, 3);
            }
            else
                Interlocked.Increment(ref _accepted);

            if (!_catalog.IsCurrent(address, generation)) return;
            rt.Sample = sample;
            rt.SeenAt = Stopwatch.GetTimestamp();
            rt.Streak = Math.Min(rt.Streak + 1, 100);
            rt.NextAt = rt.SeenAt + (long)(Stopwatch.Frequency *
                PollPolicy.PeerInterval(rt.Streak).TotalSeconds);
            rt.LastProbeInRound = true;
            if (rt.State != "Online")
                rt.PendingEvent = address + ": NTP reply validated.";
            rt.State = "Online";
            rt.Error = null;
            rt.Failures = 0;
            rt.KissCode = null;
            rt.RateStrikes = 0;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is IOException or InvalidOperationException
            or OperationCanceledException or System.Net.Sockets.SocketException)
        {
            rt.Sample = null;
            rt.SeenAt = 0;
            rt.Streak = 0;
            rt.Failures = Math.Min(rt.Failures + 1, 5);
            string nextState = NtpFailureClassifier.State(ex);
            if (rt.State != nextState)
                rt.PendingEvent = address + ": " + NtpFailureClassifier.Event(ex);
            rt.State = nextState;
            // Detailed local ephemeral port is available on row hover only.
            // It is essential for matching a RAW raw packet capture frame to the socket.
            rt.Error = ex is OperationCanceledException
                ? "NTP request timed out. Check network route and UDP 123."
                : ex.Message;
            TimeSpan delay;
            if (ex is NtpKissOfDeathException kod)
            {
                rt.KissCode = kod.Code;
                if (NetworkRecoveryPolicy.IsPermanentDenial(kod.Code))
                    delay = TimeSpan.MaxValue; // Never query DENY/RSTR again.
                else if (kod.Code == "RATE")
                {
                    rt.RateStrikes = Math.Min(rt.RateStrikes + 1, 6);
                    delay = NetworkRecoveryPolicy.RateBackoff(rt.RateStrikes);
                }
                else
                {
                    rt.KissCode = null; // unknown KoD: discard and use backoff
                    delay = TimeSpan.FromSeconds(60);
                }
            }
            else
            {
                // The prior RATE interval has already elapsed if a transport
                // probe was allowed to run. Keep the escalation count, but
                // classify this new failure as transport, not a fresh RATE.
                if (rt.KissCode == "RATE") rt.KissCode = null;
                // Transport error only. A newly restored route temporarily
                // caps retry delay without bypassing a server-side KoD.
                bool recovering = _networkRecoveryStamp != 0 &&
                    Stopwatch.GetElapsedTime(_networkRecoveryStamp) <
                        NetworkRecoveryPolicy.RecoveryWindow;
                delay = NetworkRecoveryPolicy.TransportBackoff(rt.Failures, recovering);
            }
            rt.NextAt = delay == TimeSpan.MaxValue ? long.MaxValue :
                Stopwatch.GetTimestamp() +
                    (long)(Stopwatch.Frequency * delay.TotalSeconds);
            Interlocked.Increment(ref _rejected);
        }
    }

    private void Publish(NtpPeer[] peers, string state, SntpSample? selected,
        string? evt, bool degraded = false)
    {
        var rows = peers.Select(p =>
        {
            var rt = _runtime[p.Address];
            bool fresh = p.Enabled && rt.Sample is not null && rt.SeenAt > 0 &&
                Stopwatch.GetElapsedTime(rt.SeenAt) < TimeSpan.FromSeconds(12);
            var sample = fresh ? rt.Sample : null;
            string peerState = p.Enabled ? fresh ? "Online" : rt.State : "Off";
            if (fresh && _selected is string winner &&
                _runtime.TryGetValue(winner, out var chosen) && chosen.Sample.HasValue &&
                Math.Abs(sample!.Value.Offset.TotalMilliseconds -
                    chosen.Sample.Value.Offset.TotalMilliseconds) >
                    PeerElection.AgreementMilliseconds)
                peerState = "Outlier";
            if (fresh && _selected is null && state == "Source conflict")
                peerState = "Conflict";
            return new PeerReading(
                p.Address, p.Enabled, peerState,
                sample?.Offset.TotalMilliseconds, sample?.RttMs,
                sample?.Stratum, sample is null ? null : DateTimeOffset.UtcNow + sample.Value.Offset,
                rt.Error, p.Address == _selected, rt.Streak);
        }).ToArray();
        var winner = rows.FirstOrDefault(p => p.Selected);
        var snapshot = new StatusSnapshot(DateTimeOffset.UtcNow,
            winner?.Address ?? peers.FirstOrDefault()?.Address ?? "—",
            state, winner?.OffsetMilliseconds, winner?.RttMilliseconds,
            winner?.EstimatedServerUtc, evt)
        {
            Peers = rows, SelectedServer = winner?.Address, Degraded = degraded,
            Stratum = winner?.Stratum,
            RequestCount = Interlocked.Read(ref _requests),
            AcceptedCount = Interlocked.Read(ref _accepted),
            RejectedCount = Interlocked.Read(ref _rejected),
            SourceEnabled = peers.Any(p => p.Enabled),
            StepEnabled = _options.AllowStep,
            LastCorrectionUtc = _lastCorrection,
            RecentCorrections = _history.ToArray()
        };
        try
        {
            string? dir = Path.GetDirectoryName(_statusPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var temp = _statusPath + ".tmp";
            File.WriteAllText(temp, StatusSnapshot.Serialize(snapshot), new UTF8Encoding(false));
            File.Move(temp, _statusPath, true);
        }
        catch (Exception ex) { _logger.LogError(ex, "Could not publish NTP status."); }

        if (_previousState != state ||
            _lastLogStamp == 0 || Stopwatch.GetElapsedTime(_lastLogStamp) >
            TimeSpan.FromSeconds(30))
        {
            _previousState = state;
            _lastLogStamp = Stopwatch.GetTimestamp();
            _logger.LogInformation("NTP {State}; source={Source}; peers={Count}",
                state, winner?.Address ?? "none", peers.Length);
        }
    }

    /// <summary>
    /// Bounded forensic journal, separate from calm operator history. Capture
    /// transitions immediately; repeat unchanged states at most every 30 sec.
    /// All mutations run on the single supervisor after completed probes.
    /// </summary>
    private void TraceDecision(NtpPeer[] peers, string state, string? selected,
        string reason, string? stepEvent)
    {
        string key = state + "|" + selected + "|" + reason + "|" +
            string.Join(",", peers.Where(p => p.Enabled).Select(p =>
                p.Address + ":" + _runtime[p.Address].State));
        if (stepEvent is not null) key += "|" + stepEvent;
        long now = Stopwatch.GetTimestamp();
        if (key == _lastDecisionKey && _lastDecisionStamp != 0 &&
            Stopwatch.GetElapsedTime(_lastDecisionStamp) < TimeSpan.FromSeconds(30))
            return;
        _lastDecisionKey = key;
        _lastDecisionStamp = now;
        var samples = peers.Select(peer =>
        {
            var rt = _runtime[peer.Address];
            double? ageMs = rt.Sample.HasValue && rt.SeenAt > 0
                ? Math.Round(Stopwatch.GetElapsedTime(rt.SeenAt).TotalMilliseconds, 1)
                : null;
            return new NtpDecisionPeer(peer.Address, peer.Enabled, rt.State,
                rt.Sample?.Offset.TotalMilliseconds, rt.Sample?.RttMs,
                rt.Sample?.Stratum, ageMs, rt.Streak, rt.Failures,
                rt.Error is null ? null : rt.Error[..Math.Min(128, rt.Error.Length)]);
        }).ToArray();
        if (_diagnostics.Count == 16) _diagnostics.Dequeue();
        _diagnostics.Enqueue(new NtpDecisionTrace(DateTimeOffset.UtcNow,
            state, selected, reason, stepEvent, samples,
            Interlocked.Read(ref _requests), Interlocked.Read(ref _accepted),
            Interlocked.Read(ref _rejected)));
        // Unlike the small live status, this bounded journal is persisted
        // only on meaningful state changes or a 30s heartbeat.
        try
        {
            string? directory = Path.GetDirectoryName(_diagnosticPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temporary = _diagnosticPath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_diagnostics.ToArray()),
                new UTF8Encoding(false));
            File.Move(temporary, _diagnosticPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not publish bounded NTP diagnostic journal.");
        }
    }

    private void Note(string evt)
    {
        if (_history.Count == 16) _history.Dequeue();
        _history.Enqueue(DateTimeOffset.Now.ToString("O") + " " + evt);
    }

    private void NoteOnce(string evt)
    {
        if (_history.Count == 0 || !_history.Last().EndsWith(evt, StringComparison.Ordinal))
            Note(evt);
    }
}
