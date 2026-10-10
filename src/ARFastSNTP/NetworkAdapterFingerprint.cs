using System.Net.Sockets;
using System.Net.NetworkInformation;

namespace ARFastSNTP;

/// <summary>
/// Rare missed-notification fallback. An IPv4 adapter fingerprint is only a
/// change hint, NEVER evidence that an individual NTP peer is reachable.
/// Pure string Observe is separated so no machine NIC is needed in regression.
/// </summary>
internal sealed class NetworkAdapterFingerprint
{
    private string? _previous;
    internal bool Observe(string fingerprint)
    {
        if (_previous is null)
        {
            _previous = fingerprint;
            return false;
        }
        if (string.Equals(_previous, fingerprint, StringComparison.Ordinal))
            return false;
        _previous = fingerprint;
        return true;
    }

    internal bool Check()
    {
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            var rows = new List<string>(interfaces.Length);
            foreach (NetworkInterface nic in interfaces)
            {
                // No ICMP, DNS, sockets or NTP requests. Keep the comparison
                // bounded by the OS adapter list; static ordering is stable.
                var props = nic.GetIPProperties();
                var addresses = props.UnicastAddresses
                    .Where(ip => ip.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(ip => ip.Address + "/" + ip.PrefixLength)
                    .OrderBy(x => x, StringComparer.Ordinal);
                var gateways = props.GatewayAddresses
                    .Where(g => g.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(g => g.Address.ToString())
                    .OrderBy(x => x, StringComparer.Ordinal);
                rows.Add(nic.Id + ":" + nic.OperationalStatus + ":" +
                    string.Join(",", addresses) + ":" + string.Join(",", gateways));
            }
            rows.Sort(StringComparer.Ordinal);
            return Observe(string.Join("|", rows));
        }
        catch (NetworkInformationException)
        {
            return false; // Transient adapter enumeration failures are not route changes.
        }
    }
}
