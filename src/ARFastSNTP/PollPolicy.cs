namespace ARFastSNTP;

/// <summary>
/// Deterministic, bounded SNTP poll policy. No timer fan-out, no pending request queue.
/// A suspicious offset is rechecked before the clock may be stepped.
/// </summary>
internal static class PollPolicy
{
    internal static readonly TimeSpan StableInterval = TimeSpan.FromSeconds(3);
    internal static readonly TimeSpan CorrectionInterval = TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan MonitorOffsetInterval = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan StepCooldown = TimeSpan.FromSeconds(3);
    internal static readonly TimeSpan StepThreshold = TimeSpan.FromMilliseconds(100);
    internal static readonly TimeSpan MaximumBurstSpread = TimeSpan.FromMilliseconds(250);

    internal static bool RequiresBurst(bool validatedBaseline, SntpSample first) =>
        !validatedBaseline || first.Offset.Duration() > StepThreshold;

    internal static bool IsSynchronized(TimeSpan offset) => offset.Duration() <= StepThreshold;

    internal static TimeSpan NextInterval(bool synchronized, bool allowStep) =>
        synchronized ? StableInterval
        : allowStep ? CorrectionInterval : MonitorOffsetInterval;

    internal static SntpSample MedianCoherent(SntpSample first, SntpSample second, SntpSample third)
    {
        var samples = new[] { first, second, third };
        Array.Sort(samples, (a, b) => a.Offset.CompareTo(b.Offset));
        if ((samples[2].Offset - samples[0].Offset).Duration() > MaximumBurstSpread)
            throw new InvalidOperationException("NTP measurements disagree (>250ms).");
        return samples[1];
    }
}
