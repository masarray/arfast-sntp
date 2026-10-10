namespace ARFastSNTP;

/// <summary>
/// Deterministic, bounded SNTP poll policy. No timer fan-out, no pending request queue.
/// A suspicious offset is rechecked before the clock may be stepped.
/// </summary>
internal static class PollPolicy
{
    // Every 8s while all peers are disabled: below the Studio 15s watchdog.
    // A coalesced peer-catalog signal wakes sooner; no UDP sockets are opened.
    internal static readonly TimeSpan IdleHeartbeatInterval = TimeSpan.FromSeconds(8);
    internal static readonly TimeSpan StableInterval = TimeSpan.FromSeconds(3);
    // Start-up and post-network/clock-change single-source confirmation.
    // The first validated 3-sample burst still has to be checked by a
    // different *new* Mode 4 reply, but never idles for the stable 3s period.
    internal static readonly TimeSpan ConfirmationInterval = TimeSpan.FromMilliseconds(750);
    internal static readonly TimeSpan CorrectionInterval = TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan MonitorOffsetInterval = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan StepCooldown = TimeSpan.FromSeconds(3);
    internal static readonly TimeSpan StepThreshold = TimeSpan.FromMilliseconds(100);
    internal static readonly TimeSpan MaximumBurstSpread = TimeSpan.FromMilliseconds(250);

    internal static bool RequiresBurst(bool validatedBaseline, SntpSample first) =>
        !validatedBaseline || first.Offset.Duration() > StepThreshold;

    internal static bool IsSynchronized(TimeSpan offset) => offset.Duration() <= StepThreshold;

    // Confirmation uses a later independently originated UDP transaction, not
    // a repeat of the initial burst. Refuse any meaningful source disagreement.
    internal static bool CoherentConfirmation(SntpSample previous, SntpSample current) =>
        (current.Offset - previous.Offset).Duration() <= MaximumBurstSpread;

    internal static TimeSpan PeerInterval(int validatedRounds) =>
        validatedRounds == 1 ? ConfirmationInterval : StableInterval;

    // Wait only until the earliest actual per-peer monotonic deadline. The
    // active heartbeat bounds responsiveness to a manual Windows clock jump;
    // 0 means the next due poll can begin immediately, not another fixed 1s.
    internal static TimeSpan NextSupervisorWait(TimeSpan timeUntilDue, bool synchronized) =>
        timeUntilDue <= TimeSpan.Zero ? TimeSpan.Zero :
        timeUntilDue < (synchronized ? StableInterval : CorrectionInterval)
            ? timeUntilDue : (synchronized ? StableInterval : CorrectionInterval);

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
