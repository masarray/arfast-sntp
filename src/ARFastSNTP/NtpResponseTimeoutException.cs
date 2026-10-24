using System.Net;

namespace ARFastSNTP;

/// <summary>
/// A request was sent, but the connected Windows UDP socket did not receive
/// an NTP datagram before its deadline. A raw-frame send is not proof of RX.
/// </summary>
internal sealed class NtpResponseTimeoutException : IOException
{
    internal string LocalEndpoint { get; }
    internal string RemoteEndpoint { get; }
    internal string RequestTransmitHex { get; }
    // The connected Windows socket selected the same IP as its own source.
    // This identifies a local-IP route; it does not assume a particular
    // server implementation or change any source/packet validation.
    internal bool IsSelfTarget { get; }

    internal NtpResponseTimeoutException(IPEndPoint? local, IPEndPoint remote,
        ReadOnlySpan<byte> requestTransmit, Exception inner)
        : base($"No UDP reply received at {local?.ToString() ?? "unknown local socket"} " +
            $"from {remote}; request Tx={Convert.ToHexString(requestTransmit)}. " +
            (local?.Address.Equals(remote.Address) == true
                ? "Target IP belongs to this PC: local UDP loopback may not receive " +
                  "a raw link-layer reply injected on a physical adapter. " +
                  "Use a separate NTP server PC or a genuine local UDP/123 listener."
                : "Match destination IP/port and NTP Originate in packet capture."), inner)
    {
        IsSelfTarget = local?.Address.Equals(remote.Address) == true;
        LocalEndpoint = local?.ToString() ?? "unknown";
        RemoteEndpoint = remote.ToString();
        RequestTransmitHex = Convert.ToHexString(requestTransmit);
    }
}
