namespace ARFastSNTP;

internal static class ClockDiscontinuity
{
    internal static bool Detected(DateTimeOffset before, DateTimeOffset after,
        TimeSpan monotonicElapsed, TimeSpan? tolerance = null)
    {
        return ((after - before) - monotonicElapsed).Duration() >
            (tolerance ?? TimeSpan.FromMilliseconds(500));
    }
}
