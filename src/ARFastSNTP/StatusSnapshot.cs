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
    internal long RequestCount { get; init; }
    internal long AcceptedCount { get; init; }
    internal long RejectedCount { get; init; }
    internal byte? Stratum { get; init; }
    internal DateTimeOffset? LastCorrectionUtc { get; init; }
    internal bool StepEnabled { get; init; }
    internal string Broadcast { get; init; } = "Not measured";
    internal string[] RecentCorrections { get; init; } = Array.Empty<string>();
    internal static string Serialize(StatusSnapshot value) => JsonSerializer.Serialize(value);
}
