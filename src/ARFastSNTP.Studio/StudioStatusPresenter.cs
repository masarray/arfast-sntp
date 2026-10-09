using System.Globalization;
using ARFastSNTP.Dashboard;

namespace ARFastSNTP.Studio;

/// <summary>
/// Pure, dependency-free view of a worker snapshot. Keep transport errors and
/// service configuration distinct from clock synchronization. No UI control
/// may infer a successful sync from a RUNNING service or an enabled switch.
/// </summary>
internal readonly record struct StudioStatusView(
    string PcSync, string PcDetail, string ServerHealth, string Footer,
    string ServerOffset, string Stratum, bool ValidReply, bool Synchronized);

internal static class StudioStatusPresenter
{
    internal static StudioStatusView Create(DashboardStatus snapshot)
    {
        bool valid = snapshot.OffsetMilliseconds is double offset &&
            double.IsFinite(offset) &&
            snapshot.EstimatedServerUtc.HasValue;
        bool synced = valid &&
            string.Equals(snapshot.State, "Synchronized", StringComparison.Ordinal);
        string host = snapshot.Server;
        string offsetText = valid ? FormatOffset(snapshot.OffsetMilliseconds!.Value) : "—";
        string stratum = valid && snapshot.Stratum.HasValue
            ? snapshot.Stratum.Value.ToString(CultureInfo.InvariantCulture) : "—";

        if (!valid)
        {
            bool denied = snapshot.State.Contains("denied", StringComparison.OrdinalIgnoreCase);
            bool backoff = snapshot.State.Contains("rate", StringComparison.OrdinalIgnoreCase);
            string health = denied ? "Denied" : backoff ? "Busy" : "No reply";
            string detail = denied ? "The NTP server rejected requests"
                : backoff ? "Server requested a slower connection"
                : "No time received from " + host;
            string hint = denied ? "Check server access settings"
                : backoff ? "The service will retry automatically"
                : ExplainFailure(snapshot.LastEvent);
            return new StudioStatusView(
                PcSync: "Not synced",
                PcDetail: detail,
                ServerHealth: health,
                Footer: hint + "  ·  " + snapshot.AcceptedCount + "/" +
                    snapshot.RequestCount + " valid replies",
                ServerOffset: "—",
                Stratum: "—",
                ValidReply: false,
                Synchronized: false);
        }

        string state = synced ? "Synced"
            : snapshot.State.Equals("Correcting", StringComparison.OrdinalIgnoreCase) ||
              snapshot.State.Equals("Verifying", StringComparison.OrdinalIgnoreCase)
                ? "Adjusting" : "Not synced";
        string subtitle = synced
            ? "From " + host + "  ·  " + offsetText
            : "Difference: " + offsetText + "  ·  " + host;
        string footer = "NTP replies " + snapshot.AcceptedCount + "/" +
            snapshot.RequestCount + "  ·  RTT " +
            (snapshot.RoundTripMilliseconds?.ToString("0.0", CultureInfo.InvariantCulture) ?? "—") +
            " ms";
        return new StudioStatusView(state, subtitle, "Online", footer,
            offsetText, stratum, valid, synced);
    }

    internal static string FormatOffset(double value)
    {
        if (Math.Abs(value) >= TimeSpan.FromDays(365).TotalMilliseconds)
            return (value / TimeSpan.FromDays(365.2425).TotalMilliseconds)
                .ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + " yr";
        if (Math.Abs(value) >= 60_000)
            return (value / 60_000).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) +
                " min";
        if (Math.Abs(value) >= 1_000)
            return (value / 1_000).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) +
                " s";
        return value.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture) + " ms";
    }

    internal static string ExplainFailure(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
            return "Check the server IP and network";
        if (error.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            return "Server did not respond. Check IP and UDP 123";
        if (error.Contains("refused", StringComparison.OrdinalIgnoreCase))
            return "Server refused the connection";
        if (error.Contains("unreachable", StringComparison.OrdinalIgnoreCase))
            return "Network is unreachable";
        if (error.Contains("timestamp", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("originate", StringComparison.OrdinalIgnoreCase))
            return "NTP reply was invalid";
        return "Check server settings; see Status History";
    }
}
