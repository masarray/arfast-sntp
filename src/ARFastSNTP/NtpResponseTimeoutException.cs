using System.Net;

namespace ARFastSNTP;

/// <summary>
/// The Windows UDP socket did not receive an NTP datagram before its deadline.
/// "Server sent" in raw packet-capture software is not socket delivery.
/// The local socket endpoint is retained for targeted packet-trace diagnostics.
/// </summary>
internal sealed class NtpResponseTimeoutException : IOException
{
    internal string LocalEndpoint { get; }
    internal string RemoteEndpoint { get; }

    internal NtpResponseTimeoutException(IPEndPoint? local, IPAddress remote,
        Exception inner)
        : base($"No UDP reply received at {local?.ToString() ?? "unknown local socket"} " +
            $"from {remote}:123. Compare destination IP/UDP port in packet capture.",
            inner)
    {
        LocalEndpoint = local?.ToString() ?? "unknown";
        RemoteEndpoint = remote + ":123";
    }
}
