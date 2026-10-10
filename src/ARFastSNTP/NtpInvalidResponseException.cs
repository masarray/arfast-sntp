using System.Net;

namespace ARFastSNTP;

/// <summary>
/// A UDP datagram arrived at the connected socket but failed NTP validation.
/// Distinct from NtpResponseTimeoutException: never imply packet loss here.
/// </summary>
internal sealed class NtpInvalidResponseException : InvalidOperationException
{
    internal NtpInvalidResponseException(IPEndPoint? local, IPEndPoint remote,
        ReadOnlySpan<byte> requestTransmit, Exception inner)
        : base($"UDP reply received at {local?.ToString() ?? "unknown local socket"} " +
            $"from {remote}, but NTP rejected it: {inner.Message}; " +
            $"request Tx={Convert.ToHexString(requestTransmit)}.", inner)
    {
    }
}
