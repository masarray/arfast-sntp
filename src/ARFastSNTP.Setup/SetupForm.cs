using System.ComponentModel;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Security.Principal;
using System.Windows.Forms;

namespace ARFastSNTP.Setup;

internal sealed class SetupForm : Form
{
    private readonly SetupArguments _initial;
    private readonly TextBox _server = new();
    private readonly NumericUpDown _year = new();
    private readonly RadioButton _monitor = new();
    private readonly RadioButton _lab = new();
    private readonly CheckBox _confirmLab = new();
    private readonly Button _install = new();
    private readonly Button _remove = new();
    private readonly Button _openLog = new();
    private readonly TextBox _log = new();
    private readonly Label _service = new();
    private bool _busy;

    internal SetupForm(SetupArguments initial)
    {
        _initial = initial;
        Text = "AR Fast SNTP — Windows Service Setup";
        Font = new Font("Segoe UI", 10);
        Size = new Size(760, 610);
        MinimumSize = new Size(670, 520);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(23, 30, 40);
        ForeColor = Color.WhiteSmoke;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 8,
            Padding = new Padding(20), BackColor = BackColor
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 164));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var row in new[] { 57, 38, 39, 42, 51, 43, 45 })
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, row));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(layout);

        var heading = MakeLabel("INSTALL / REMOVE WINDOWS SERVICE", 17, Color.White);
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
        _year.Dock = DockStyle.Left;
        _year.Width = 95;
        _year.BackColor = Color.FromArgb(41, 54, 70);
        _year.ForeColor = Color.White;
        layout.Controls.Add(_year, 1, 2);

        var modes = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, WrapContents = false,
            BackColor = Color.Transparent, AutoScroll = true
        };
        _monitor.Text = "Monitor only (no Windows clock correction)";
        _monitor.AutoSize = true;
        _monitor.ForeColor = Color.LightGreen;
        _monitor.Checked = !initial.Lab;
        _lab.Text = "Isolated lab clock correction";
        _lab.AutoSize = true;
        _lab.ForeColor = Color.FromArgb(255, 197, 126);
        _lab.Checked = initial.Lab;
        modes.Controls.Add(_monitor);
        modes.Controls.Add(_lab);
        layout.Controls.Add(MakeLabel("SERVICE MODE", 10, Color.Gainsboro), 0, 3);
        layout.Controls.Add(modes, 1, 3);

        _confirmLab.Text =
            "I confirm this is an ISOLATED test PC and accept that the Windows clock may jump YEARS.";
        _confirmLab.Dock = DockStyle.Fill;
        _confirmLab.ForeColor = Color.FromArgb(255, 197, 126);
        _confirmLab.BackColor = Color.Transparent;
        _confirmLab.Enabled = initial.Lab;
        _lab.CheckedChanged += (_, _) =>
        {
            _confirmLab.Enabled = _lab.Checked;
            if (!_lab.Checked) _confirmLab.Checked = false;
        };
        layout.Controls.Add(_confirmLab, 0, 4);
        layout.SetColumnSpan(_confirmLab, 2);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, WrapContents = false, BackColor = Color.Transparent
        };
        SetupButton(_install, "INSTALL SERVICE", Color.FromArgb(40, 107, 180), 158);
        SetupButton(_remove, "REMOVE SERVICE", Color.FromArgb(93, 69, 68), 158);
        SetupButton(_openLog, "OPEN LOG", Color.FromArgb(59, 76, 96), 112);
        actions.Controls.AddRange(new Control[] { _install, _remove, _openLog });
        layout.Controls.Add(actions, 0, 5);
        layout.SetColumnSpan(actions, 2);

        _service = MakeLabel("Inspecting existing Windows Service...", 9, Color.LightGray);
        layout.Controls.Add(_service, 0, 6);
        layout.SetColumnSpan(_service, 2);

        _log.Dock = DockStyle.Fill;
        _log.Multiline = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.ReadOnly = true;
        _log.WordWrap = true;
        _log.Font = new Font("Consolas", 9);
        _log.BackColor = Color.FromArgb(17, 24, 33);
        _log.ForeColor = Color.Gainsboro;
        layout.Controls.Add(_log, 0, 7);
        layout.SetColumnSpan(_log, 2);

        _install.Click += async (_, _) => await ExecuteAsync(remove: false);
        _remove.Click += async (_, _) => await ExecuteAsync(remove: true);
        _openLog.Click += (_, _) =>
        {
            try
            {
                string path = InstallerOperations.SetupLog;
                if (!File.Exists(path)) { Append("No saved setup log yet."); return; }
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path, UseShellExecute = true
                });
            }
            catch (Exception ex) { Append("Cannot open log: " + ex.Message); }
        };

        Shown += (_, _) =>
        {
            Append("Ready. Service installation does not require PowerShell.");
            Append("Bundle folder: " + AppContext.BaseDirectory);
            Append("Setup log: " + InstallerOperations.SetupLog);
            if (_initial.Remove) Append("Remove requested. Click REMOVE SERVICE to confirm.");
            UpdateServiceInfo();
        };
    }

    private static Label MakeLabel(string value, float fontSize, Color color) => new()
    {
        Text = value, Dock = DockStyle.Fill, AutoEllipsis = true,
        ForeColor = color, BackColor = Color.Transparent,
        TextAlign = ContentAlignment.MiddleLeft,
        Font = new Font("Segoe UI", fontSize)
    };

    private static void SetupButton(Button button, string text, Color color, int width)
    {
        button.Text = text;
        button.Width = width;
        button.Height = 32;
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
                ? "Status: no ARFastSNTP Windows Service installed."
                : "Status: ARFastSNTP already registered. Remove before reinstalling.";
        }
        catch (Exception ex) { _service.Text = "Service status unavailable: " + ex.Message; }
    }

    private SetupArguments? ReadConfiguration()
    {
        if (!IPAddress.TryParse(_server.Text.Trim(), out var ip) ||
            ip.AddressFamily != AddressFamily.InterNetwork)
        {
            MessageBox.Show(this, "Please enter a valid SNTP Server IPv4 address.",
                "Invalid IP", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }
        if (_lab.Checked)
        {
            if (!_confirmLab.Checked)
            {
                MessageBox.Show(this,
                    "LAB CLOCK CORRECTION requires explicit isolated-test-PC confirmation.",
                    "Lab authorization required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }
            if (MessageBox.Show(this,
                    "Last check: this operation will allow the Windows Service to step system time, " +
                    "including large date changes.\n\nNo live SCADA/SOE/historian may be connected.\n\nContinue?",
                    "Lab clock correction — final confirmation",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return null;
        }
        return new SetupArguments(ip, (int)_year.Value, _lab.Checked, false, false);
    }

    private async Task ExecuteAsync(bool remove)
    {
        if (_busy) return;
        SetupArguments? configuration = remove
            ? new SetupArguments(_initial.Server, _initial.ServerYear, false, true, false)
            : ReadConfiguration();
        if (configuration is null) return;

        if (!InstallerOperations.IsAdministrator())
        {
            try
            {
                string exe = Environment.ProcessPath ??
                    throw new InvalidOperationException("Setup executable path is unavailable.");
                var start = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exe,
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = AppContext.BaseDirectory
                };
                foreach (string arg in configuration.CommandLine(remove))
                    start.ArgumentList.Add(arg);
                using var elevated = System.Diagnostics.Process.Start(start);
                Append("Administrator approval requested. Complete the operation in the elevated SETUP window.");
            }
            catch (Win32Exception ex)
            {
                Append("Administrator approval was cancelled or blocked: " + ex.Message);
                MessageBox.Show(this, ex.Message, "Administrator approval failed",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (InvalidOperationException ex)
            {
                Append("Cannot elevate: " + ex.Message);
            }
            return;
        }

        _busy = true;
        _install.Enabled = false;
        _remove.Enabled = false;
        _service.Text = remove ? "Removing Windows Service..." : "Installing Windows Service...";
        IProgress<string> progress = new Progress<string>(Append);
        try
        {
            if (remove)
            {
                Append("Removing owned ARFastSNTP Windows Service...");
                await Task.Run(() => InstallerOperations.Remove(progress.Report));
            }
            else
            {
                Append("Starting " + (configuration.Lab ? "ISOLATED LAB" : "MONITOR ONLY") +
                    " installation from extracted package...");
                await Task.Run(() => InstallerOperations.Install(
                    configuration, AppContext.BaseDirectory, progress.Report));
            }
            Append(remove ? "Removal completed." : "Installation completed successfully.");
        }
        catch (Exception ex)
        {
            Append("FAILED: " + ex.Message);
            MessageBox.Show(this,
                ex.Message + "\n\nThe SETUP window will remain open. Details: " +
                InstallerOperations.SetupLog,
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
