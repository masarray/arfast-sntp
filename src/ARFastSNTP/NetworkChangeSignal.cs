using System.Diagnostics;
using System.Net.NetworkInformation;

namespace ARFastSNTP;

/// <summary>
/// One process-wide pair of native network notifications for one worker.
/// Event callbacks never poll, resolve routes, write status, or change time:
/// they only coalesce and wake the supervisor. Subscription is disposed when
/// the Windows Service worker stops.
/// </summary>
internal sealed class NetworkChangeSignal : IDisposable
{
    private readonly Action _wake;
    private long _version;
    private long _lastStamp;
    private int _disposed;

    internal NetworkChangeSignal(Action wake)
    {
        _wake = wake;
        NetworkChange.NetworkAddressChanged += OnAddressChanged;
        try
        {
            NetworkChange.NetworkAvailabilityChanged += OnAvailabilityChanged;
        }
        catch
        {
            NetworkChange.NetworkAddressChanged -= OnAddressChanged;
            throw;
        }
    }

    private void OnAddressChanged(object? sender, EventArgs args) => Notify();

    private void OnAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs args)
        => Notify();

    private void Notify()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        Interlocked.Exchange(ref _lastStamp, Stopwatch.GetTimestamp());
        Interlocked.Increment(ref _version);
        _wake();
    }

    // Same coalesced wake path for a missed-NIC-event fallback; no extra
    // timer or new thread is created by this method.
    internal void ReportAdapterChanged() => Notify();

    internal long Version => Interlocked.Read(ref _version);

    internal TimeSpan QuietFor
    {
        get
        {
            long stamp = Interlocked.Read(ref _lastStamp);
            return stamp == 0 ? TimeSpan.MaxValue : Stopwatch.GetElapsedTime(stamp);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        NetworkChange.NetworkAddressChanged -= OnAddressChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnAvailabilityChanged;
    }
}
