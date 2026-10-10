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
    public bool SourceEnabled { get; init; } = true;
    public string Broadcast { get; init; } = "Not measured";
    public string? SelectedServer { get; init; }
    public bool Degraded { get; init; }
    public PeerReading[] Peers { get; init; } = Array.Empty<PeerReading>();
    public string[] RecentCorrections { get; init; } = Array.Empty<string>();
    public NtpDecisionTrace[] RecentDecisions { get; init; } = Array.Empty<NtpDecisionTrace>();
    internal static string Serialize(StatusSnapshot value) => JsonSerializer.Serialize(value);
}

 
/// <summary>Structured, bounded worker-owned evidence for diagnostic export.</summary>
internal sealed record NtpDecisionPeer(
    string Address, bool Enabled, string State, double? OffsetMilliseconds,
    double? RttMilliseconds, byte? Stratum, double? SampleAgeMilliseconds,
    int ValidStreak, int Failures, string? LastError);

internal sealed record NtpDecisionTrace(
    DateTimeOffset CapturedUtc, string State, string? SelectedServer,
    string Reason, string? StepEvent, NtpDecisionPeer[] Peers,
    long RequestCount, long AcceptedCount, long RejectedCount);
