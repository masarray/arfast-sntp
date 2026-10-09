using System.Text.Json;

namespace ARFastSNTP;

/// <summary>Small overwrite-only status: no unbounded transaction or event history.</summary>
internal sealed record StatusSnapshot(
    DateTimeOffset UpdatedUtc,
    string Server,
    string State,
    double? OffsetMilliseconds,
    double? RoundTripMilliseconds,
    DateTimeOffset? EstimatedServerUtc,
    string? LastEvent)
{
    public long RequestCount { get; init; }
    public long AcceptedCount { get; init; }
    public long RejectedCount { get; init; }
    public byte? Stratum { get; init; }
    public DateTimeOffset? LastCorrectionUtc { get; init; }
    public bool StepEnabled { get; init; }
    public string Broadcast { get; init; } = "Not measured";
    public string[] RecentCorrections { get; init; } = Array.Empty<string>();
    internal static string Serialize(StatusSnapshot value) => JsonSerializer.Serialize(value);
}
