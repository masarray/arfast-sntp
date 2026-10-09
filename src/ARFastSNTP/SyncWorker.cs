using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ARFastSNTP;

/// <summary>Single owner of polling and time correction; no parallel setters or unbounded queues.</summary>
internal sealed class SyncWorker : BackgroundService
{
    private readonly SyncOptions _options;
    private readonly ILogger<SyncWorker> _logger;
    private readonly Queue<string> _corrections = new();
    private long _requests, _accepted, _rejected;
    private DateTimeOffset? _lastCorrection;

    internal SyncWorker(SyncOptions options, ILogger<SyncWorker> logger)
    {
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int failures = 0;
        _logger.LogInformation("SNTP service started; server={Server}, clock stepping={Step}",
            _options.Server, _options.AllowStep);
        while (!stoppingToken.IsCancellationRequested)
        {
            DateTimeOffset batchStartUtc = DateTimeOffset.UtcNow;
            var batchClock = Stopwatch.StartNew();
            try
            {
                var samples = new SntpSample[3];
                for (int i = 0; i < samples.Length; i++)
                {
                    _requests++;
                    try
                    {
                        samples[i] = await NtpProtocol.QueryAsync(_options.Server,
                            _options.ServerYear, stoppingToken);
                        _accepted++;
                    }
                    catch
                    {
                        _rejected++;
                        throw;
                    }
                    if (i < samples.Length - 1)
                        await Task.Delay(150, stoppingToken);
                }
                if (ClockDiscontinuity.Detected(batchStartUtc, DateTimeOffset.UtcNow,
                    batchClock.Elapsed))
                    throw new InvalidOperationException("PC clock changed during sample batch.");
                Array.Sort(samples, (a, b) => a.Offset.CompareTo(b.Offset));
                if ((samples[2].Offset - samples[0].Offset).Duration() >
                    TimeSpan.FromMilliseconds(250))
                    throw new InvalidOperationException("NTP measurements disagree (>250ms).");

                var median = samples[1];
                var offset = median.Offset;
                // Estimate server time before any local clock adjustment.
                DateTimeOffset serverUtc = DateTimeOffset.UtcNow + offset;
                string state = offset.Duration() <= TimeSpan.FromMilliseconds(100)
                    ? "Synchronized" : "Offset detected";
                string? evt = null;
                if (_options.AllowStep && offset.Duration() > TimeSpan.FromMilliseconds(100))
                {
                    if (ClockDiscontinuity.Detected(batchStartUtc, DateTimeOffset.UtcNow,
                        batchClock.Elapsed))
                        throw new InvalidOperationException("PC clock moved before correction.");
                    var set = WindowsClock.SetUtc(DateTimeOffset.UtcNow + offset);
                    evt = set.Success
                        ? $"Clock stepped by {offset.TotalMilliseconds:+0.0;-0.0;0} ms; verification pending."
                        : $"Clock correction failed (Win32 {set.Error}).";
                    state = set.Success ? "Correcting" : "Correction failed";
                    _lastCorrection = DateTimeOffset.UtcNow;
                    Remember(evt);
                    _logger.LogWarning("{ClockEvent}", evt);
                }

                failures = 0;
                Publish(new StatusSnapshot(DateTimeOffset.UtcNow, _options.Server.ToString(),
                    state, offset.TotalMilliseconds, median.RttMs, serverUtc, evt)
                {
                    Stratum = median.Stratum, LastCorrectionUtc = _lastCorrection
                });
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                failures = Math.Min(failures + 1, 5);
                _logger.LogWarning("SNTP sample rejected: {Reason}", ex.Message);
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

    private void Remember(string text)
    {
        if (_corrections.Count == 16) _corrections.Dequeue();
        _corrections.Enqueue($"{DateTimeOffset.Now:O} {text}");
    }

    private void Publish(StatusSnapshot snapshot)
    {
        snapshot = snapshot with
        {
            RequestCount = _requests, AcceptedCount = _accepted, RejectedCount = _rejected,
            StepEnabled = _options.AllowStep,
            RecentCorrections = _corrections.ToArray()
        };
        try
        {
            string? directory = Path.GetDirectoryName(_options.StatusPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temporary = _options.StatusPath + ".tmp";
            File.WriteAllText(temporary, StatusSnapshot.Serialize(snapshot),
                new UTF8Encoding(false));
            File.Move(temporary, _options.StatusPath, true); // Atomic same-volume replacement.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cannot write status file {Path}", _options.StatusPath);
        }
        _logger.LogInformation("SNTP {State}; offset={Offset}ms RTT={Rtt}ms",
            snapshot.State, snapshot.OffsetMilliseconds, snapshot.RoundTripMilliseconds);
    }
}
