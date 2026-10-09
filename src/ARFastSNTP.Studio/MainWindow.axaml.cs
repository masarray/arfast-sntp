using System.Diagnostics;
using System.ComponentModel;
using Microsoft.Win32;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using ARFastSNTP.Dashboard;
using ARFastSNTP.Studio.Design;

namespace ARFastSNTP.Studio;

/// <summary>
/// Passive Windows monitor. Single coalesced status-file reader, no service
/// mutation or sockets in UI. All staleness detection after first observation
/// uses monotonic time so multi-year wall clock corrections do not break the UI.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly TextBlock _pcClock;
    private readonly TextBlock _pcDate;
    private readonly TextBlock _globalLamp;
    private readonly TextBlock _globalStatus;
    private readonly TextBlock _syncLamp;
    private readonly TextBlock _syncState;
    private readonly TextBlock _syncDetail;
    private readonly TextBlock _peerAddress;
    private readonly Avalonia.Controls.Shapes.Path _switchIcon;
    private readonly Button _sourceToggle;
    private readonly TextBlock _peerHealth;
    private readonly TextBlock _peerStratum;
    private readonly TextBlock _peerOffset;
    private readonly TextBlock _peerClock;
    private readonly TextBlock _peerBroadcast;
    private readonly TextBlock _connectionHint;
    private readonly TextBlock _lastReply;
    private readonly TextBlock _eventCount;
    private readonly StackPanel _historyEvents;
    private readonly ScrollViewer _historyScroll;
    private readonly Avalonia.Controls.Shapes.Path _historyChevron;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _lastFileChange = new();
    private readonly Stopwatch _serverClock = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly string _statusPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ARFastSNTP", "status.json");

    private string? _lastSnapshot;
    private string? _lastEventsSignature;
    private string _lastKnownServer = "—";
    private DateTimeOffset? _baseServerUtc;
    private bool _lastSampleValid;
    private bool _reading;
    private bool _closed;
    private bool _historyExpanded;
    private int _ticks;
    private bool _configuredEnabled;
    private bool _serviceInstalled;
    private bool _sourceActionPending;
    private static readonly string SwitchOn =
        "M280-240q-100 0-170-70T40-480q0-100 70-170t170-70h400q100 0 170 70t70 170t-170 70H280Zm0-80h400q66 0 113-47t47-113q0-66-47-113t-113-47H280q-66 0-113 47t-47 113q0 66 47 113t113 47Zm485-75q35-35 35-85t-35-85q-35-35-85-35t-85 35q-35 35-35 85t35 85q35 35 85 35t85-35Zm-285-85Z";
    private static readonly string SwitchOff =
        "M280-240q-100 0-170-70T40-480q0-100 70-170t170-70h400q100 0 170 70t70 170q0 100-70 170t-170 70H280Zm0-80h400q66 0 113-47t47-113q0-66-47-113t-113-47H280q-66 0-113 47t-47 113q0 66 47 113t113 47Zm85-75q35-35 35-85t-35-85q-35-35-85-35t-85 35q-35 35-35 85t35 85q35 35 85 35t85-35Zm115-85Z";
    private static readonly string ChevronDown =
        "m480-340 180-180-57-56-123 123-123-123-57 56 180 180Zm0 260q-83 0-156-31.5T197-197q-54-54-85.5-127T80-480q0-83 31.5-156T197-763q54-54 127-85.5T480-880q83 0 156 31.5T763-763q54 54 85.5 127T880-480q0 83-31.5 156T763-197q-54 54-127 85.5T480-80Zm0-80q134 0 227-93t93-227q0-134-93-227t-227-93q-134 0-227 93t-93 227q0 134 93 227t227 93Zm0-320Z";
    private static readonly string ChevronUp =
        "m357-384 123-123 123 123 57-56-180-180-180 180 57 56ZM480-80q-83 0-156-31.5T197-197q-54-54-85.5-127T80-480q0-83 31.5-156T197-763q54-54 127-85.5T480-880q83 0 156 31.5T763-763q54 54 85.5 127T880-480q0 83-31.5 156T763-197q-54 54-127 85.5T480-80Zm0-80q134 0 227-93t93-227q0-134-93-227t-227-93q-134 0-227 93t-93 227q0 134 93 227t227 93Zm0-320Z";

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        _pcClock = Required<TextBlock>("PcClock");
        _pcDate = Required<TextBlock>("PcDate");
        _globalLamp = Required<TextBlock>("GlobalLamp");
        _globalStatus = Required<TextBlock>("GlobalStatus");
        _syncLamp = Required<TextBlock>("SyncLamp");
        _syncState = Required<TextBlock>("SyncState");
        _syncDetail = Required<TextBlock>("SyncDetail");
        _peerAddress = Required<TextBlock>("PeerAddress");
        _switchIcon = Required<Avalonia.Controls.Shapes.Path>("SwitchIcon");
        _sourceToggle = Required<Button>("SourceToggle");
        _peerHealth = Required<TextBlock>("PeerHealth");
        _peerStratum = Required<TextBlock>("PeerStratum");
        _peerOffset = Required<TextBlock>("PeerOffset");
        _peerClock = Required<TextBlock>("PeerClock");
        _peerBroadcast = Required<TextBlock>("PeerBroadcast");
        _connectionHint = Required<TextBlock>("ConnectionHint");
        _lastReply = Required<TextBlock>("LastReply");
        _eventCount = Required<TextBlock>("EventCount");
        _historyEvents = Required<StackPanel>("HistoryEvents");
        _historyScroll = Required<ScrollViewer>("HistoryScroll");
        _historyChevron = Required<Avalonia.Controls.Shapes.Path>("HistoryChevron");

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += OnTick;
        _timer.Start();
        Closed += (_, _) =>
        {
            _closed = true;
            _timer.Stop();
            _timer.Tick -= OnTick;
            _shutdown.Cancel();
            _lastFileChange.Stop();
            _serverClock.Stop();
            _shutdown.Dispose();
        };
        RefreshClock();
        RefreshServiceState();
        _ = PollStatusAsync();
    }

    private T Required<T>(string name) where T : Control =>
        this.FindControl<T>(name)
        ?? throw new InvalidOperationException("Missing Studio component: " + name);

    private void OnTick(object? sender, EventArgs e)
    {
        RefreshClock();
        if (_ticks % 2 == 0)
            RefreshServiceState();
        if (_lastSnapshot is not null && _lastFileChange.IsRunning &&
            _lastFileChange.Elapsed > TimeSpan.FromSeconds(12))
            ShowUnavailable("Service status is not updating.");
        if (++_ticks % 2 == 0)
            _ = PollStatusAsync();
    }

    private void RefreshClock()
    {
        DateTimeOffset pcUtc = DateTimeOffset.UtcNow;
        _pcClock.Text = pcUtc.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        _pcDate.Text = pcUtc.ToString("ddd, dd MMM yyyy", CultureInfo.InvariantCulture);
        if (_lastSampleValid && _baseServerUtc is DateTimeOffset reference &&
            _lastFileChange.Elapsed <= TimeSpan.FromSeconds(12))
        {
            DateTimeOffset estimate = reference + _serverClock.Elapsed;
            _peerClock.Text = estimate.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            ToolTip.SetTip(_peerClock, estimate.ToString("ddd, dd MMM yyyy HH:mm:ss 'UTC'",
                CultureInfo.InvariantCulture));
        }
        else
            _peerClock.Text = "—";
    }

    private async Task PollStatusAsync()
    {
        if (_closed || _reading) return;
        _reading = true;
        try
        {
            if (!_configuredEnabled)
            {
                ShowUnavailable(_serviceInstalled
                    ? "NTP is turned off."
                    : "NTP service is not installed.");
                return;
            }
            var info = new FileInfo(_statusPath);
            if (!info.Exists)
            {
                ShowUnavailable("Sync service not detected.");
                return;
            }
            if (info.Length > 64 * 1024)
            {
                ShowUnavailable("Service data unavailable.");
                return;
            }
            string json = await File.ReadAllTextAsync(_statusPath, _shutdown.Token);
            if (_closed) return;
            if (!DashboardStatus.TryParse(json, out var snap, out _) || snap is null)
            {
                ShowUnavailable("Service data unavailable.");
                return;
            }

            if (!string.Equals(json, _lastSnapshot, StringComparison.Ordinal))
            {
                _lastSnapshot = json;
                _lastFileChange.Restart();
            }

            if (_lastFileChange.Elapsed > TimeSpan.FromSeconds(12))
            {
                ShowUnavailable("Waiting for a fresh NTP update.");
                return;
            }

            // The worker writes its snapshot in local PC wall time; the PC may
            // jump by years between two valid replies. Never use updated-UTC
            // subtraction to pronounce a fresh snapshot offline.
            _lastKnownServer = snap.Server;
            _peerAddress.Text = snap.Server;
            bool valid = snap.OffsetMilliseconds is not null &&
                snap.EstimatedServerUtc is not null;
            bool synced = valid && snap.State == "Synchronized";
            _lastSampleValid = valid;

            _globalLamp.Foreground = BrushResolver.Require(this,
                valid ? "ArBrush.Success" : "ArBrush.Warning");
            _globalStatus.Text = valid ? "Connected" : "No reply";
            _syncLamp.Foreground = BrushResolver.Require(this,
                synced ? "ArBrush.Success" : "ArBrush.Warning");
            _syncState.Text = synced ? "Synced" : valid ? "Offset" : "No reply";
            _syncDetail.Text = synced || valid && snap.OffsetMilliseconds is double
                ? $"{snap.Server}  ·  {FormatOffset(snap.OffsetMilliseconds!.Value)}"
                : "Waiting for NTP response";
            _peerHealth.Text = valid ? "● Online" : "● No reply";
            _peerHealth.Foreground = BrushResolver.Require(this,
                valid ? "ArBrush.Success" : "ArBrush.Warning");
            _peerStratum.Text = valid && snap.Stratum.HasValue
                ? snap.Stratum.Value.ToString(CultureInfo.InvariantCulture) : "—";
            _peerOffset.Text = valid && snap.OffsetMilliseconds is double offset
                ? FormatOffset(offset) : "—";
            _peerBroadcast.Text = "—";
            _connectionHint.Text = valid
                ? $"Valid {snap.AcceptedCount} / {snap.RequestCount}  ·  RTT {snap.RoundTripMilliseconds:0.0} ms"
                : $"No reply  ·  {snap.State}";
            _lastReply.Text = valid
                ? "Last " + snap.UpdatedUtc.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
                : "—";

            _baseServerUtc = null;
            _serverClock.Reset();
            if (valid && snap.EstimatedServerUtc is DateTimeOffset server)
            {
                // Drift since snapshot uses wall-time only for small differences.
                double age = (DateTimeOffset.UtcNow - snap.UpdatedUtc).TotalSeconds;
                if (age is >= 0 and <= 12)
                    server = server.AddSeconds(age);
                _baseServerUtc = server;
                _serverClock.Start();
            }

            UpdateHistory(snap);
            RefreshClock();
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ArgumentException or InvalidOperationException)
        {
            if (!_closed) ShowUnavailable("Unable to read service status.");
        }
        finally
        {
            _reading = false;
        }
    }

    private void UpdateHistory(DashboardStatus snap)
    {
        var events = (snap.RecentCorrections ?? Array.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .TakeLast(16).Reverse().ToList();
        // An unreachable server has no correction events. Preserve the actual
        // last error in the expanded History instead of showing an empty panel.
        if (!string.IsNullOrWhiteSpace(snap.LastEvent) &&
            !events.Any(entry => entry.Contains(snap.LastEvent,
                StringComparison.Ordinal)))
            events.Insert(0, snap.LastEvent);
        if (events.Count > 16) events.RemoveAt(events.Count - 1);
        string signature = string.Join("\n", events);
        if (string.Equals(signature, _lastEventsSignature, StringComparison.Ordinal))
            return;
        _lastEventsSignature = signature;
        _eventCount.Text = events.Count == 1 ? "1 event" : $"{events.Count} events";
        _historyEvents.Children.Clear();
        if (events.Count == 0)
        {
            _historyEvents.Children.Add(new TextBlock
            {
                Text = "No events yet.",
                Foreground = BrushResolver.Require(this, "ArBrush.TextSecondary")
            });
            return;
        }
        foreach (string evt in events)
        {
            _historyEvents.Children.Add(new TextBlock
            {
                Text = evt,
                TextWrapping = TextWrapping.Wrap,
                Foreground = BrushResolver.Require(this, "ArBrush.TextSecondary")
            });
        }
    }

    private void ShowUnavailable(string message)
    {
        _lastSampleValid = false;
        _baseServerUtc = null;
        _serverClock.Reset();
        _peerAddress.Text = _lastKnownServer;
        _globalLamp.Foreground = BrushResolver.Require(this, "ArBrush.Warning");
        _globalStatus.Text = _configuredEnabled ? "No reply" : "Disabled";
        _syncLamp.Foreground = BrushResolver.Require(this, "ArBrush.Warning");
        _syncState.Text = _configuredEnabled ? "Not synced"
            : _serviceInstalled ? "Disabled" : "Not installed";
        _syncDetail.Text = message;
        _peerHealth.Text = "● Offline";
        _peerHealth.Foreground = BrushResolver.Require(this, "ArBrush.Warning");
        _peerStratum.Text = "—";
        _peerOffset.Text = "—";
        _peerBroadcast.Text = "—";
        _peerClock.Text = "—";
        _connectionHint.Text = message;
        _lastReply.Text = "—";
    }

    private static string FormatOffset(double value)
    {
        if (Math.Abs(value) >= TimeSpan.FromDays(365).TotalMilliseconds)
        {
            double years = value / TimeSpan.FromDays(365.2425).TotalMilliseconds;
            return years.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + " y";
        }
        return value.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture) + " ms";
    }

    private void History_OnClick(object? sender, RoutedEventArgs e)
    {
        _historyExpanded = !_historyExpanded;
        _historyScroll.IsVisible = _historyExpanded;
        _historyChevron.Data = Geometry.Parse(_historyExpanded ? ChevronUp : ChevronDown);
    }

    private void RefreshServiceState()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\ARFastSNTP");
            bool exists = key is not null;
            _serviceInstalled = exists;
            int startMode = exists ? Convert.ToInt32(key!.GetValue("Start", 3)) : 4;
            _configuredEnabled = exists && startMode != 4;
            _sourceToggle.IsEnabled = exists && !_sourceActionPending;
            _switchIcon.Data = Geometry.Parse(_configuredEnabled ? SwitchOn : SwitchOff);
            _switchIcon.Fill = BrushResolver.Require(this,
                _configuredEnabled ? "ArBrush.Success" : "ArBrush.TextDim");
            ToolTip.SetTip(_sourceToggle, exists
                ? _configuredEnabled ? "Disable NTP source" : "Enable NTP source"
                : "Install the NTP service first");
            if (!_configuredEnabled && exists && !_sourceActionPending)
            {
                _globalStatus.Text = "Disabled";
                _syncState.Text = "Disabled";
                _syncDetail.Text = "NTP is turned off";
                _peerHealth.Text = "● Disabled";
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException
            or System.Security.SecurityException)
        {
            _sourceToggle.IsEnabled = false;
        }
    }

    private async void ToggleSource_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_sourceActionPending) return;
        string exe = System.IO.Path.GetFullPath(System.IO.Path.Combine(
            AppContext.BaseDirectory, "..", "setup", "ARFastSNTP.Setup.exe"));
        if (!File.Exists(exe))
        {
            _connectionHint.Text = "NTP service setup is missing. Run the installer again.";
            return;
        }
        _sourceActionPending = true;
        _sourceToggle.IsEnabled = false;
        bool enable = !_configuredEnabled;
        _connectionHint.Text = enable ? "Enabling NTP..." : "Disabling NTP...";
        try
        {
            using var child = Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = System.IO.Path.GetDirectoryName(exe)!,
                UseShellExecute = true,
                Verb = "runas",
                Arguments = enable ? "--source-enable" : "--source-disable"
            }) ?? throw new InvalidOperationException("NTP control could not start.");
            await child.WaitForExitAsync(_shutdown.Token);
            if (_closed) return;
            _connectionHint.Text = child.ExitCode == 0
                ? enable ? "NTP enabled" : "NTP disabled"
                : "Could not change NTP. Check service permissions.";
            _lastSnapshot = null;
            _ = PollStatusAsync();
        }
        catch (Win32Exception)
        {
            _connectionHint.Text = "Windows Administrator approval was cancelled.";
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            _connectionHint.Text = "Could not change NTP: " + ex.Message;
        }
        finally
        {
            _sourceActionPending = false;
            if (!_closed) RefreshServiceState();
        }
    }

    private void Refresh_OnClick(object? sender, RoutedEventArgs e) =>
        _ = PollStatusAsync();
}
