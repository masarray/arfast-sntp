using System.Diagnostics;
using Microsoft.Win32;

namespace ARFastSNTP;

/// <summary>
/// Single authoritative on/off flag owned by the already-running Windows Service.
/// Persisted beneath HKLM (not a user-editable file), and synchronized with
/// Windows time-stepping so no correction can start after Disable is ACKed.
/// </summary>
internal sealed class SourceControlState : IDisposable
{
    private readonly object _gate = new();
    private readonly Action<bool> _persist;
    private readonly SemaphoreSlim _changed = new(0, 1);
    private bool _enabled;

    internal SourceControlState(bool initialEnabled, Action<bool> persist)
    {
        _enabled = initialEnabled;
        _persist = persist;
    }

    internal bool Enabled
    {
        get { lock (_gate) return _enabled; }
    }

    internal bool SetEnabled(bool enabled)
    {
        bool changed;
        lock (_gate)
        {
            changed = _enabled != enabled;
            if (changed)
            {
                // Failed persistent write means no runtime state change.
                _persist(enabled);
                _enabled = enabled;
            }
        }
        if (changed && _changed.CurrentCount == 0)
        {
            try { _changed.Release(); }
            catch (SemaphoreFullException) { /* concurrent signal already pending */ }
        }
        return enabled;
    }

    internal bool TrySetTime(DateTimeOffset utc, out (bool Success, int Error) result)
    {
        lock (_gate)
        {
            if (!_enabled)
            {
                result = (false, 0);
                return false;
            }
            result = WindowsClock.SetUtc(utc);
            return true;
        }
    }

    internal Task<bool> WaitAsync(TimeSpan delay, CancellationToken stoppingToken) =>
        _changed.WaitAsync(delay, stoppingToken);

    internal Task WaitUntilChangedAsync(CancellationToken stoppingToken) =>
        _changed.WaitAsync(stoppingToken);

    public void Dispose() => _changed.Dispose();

    internal static SourceControlState LoadWindows()
    {
        if (!OperatingSystem.IsWindows())
            return new SourceControlState(initialEnabled: true, _ => { });

        using var key = Registry.LocalMachine.OpenSubKey(SourceControlProtocol.SettingsKey);
        bool initial = (key?.GetValue(SourceControlProtocol.SettingName) as int?) != 0;
        return new SourceControlState(initial, value =>
        {
            // Service identity should be LocalSystem. No elevated GUI executable
            // is ever launched merely to change this trusted source flag.
            using var writable = Registry.LocalMachine.CreateSubKey(
                SourceControlProtocol.SettingsKey, writable: true)
                ?? throw new IOException("Cannot open protected NTP source settings.");
            writable.SetValue(SourceControlProtocol.SettingName, value ? 1 : 0,
                RegistryValueKind.DWord);
        });
    }
}
