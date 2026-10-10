using System.Diagnostics;

namespace ARFastSNTP;

/// <summary>
/// Detects discontinuities between Windows UTC wall time and a monotonic clock.
/// Local clock-step changes invalidate ALL previously collected NTP samples;
/// service-originated steps must call Reset after SetSystemTime succeeds.
/// This class never writes the Windows clock or modifies any OS policy.
/// </summary>
internal sealed class ClockEpochGuard
{
    private DateTimeOffset _utc;
    private long _stamp;
    private bool _initialized;

    internal bool Observe(DateTimeOffset now, long monotonicStamp)
    {
        bool changed = _initialized && ClockDiscontinuity.Detected(
            _utc, now, Stopwatch.GetElapsedTime(_stamp, monotonicStamp));
        Reset(now, monotonicStamp);
        return changed;
    }

    internal void Reset(DateTimeOffset now, long monotonicStamp)
    {
        _utc = now;
        _stamp = monotonicStamp;
        _initialized = true;
    }
}
