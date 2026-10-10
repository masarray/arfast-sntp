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

    internal NtpResponseTimeoutException(IPEndPoint? local, IPEndPoint remote,
        ReadOnlySpan<byte> requestTransmit, Exception inner)
        : base($"No UDP reply received at {local?.ToString() ?? "unknown local socket"} " +
            $"from {remote}; request Tx={Convert.ToHexString(requestTransmit)}. " +
            "Match destination IP/port and NTP Originate in packet capture.", inner)
    {
        LocalEndpoint = local?.ToString() ?? "unknown";
        RemoteEndpoint = remote.ToString();
        RequestTransmitHex = Convert.ToHexString(requestTransmit);
    }
}
