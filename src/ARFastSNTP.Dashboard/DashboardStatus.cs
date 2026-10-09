using System.Text.Json;

namespace ARFastSNTP.Dashboard;

/// <summary>
/// Backward-compatible, read-only status DTO. Never require optional JSON keys:
/// older worker builds and incomplete files must not crash the WinForms UI.
/// </summary>
internal sealed record DashboardStatus
{
    public DateTimeOffset UpdatedUtc { get; init; }
    public string Server { get; init; } = "";
    public string State { get; init; } = "Unknown";
    public double? OffsetMilliseconds { get; init; }
    public double? RoundTripMilliseconds { get; init; }
    public DateTimeOffset? EstimatedServerUtc { get; init; }
    public string? LastEvent { get; init; }
    public long RequestCount { get; init; }
    public long AcceptedCount { get; init; }
    public long RejectedCount { get; init; }
    public bool StepEnabled { get; init; }
    public byte? Stratum { get; init; }
    public DateTimeOffset? LastCorrectionUtc { get; init; }
    public string Broadcast { get; init; } = "Not measured";
    public string[]? RecentCorrections { get; init; }

    internal static bool TryParse(string json, out DashboardStatus? status, out string reason)
    {
        status = null;
        reason = "";
        try
        {
            var value = JsonSerializer.Deserialize<DashboardStatus>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (value is null || value.UpdatedUtc == default || string.IsNullOrWhiteSpace(value.Server))
            {
                reason = "Worker status missing required server or update time.";
                return false;
            }
            if (value.OffsetMilliseconds is double offset && !double.IsFinite(offset))
            {
                reason = "Invalid SNTP offset.";
                return false;
            }
            status = value;
            return true;
        }
        catch (JsonException)
        {
            reason = "Worker status is incomplete or invalid.";
            return false;
        }
        catch (NotSupportedException)
        {
            reason = "Unsupported worker status format.";
            return false;
        }
    }

    internal bool IsFresh(DateTimeOffset nowUtc, TimeSpan? maxAge = null)
    {
        double ageSeconds = (nowUtc - UpdatedUtc).TotalSeconds;
        return ageSeconds >= -1 && ageSeconds <= (maxAge ?? TimeSpan.FromSeconds(12)).TotalSeconds;
    }

    internal bool HasValidReply(DateTimeOffset nowUtc) =>
        IsFresh(nowUtc) && OffsetMilliseconds is not null &&
        EstimatedServerUtc is not null;

    internal bool IsSynchronized(DateTimeOffset nowUtc) =>
        HasValidReply(nowUtc) && State == "Synchronized";

    internal string? LatestEvent => !string.IsNullOrWhiteSpace(LastEvent) ? LastEvent :
        RecentCorrections is { Length: > 0 } ? RecentCorrections[^1] : null;
}
