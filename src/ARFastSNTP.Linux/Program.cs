using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using ARFastSNTP;

namespace ARFastSNTP.Linux;

internal sealed record Peer(string Address, bool Enabled);
internal sealed record PeerResult(string Address, bool Enabled, string State,
    double? OffsetMilliseconds, double? RttMilliseconds, byte? Stratum,
    string? Error, DateTimeOffset? EstimatedServerUtc);
internal sealed record WorkerStatus(DateTimeOffset UpdatedUtc, string State,
    string? SelectedServer, DateTimeOffset? LastCorrectionUtc,
    bool StepEnabled, string? LastEvent, PeerResult[] Peers);

internal static class LinuxClock
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Timespec { public long Seconds; public long Nanoseconds; }

    [DllImport("libc", EntryPoint = "clock_settime", SetLastError = true)]
    private static extern int ClockSetTime(int id, ref Timespec target);

    [DllImport("libc", EntryPoint = "geteuid")]
    internal static extern uint EffectiveUserId();

    internal static void SetUtc(DateTimeOffset instant)
    {
        if (!OperatingSystem.IsLinux() || EffectiveUserId() != 0)
            throw new UnauthorizedAccessException("Clock correction requires the authorized Linux service.");
        var ts = new Timespec { Seconds = instant.ToUnixTimeSeconds(),
            Nanoseconds = (instant.UtcTicks % TimeSpan.TicksPerSecond) * 100 };
        if (ClockSetTime(0, ref ts) != 0)
            throw new IOException("clock_settime failed; Linux errno=" +
                Marshal.GetLastPInvokeError());
    }
}

internal static class TrustedSourcePolicy
{
    internal static TimeSpan? Choose(IReadOnlyList<SntpSample> samples)
    {
        if (samples.Count < 3 || samples.Any(s => s.Stratum is 0 or > 15 ||
            s.RttMs > 250 || s.RttMs < 0))
            return null;
        long[] offsets = samples.Select(s => s.Offset.Ticks).Order().ToArray();
        if ((offsets[^1] - offsets[0]) > TimeSpan.FromMilliseconds(250).Ticks)
            return null;
        return TimeSpan.FromTicks(offsets[offsets.Length / 2]);
    }
}

internal static class LinuxPaths
{
    internal static string Root => Environment.GetEnvironmentVariable("ARFASTSNTP_STATE_DIR")
        ?? "/var/lib/arfast-sntp";
    internal static string Catalog => Path.Combine(Root, "peers.json");
    internal static string Status => Path.Combine(Root, "status.json");
}

internal static class Catalog
{
    internal static bool ValidIp(string? raw)
    {
        if (!IPAddress.TryParse(raw, out var ip) ||
            ip.AddressFamily != AddressFamily.InterNetwork) return false;
        byte[] b = ip.GetAddressBytes();
        return b[0] is > 0 and < 224 && raw == ip.ToString();
    }

    internal static Peer[] Read()
    {
        if (!File.Exists(LinuxPaths.Catalog)) return Array.Empty<Peer>();
        var file = new FileInfo(LinuxPaths.Catalog);
        if (file.Length > 8192) throw new InvalidDataException("Peer catalog too large.");
        Peer[] peers = JsonSerializer.Deserialize<Peer[]>(File.ReadAllText(file.FullName))
            ?? throw new InvalidDataException("Corrupt peer catalog.");
        if (peers.Length > 16 || peers.Any(p => !ValidIp(p.Address)) ||
            peers.Select(p => p.Address).Distinct(StringComparer.Ordinal).Count() != peers.Length)
            throw new InvalidDataException("Invalid peer configuration.");
        return peers;
    }

    internal static void Save(Peer[] peers)
    {
        Directory.CreateDirectory(LinuxPaths.Root);
        string tmp = LinuxPaths.Catalog + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tmp, JsonSerializer.Serialize(peers));
            File.Move(tmp, LinuxPaths.Catalog, true);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }

    internal static void Change(string action, string address)
    {
        if (!ValidIp(address)) throw new ArgumentException("Enter a canonical unicast IPv4 address.");
        Peer[] existing = Read();
        Peer[] updated = action switch
        {
            "--add" when existing.Any(x => x.Address == address) => existing,
            "--add" when existing.Length < 16 => existing.Append(new Peer(address, true)).ToArray(),
            "--remove" => existing.Where(x => x.Address != address).ToArray(),
            "--enable" => existing.Select(x => x.Address == address ? x with { Enabled = true } : x).ToArray(),
            "--disable" => existing.Select(x => x.Address == address ? x with { Enabled = false } : x).ToArray(),
            _ => throw new InvalidOperationException("Invalid action or maximum of 16 servers reached.")
        };
        Save(updated);
    }
}

internal static class LinuxWorker
{
    internal static async Task Run(CancellationToken token, bool monitorOnly)
    {
        DateTimeOffset? corrected = null;
        string? lastEvent = null;
        var backoff = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        Directory.CreateDirectory(LinuxPaths.Root);
        while (!token.IsCancellationRequested)
        {
            var results = new List<PeerResult>();
            Peer[] peers;
            try { peers = Catalog.Read(); }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
            {
                WriteStatus(new WorkerStatus(DateTimeOffset.UtcNow, "Configuration error", null,
                    corrected, !monitorOnly, ex.Message, Array.Empty<PeerResult>()));
                await Task.Delay(2000, token);
                continue;
            }
            string state = peers.Any(x => x.Enabled) ? "No reply" : "NTP off";
            string? selected = null;
            foreach (Peer peer in peers)
            {
                if (!peer.Enabled)
                {
                    results.Add(new PeerResult(peer.Address, false, "Off", null, null, null, null, null));
                    continue;
                }
                if (backoff.TryGetValue(peer.Address, out var until) && until > DateTimeOffset.UtcNow)
                {
                    results.Add(new PeerResult(peer.Address, true, "Rate limited", null,
                        null, null, "NTP server requested backoff", null));
                    continue;
                }
                try
                {
                    var server = IPAddress.Parse(peer.Address);
                    var samples = new List<SntpSample>(3);
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        samples.Add(await NtpProtocol.QueryAsync(server, 2026, token));
                        if (attempt != 2) await Task.Delay(120, token);
                    }
                    TimeSpan? candidate = TrustedSourcePolicy.Choose(samples);
                    if (candidate is null)
                    {
                        results.Add(new PeerResult(peer.Address, true, "Invalid sample", null,
                            null, null, "Three SNTP samples did not agree", null));
                        continue;
                    }
                    var median = samples.OrderBy(s => s.Offset.Ticks).ElementAt(1);
                    selected = peer.Address;
                    state = "Synchronized";
                    if (!monitorOnly && candidate.Value.Duration() > TimeSpan.FromMilliseconds(100))
                    {
                        try
                        {
                            LinuxClock.SetUtc(DateTimeOffset.UtcNow + candidate.Value);
                            corrected = DateTimeOffset.UtcNow;
                            lastEvent = corrected.Value.ToString("O") + " | Clock corrected from " +
                                peer.Address;
                            state = "Correcting";
                        }
                        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                        {
                            state = "Correction failed";
                            lastEvent = ex.Message;
                        }
                    }
                    results.Add(new PeerResult(peer.Address, true, state,
                        candidate.Value.TotalMilliseconds, median.RttMs, median.Stratum,
                        null, DateTimeOffset.UtcNow + candidate.Value));
                    break; // Exactly one active clock writer; avoid competing reference steps.
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (NtpKissOfDeathException ex)
                {
                    if (ex.Code is "RATE" or "DENY" or "RSTR")
                        backoff[peer.Address] = DateTimeOffset.UtcNow.AddSeconds(ex.Code == "RATE" ? 60 : 300);
                    results.Add(new PeerResult(peer.Address, true, "Server rejected", null, null, null, ex.Message, null));
                }
                catch (Exception ex) when (ex is IOException or SocketException or InvalidOperationException or TimeoutException)
                {
                    results.Add(new PeerResult(peer.Address, true, "No reply", null, null, null, ex.Message, null));
                }
            }
            WriteStatus(new WorkerStatus(DateTimeOffset.UtcNow, state, selected, corrected,
                !monitorOnly, lastEvent, results.ToArray()));
            await Task.Delay(selected is null ? 1500 : 2000, token);
        }
    }

    private static void WriteStatus(WorkerStatus status)
    {
        string temp = LinuxPaths.Status + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(status));
        File.Move(temp, LinuxPaths.Status, true);
    }
}

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] is "--list")
            {
                foreach (var peer in Catalog.Read())
                    Console.WriteLine(peer.Address + " " + (peer.Enabled ? "ON" : "OFF"));
                return 0;
            }
            if (args.Length == 2 && args[0] is "--add" or "--remove" or "--enable" or "--disable")
            {
                if (LinuxClock.EffectiveUserId() != 0 &&
                    Environment.GetEnvironmentVariable("ARFASTSNTP_STATE_DIR") is null)
                    throw new UnauthorizedAccessException("Use polkit authorization to change the system service configuration.");
                Catalog.Change(args[0], args[1]);
                Console.WriteLine("NTP server catalog updated.");
                return 0;
            }
            if (args.Length is 1 or 2 && args[0] == "--daemon" &&
                (args.Length == 1 || args[1] == "--monitor-only"))
            {
                bool monitorOnly = args.Length == 2;
                if (!monitorOnly && LinuxClock.EffectiveUserId() != 0)
                    throw new UnauthorizedAccessException("The clock-setting Linux daemon must run under the authorized systemd unit.");
                using var stop = new CancellationTokenSource();
                Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
                AppDomain.CurrentDomain.ProcessExit += (_, _) => stop.Cancel();
                try { await LinuxWorker.Run(stop.Token, monitorOnly); }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
                return 0;
            }
            Console.WriteLine("AR Fast SNTP Linux: --add <IPv4> | --remove <IPv4> | --enable <IPv4> | --disable <IPv4> | --list | --daemon [--monitor-only]");
            return args.Length == 0 || args[0] == "--help" ? 0 : 2;
        }
        catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException
            or IOException or InvalidDataException or JsonException or InvalidOperationException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
