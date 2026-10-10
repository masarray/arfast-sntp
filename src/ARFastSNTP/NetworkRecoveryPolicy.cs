namespace ARFastSNTP;

/// <summary>
/// Deterministic network-recovery bounds. Transport loss is recoverable when
/// adapter/routes change; NTP server RATE/DENY/RSTR responses are *not*.
/// All intervals use Stopwatch's monotonic time in the supervisor.
/// </summary>
internal static class NetworkRecoveryPolicy
{
    internal static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(400);
    internal static readonly TimeSpan MinimumRearm = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan RecoveryWindow = TimeSpan.FromSeconds(16);
    internal static readonly TimeSpan RecoveryRetry = TimeSpan.FromSeconds(2);

    internal static bool CanRearm(string? kissCode) => kissCode is null;
    internal static bool IsPermanentDenial(string? code) => code is "DENY" or "RSTR";

    internal static TimeSpan RateBackoff(int successiveRateResponses) =>
        TimeSpan.FromSeconds(Math.Min(1024, 60 * Math.Pow(2,
            Math.Clamp(successiveRateResponses - 1, 0, 5))));

    internal static TimeSpan TransportBackoff(int failures, bool recovering)
    {
        if (recovering) return RecoveryRetry;
        return TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Clamp(failures, 0, 5)));
    }
}
