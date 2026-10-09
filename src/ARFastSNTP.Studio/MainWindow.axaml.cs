using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ARFastSNTP.Dashboard;
using ARFastSNTP.Studio.Design;

namespace ARFastSNTP.Studio;

/// <summary>
/// Read-only Avalonia presentation shell over the already verified Windows worker.
/// No socket, service or clock-set API is owned by the view.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly TextBlock _serverClock;
    private readonly TextBlock _serverDate;
    private readonly TextBlock _pcClock;
    private readonly TextBlock _pcDate;
    private readonly TextBlock _serviceLamp;
    private readonly TextBlock _serviceState;
    private readonly TextBlock _peerAddress;
    private readonly TextBlock _peerHealth;
    private readonly TextBlock _peerStratum;
    private readonly TextBlock _peerOffset;
    private readonly TextBlock _peerRtt;
    private readonly TextBlock _peerLastReply;
    private readonly TextBlock _syncHealth;
    private readonly TextBlock _description;
    private readonly TextBlock _counters;
    private readonly TextBlock _correctionEvent;
    private readonly TextBlock _footer;
    private readonly TextBox _serverInput;
    private readonly TextBox _yearInput;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _estimatedClock = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly string _statusFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ARFastSNTP", "status.json");

    private DashboardStatus? _latest;
    private DateTimeOffset? _estimatedServerAtRead;
    private bool _reading;
    private bool _closed;
    private int _ticks;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        _serverClock = Required<TextBlock>("ServerClock");
        _serverDate = Required<TextBlock>("ServerDate");
        _pcClock = Required<TextBlock>("PcClock");
        _pcDate = Required<TextBlock>("PcDate");
        _serviceLamp = Required<TextBlock>("ServiceLamp");
        _serviceState = Required<TextBlock>("ServiceState");
        _peerAddress = Required<TextBlock>("PeerAddress");
        _peerHealth = Required<TextBlock>("PeerHealth");
        _peerStratum = Required<TextBlock>("PeerStratum");
        _peerOffset = Required<TextBlock>("PeerOffset");
        _peerRtt = Required<TextBlock>("PeerRtt");
        _peerLastReply = Required<TextBlock>("PeerLastReply");
        _syncHealth = Required<TextBlock>("SyncHealth");
        _description = Required<TextBlock>("StatusDescription");
        _counters = Required<TextBlock>("Counters");
        _correctionEvent = Required<TextBlock>("CorrectionEvent");
        _footer = Required<TextBlock>("Footer");
        _serverInput = Required<TextBox>("ServerInput");
        _yearInput = Required<TextBox>("YearInput");

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += TimerTick;
        _timer.Start();
        Closed += (_, _) =>
        {
            _closed = true;
            _timer.Stop();
            _timer.Tick -= TimerTick;
            _shutdown.Cancel();
            _estimatedClock.Stop();
        };
        _ = ReadStatusAsync();
    }

    private T Required<T>(string name) where T : Control =>
        this.FindControl<T>(name) ??
        throw new InvalidOperationException("Missing Studio visual control: " + name);

    private void TimerTick(object? sender, EventArgs args)
    {
        DrawClocks();
        if (++_ticks % 2 == 0)
            _ = ReadStatusAsync();
    }

    private void DrawClocks()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        _pcClock.Text = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        _pcDate.Text = now.ToString("ddd, dd MMM yyyy", CultureInfo.InvariantCulture);

        // The server clock is a bounded *estimate* based on the last validated
        // snapshot and a monotonic Stopwatch, never on fabricated broadcast data.
        if (_latest is not null &&
            _latest.HasValidReply(now) &&
            _estimatedServerAtRead is DateTimeOffset baseServer)
        {
            DateTimeOffset estimate = baseServer + _estimatedClock.Elapsed;
            _serverClock.Text = estimate.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            _serverDate.Text = estimate.ToString("ddd, dd MMM yyyy", CultureInfo.InvariantCulture);
        }
        else
        {
            _serverClock.Text = "--:--:--";
            _serverDate.Text = "Waiting for a valid SNTP transaction";
        }
    }

    private async Task ReadStatusAsync()
    {
        if (_reading || _closed)
            return;
        _reading = true;
        try
        {
            var file = new FileInfo(_statusFile);
            // Guard read amplification by a malformed or untrusted status file.
            if (!file.Exists)
            {
                ShowUnavailable("No worker status. Install & Sync to start the Windows Service.");
                return;
            }
            if (file.Length > 64 * 1024)
            {
                ShowUnavailable("Status file exceeds the 64 KiB snapshot limit.");
                return;
            }
            string json = await File.ReadAllTextAsync(_statusFile, _shutdown.Token);
            if (!DashboardStatus.TryParse(json, out var snapshot, out string reason) ||
                snapshot is null)
            {
                ShowUnavailable(reason);
                return;
            }
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (!snapshot.IsFresh(now))
            {
                ShowUnavailable("Worker status is stale; waiting for a fresh validated reply.");
                return;
            }
            _latest = snapshot;
            bool valid = snapshot.HasValidReply(now);
            bool synchronized = snapshot.IsSynchronized(now);
            _serviceLamp.Foreground = BrushResolver.Require(this, "ArBrush.Success");
            _serviceState.Text = snapshot.StepEnabled
                ? "Service reporting · clock correction enabled"
                : "Worker reporting · monitor only";
            _peerAddress.Text = snapshot.Server;
            _serverInput.Text = _serverInput.IsKeyboardFocusWithin
                ? _serverInput.Text : snapshot.Server;
            _peerHealth.Text = valid ? "● VALID" : "● NO REPLY";
            _peerHealth.Foreground = BrushResolver.Require(this,
                valid ? "ArBrush.Success" : "ArBrush.Warning");
            _peerStratum.Text = snapshot.Stratum?.ToString(CultureInfo.InvariantCulture) ?? "—";
            _peerOffset.Text = valid && snapshot.OffsetMilliseconds is double offset
                ? FormatOffset(offset) : "—";
            _peerRtt.Text = valid && snapshot.RoundTripMilliseconds is double rtt
                ? rtt.ToString("0.0", CultureInfo.InvariantCulture) + " ms" : "—";
            _peerLastReply.Text = valid
                ? snapshot.UpdatedUtc.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " UTC"
                : "No validated reply";
            _syncHealth.Text = synchronized ? "● PC SYNCHRONIZED" :
                valid ? "● OFFSET DETECTED" : "● WAITING FOR SNTP REPLY";
            _syncHealth.Foreground = BrushResolver.Require(this,
                synchronized ? "ArBrush.Success" : "ArBrush.Warning");
            _description.Text = snapshot.StepEnabled
                ? "Clock correction is enabled in the service. A correction is verified only by a new SNTP sample."
                : "Worker is monitor-only. INSTALL & SYNC enables correction on the approved isolated test PC.";
            _counters.Text = $"Requests {snapshot.RequestCount}   ·   Valid {snapshot.AcceptedCount}" +
                $"   ·   Rejected {snapshot.RejectedCount}   ·   Engine: {snapshot.State}";
            _correctionEvent.Text = !string.IsNullOrWhiteSpace(snapshot.LatestEvent)
                ? snapshot.LatestEvent : "No clock correction event yet.";
            _footer.Text = "Last status: " +
                snapshot.UpdatedUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) +
                " UTC · read-only · no GUI polling of UDP/123";
            if (valid && snapshot.EstimatedServerUtc is DateTimeOffset estimate)
            {
                _estimatedServerAtRead = estimate + (now - snapshot.UpdatedUtc);
                _estimatedClock.Restart();
            }
            else
            {
                _estimatedServerAtRead = null;
                _estimatedClock.Reset();
            }
            DrawClocks();
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ArgumentException or InvalidOperationException)
        {
            if (!_closed)
                ShowUnavailable("Worker status cannot be read: " + ex.Message);
        }
        finally
        {
            _reading = false;
        }
    }

    private void ShowUnavailable(string reason)
    {
        _latest = null;
        _estimatedServerAtRead = null;
        _estimatedClock.Reset();
        _serviceState.Text = "Service status unavailable";
        _serviceLamp.Foreground = BrushResolver.Require(this, "ArBrush.Warning");
        _peerAddress.Text = "—";
        _peerHealth.Text = "● OFFLINE";
        _peerHealth.Foreground = BrushResolver.Require(this, "ArBrush.Warning");
        _peerStratum.Text = _peerOffset.Text = _peerRtt.Text = _peerLastReply.Text = "—";
        _syncHealth.Text = "● NOT VERIFIED";
        _syncHealth.Foreground = BrushResolver.Require(this, "ArBrush.Warning");
        _description.Text = reason;
        _counters.Text = "Requests —   ·   Valid —   ·   Rejected —";
        _correctionEvent.Text = "No live correction event available.";
        _footer.Text = "Service status file: %ProgramData%\\ARFastSNTP\\status.json";
        DrawClocks();
    }

    private static string FormatOffset(double value)
    {
        string milliseconds = value.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture);
        if (Math.Abs(value) >= TimeSpan.FromDays(365).TotalMilliseconds)
            return (value / TimeSpan.FromDays(365.2425).TotalMilliseconds)
                .ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + " y";
        return milliseconds + " ms";
    }

    private void Refresh_OnClick(object? sender, RoutedEventArgs args) =>
        _ = ReadStatusAsync();

    private void InstallSync_OnClick(object? sender, RoutedEventArgs args)
    {
        if (!IPAddress.TryParse(_serverInput.Text, out var server) ||
            server.AddressFamily != AddressFamily.InterNetwork)
        {
            _description.Text = "Enter a valid IPv4 SNTP server before installation.";
            return;
        }
        if (!int.TryParse(_yearInput.Text, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out int year) ||
            year is < 2020 or > 2090)
        {
            _description.Text = "NTP era reference year must be between 2020 and 2090.";
            return;
        }

        // The installer still owns service mutation. The Studio never modifies
        // the clock and does not elevate itself.
        string setup = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "ARFastSNTP.Setup.exe"));
        if (!File.Exists(setup))
        {
            _description.Text = "Setup executable not found. Extract the complete " +
                "Windows artifact before using INSTALL & SYNC.";
            return;
        }
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = setup,
                WorkingDirectory = Path.GetDirectoryName(setup)!,
                UseShellExecute = true,
                Verb = "runas"
            };
            foreach (string argument in new[]
                { "--server", server.ToString(), "--year",
                    year.ToString(CultureInfo.InvariantCulture), "--lab", "--auto-apply" })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start);
            _description.Text = "Install & Sync requested. Follow the Windows UAC prompt; " +
                "the service continues when Studio is closed.";
        }
        catch (Win32Exception ex)
        {
            _description.Text = "Setup was declined or blocked: " + ex.Message;
        }
        catch (InvalidOperationException ex)
        {
            _description.Text = "Cannot start setup: " + ex.Message;
        }
    }
}
