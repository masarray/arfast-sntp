using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Windows.Forms;

namespace ARFastSNTP.Dashboard;

/// <summary>
/// View-only SNTP client dashboard. An explicitly owned portable monitor worker
/// enables immediate Connect without UAC, PowerShell or Windows Service setup.
/// Clock adjustments are never performed by this process.
/// </summary>
internal sealed class DashboardForm : Form
{
    private static readonly Color BackgroundColor = Color.FromArgb(22, 28, 37);
    private static readonly Color PanelColor = Color.FromArgb(35, 45, 57);

    private readonly TextBox _serverInput = new();
    private readonly NumericUpDown _yearInput = new();
    private readonly Button _connectButton = new();
    private readonly Button _disconnectButton = new();
    private readonly Button _installButton = new();
    private readonly Button _labButton = new();

    private readonly Label _serverClock = new();
    private readonly Label _pcClock = new();
    private readonly Label _serverLamp = new();
    private readonly Label _responseLamp = new();
    private readonly Label _syncLamp = new();
    private readonly Label _broadcastLamp = new();
    private readonly Label _statistics = new();
    private readonly Label _lastEvent = new();
    private readonly Label _status = new();
    private readonly Label _connectionInfo = new();

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 250 };
    private readonly Stopwatch _estimateTimer = new();

    private readonly string _serviceStatusFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ARFastSNTP", "status.json");
    private readonly string _preferencesFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ARFastSNTP", "dashboard-settings.json");
    private string _statusFile;
    private string? _portableStatusFile;
    private Process? _portableWorker;
    private DateTimeOffset? _serverEstimate;
    private int _refreshTick;
    private string? _workerExitMessage;
    private string? _lastWorkerError;

    internal DashboardForm()
    {
        _statusFile = _serviceStatusFile;
        Text = "AR Fast SNTP — Connection & Live Monitor";
        BackColor = BackgroundColor;
        ForeColor = Color.WhiteSmoke;
        MinimumSize = new Size(760, 545);
        Size = new Size(870, 585);
        Font = new Font("Segoe UI", 10);
        DoubleBuffered = true;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18, 12, 18, 14),
            ColumnCount = 2, RowCount = 9,
            BackColor = BackgroundColor
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        foreach (int size in new[] { 44, 76, 35, 110, 60, 60, 42, 52 })
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, size));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(layout);

        var heading = MakeLabel("AR FAST SNTP  ·  LIVE MONITOR", 16, Color.White);
        layout.Controls.Add(heading, 0, 0);
        layout.SetColumnSpan(heading, 2);

        var connectionPanel = BuildConnectionPanel();
        layout.Controls.Add(connectionPanel, 0, 1);
        layout.SetColumnSpan(connectionPanel, 2);

        _connectionInfo = MakeLabel(
            "Enter SNTP Server IP and click CONNECT · Monitor Only (does not set PC time)",
            9, Color.FromArgb(178, 194, 213));
        layout.Controls.Add(_connectionInfo, 0, 2);
        layout.SetColumnSpan(_connectionInfo, 2);

        layout.Controls.Add(ClockPanel("SERVER UTC  ·  ESTIMATED", _serverClock), 0, 3);
        layout.Controls.Add(ClockPanel("PC UTC", _pcClock), 1, 3);
        layout.Controls.Add(LampPanel("SNTP SERVER", _serverLamp), 0, 4);
        layout.Controls.Add(LampPanel("VALID REPLY", _responseLamp), 1, 4);
        layout.Controls.Add(LampPanel("SYNCHRONIZED", _syncLamp), 0, 5);
        layout.Controls.Add(LampPanel("BROADCAST", _broadcastLamp), 1, 5);

        _status = MakeLabel("Ready. Enter IP and connect to monitor server.", 10, Color.WhiteSmoke);
        layout.Controls.Add(_status, 0, 6);
        layout.SetColumnSpan(_status, 2);
        _statistics = MakeLabel("Requests —   |   Accepted —   |   Rejected —   |   Offset —", 10, Color.Gainsboro);
        layout.Controls.Add(_statistics, 0, 7);
        layout.SetColumnSpan(_statistics, 2);
        _lastEvent = MakeLabel("Last correction: none", 9, Color.Silver);
        layout.Controls.Add(_lastEvent, 0, 8);
        layout.SetColumnSpan(_lastEvent, 2);

        _serverLamp.Text = _responseLamp.Text = _syncLamp.Text = "● OFFLINE";
        _broadcastLamp.Text = "● UNKNOWN";
        _serverClock.Text = _pcClock.Text = "--:--:--";
        _serverInput.Text = "192.16.1.175";
        LoadPreferences();

        _connectButton.Click += (_, _) => Connect();
        _disconnectButton.Click += (_, _) => StopPortable();
        _installButton.Click += (_, _) => InstallService(false);
        _labButton.Click += (_, _) => InstallService(true);
        _serverInput.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { Connect(); e.SuppressKeyPress = true; }
        };

        _timer.Tick += (_, _) =>
        {
            _pcClock.Text = DateTimeOffset.UtcNow.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            if (_serverEstimate is { } utc)
                _serverClock.Text = (utc + _estimateTimer.Elapsed).ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            if (++_refreshTick % 4 == 0) RefreshStatus();
        };
        _timer.Start();
        FormClosing += (_, _) => { _timer.Stop(); StopPortable(false); };
        FormClosed += (_, _) => _timer.Dispose();
    }

    private Control BuildConnectionPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 2,
            BackColor = PanelColor, Padding = new Padding(9, 5, 9, 5),
            Margin = new Padding(3, 0, 3, 0)
        };
        foreach (int size in new[] { 77, -1, 49, 76, 118, 84 })
        {
            panel.ColumnStyles.Add(size < 0
                ? new ColumnStyle(SizeType.Percent, 100)
                : new ColumnStyle(SizeType.Absolute, size));
        }
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        panel.Controls.Add(MakeLabel("SERVER IP", 8.5f, Color.WhiteSmoke), 0, 0);
        _serverInput.Dock = DockStyle.Fill;
        _serverInput.Font = new Font("Consolas", 10);
        _serverInput.BackColor = Color.FromArgb(24, 34, 46);
        _serverInput.ForeColor = Color.White;
        _serverInput.BorderStyle = BorderStyle.FixedSingle;
        panel.Controls.Add(_serverInput, 1, 0);

        panel.Controls.Add(MakeLabel("YEAR", 8, Color.WhiteSmoke), 2, 0);
        _yearInput.Minimum = 2020;
        _yearInput.Maximum = 2090;
        _yearInput.Value = 2026;
        _yearInput.Dock = DockStyle.Fill;
        _yearInput.BackColor = Color.FromArgb(24, 34, 46);
        _yearInput.ForeColor = Color.White;
        panel.Controls.Add(_yearInput, 3, 0);

        ConfigureButton(_connectButton, "CONNECT", Color.FromArgb(37, 112, 193), Color.White);
        ConfigureButton(_disconnectButton, "STOP", Color.FromArgb(75, 83, 96), Color.White);
        _disconnectButton.Enabled = false;
        ConfigureButton(_installButton, "INSTALL SERVICE", Color.FromArgb(57, 77, 100), Color.White);
        ConfigureButton(_labButton, "LAB CLOCK STEP", Color.FromArgb(105, 65, 42), Color.White);

        panel.Controls.Add(_connectButton, 4, 0);
        panel.Controls.Add(_disconnectButton, 5, 0);
        panel.Controls.Add(_installButton, 0, 1);
        panel.SetColumnSpan(_installButton, 2);
        panel.Controls.Add(_labButton, 2, 1);
        panel.SetColumnSpan(_labButton, 2);

        var hint = MakeLabel("CONNECT: no installation or clock correction", 8, Color.FromArgb(178, 194, 213));
        panel.Controls.Add(hint, 4, 1);
        panel.SetColumnSpan(hint, 2);
        return panel;
    }

    private static void ConfigureButton(Button button, string label, Color background, Color foreground)
    {
        button.Text = label;
        button.Dock = DockStyle.Fill;
        button.Font = new Font("Segoe UI", 8, FontStyle.Bold);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = background;
        button.ForeColor = foreground;
        button.UseVisualStyleBackColor = false;
        button.Margin = new Padding(3, 0, 3, 0);
    }

    private bool TryGetServer(out IPAddress server)
    {
        bool valid = IPAddress.TryParse(_serverInput.Text.Trim(), out var parsed)
            && parsed is { AddressFamily: AddressFamily.InterNetwork };
        if (valid)
        {
            server = parsed!;
            return true;
        }
        server = IPAddress.None;
        MessageBox.Show(this, "Enter a valid IPv4 address, for example 192.16.1.175.",
            "Invalid SNTP Server IP", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        _serverInput.Focus();
        return false;
    }

    private void SavePreferences(IPAddress address)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_preferencesFile)!);
            File.WriteAllText(_preferencesFile, JsonSerializer.Serialize(
                new ServerPreferences(address.ToString(), (int)_yearInput.Value)));
        }
        catch (IOException) { /* Preferences never block a connection. */ }
        catch (UnauthorizedAccessException) { }
    }

    private void LoadPreferences()
    {
        try
        {
            if (!File.Exists(_preferencesFile)) return;
            var saved = JsonSerializer.Deserialize<ServerPreferences>(File.ReadAllText(_preferencesFile));
            if (saved is null || !IPAddress.TryParse(saved.Address, out var ip) ||
                ip.AddressFamily != AddressFamily.InterNetwork) return;
            _serverInput.Text = ip.ToString();
            if (saved.Year >= 2020 && saved.Year <= 2090) _yearInput.Value = saved.Year;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (JsonException) { }
    }

    private void Connect()
    {
        if (!TryGetServer(out var server)) return;
        SavePreferences(server);
        StopPortable(false);
        string workerPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "worker", "ARFastSNTP.exe"));
        if (!File.Exists(workerPath))
        {
            _status.Text = "Worker executable not found. Extract the entire GitHub artifact ZIP.";
            MessageBox.Show(this,
                "ARFastSNTP.exe is missing from the worker folder.\n" +
                "Extract the complete ARFastSNTP-Windows-x64 ZIP, retaining worker/ and dashboard/ directories.",
                "Worker not found", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        var directory = Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "ARFastSNTP");
        try
        {
            Directory.CreateDirectory(directory);
            _portableStatusFile = Path.Combine(directory,
                $"portable-{Environment.ProcessId}.json");
            if (File.Exists(_portableStatusFile)) File.Delete(_portableStatusFile);
            var start = new ProcessStartInfo
            {
                FileName = workerPath,
                WorkingDirectory = Path.GetDirectoryName(workerPath)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add("--server");
            start.ArgumentList.Add(server.ToString());
            start.ArgumentList.Add("--server-year");
            start.ArgumentList.Add(((int)_yearInput.Value).ToString(CultureInfo.InvariantCulture));
            start.ArgumentList.Add("--status-file");
            start.ArgumentList.Add(_portableStatusFile);
            _lastWorkerError = null;
            _portableWorker = Process.Start(start)
                ?? throw new InvalidOperationException("Windows did not launch SNTP worker.");
            _portableWorker.ErrorDataReceived += (_, eventArgs) =>
            {
                string? line = eventArgs.Data;
                if (!string.IsNullOrWhiteSpace(line))
                    _lastWorkerError = line.Length > 250 ? line[..250] : line;
            };
            _portableWorker.BeginErrorReadLine();
            _statusFile = _portableStatusFile;
            _workerExitMessage = null;
            _connectionInfo.Text = $"LIVE MONITOR  ·  {server}:123  ·  No PC clock correction  ·  STOP disconnects";
            _status.Text = $"Connecting to SNTP server {server}:123...";
            _connectButton.Enabled = false;
            _disconnectButton.Enabled = true;
            _serverInput.Enabled = false;
            _yearInput.Enabled = false;
            _serverEstimate = null;
            _serverClock.Text = "--:--:--";
            _refreshTick = 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            StopPortable(false);
            _status.Text = "Cannot launch monitoring worker.";
            MessageBox.Show(this, ex.Message, "SNTP monitor could not start",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void StopPortable(bool updateMessage = true)
    {
        if (_portableWorker is not null)
        {
            try
            {
                if (!_portableWorker.HasExited)
                {
                    // Kill ONLY the child monitor process created by this window.
                    _portableWorker.Kill(entireProcessTree: true);
                    _portableWorker.WaitForExit(1500);
                }
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            finally
            {
                _portableWorker.Dispose();
                _portableWorker = null;
            }
        }
        if (!string.IsNullOrEmpty(_portableStatusFile))
        {
            try { File.Delete(_portableStatusFile); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        _portableStatusFile = null;
        _statusFile = _serviceStatusFile;
        _serverEstimate = null;
        _serverClock.Text = "--:--:--";
        _connectButton.Enabled = true;
        _disconnectButton.Enabled = false;
        _serverInput.Enabled = true;
        _yearInput.Enabled = true;
        if (updateMessage)
        {
            _status.Text = "Portable monitoring stopped. Windows Service status will display if installed.";
            _connectionInfo.Text = "MONITOR ONLY  ·  Connect to test any IPv4 SNTP server";
        }
    }

    private void InstallService(bool certification)
    {
        if (!TryGetServer(out var server)) return;
        SavePreferences(server);
        if (certification)
        {
            var answer = MessageBox.Show(this,
                "LAB CERTIFICATION ONLY.\n\n" +
                "This will ENABLE Windows clock corrections, including jumps of YEARS.\n" +
                "It can invalidate SCADA SOE and historian timestamps.\n\n" +
                "Only continue if this is a dedicated ISOLATED test PC,\n" +
                "with other clock-setting services disabled by an authorized operator.\n\n" +
                "Continue with installation?",
                "Danger: enable clock stepping", MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;
        }
        string script = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "install.ps1"));
        string ps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(script))
        {
            MessageBox.Show(this,
                "Installer script not found. Run this dashboard from the EXTRACTED GitHub artifact, " +
                "not a folder copied without install.ps1.\n\nCONNECT will work without an installer.",
                "Installer not found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!File.Exists(ps))
        {
            MessageBox.Show(this,
                "Windows PowerShell is unavailable at its normal system path.\n" +
                "CONNECT monitoring does not require PowerShell.\n" +
                "Ask your Windows administrator to enable the built-in installer dependency.",
                "Windows PowerShell unavailable", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        StopPortable(false); // Do not create two workers for the same peer while installing a service.
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = ps,
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" " +
                    $"-Server {server} -ServerYear {(int)_yearInput.Value}" +
                    (certification ? " -CertificationStep -AcceptLabRisk" : ""),
                WorkingDirectory = Path.GetDirectoryName(script)!,
                Verb = "runas", UseShellExecute = true
            };
            using var process = Process.Start(start);
            _status.Text = $"Service installation requested for {server}. Wait for Windows approval.";
            _connectionInfo.Text = "Service setup: Windows Administrator approval required";
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Installation cancelled or blocked",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private static Label MakeLabel(string text, float size, Color color) => new()
    {
        Text = text, Dock = DockStyle.Fill, AutoEllipsis = true,
        TextAlign = ContentAlignment.MiddleLeft, ForeColor = color,
        Font = new Font("Segoe UI", size, FontStyle.Regular),
        BackColor = Color.Transparent
    };

    private static Control ClockPanel(string title, Label clock)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, BackColor = PanelColor,
            ColumnCount = 1, RowCount = 2, Margin = new Padding(3)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(MakeLabel("    " + title, 9, Color.LightGray), 0, 0);
        clock.Dock = DockStyle.Fill;
        clock.TextAlign = ContentAlignment.MiddleCenter;
        clock.Font = new Font("Consolas", 26, FontStyle.Bold);
        clock.ForeColor = Color.White;
        panel.Controls.Add(clock, 0, 1);
        return panel;
    }

    private static Control LampPanel(string title, Label indicator)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, BackColor = PanelColor,
            Margin = new Padding(3)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        panel.Controls.Add(MakeLabel("    " + title, 9, Color.WhiteSmoke), 0, 0);
        indicator.Dock = DockStyle.Fill;
        indicator.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        indicator.TextAlign = ContentAlignment.MiddleCenter;
        indicator.ForeColor = Color.Silver;
        panel.Controls.Add(indicator, 1, 0);
        return panel;
    }

    private static void SetLamp(Label indicator, bool on, string title)
    {
        indicator.Text = on ? "● " + title : "● OFFLINE";
        indicator.ForeColor = on ? Color.LightGreen : Color.LightSalmon;
    }

    private void MarkDisconnected(string reason)
    {
        _status.Text = reason;
        _serverEstimate = null;
        _serverClock.Text = "--:--:--";
        SetLamp(_serverLamp, false, "ONLINE");
        SetLamp(_responseLamp, false, "VALID");
        SetLamp(_syncLamp, false, "SYNCED");
    }

    private void RefreshStatus()
    {
        if (_portableWorker is not null)
        {
            try
            {
                if (_portableWorker.HasExited)
                {
                    int code = _portableWorker.ExitCode;
                    StopPortable(false);
                    _workerExitMessage = $"Monitor worker stopped (code {code}). " +
                        (_lastWorkerError ?? "Check Windows policy or firewall.");
                }
            }
            catch (InvalidOperationException)
            {
                StopPortable(false);
                _workerExitMessage = "Monitoring process exited unexpectedly.";
            }
        }

        string json;
        try
        {
            json = File.ReadAllText(_statusFile);
        }
        catch (FileNotFoundException)
        {
            MarkDisconnected(_workerExitMessage ??
                (_portableWorker is null
                    ? "No active Windows Service. Enter server IP and click CONNECT."
                    : "Connecting... waiting for first SNTP reply."));
            return;
        }
        catch (DirectoryNotFoundException)
        {
            MarkDisconnected(_workerExitMessage ?? "No worker status yet. Click CONNECT.");
            return;
        }
        catch (IOException)
        {
            MarkDisconnected("Status unavailable; waiting for next update.");
            return;
        }
        catch (UnauthorizedAccessException)
        {
            MarkDisconnected("Status file access denied; check permissions.");
            return;
        }

        // Deserialize optional keys defensively. Older or partial snapshots may
        // omit fields such as StepEnabled: the UI must NEVER crash as a result.
        if (!DashboardStatus.TryParse(json, out var snapshot, out string reason) || snapshot is null)
        {
            MarkDisconnected(reason);
            return;
        }

        try
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            bool fresh = snapshot.IsFresh(now);
            bool valid = snapshot.HasValidReply(now);
            bool synced = snapshot.IsSynchronized(now);
            SetLamp(_serverLamp, valid, "ONLINE");
            SetLamp(_responseLamp, valid, "VALID");
            SetLamp(_syncLamp, synced, "SYNCED");
            _broadcastLamp.Text = "● UNKNOWN";
            _broadcastLamp.ForeColor = Color.Silver;

            string activity = fresh ? snapshot.State : "Stale status — waiting for worker";
            _status.Text = $"{snapshot.Server}:123   ·   {activity}   ·   " +
                (snapshot.StepEnabled ? "LAB CLOCK STEP ENABLED" : "MONITOR ONLY (NO CLOCK CORRECTION)");

            string offset = valid && snapshot.OffsetMilliseconds is double value
                ? value.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture) + " ms"
                : "—";
            string rtt = valid && snapshot.RoundTripMilliseconds is double rttValue
                ? rttValue.ToString("0.0", CultureInfo.InvariantCulture) + " ms" : "—";
            _statistics.Text = $"Requests {snapshot.RequestCount}   |   " +
                $"Accepted {snapshot.AcceptedCount}   |   " +
                $"Rejected {snapshot.RejectedCount}   |   Offset {offset}   |   RTT {rtt}";

            if (snapshot.LatestEvent is string evt && !string.IsNullOrWhiteSpace(evt))
                _lastEvent.Text = "Last event: " + evt;

            if (valid && snapshot.EstimatedServerUtc is DateTimeOffset estimated)
            {
                // Server estimate is written at snapshot.UpdatedUtc; account for
                // elapsed snapshot age rather than resetting the clock on each UI read.
                double age = Math.Max(0, (now - snapshot.UpdatedUtc).TotalSeconds);
                _serverEstimate = estimated.AddSeconds(age);
                _estimateTimer.Restart();
            }
            else
            {
                _serverEstimate = null;
                _serverClock.Text = "--:--:--";
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
            or OverflowException)
        {
            MarkDisconnected("Worker status contained an invalid value; waiting for next update.");
        }
    }

    private sealed record ServerPreferences(string Address, int Year);
}
