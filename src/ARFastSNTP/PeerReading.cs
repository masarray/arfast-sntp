namespace ARFastSNTP;

/// <summary>One bounded status row for the GUI. UTC is authoritative;
/// displayed local time is a formatting decision made by Studio.</summary>
internal sealed record PeerReading(
    string Address,
    bool Enabled,
    string State,
    double? OffsetMilliseconds,
    double? RttMilliseconds,
    byte? Stratum,
    DateTimeOffset? EstimatedServerUtc,
    string? Error,
    bool Selected,
    int ValidStreak);
