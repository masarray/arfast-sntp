using System.ComponentModel;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Windows.Forms;

namespace ARFastSNTP.Setup;

/// <summary>
/// Canonical single-action setup: INSTALL & SYNC updates an existing owned
/// service in-place. UAC may elevate once; no second install click or mode picker.
/// </summary>
internal sealed class SetupForm : Form
{
    private readonly SetupArguments _initial;
    private readonly TextBox _server = new();
    private readonly NumericUpDown _year = new();
    private readonly Button _install = new();
    private readonly Button _remove = new();
    private readonly Button _openLog = new();
    private readonly TextBox _log = new();
    private readonly Label _service = new();
    private bool _busy;

    internal SetupForm(SetupArguments initial)
    {
        _initial = initial;
        Text = "AR Fast SNTP — Install & Sync";
        Font = new Font("Segoe UI", 10);
        Size = new Size(750, 550);
        MinimumSize = new Size(660, 490);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(23, 30, 40);
        ForeColor = Color.WhiteSmoke;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 7,
            Padding = new Padding(20), BackColor = BackColor
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 163));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (int height in new[] { 55, 42, 42, 65, 50, 52 })
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(layout);

        var heading = MakeLabel("AR FAST SNTP  ·  INSTALL & SYNC", 17, Color.White);
        layout.Controls.Add(heading, 0, 0);
        layout.SetColumnSpan(heading, 2);

        layout.Controls.Add(MakeLabel("SNTP SERVER IP", 10, Color.Gainsboro), 0, 1);
        _server.Text = initial.Server.ToString();
        _server.Dock = DockStyle.Fill;
        _server.BackColor = Color.FromArgb(41, 54, 70);
        _server.ForeColor = Color.White;
        _server.BorderStyle = BorderStyle.FixedSingle;
        _server.Font = new Font("Consolas", 11);
        layout.Controls.Add(_server, 1, 1);

        layout.Controls.Add(MakeLabel("REFERENCE YEAR", 10, Color.Gainsboro), 0, 2);
        _year.Minimum = 2020;
        _year.Maximum = 2090;
        _year.Value = initial.ServerYear;
        _year.Width = 100;
        _year.Dock = DockStyle.Left;
        _year.BackColor = Color.FromArgb(41, 54, 70);
        _year.ForeColor = Color.White;
        layout.Controls.Add(_year, 1, 2);

        string note = initial.Lab
            ? "INSTALL & SYNC starts/reconfigures the automatic Windows Service and lets it correct " +
              "system time, including large date jumps. Use only on an authorized ISOLATED test PC; " +
              "not live SCADA, SOE or historian equipment."
            : "Monitor-only service requested. It will run automatically but WILL NOT adjust Windows time.";
        var warning = MakeLabel(note, 9, initial.Lab
            ? Color.FromArgb(255, 198, 134) : Color.LightGreen);
        layout.Controls.Add(warning, 0, 3);
        layout.SetColumnSpan(warning, 2);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, WrapContents = false,
            BackColor = Color.Transparent, AutoScroll = true
        };
        SetupButton(_install, initial.Lab ? "INSTALL & SYNC" : "INSTALL MONITOR",
            Color.FromArgb(32, 113, 187), 215);
        SetupButton(_remove, "REMOVE SERVICE", Color.FromArgb(90, 66, 67), 160);
        SetupButton(_openLog, "OPEN LOG", Color.FromArgb(59, 76, 96), 110);
        actions.Controls.AddRange(new Control[] { _install, _remove, _openLog });
        layout.Controls.Add(actions, 0, 4);
        layout.SetColumnSpan(actions, 2);

        _service = MakeLabel("Reading Windows Service state...", 9, Color.LightGray);
        layout.Controls.Add(_service, 0, 5);
        layout.SetColumnSpan(_service, 2);

        _log.Dock = DockStyle.Fill;
        _log.Multiline = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.ReadOnly = true;
        _log.WordWrap = true;
        _log.Font = new Font("Consolas", 9);
        _log.BackColor = Color.FromArgb(17, 24, 33);
        _log.ForeColor = Color.Gainsboro;
        layout.Controls.Add(_log, 0, 6);
        layout.SetColumnSpan(_log, 2);

        _install.Click += async (_, _) => await ExecuteAsync(remove: false);
        _remove.Click += async (_, _) => await ExecuteAsync(remove: true);
        _openLog.Click += (_, _) =>
        {
            try
            {
                string path = InstallerOperations.SetupLog;
                if (!File.Exists(path)) { Append("No setup log found yet."); return; }
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                { FileName = path, UseShellExecute = true });
            }
            catch (Exception ex) { Append("Cannot open log: " + ex.Message); }
        };

        Shown += (_, _) =>
        {
            Append("Ready. INSTALL & SYNC installs OR UPDATES the current service.");
            Append("No uninstall/reinstall is required to switch from Monitor to Sync.");
            UpdateServiceInfo();

            if (initial.AutoApply)
            {
                // Dashboard already requested this action. Do not require another click
                // after the one Windows Administrator elevation.
                BeginInvoke(new Action(async () => await ExecuteAsync(initial.Remove)));
            }
        };
    }

    private static Label MakeLabel(string value, float fontSize, Color color) => new()
    {
        Text = value, Dock = DockStyle.Fill, AutoEllipsis = false,
        ForeColor = color, BackColor = Color.Transparent,
        TextAlign = ContentAlignment.MiddleLeft,
        Font = new Font("Segoe UI", fontSize)
    };

    private static void SetupButton(Button button, string text, Color color, int width)
    {
        button.Text = text;
        button.Width = width;
        button.Height = 34;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = color;
        button.ForeColor = Color.White;
        button.UseVisualStyleBackColor = false;
        button.Font = new Font("Segoe UI", 9, FontStyle.Bold);
    }

    private void Append(string message)
    {
        string line = $"{DateTime.Now:HH:mm:ss}   {message}";
        _log.AppendText(line + Environment.NewLine);
        InstallerOperations.PersistLog(line);
    }

    private void UpdateServiceInfo()
    {
        try
        {
            string? image = InstallerOperations.ExistingServiceImage();
            _service.Text = image is null
                ? "Service: NOT INSTALLED — click INSTALL & SYNC."
                : "Installed mode: " + InstallerOperations.ServiceModeLabel(image) +
                  " — INSTALL & SYNC updates in place.";
        }
        catch (Exception ex) { _service.Text = "Service status unavailable: " + ex.Message; }
    }

    private SetupArguments? ReadConfiguration()
    {
        if (!IPAddress.TryParse(_server.Text.Trim(), out var ip) ||
            ip.AddressFamily != AddressFamily.InterNetwork)
        {
            MessageBox.Show(this, "Enter a valid IPv4 SNTP server address.",
                "Invalid SNTP IP", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }
        // The explicit INSTALL & SYNC action authorizes the selected sync mode.
        // No redundant mode picker, checkbox, or second confirmation dialog.
        return new SetupArguments(ip, (int)_year.Value, _initial.Lab,
            false, false, _initial.AutoApply);
    }

    private async Task ExecuteAsync(bool remove)
    {
        if (_busy) return;
        SetupArguments? config = remove
            ? new SetupArguments(_initial.Server, _initial.ServerYear,
                false, true, false, true)
            : ReadConfiguration();
        if (config is null) return;

        if (!InstallerOperations.IsAdministrator())
        {
            try
            {
                string exe = Environment.ProcessPath ??
                    throw new InvalidOperationException("Setup executable path unavailable.");
                var start = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exe,
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = AppContext.BaseDirectory
                };
                // Carry mode/IP through elevation and automatically complete the
                // requested operation, instead of showing a second idle setup window.
                var elevatedArgs = config with { AutoApply = true };
                foreach (string arg in elevatedArgs.CommandLine(remove))
                    start.ArgumentList.Add(arg);
                using var elevated = System.Diagnostics.Process.Start(start)
                    ?? throw new InvalidOperationException("Administrator setup did not launch.");
                Append("Windows Administrator approved; continuing in elevated setup.");
                Close();
            }
            catch (Win32Exception ex)
            {
                Append("Administrator approval cancelled or blocked: " + ex.Message);
                MessageBox.Show(this, ex.Message, "Administrator approval needed",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (InvalidOperationException ex)
            { Append("Cannot elevate: " + ex.Message); }
            return;
        }

        _busy = true;
        _install.Enabled = false;
        _remove.Enabled = false;
        _service.Text = remove ? "Removing service..." : "Installing / updating sync service...";
        IProgress<string> progress = new Progress<string>(Append);
        try
        {
            if (remove)
            {
                Append("Removing ARFastSNTP service...");
                await Task.Run(() => InstallerOperations.Remove(progress.Report));
            }
            else
            {
                Append("INSTALL & SYNC: applying IP=" + config.Server + ", year=" +
                    config.ServerYear + ", sync=" + config.Lab + "...");
                await Task.Run(() => InstallerOperations.Install(
                    config, AppContext.BaseDirectory, progress.Report));
            }
            Append(remove ? "Service removed." :
                "Service RUNNING. Dashboard now monitors SNTP synchronization.");
        }
        catch (Exception ex)
        {
            Append("FAILED: " + ex.Message);
            MessageBox.Show(this,
                ex.Message + "\n\nError log: " + InstallerOperations.SetupLog,
                "AR Fast SNTP Setup failed",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _busy = false;
            _install.Enabled = true;
            _remove.Enabled = true;
            UpdateServiceInfo();
        }
    }
}
