using System.Text.Json;

namespace ARFastSNTP.Setup;

/// <summary>
/// Bounded install-time verification of the running NTP service.
/// Runtime source ON/OFF is exclusively handled inside the worker over its
/// restricted local named pipe; it must never change SCM startup mode.
/// </summary>
internal static class SourceServiceControl
{
    internal enum Health { Synced, Reply, Waiting }

    internal static Health WaitForFreshResult(TimeSpan maximum, Action<string> report)
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            InstallerOperations.ServiceName, "status.json");
        // The current service has just started: new snapshots use fresh
        // file writes. Do not compare NTP timestamps to the PC clock because
        // authorized clock correction can jump by years.
        long initialLength = File.Exists(path) ? new FileInfo(path).Length : -1;
        DateTime initialWritten = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        long initialCounter = -1;
        if (File.Exists(path))
        {
            try
            {
                using var prior = JsonDocument.Parse(File.ReadAllText(path));
                if (prior.RootElement.TryGetProperty("RequestCount", out var count))
                    initialCounter = count.GetInt64();
            }
            catch (JsonException) { }
        }

        var clock = System.Diagnostics.Stopwatch.StartNew();
        bool hadValidReply = false;
        while (clock.Elapsed < maximum)
        {
            try
            {
                if (File.Exists(path))
                {
                    // Incoming service snapshots are atomically replaced.
                    // Counter reset or increment proves new worker activity;
                    // file modification may be unreliable after a year jump.
                    using var data = JsonDocument.Parse(File.ReadAllText(path));
                    var root = data.RootElement;
                    long counter = root.TryGetProperty("RequestCount", out var requests)
                        ? requests.GetInt64() : 0;
                    bool newData = counter > 0 &&
                        (initialCounter != counter || initialLength != new FileInfo(path).Length ||
                         initialWritten != File.GetLastWriteTimeUtc(path));
                    if (newData)
                    {
                        bool hasOffset = root.TryGetProperty("OffsetMilliseconds", out var offset) &&
                            offset.ValueKind == JsonValueKind.Number;
                        bool hasClock = root.TryGetProperty("EstimatedServerUtc", out var serverTime) &&
                            serverTime.ValueKind == JsonValueKind.String;
                        bool valid = hasOffset && hasClock;
                        if (valid)
                        {
                            hadValidReply = true;
                            string state = root.TryGetProperty("State", out var mode)
                                ? mode.GetString() ?? "" : "";
                            if (state == "Synchronized")
                            {
                                report("Verified: NTP replied and PC clock is synchronized.");
                                return Health.Synced;
                            }
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or JsonException)
            {
                // Bounded best-effort health observation, not an install failure.
            }
            Thread.Sleep(250);
        }
        report(hadValidReply
            ? "NTP responded; PC synchronization is still being verified."
            : "Service running; no verified NTP reply yet. Studio will continue monitoring.");
        return hadValidReply ? Health.Reply : Health.Waiting;
    }

    internal static void WriteInstallResult(Health health)
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            InstallerOperations.ServiceName);
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "install-result.txt");
        string temp = path + ".tmp";
        File.WriteAllText(temp, health switch
        {
            Health.Synced => "SYNCED",
            Health.Reply => "REPLY",
            _ => "WAITING"
        });
        File.Move(temp, path, overwrite: true);
    }
}
