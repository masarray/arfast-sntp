using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using ARFastSNTP;
using ARFastSNTP.Dashboard;
using ARFastSNTP.Studio.Design;

namespace ARFastSNTP.Studio;

/// <summary>
/// Compact view of the service-owned catalog. GUI reads bounded status and
/// sends fixed local pipe commands only; it never opens NTP sockets, writes
/// ProgramData configuration, restarts SCM or changes Windows time.
/// </summary>
public sealed partial class MainWindow : Window
{
    public ObservableCollection<PeerRowModel> Rows { get; } = new();
    private readonly Grid _mainLayout;
    private readonly Grid _historyPanel;
    private readonly TextBlock _pcClock;
    private readonly TextBlock _pcDate;
    private readonly TextBlock _syncState;
    private readonly TextBlock _syncLamp;
    private readonly TextBlock _serverCount;
    private readonly TextBlock _hint;
    private readonly TextBlock _eventCount;
    private readonly StackPanel _history;
    private readonly ScrollViewer _historyScroll;
    private readonly Avalonia.Controls.Shapes.Path _chevron;
    private readonly Button _add;
    private readonly DispatcherTimer _timer;
    private readonly CancellationTokenSource _closedToken = new();
    private readonly Stopwatch _freshness = new();
    private readonly string _statusPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ARFastSNTP", "status.json");

    private readonly string _diagnosticPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ARFastSNTP", "status.diagnostics.json");

    private NtpPeer[] _config = Array.Empty<NtpPeer>();
    private bool _catalogReady;
    private bool _catalogLoading;
    private bool _reading;
    private bool _editing;
    private bool _closed;
    private bool _historyOpen;
    private int _ticks;
    private string? _lastJson;
    private string? _lastEvents;
    private string _timezone = "";
    private static readonly string ChevronDown = "m480-340 180-180-57-56-123 123-123-123-57 56 180 180Zm0 260q-83 0-156-31.5T197-197q-54-54-85.5-127T80-480q0-83 31.5-156T197-763q54-54 127-85.5T480-880q83 0 156 31.5T763-763q54 54 85.5 127T880-480q0 83-31.5 156T763-197q-54 54-127 85.5T480-80Zm0-80q134 0 227-93t93-227q0-134-93-227t-227-93q-134 0-227 93t-93 227q0 134 93 227t227 93Zm0-320Z";
    private static readonly string ChevronUp = "m357-384 123-123 123 123 57-56-180-180-180 180 57 56ZM480-80q-83 0-156-31.5T197-197q-54-54-85.5-127T80-480q0-83 31.5-156T197-763q54-54 127-85.5T480-880q83 0 156 31.5T763-763q54 54 85.5 127T880-480q0 83-31.5 156T763-197q-54 54-127 85.5T480-80Zm0-80q134 0 227-93t93-227q0-134-93-227t-227-93q-134 0-227 93t-93 227q0 134 93 227t227 93Zm0-320Z";

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = this;
        _mainLayout = Require<Grid>("MainLayout");
        _historyPanel = Require<Grid>("HistoryPanel");
        _pcClock = Require<TextBlock>("PcClock");
        _pcDate = Require<TextBlock>("PcDate");
        _syncState = Require<TextBlock>("SyncState");
        _syncLamp = Require<TextBlock>("SyncLamp");
        _serverCount = Require<TextBlock>("ListCount");
        _hint = Require<TextBlock>("ConnectionHint");
        _eventCount = Require<TextBlock>("EventCount");
        _history = Require<StackPanel>("HistoryEvents");
        _historyScroll = Require<ScrollViewer>("HistoryScroll");
        _chevron = Require<Avalonia.Controls.Shapes.Path>("HistoryChevron");
        _add = Require<Button>("AddServerButton");
        ApplyWindowLayout();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTick;
        _timer.Start();
        Closed += (_, _) =>
        {
            _closed = true;
            _timer.Stop();
            _timer.Tick -= OnTick;
            _closedToken.Cancel();
        };
        UpdatePcClock();
        _ = RefreshCatalogAsync();
        _ = RefreshStatusAsync();
    }

    private T Require<T>(string name) where T : Control =>
        this.FindControl<T>(name) ??
        throw new InvalidOperationException("Missing component: " + name);

    private static void Set(TextBlock target, string text)
    {
        if (target.Text != text) target.Text = text;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        UpdatePcClock();
        foreach (var row in Rows) row.Tick();
        _ticks++;
        if (_ticks % 2 == 0) _ = RefreshStatusAsync();
        if (_ticks % 5 == 0) _ = RefreshCatalogAsync();
        if (_freshness.IsRunning && _freshness.Elapsed > TimeSpan.FromSeconds(15))
            SetSync("Service offline", false);
    }

    private void UpdatePcClock()
    {
        DateTimeOffset now = DateTimeOffset.Now;
        Set(_pcClock, now.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
        Set(_pcDate, now.ToString("dd MMM yyyy", CultureInfo.InvariantCulture));
        string zone = now.ToString("zzz", CultureInfo.InvariantCulture);
        if (_timezone != zone)
        {
            _timezone = zone;
            ToolTip.SetTip(_pcClock, "Windows local time · UTC" + zone);
        }
    }

    private async Task RefreshCatalogAsync()
    {
        if (_closed || _catalogLoading || _editing) return;
        _catalogLoading = true;
        try
        {
            var response = await PeerControlProtocol.SendAsync(
                "list", null, _closedToken.Token);
            if (_closed) return;
            _catalogReady = response.Ok;
            if (response.Ok)
            {
                _config = response.Peers ?? Array.Empty<NtpPeer>();
                ReconcileRows();
            }
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or
            UnauthorizedAccessException or InvalidDataException or
            OperationCanceledException)
        {
            if (_closed) return;
            _catalogReady = false;
            _hint.IsVisible = Rows.Count == 0;
            if (Rows.Count == 0)
                Set(_hint, "Waiting for NTP service...");
        }
        finally
        {
            _catalogLoading = false;
            if (!_closed) RefreshButtons();
        }
    }

    private void ReconcileRows()
    {
        if (_closed) return;
        var addresses = _config.Select(p => p.Address).ToArray();
        for (int i = Rows.Count - 1; i >= 0; i--)
            if (!addresses.Contains(Rows[i].Address, StringComparer.Ordinal))
                Rows.RemoveAt(i);

        for (int i = 0; i < _config.Length; i++)
        {
            var entry = _config[i];
            var row = Rows.FirstOrDefault(r => r.Address == entry.Address);
            if (row is null)
            {
                row = new PeerRowModel(entry.Address, i + 1, entry.Enabled,
                    BrushResolver.Require(this, "ArBrush.Success"),
                    BrushResolver.Require(this, "ArBrush.TextDim"),
                    BrushResolver.Require(this, "ArBrush.StrokeStrong"),
                    BrushResolver.Require(this, "ArBrush.Text"),
                    BrushResolver.Require(this, "ArBrush.Warning"));
                Rows.Insert(i, row);
            }
            else if (Rows.IndexOf(row) != i)
                Rows.Move(Rows.IndexOf(row), i);
            row.SetIndex(i + 1);
            row.SetEnabled(entry.Enabled);
        }
        Set(_serverCount, Rows.Count == 1 ? "1 server" : Rows.Count + " servers");
        ApplyWindowLayout();
        RefreshButtons();
    }

    private void RefreshButtons()
    {
        _add.IsEnabled = _catalogReady && !_editing &&
            _config.Length < 16;
        foreach (var row in Rows)
            row.SetCanChange(_catalogReady && !_editing);
    }

    private async Task RefreshStatusAsync()
    {
        if (_closed || _reading || _editing) return;
        _reading = true;
        try
        {
            var info = new FileInfo(_statusPath);
            if (!info.Exists || info.Length is <= 0 or > 131072)
            {
                SetSync("Service offline", false);
                return;
            }
            string json = await File.ReadAllTextAsync(_statusPath, _closedToken.Token);
            if (_closed) return;
            if (!DashboardStatus.TryParse(json, out var snap, out _) || snap is null)
            {
                SetSync("Status unavailable", false);
                return;
            }
            if (json == _lastJson) return;
            _lastJson = json;
            _freshness.Restart();
            if (snap.Peers.Length > 0)
            {
                if (!_catalogReady)
                {
                    _config = snap.Peers.Select(p => new NtpPeer(p.Address, p.Enabled)).ToArray();
                    ReconcileRows();
                }
                foreach (var row in Rows)
                {
                    var peer = snap.Peers.FirstOrDefault(p => p.Address == row.Address);
                    if (peer is not null)
                        row.Apply(peer);
                }
            }
            else if (!_catalogReady && Rows.Count == 0 &&
                IPAddress.TryParse(snap.Server, out _))
            {
                _config = new[] { new NtpPeer(snap.Server, snap.SourceEnabled) };
                ReconcileRows();
            }
            bool verified = snap.State == "Synchronized" &&
                !string.IsNullOrEmpty(snap.SelectedServer ?? snap.Server) &&
                snap.OffsetMilliseconds.HasValue &&
                snap.EstimatedServerUtc.HasValue;
            string global = verified ? snap.Degraded ? "Synced (fallback)" : "Synced"
                : snap.State switch
                {
                    "NTP off" or "Disabled" => "NTP off",
                    "Source conflict" => "Server conflict",
                    "Correcting" => "Correcting time",
                    "No reply" => "No reply",
                    "Local target" => "Local target",
                    "Invalid reply" => "Invalid reply",
                    "Invalid sample" => "Invalid sample",
                    "Network error" => "Network error",
                    "Rate limited" => "Rate limited",
                    "Denied" => "Server denied",
                    _ => "Not synced"
                };
            SetSync(global, verified);
            if (snap.State is "Source conflict" or "No reply" or "Local target" or "Invalid reply" or
                "Invalid sample" or "Network error" or "Rate limited" or "Denied" or
                "Correction failed")
            {
                _hint.IsVisible = true;
                Set(_hint, snap.State switch
                {
                    "Source conflict" => "Servers disagree on time. Correction paused.",
                    "No reply" => "No UDP response at Windows socket. Check IP and port 123.",
                    "Local target" => "NTP server IP is on this PC. RAW Npcap may not loop back to UDP socket.",
                    "Invalid reply" => "UDP reply arrived, but NTP checks rejected it. See row tooltip.",
                    "Invalid sample" => "NTP samples were inconsistent. See row tooltip.",
                    "Network error" => "Windows UDP transport error. See row tooltip.",
                    "Rate limited" => "NTP server requested slower polling.",
                    "Denied" => "NTP server refused this client.",
                    _ => "Windows did not accept the time correction."
                });
            }
            else _hint.IsVisible = false;
            UpdateHistory(snap);
        }
        catch (OperationCanceledException) when (_closed) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            InvalidOperationException)
        {
            if (!_closed) SetSync("Service offline", false);
        }
        finally { _reading = false; }
    }

    private void SetSync(string state, bool synced)
    {
        Set(_syncState, state);
        var brush = BrushResolver.Require(this,
            synced ? "ArBrush.Success" : "ArBrush.Warning");
        if (!ReferenceEquals(_syncLamp.Foreground, brush))
            _syncLamp.Foreground = brush;
    }

    private void UpdateHistory(DashboardStatus snap)
    {
        var events = (snap.RecentCorrections ?? Array.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .TakeLast(16).Reverse().ToList();
        string key = string.Join("\n", events);
        if (_lastEvents == key) return;
        _lastEvents = key;
        Set(_eventCount, events.Count == 1 ? "1 event" : events.Count + " events");
        _history.Children.Clear();
        if (events.Count == 0) events.Add("No events yet.");
        foreach (string original in events)
        {
            var (stamp, message) = EventDisplay.Format(original);
            if (stamp.Length == 0)
            {
                _history.Children.Add(new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    Foreground = BrushResolver.Require(this, "ArBrush.TextSecondary")
                });
                continue;
            }
            // One calm timestamp column; keep high-resolution raw event in
            // tooltip for engineering diagnostics, not visible prose.
            var line = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("66,*"),
                ColumnSpacing = 10
            };
            line.Children.Add(new TextBlock
            {
                Text = stamp,
                FontSize = 11,
                Foreground = BrushResolver.Require(this, "ArBrush.TextDim")
            });
            var description = new TextBlock
            {
                Text = message,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = BrushResolver.Require(this, "ArBrush.TextSecondary")
            };
            Grid.SetColumn(description, 1);
            ToolTip.SetTip(description, original);
            line.Children.Add(description);
            _history.Children.Add(line);
        }
    }

    private async Task ChangePeerAsync(string action, string address)
    {
        if (_closed || _editing || !_catalogReady) return;
        _editing = true;
        RefreshButtons();
        try
        {
            var response = await PeerControlProtocol.SendAsync(
                action, address, _closedToken.Token);
            if (_closed) return;
            if (!response.Ok)
            {
                _hint.IsVisible = true;
                Set(_hint, response.Error ?? "NTP server change was refused.");
                return;
            }
            _config = response.Peers;
            ReconcileRows();
            _lastJson = null;
            _ = RefreshStatusAsync();
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or
            UnauthorizedAccessException or InvalidDataException or
            OperationCanceledException)
        {
            if (!_closed)
            {
                _hint.IsVisible = true;
                Set(_hint, "Cannot contact NTP service. Try Refresh.");
            }
        }
        finally
        {
            _editing = false;
            if (!_closed) RefreshButtons();
        }
    }

    private async void ExportDiagnostics_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_closed) return;
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save NTP diagnostics",
                SuggestedFileName = "ARFastSNTP-Diagnostics-" +
                    DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".json",
                DefaultExtension = "json",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("JSON diagnostics") { Patterns = new[] { "*.json" } }
                },
                ShowOverwritePrompt = true
            });
            if (file is null || _closed) return;
            // Persist the authoritative bounded service snapshot verbatim.
            // Never export per-user secrets, registry keys or unrelated logs.
            var info = new FileInfo(_statusPath);
            if (!info.Exists || info.Length is <= 0 or > 131072)
                throw new InvalidDataException("Service diagnostics are not available.");
            string json = await File.ReadAllTextAsync(_statusPath, _closedToken.Token);
            if (json.Length == 0 || !DashboardStatus.TryParse(json, out _, out _))
                throw new InvalidDataException("Service diagnostics are not valid.");
            string decisionJournal = "[]";
            var journalInfo = new FileInfo(_diagnosticPath);
            if (journalInfo.Exists)
            {
                if (journalInfo.Length is <= 0 or > 131072)
                    throw new InvalidDataException("The diagnostic journal is invalid or too large.");
                decisionJournal = await File.ReadAllTextAsync(
                    _diagnosticPath, _closedToken.Token);
            }
            using var statusDoc = JsonDocument.Parse(json);
            using var decisionsDoc = JsonDocument.Parse(decisionJournal);
            if (decisionsDoc.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("The diagnostic journal format is invalid.");
            string exported = JsonSerializer.Serialize(new
            {
                FormatVersion = 1,
                ExportedUtc = DateTimeOffset.UtcNow,
                LiveStatus = statusDoc.RootElement,
                DecisionHistory = decisionsDoc.RootElement
            }, new JsonSerializerOptions { WriteIndented = true });
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            await writer.WriteAsync(exported.AsMemory(), _closedToken.Token);
        }
        catch (OperationCanceledException) when (_closed) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            InvalidDataException or InvalidOperationException or JsonException)
        {
            if (!_closed)
            {
                _hint.IsVisible = true;
                Set(_hint, "Could not save diagnostics. Check the location and try again.");
            }
        }
    }

    private async void AddServer_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!_catalogReady || _editing) return;
        string? ip = await new AddPeerDialog(this,
            Rows.Select(row => row.Address).ToHashSet(StringComparer.Ordinal))
            .ShowDialog<string?>(this);
        if (!string.IsNullOrWhiteSpace(ip))
            await ChangePeerAsync("add", ip);
    }

    private async void PeerToggle_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: PeerRowModel row })
            await ChangePeerAsync(row.Enabled ? "disable" : "enable", row.Address);
    }

    private async void PeerDelete_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: PeerRowModel row } || _editing) return;
        bool remove = await new RemovePeerDialog(this, row.Address)
            .ShowDialog<bool>(this);
        if (remove)
            await ChangePeerAsync("delete", row.Address);
    }

    private void Refresh_OnClick(object? sender, RoutedEventArgs e)
    {
        _lastJson = null;
        _ = RefreshCatalogAsync();
        _ = RefreshStatusAsync();
    }

    private void History_OnClick(object? sender, RoutedEventArgs e)
    {
        _historyOpen = !_historyOpen;
        _historyScroll.IsVisible = _historyOpen;
        _chevron.Data = Geometry.Parse(_historyOpen ? ChevronUp : ChevronDown);
        ApplyWindowLayout();
    }

    /// <summary>
    /// In collapsed mode, window height fits visible server rows and is
    /// non-shrinkable vertically; horizontal resize still works. Expanded
    /// mode allocates spare height to history using a star Grid row.
    /// Each viewport scrolls independently and table controls never clip.
    /// All measurements use Avalonia device-independent pixels.
    /// </summary>
    private void ApplyWindowLayout()
    {
        // Compact chrome + table headers + hint allowance.
        // Rows are 44 DIP plus 2 DIP inter-row space; after seven, scroll.
        double closedHeight = 242 + Math.Clamp(Rows.Count, 1, 7) * 46;
        const double maxExpandedHeight = 830;
        if (_historyOpen)
        {
            _mainLayout.RowDefinitions = new RowDefinitions("32,Auto,*");
            _historyPanel.RowDefinitions = new RowDefinitions("32,*");
            double minimum = Math.Min(maxExpandedHeight, closedHeight + 92);
            double desired = Math.Min(maxExpandedHeight, closedHeight + 216);
            MaxHeight = maxExpandedHeight;
            MinHeight = minimum;
            if (Height < desired) Height = desired;
        }
        else
        {
            _mainLayout.RowDefinitions = new RowDefinitions("32,*,Auto");
            _historyPanel.RowDefinitions = new RowDefinitions("32,Auto");
            MaxHeight = double.PositiveInfinity;
            MinHeight = closedHeight;
            Height = closedHeight;
            MaxHeight = closedHeight;
        }
    }
}
