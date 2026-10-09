using System.Diagnostics;
using System.Net;
using System.Text;
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

    private readonly SyncOptions _options;
    private readonly PeerCatalog _catalog;
    private readonly ILogger<MultiPeerWorker> _logger;
    private readonly Dictionary<string, PeerRuntime> _runtime = new(StringComparer.Ordinal);
    private readonly Queue<string> _history = new();
    private readonly string _statusPath;
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
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Multi-peer SNTP worker initialized; correction={Allowed}",
            _options.AllowStep);
        while (!stoppingToken.IsCancellationRequested)
        {
            var (peers, generation) = _catalog.Snapshot();
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
                Publish(peers, "NTP off", null, "All NTP sources disabled.");
                try { await _catalog.WaitAsync(TimeSpan.FromHours(12), stoppingToken); }
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
            string overallState = chosen is null
                ? eligible.Length == 0 ? "No reply" : "Source conflict"
                : "Verifying";
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
                            _lastStepStamp = Stopwatch.GetTimestamp();
                            _lastCorrection = DateTimeOffset.UtcNow;
                            _pendingVerification = true;
                            // All pre-step peer offsets have a different epoch.
                            foreach (var rt in _runtime.Values)
                            {
                                rt.Sample = null;
                                rt.Streak = 0;
                                rt.SeenAt = 0;
                                rt.NextAt = 0;
                            }
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

            Publish(peers, overallState, primary, evt ?? decision.Reason,
                decision.Degraded && enabled.Length > 1);
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
            string nextState = ex is NtpKissOfDeathException kod &&
                kod.Code is "DENY" or "RSTR" ? "Denied" : "No reply";
            if (rt.State != nextState)
                rt.PendingEvent = address + ": " + (ex is OperationCanceledException
                    ? "No reply (UDP 123 timeout)." : ex.Message);
            rt.State = nextState;
            rt.Error = ex is OperationCanceledException
                ? "NTP timeout: check IP address and UDP port 123."
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
