using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace ARFastSNTP;

/// <summary>Service-owned canonical peer list. Never created by the GUI and
/// never requires a service restart. File replacement is atomic.</summary>
internal sealed class PeerCatalog : IDisposable
{
    internal const int MaximumPeers = 16;
    private readonly object _gate = new();
    private readonly string _path;
    private readonly SourceControlState _master;
    private readonly SemaphoreSlim _changed = new(0, 1);
    private NtpPeer[] _peers;
    private long _generation;

    private PeerCatalog(string path, NtpPeer[] peers, SourceControlState master)
    {
        _path = path;
        _peers = peers;
        _master = master;
    }

    internal static PeerCatalog Load(string path, IPAddress initial, SourceControlState master)
    {
        NtpPeer[] peers;
        if (File.Exists(path))
        {
            if (new FileInfo(path).Length > 8192)
                throw new InvalidDataException("NTP server list is unexpectedly large.");
            peers = JsonSerializer.Deserialize<NtpPeer[]>(File.ReadAllText(path))
                ?? throw new InvalidDataException("NTP server list is empty/corrupted.");
            ValidateList(peers);
        }
        else
        {
            peers = new[] { new NtpPeer(initial.ToString(), master.Enabled) };
            Persist(path, peers);
        }
        var catalog = new PeerCatalog(path, peers, master);
        master.SetEnabled(peers.Any(p => p.Enabled));
        return catalog;
    }

    internal (NtpPeer[] Peers, long Generation) Snapshot()
    {
        lock (_gate) return (_peers.ToArray(), _generation);
    }

    internal bool IsCurrent(string ip, long generation)
    {
        lock (_gate)
            return generation == _generation &&
                _peers.Any(p => p.Enabled && p.Address == ip);
    }

    internal bool TryStep(string ip, long generation, DateTimeOffset target,
        out (bool Success, int Error) result)
    {
        lock (_gate)
        {
            if (!IsCurrent(ip, generation))
            {
                result = (false, 0);
                return false;
            }
            return _master.TrySetTime(target, out result);
        }
    }

    internal Task<bool> WaitAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        _changed.WaitAsync(delay, cancellationToken);

    // Wakes the same supervisor on a NIC/route change without mutating peer
    // catalog generation. Only one pending signal can exist at a time.
    internal void Wake()
    {
        if (_changed.CurrentCount != 0) return;
        try { _changed.Release(); }
        catch (SemaphoreFullException) { /* another signal is already queued */ }
        catch (ObjectDisposedException) { /* service is shutting down */ }
    }

    internal NtpPeer[] Mutate(string action, string? rawAddress)
    {
        if (!TryNormalize(rawAddress, out var ip))
            throw new ArgumentException("Enter a valid unicast NTP Server IPv4 address.");
        lock (_gate)
        {
            NtpPeer[] next = action switch
            {
                "add" => Add(_peers, ip),
                "delete" => _peers.Where(p => p.Address != ip).ToArray(),
                "enable" => _peers.Select(p => p.Address == ip ? p with { Enabled = true } : p).ToArray(),
                "disable" => _peers.Select(p => p.Address == ip ? p with { Enabled = false } : p).ToArray(),
                _ => throw new ArgumentException("Unsupported NTP server operation.")
            };
            if (action != "add" && !_peers.Any(p => p.Address == ip))
                throw new InvalidOperationException("NTP server was already deleted.");
            if (next.SequenceEqual(_peers)) return _peers.ToArray();
            ValidateList(next);
            bool oldMaster = _master.Enabled;
            bool newMaster = next.Any(p => p.Enabled);
            // Source time-stepping barrier happens before any ACK. All
            // modifications and TryStep share _gate to prevent races.
            if (oldMaster != newMaster)
                _master.SetEnabled(newMaster);
            try { Persist(_path, next); }
            catch
            {
                if (oldMaster != newMaster)
                    _master.SetEnabled(oldMaster);
                throw;
            }
            _peers = next;
            _generation++;
            if (_changed.CurrentCount == 0)
                try { _changed.Release(); } catch (SemaphoreFullException) { }
            return next.ToArray();
        }
    }

    private static NtpPeer[] Add(NtpPeer[] old, string ip)
    {
        if (old.Any(p => p.Address == ip))
            throw new InvalidOperationException("NTP server already exists.");
        if (old.Length >= MaximumPeers)
            throw new InvalidOperationException("Maximum of 16 NTP servers reached.");
        return old.Append(new NtpPeer(ip, true)).ToArray();
    }

    internal static bool TryNormalize(string? raw, out string ip)
    {
        ip = "";
        if (!IPAddress.TryParse(raw, out var parsed) ||
            parsed.AddressFamily != AddressFamily.InterNetwork)
            return false;
        byte[] bytes = parsed.GetAddressBytes();
        if (bytes[0] is 0 or >= 224 ||
            bytes.SequenceEqual(new byte[] {255,255,255,255}))
            return false;
        ip = parsed.ToString();
        // Prevent IPAddress.TryParse accepting legacy octal/shorthand forms.
        return string.Equals(raw?.Trim(), ip, StringComparison.Ordinal);
    }

    private static void ValidateList(NtpPeer[] peers)
    {
        if (peers.Length > MaximumPeers || peers.Length != peers
            .Select(p => p.Address).Distinct(StringComparer.Ordinal).Count() ||
            peers.Any(p => !TryNormalize(p.Address, out string canonical) ||
                p.Address != canonical))
            throw new InvalidDataException("NTP server configuration is invalid.");
    }

    private static void Persist(string path, NtpPeer[] peers)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(peers), new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }

    public void Dispose() => _changed.Dispose();
}
