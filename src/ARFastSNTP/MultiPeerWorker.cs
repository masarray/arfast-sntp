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
        while (!stoppingToken.IsCancellationRequested)
        {
            // The clock may be changed by Windows Settings, W32Time, or another
            // process while we are sleeping. A monotonic epoch guard prevents
            // stale pre-change offsets from ever authorizing a clock step.
            bool clockChanged = _clockEpoch.Observe(
                DateTimeOffset.UtcNow, Stopwatch.GetTimestamp());
            var (peers, generation) = _catalog.Snapshot();
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
                    rt.State = "Off";
                    rt.Sample = null;
                    rt.Error = null;
                    rt.Streak = 0;
                }
                rt.LastProbeInRound = false;
            }

            var enabled = peers.Where(p => p.Enabled).ToArray();
            if (enabled.Length == 0)
            {
                _selected = null;
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
                            DiscardSamples();
                        }
                    }
                }
            }
            else
            {
                // No current consensus: never carry a stale selection into
                // the status snapshot or use its old offset for correction.
                _selected = null;
                _pendingVerification = false;
            }
            if (chosen is null && eligible.Length > 1)
                NoteOnce("NTP sources disagree; Windows correction held.");

            TraceDecision(peers, overallState, chosen, decision.Reason, evt);
            Publish(peers, overallState, primary, evt ?? decision.Reason,
                decision.Degraded);
            // Responsive control mutations, but polling cadence remains bounded.
            try
            {
                await _catalog.WaitAsync(
                    chosen is not null && primary.HasValue &&
                    PollPolicy.IsSynchronized(primary.Value.Offset)
                        ? PollPolicy.StableInterval : TimeSpan.FromSeconds(1),
                    stoppingToken);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>
    /// Only the single supervisor calls this after outstanding probes finish.
    /// Clear the election, verification and all per-peer timestamps together.
    /// Reset due times so recovery probes are scheduled without waiting for a
    /// stable-poll interval, while respecting per-query bounded concurrency.
    /// </summary>
    private void DiscardSamples()
    {
        _selected = null;
        _pendingVerification = false;
        foreach (var rt in _runtime.Values)
        {
            rt.Sample = null;
            rt.SeenAt = 0;
            rt.NextAt = 0;
            rt.Streak = 0;
            rt.PendingEvent = null;
            rt.LastProbeInRound = false;
            rt.State = "Waiting";
            rt.Error = null;
        }
    }

    private static bool IsDue(PeerRuntime peer) =>
        peer.NextAt == 0 || Stopwatch.GetElapsedTime(peer.NextAt) >= TimeSpan.Zero;
    // NextAt uses a monotonic expiry timestamp, not PC wall-clock time.

    private async Task ProbeAsync(string address, long generation, CancellationToken token)
    {
        var rt = _runtime[address];
        try
        {
            Interlocked.Increment(ref _requests);
            var server = IPAddress.Parse(address);
            var first = await NtpProtocol.QueryAsync(server, _options.ServerYear, token)
                .ConfigureAwait(false);
            SntpSample sample = first;
            if (rt.Streak == 0 || first.Offset.Duration() > PollPolicy.StepThreshold)
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
            rt.NextAt = rt.SeenAt + (long)(Stopwatch.Frequency *
                PollPolicy.StableInterval.TotalSeconds);
            rt.LastProbeInRound = true;
            if (rt.State != "Online")
                rt.PendingEvent = address + ": NTP reply validated.";
            rt.State = "Online";
            rt.Error = null;
            rt.Streak = Math.Min(rt.Streak + 1, 100);
            rt.Failures = 0;
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
            // It is essential for matching a RAW Npcap frame to the socket.
            rt.Error = ex is OperationCanceledException
                ? "NTP request timed out. Check network route and UDP 123."
                : ex.Message;
            // RATE and DENY must not trigger busy-loop retransmission.
            double seconds = ex is NtpKissOfDeathException
                ? 60 : Math.Min(30, 1 << rt.Failures);
            rt.NextAt = Stopwatch.GetTimestamp() +
                (long)(Stopwatch.Frequency * seconds);
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
