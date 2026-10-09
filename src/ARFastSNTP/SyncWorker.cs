using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ARFastSNTP;

/// <summary>One async worker owns SNTP polling, correction and bounded dashboard snapshots.</summary>
internal sealed class SyncWorker : BackgroundService
{
    private readonly SyncOptions _options;
    private readonly ILogger<SyncWorker> _logger;
    private readonly Queue<string> _corrections = new();
    private long _requests, _accepted, _rejected;
    private DateTimeOffset? _lastCorrection;
    private long _lastStepStamp;
    private bool _pendingVerification;
    private bool _validatedBaseline;
    private long _lastStatusLogTimestamp;
    private string? _lastLoggedState;

    internal SyncWorker(SyncOptions options, ILogger<SyncWorker> logger)
    {
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int failures = 0;
        _logger.LogInformation("SNTP worker started; peer={Peer}, step enabled={Step}",
            _options.Server, _options.AllowStep);
        while (!stoppingToken.IsCancellationRequested)
        {
            var batchStartUtc = DateTimeOffset.UtcNow;
            var batchClock = Stopwatch.StartNew();
            try
            {
                SntpSample first = await QueryCountedAsync(stoppingToken);
                SntpSample measured = first;
                bool burst = PollPolicy.RequiresBurst(_validatedBaseline, first);
                if (burst)
                {
                    await Task.Delay(100, stoppingToken);
                    SntpSample second = await QueryCountedAsync(stoppingToken);
                    await Task.Delay(100, stoppingToken);
                    SntpSample third = await QueryCountedAsync(stoppingToken);
                    measured = PollPolicy.MedianCoherent(first, second, third);
                }

                // Never apply an offset derived from samples spanning a wall-clock discontinuity.
                if (ClockDiscontinuity.Detected(batchStartUtc, DateTimeOffset.UtcNow,
                    batchClock.Elapsed))
                    throw new InvalidOperationException("Windows clock changed during poll batch.");

                _validatedBaseline = true;
                bool synchronized = PollPolicy.IsSynchronized(measured.Offset);
                DateTimeOffset serverUtc = DateTimeOffset.UtcNow + measured.Offset;
                string state = synchronized ? "Synchronized" : "Offset detected";
                string? evt = null;

                if (synchronized && _pendingVerification)
                {
                    _pendingVerification = false;
                    evt = "Previous clock correction verified by fresh SNTP samples.";
                    Remember(evt);
                    _logger.LogInformation("{Event}", evt);
                }
                else if (_options.AllowStep && !synchronized)
                {
                    // One owner, minimum correction interval measured with monotonic time.
                    // A suspicious sample is ALWAYS corroborated by three SNTP exchanges.
                    if (!burst)
                        throw new InvalidOperationException("Unverified correction request.");
                    if (_lastStepStamp != 0 &&
                        Stopwatch.GetElapsedTime(_lastStepStamp) < PollPolicy.StepCooldown)
                    {
                        state = "Verifying";
                    }
                    else
                    {
                        if (ClockDiscontinuity.Detected(batchStartUtc, DateTimeOffset.UtcNow,
                            batchClock.Elapsed))
                            throw new InvalidOperationException("Windows time moved before correction.");
                        var set = WindowsClock.SetUtc(DateTimeOffset.UtcNow + measured.Offset);
                        evt = set.Success
                            ? $"Clock stepped by {measured.Offset.TotalMilliseconds:+0.0;-0.0;0.0} ms; verifying."
                            : $"Clock correction FAILED (Win32 {set.Error}).";
                        state = set.Success ? "Correcting" : "Correction failed";
                        if (set.Success)
                        {
                            _lastCorrection = DateTimeOffset.UtcNow;
                            _lastStepStamp = Stopwatch.GetTimestamp();
                            _pendingVerification = true;
                            _validatedBaseline = false; // Re-establish coherent post-step samples.
                        }
                        Remember(evt);
                        _logger.LogWarning("{Event}", evt);
                    }
                }

                failures = 0;
                Publish(new StatusSnapshot(DateTimeOffset.UtcNow, _options.Server.ToString(),
                    state, measured.Offset.TotalMilliseconds, measured.RttMs, serverUtc, evt)
                {
                    Stratum = measured.Stratum, LastCorrectionUtc = _lastCorrection
                });
                await Task.Delay(PollPolicy.NextInterval(synchronized, _options.AllowStep),
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (NtpKissOfDeathException ex) when (ex.Code is "DENY" or "RSTR")
            {
                _validatedBaseline = false;
                _logger.LogError("NTP peer permanently denied polling: {Code}", ex.Code);
                Publish(new StatusSnapshot(DateTimeOffset.UtcNow, _options.Server.ToString(),
                    "Server denied", null, null, null, ex.Message)
                { LastCorrectionUtc = _lastCorrection });
                // Remain visible to the GUI without sending additional NTP requests.
                while (!stoppingToken.IsCancellationRequested)
                {
                    try { await Task.Delay(PollPolicy.StableInterval, stoppingToken); }
                    catch (OperationCanceledException) { break; }
                    Publish(new StatusSnapshot(DateTimeOffset.UtcNow, _options.Server.ToString(),
                        "Server denied", null, null, null, ex.Message)
                    { LastCorrectionUtc = _lastCorrection });
                }
                break; // A denied peer is not polled again until service restart.
            }
            catch (NtpKissOfDeathException ex) when (ex.Code == "RATE")
            {
                _validatedBaseline = false;
                _logger.LogWarning("NTP server requested rate backoff.");
                Publish(new StatusSnapshot(DateTimeOffset.UtcNow, _options.Server.ToString(),
                    "Rate limited", null, null, null, ex.Message)
                { LastCorrectionUtc = _lastCorrection });
                try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
            catch (Exception ex)
            {
                _validatedBaseline = false;
                failures = Math.Min(failures + 1, 5);
                _logger.LogWarning("SNTP poll rejected: {Reason}", ex.Message);
                Publish(new StatusSnapshot(DateTimeOffset.UtcNow, _options.Server.ToString(),
                    "Offline / rejected", null, null, null, ex.Message)
                { LastCorrectionUtc = _lastCorrection });
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 1 << failures)),
                        stoppingToken);
                }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task<SntpSample> QueryCountedAsync(CancellationToken cancellationToken)
    {
        _requests++;
        try
        {
            SntpSample response = await NtpProtocol.QueryAsync(_options.Server,
                _options.ServerYear, cancellationToken);
            _accepted++;
            return response;
        }
        catch
        {
            _rejected++;
            throw;
        }
    }

    private void Remember(string value)
    {
        if (_corrections.Count == 16) _corrections.Dequeue();
        _corrections.Enqueue($"{DateTimeOffset.Now:O} {value}");
    }

    private void Publish(StatusSnapshot snapshot)
    {
        snapshot = snapshot with
        {
            RequestCount = _requests, AcceptedCount = _accepted, RejectedCount = _rejected,
            StepEnabled = _options.AllowStep, RecentCorrections = _corrections.ToArray()
        };
        try
        {
            string? directory = Path.GetDirectoryName(_options.StatusPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temporary = _options.StatusPath + ".tmp";
            File.WriteAllText(temporary, StatusSnapshot.Serialize(snapshot),
                new UTF8Encoding(false));
            File.Move(temporary, _options.StatusPath, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cannot write SNTP status {Path}", _options.StatusPath);
        }
        // Surface successful SNTP traffic in the console without flooding
        // Windows service logs or growing an unbounded in-memory history.
        bool stateChanged = _lastLoggedState != snapshot.State;
        bool heartbeatDue = _lastStatusLogTimestamp == 0 ||
            Stopwatch.GetElapsedTime(_lastStatusLogTimestamp) >= TimeSpan.FromSeconds(30);
        if (stateChanged || heartbeatDue)
        {
            _lastStatusLogTimestamp = Stopwatch.GetTimestamp();
            _lastLoggedState = snapshot.State;
            _logger.LogInformation(
                "SNTP {State}: server={Server}:123 offset={Offset}ms RTT={Rtt}ms requests={Requests} valid={Valid} rejected={Rejected}",
                snapshot.State, snapshot.Server, snapshot.OffsetMilliseconds,
                snapshot.RoundTripMilliseconds, snapshot.RequestCount,
                snapshot.AcceptedCount, snapshot.RejectedCount);
        }
    }
}
