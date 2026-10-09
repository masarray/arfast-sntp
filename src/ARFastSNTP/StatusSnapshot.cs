using System.Text.Json;

namespace ARFastSNTP;

/// <summary>Bounded status snapshot for a read-only dashboard; no event queue accumulation.</summary>
internal sealed record StatusSnapshot(
    DateTimeOffset UpdatedUtc,
    string Server,
    string State,
    double? OffsetMilliseconds,
    double? RoundTripMilliseconds,
    DateTimeOffset? EstimatedServerUtc,
    string? LastEvent)
{
    internal static string Serialize(StatusSnapshot value) => JsonSerializer.Serialize(value);
}
