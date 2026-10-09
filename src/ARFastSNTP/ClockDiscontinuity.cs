namespace ARFastSNTP;

internal static class ClockDiscontinuity
{
    internal static bool Detected(DateTimeOffset before, DateTimeOffset after,
        TimeSpan monotonicElapsed, TimeSpan? tolerance = null)
    {
        var difference = ((after - before) - monotonicElapsed).Duration();
        return difference > (tolerance ?? TimeSpan.FromMilliseconds(500));
    }
}
