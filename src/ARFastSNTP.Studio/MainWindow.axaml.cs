using System.Diagnostics;
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
    private readonly TextBlock _enabledState;
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
        _enabledState = Required<TextBlock>("EnabledState");
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
        _ = PollStatusAsync();
    }

    private T Required<T>(string name) where T : Control =>
        this.FindControl<T>(name)
        ?? throw new InvalidOperationException("Missing Studio component: " + name);

    private void OnTick(object? sender, EventArgs e)
    {
        RefreshClock();
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
                ShowUnavailable("Sync service is not responding.");
                return;
            }

            // The worker writes its snapshot in local PC wall time; the PC may
            // jump by years between two valid replies. Never use updated-UTC
            // subtraction to pronounce a fresh snapshot offline.
            _lastKnownServer = snap.Server;
            _peerAddress.Text = snap.Server;
            _enabledState.Text = "● On";
            _enabledState.Foreground = BrushResolver.Require(this, "ArBrush.Success");
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
            .TakeLast(16).Reverse().ToArray();
        string signature = string.Join("\n", events);
        if (string.Equals(signature, _lastEventsSignature, StringComparison.Ordinal))
            return;
        _lastEventsSignature = signature;
        _eventCount.Text = events.Length == 1 ? "1 event" : $"{events.Length} events";
        _historyEvents.Children.Clear();
        if (events.Length == 0)
        {
            _historyEvents.Children.Add(new TextBlock
            {
                Text = "No corrections yet.",
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
        _enabledState.Text = "—";
        _enabledState.Foreground = BrushResolver.Require(this, "ArBrush.TextDim");
        _globalLamp.Foreground = BrushResolver.Require(this, "ArBrush.Warning");
        _globalStatus.Text = "Unavailable";
        _syncLamp.Foreground = BrushResolver.Require(this, "ArBrush.Warning");
        _syncState.Text = "Offline";
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
        _historyChevron.Data = Geometry.Parse(_historyExpanded
            ? "M6 9l6 6 6-6" : "M9 18l6-6-6-6");
    }

    private void Refresh_OnClick(object? sender, RoutedEventArgs e) =>
        _ = PollStatusAsync();
}
