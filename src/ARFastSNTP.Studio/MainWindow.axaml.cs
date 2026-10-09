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
    private readonly TextBlock _syncLamp;
    private readonly TextBlock _syncState;
    private readonly TextBlock _peerAddress;
    private readonly Avalonia.Controls.Shapes.Path _serverIcon;
    private readonly Border _sourceTrack;
    private readonly Avalonia.Controls.Shapes.Ellipse _sourceThumb;
    private readonly Button _sourceToggle;
    private readonly TextBlock _peerHealth;
    private readonly TextBlock _peerStratum;
    private readonly TextBlock _peerOffset;
    private readonly TextBlock _peerClock;
    private readonly TextBlock _peerBroadcast;
    private readonly TextBlock _connectionHint;
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
    private double _heightBeforeHistory;
    private bool _configuredEnabled;
    private bool _serviceInstalled;
    private bool _serviceStateKnown;
    private bool _sourceActionPending;
    private string? _lastUnavailableKey;
    private string? _displayedTimezone;
    private static readonly string ChevronDown =
        "m480-340 180-180-57-56-123 123-123-123-57 56 180 180Zm0 260q-83 0-156-31.5T197-197q-54-54-85.5-127T80-480q0-83 31.5-156T197-763q54-54 127-85.5T480-880q83 0 156 31.5T763-763q54 54 85.5 127T880-480q0 83-31.5 156T763-197q-54 54-127 85.5T480-80Zm0-80q134 0 227-93t93-227q0-134-93-227t-227-93q-134 0-227 93t-93 227q0 134 93 227t227 93Zm0-320Z";
    private static readonly string ChevronUp =
        "m357-384 123-123 123 123 57-56-180-180-180 180 57 56ZM480-80q-83 0-156-31.5T197-197q-54-54-85.5-127T80-480q0-83 31.5-156T197-763q54-54 127-85.5T480-880q83 0 156 31.5T763-763q54 54 85.5 127T880-480q0 83-31.5 156T763-197q-54 54-127 85.5T480-80Zm0-80q134 0 227-93t93-227q0-134-93-227t-227-93q-134 0-227 93t-93 227q0 134 93 227t227 93Zm0-320Z";

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        _pcClock = Required<TextBlock>("PcClock");
        _pcDate = Required<TextBlock>("PcDate");
        _syncLamp = Required<TextBlock>("SyncLamp");
        _syncState = Required<TextBlock>("SyncState");
        _peerAddress = Required<TextBlock>("PeerAddress");
        _serverIcon = Required<Avalonia.Controls.Shapes.Path>("ServerIcon");
        _sourceTrack = Required<Border>("SourceTrack");
        _sourceThumb = Required<Avalonia.Controls.Shapes.Ellipse>("SourceThumb");
        _sourceToggle = Required<Button>("SourceToggle");
        _peerHealth = Required<TextBlock>("PeerHealth");
        _peerStratum = Required<TextBlock>("PeerStratum");
        _peerOffset = Required<TextBlock>("PeerOffset");
        _peerClock = Required<TextBlock>("PeerClock");
        _peerBroadcast = Required<TextBlock>("PeerBroadcast");
        _connectionHint = Required<TextBlock>("ConnectionHint");
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
        if (++_ticks % 2 != 0) return; // one status tick per second
        RefreshServiceState();
        if (_lastSnapshot is not null && _lastFileChange.IsRunning &&
            _lastFileChange.Elapsed > TimeSpan.FromSeconds(12))
            ShowUnavailable("NTP data is out of date.");
        // Never suspend polling when stale: this is how the monitor recovers
        // automatically when the worker/service resumes producing snapshots.
        _ = PollStatusAsync();
    }

    // Avoid reassigning identical Text properties on every timer tick.
    // A stable status label is not an animation and should not flicker.
    private static void SetText(TextBlock target, string value)
    {
        if (!string.Equals(target.Text, value, StringComparison.Ordinal))
            target.Text = value;
    }

    private void RefreshClock()
    {
        // Display what the operator sees in Windows Settings. All SNTP math
        // remains UTC, and the timezone is a visual-only conversion.
        DateTimeOffset pcLocal = DateTimeOffset.Now;
        SetText(_pcClock, pcLocal.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
        SetText(_pcDate, pcLocal.ToString("ddd, dd MMM yyyy",
            CultureInfo.InvariantCulture));
        string timezone = pcLocal.ToString("zzz", CultureInfo.InvariantCulture);
        if (!string.Equals(timezone, _displayedTimezone, StringComparison.Ordinal))
        {
            _displayedTimezone = timezone;
            ToolTip.SetTip(_pcClock, "Local Windows time · UTC" + timezone);
        }
        if (_lastSampleValid && _baseServerUtc is DateTimeOffset reference &&
            _lastFileChange.Elapsed <= TimeSpan.FromSeconds(12))
        {
            DateTimeOffset estimate = reference + _serverClock.Elapsed;
            var local = estimate.ToLocalTime();
            string next = local.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            if (!string.Equals(_peerClock.Text, next, StringComparison.Ordinal))
            {
                SetText(_peerClock, next);
                ToolTip.SetTip(_peerClock, "Server: " +
                    local.ToString("ddd, dd MMM yyyy HH:mm:ss zzz",
                        CultureInfo.InvariantCulture));
            }
        }
        else
            SetText(_peerClock, "—");
    }

    private async Task PollStatusAsync()
    {
        if (_closed || _reading || _sourceActionPending) return;
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

            bool changed = !string.Equals(json, _lastSnapshot, StringComparison.Ordinal);
            if (changed)
            {
                _lastSnapshot = json;
                _lastFileChange.Restart();
            }

            if (_lastFileChange.Elapsed > TimeSpan.FromSeconds(12))
            {
                ShowUnavailable("NTP data is out of date.");
                return;
            }
            if (!changed && _lastUnavailableKey is null)
                return; // no data change -> no label, brush or clock rebinding
            _lastUnavailableKey = null;

            // The worker writes its snapshot in local PC wall time; the PC may
            // jump by years between two valid replies. Never use updated-UTC
            // subtraction to pronounce a fresh snapshot offline.
            _lastKnownServer = snap.Server;
            SetText(_peerAddress, snap.Server);
            var view = StudioStatusPresenter.Create(snap);
            _lastSampleValid = view.ValidReply;
            SetText(_syncState, view.PcSync);
            SetText(_peerHealth, view.ServerHealth);
            SetText(_peerStratum, view.Stratum);
            SetText(_peerOffset, view.ServerOffset);
            SetText(_peerBroadcast, "—");
            SetText(_connectionHint, view.Footer);
            _connectionHint.IsVisible = !string.IsNullOrWhiteSpace(view.Footer);
            var syncColor = BrushResolver.Require(this,
                view.Synchronized ? "ArBrush.Success" : "ArBrush.Warning");
            if (!ReferenceEquals(_syncLamp.Foreground, syncColor))
                _syncLamp.Foreground = syncColor;
            var peerColor = BrushResolver.Require(this,
                view.ValidReply ? "ArBrush.Success" : "ArBrush.Warning");
            if (!ReferenceEquals(_peerHealth.Foreground, peerColor))
                _peerHealth.Foreground = peerColor;

            if (view.ValidReply && snap.EstimatedServerUtc is DateTimeOffset server)
            {
                // Rebase a monotonic clock only for a genuine discontinuity.
                // Rebinding every 1s NTP status write made seconds stall/jump.
                double age = (DateTimeOffset.UtcNow - snap.UpdatedUtc).TotalSeconds;
                if (age is >= 0 and <= 12)
                    server = server.AddSeconds(age);
                DateTimeOffset estimatedNow = _baseServerUtc is DateTimeOffset baseUtc
                    ? baseUtc + _serverClock.Elapsed
                    : server;
                if (_baseServerUtc is null || !_serverClock.IsRunning ||
                    Math.Abs((server - estimatedNow).TotalSeconds) >= 0.8)
                {
                    _baseServerUtc = server;
                    _serverClock.Restart();
                }
            }
            else
            {
                _baseServerUtc = null;
                _serverClock.Reset();
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
        string key = (_configuredEnabled ? "on" : "off") + "|" +
            (_serviceInstalled ? "installed" : "missing") + "|" + message;
        if (string.Equals(_lastUnavailableKey, key, StringComparison.Ordinal))
            return; // do not repaint an unchanged failure every second
        _lastUnavailableKey = key;
        _lastSampleValid = false;
        _baseServerUtc = null;
        _serverClock.Reset();
        SetText(_peerAddress, _lastKnownServer);
        SetText(_syncState, _configuredEnabled ? "Not synced"
            : _serviceInstalled ? "NTP off" : "Not installed");
        SetText(_peerHealth, _configuredEnabled ? "No data" : "Off");
        SetText(_peerStratum, "—");
        SetText(_peerOffset, "—");
        SetText(_peerBroadcast, "—");
        SetText(_peerClock, "—");
        SetText(_connectionHint, message);
        _connectionHint.IsVisible = true;
        var stateColor = BrushResolver.Require(this,
            _configuredEnabled ? "ArBrush.Warning" : "ArBrush.TextDim");
        if (!ReferenceEquals(_syncLamp.Foreground, stateColor))
            _syncLamp.Foreground = stateColor;
        if (!ReferenceEquals(_peerHealth.Foreground, stateColor))
            _peerHealth.Foreground = stateColor;
    }

    private void History_OnClick(object? sender, RoutedEventArgs e)
    {
        _historyExpanded = !_historyExpanded;
        _historyScroll.IsVisible = _historyExpanded;
        _historyChevron.Data = Geometry.Parse(_historyExpanded ? ChevronUp : ChevronDown);
        if (_historyExpanded)
        {
            _heightBeforeHistory = Height;
            Height = Math.Max(Height, 550);
        }
        else if (_heightBeforeHistory > 0)
        {
            Height = _heightBeforeHistory;
        }
    }

    private void RefreshServiceState()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\ARFastSNTP");
            bool installed = key is not null;
            int startMode = installed ? Convert.ToInt32(key!.GetValue("Start", 3)) : 4;
            bool enabled = installed && startMode != 4;
            _sourceToggle.IsEnabled = installed && !_sourceActionPending;

            // SCM start type seldom changes. Repaint only at the actual
            // transition; do not race status-reader results every second.
            if (_serviceStateKnown && installed == _serviceInstalled &&
                enabled == _configuredEnabled)
                return;

            _serviceStateKnown = true;
            _serviceInstalled = installed;
            _configuredEnabled = enabled;
            var green = BrushResolver.Require(this, "ArBrush.Success");
            var gray = BrushResolver.Require(this, "ArBrush.TextDim");
            _sourceTrack.Background = BrushResolver.Require(this,
                enabled ? "ArBrush.Success" : "ArBrush.StrokeStrong");
            _sourceThumb.HorizontalAlignment = enabled
                ? Avalonia.Layout.HorizontalAlignment.Right
                : Avalonia.Layout.HorizontalAlignment.Left;
            _sourceThumb.Fill = BrushResolver.Require(this,
                enabled ? "ArBrush.Canvas" : "ArBrush.Text");
            // Enabled is a configured source, NOT proof the server replied.
            _serverIcon.Fill = enabled ? green : gray;
            ToolTip.SetTip(_serverIcon, enabled ? "NTP source enabled" : "NTP source off");
            ToolTip.SetTip(_sourceToggle, installed
                ? enabled ? "Turn NTP off" : "Turn NTP on"
                : "Install AR Fast SNTP first");

            // Invalidate the cache only when the service is toggled.
            _lastSnapshot = null;
            _lastUnavailableKey = null;
            _lastFileChange.Reset();
            if (!enabled)
                ShowUnavailable(installed ? "Turn on NTP to sync time."
                    : "NTP service is not installed.");
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
            _lastUnavailableKey = null;
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
            if (!_closed)
            {
                RefreshServiceState();
                _ = PollStatusAsync();
            }
        }
    }

    private void Refresh_OnClick(object? sender, RoutedEventArgs e) =>
        _ = PollStatusAsync();
}
