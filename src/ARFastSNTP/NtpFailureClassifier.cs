using System.Net.Sockets;

namespace ARFastSNTP;

/// <summary>
/// Translate probe failures into distinct operator states, without asserting
/// that a physical RAW send has reached a Windows UDP receive socket.
/// Pure and independently regression-tested; no network or timer state.
/// </summary>
internal static class NtpFailureClassifier
{
    internal static string State(Exception error) => error switch
    {
        NtpKissOfDeathException { Code: "DENY" or "RSTR" } => "Denied",
        NtpKissOfDeathException { Code: "RATE" } => "Rate limited",
        NtpKissOfDeathException => "Invalid reply",
        NtpInvalidResponseException => "Invalid reply",
        NtpResponseTimeoutException { IsSelfTarget: true } => "Local target",
        NtpResponseTimeoutException => "No reply",
        SocketException => "Network error",
        IOException => "Network error",
        InvalidOperationException => "Invalid sample",
        _ => "Network error"
    };

    internal static string Event(Exception error) =>
        error is NtpResponseTimeoutException { IsSelfTarget: true }
            ? "NTP server IP resolves to this PC; inspect socket vs RAW adapter path."
            : error is NtpResponseTimeoutException
                ? "No UDP reply at the Windows socket."
                : error.Message;
}
