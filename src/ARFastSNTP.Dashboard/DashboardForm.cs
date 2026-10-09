using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Text.Json;
using System.Windows.Forms;

namespace ARFastSNTP.Dashboard;

internal sealed class DashboardForm : Form
{
    private readonly Label _serverClock = new();
    private readonly Label _pcClock = new();
    private readonly Label _serverLamp = new();
    private readonly Label _responseLamp = new();
    private readonly Label _syncLamp = new();
    private readonly Label _broadcastLamp = new();
    private readonly Label _statistics = new();
    private readonly Label _lastEvent = new();
    private readonly Label _status = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 250 };
    private readonly Stopwatch _estimateTimer = new();
    private DateTimeOffset? _serverEstimate;
    private long _tick;
    private readonly string _statusFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ARFastSNTP", "status.json");

    internal DashboardForm()
    {
        Text = "AR Fast SNTP — Live Monitor";
        BackColor = Color.FromArgb(22, 28, 37);
        ForeColor = Color.WhiteSmoke;
        MinimumSize = new Size(650, 465);
        Size = new Size(760, 510);
        Font = new Font("Segoe UI", 10);
        DoubleBuffered = true;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(20),
            ColumnCount = 2, RowCount = 7, BackColor = BackColor
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 105));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 65));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 65));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(layout);

        var heading = MakeLabel("AR FAST SNTP  ·  LIVE STATUS", 17, Color.White);
        layout.Controls.Add(heading, 0, 0);
        layout.SetColumnSpan(heading, 2);

        layout.Controls.Add(ClockPanel("SERVER UTC (estimated)", _serverClock), 0, 1);
        layout.Controls.Add(ClockPanel("PC UTC", _pcClock), 1, 1);

        layout.Controls.Add(LampPanel("SNTP SERVER", _serverLamp), 0, 2);
        layout.Controls.Add(LampPanel("VALID TRANSACTION", _responseLamp), 1, 2);
        layout.Controls.Add(LampPanel("SYNCHRONIZED", _syncLamp), 0, 3);
        layout.Controls.Add(LampPanel("BROADCAST  ·  NOT MEASURED", _broadcastLamp), 1, 3);

        _status = MakeLabel("Waiting for service status...", 10, Color.Gainsboro);
        layout.Controls.Add(_status, 0, 4);
        layout.SetColumnSpan(_status, 2);
        _statistics = MakeLabel("Requests —  |  Accepted —  |  Rejected —  |  Offset —", 10, Color.Gainsboro);
        layout.Controls.Add(_statistics, 0, 5);
        layout.SetColumnSpan(_statistics, 2);
        _lastEvent = MakeLabel("Last correction: none", 9, Color.Silver);
        layout.Controls.Add(_lastEvent, 0, 6);
        layout.SetColumnSpan(_lastEvent, 2);

        _serverLamp.Text = _responseLamp.Text = _syncLamp.Text = _broadcastLamp.Text = "● UNKNOWN";
        _serverClock.Text = _pcClock.Text = "--:--:--";
        _timer.Tick += (_, _) =>
        {
            _pcClock.Text = DateTimeOffset.UtcNow.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            if (_serverEstimate is { } server)
                _serverClock.Text = (server + _estimateTimer.Elapsed)
                    .ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            if (++_tick % 8 == 1) RefreshStatus();
        };
        _timer.Start();
        FormClosed += (_, _) => _timer.Dispose();
    }

    private static Label MakeLabel(string value, float size, Color color) => new()
    {
        Text = value, Dock = DockStyle.Fill, AutoEllipsis = true,
        TextAlign = ContentAlignment.MiddleLeft, ForeColor = color,
        Font = new Font("Segoe UI", size, FontStyle.Regular),
        BackColor = Color.Transparent
    };

    private static Control ClockPanel(string label, Label value)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, BackColor = Color.FromArgb(35, 45, 57),
            ColumnCount = 1, RowCount = 2, Margin = new Padding(3)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(MakeLabel("    " + label, 9, Color.LightGray), 0, 0);
        value.Dock = DockStyle.Fill;
        value.TextAlign = ContentAlignment.MiddleCenter;
        value.Font = new Font("Consolas", 25, FontStyle.Bold);
        value.ForeColor = Color.White;
        panel.Controls.Add(value, 0, 1);
        return panel;
    }

    private static Control LampPanel(string label, Label lamp)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2,
            BackColor = Color.FromArgb(35, 45, 57), Margin = new Padding(3)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
        panel.Controls.Add(MakeLabel("    " + label, 9, Color.WhiteSmoke), 0, 0);
        lamp.Dock = DockStyle.Fill;
        lamp.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        lamp.TextAlign = ContentAlignment.MiddleCenter;
        lamp.ForeColor = Color.Silver;
        panel.Controls.Add(lamp, 1, 0);
        return panel;
    }

    private static void SetLamp(Label lamp, bool online, string active)
    {
        lamp.Text = online ? "● " + active : "● OFFLINE";
        lamp.ForeColor = online ? Color.LightGreen : Color.LightSalmon;
    }

    private void RefreshStatus()
    {
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(_statusFile));
            var root = json.RootElement;
            string state = root.GetProperty("State").GetString() ?? "Unknown";
            var updated = root.GetProperty("UpdatedUtc").GetDateTimeOffset();
            // A large local clock jump can make UTC age unreliable.
            // Unknown/stale data must never be shown as a live validated response.
            double age = (DateTimeOffset.UtcNow - updated).TotalSeconds;
            bool fresh = age >= -1 && age <= 10;
            bool valid = fresh && root.GetProperty("OffsetMilliseconds").ValueKind == JsonValueKind.Number;
            bool synced = fresh && state == "Synchronized";
            SetLamp(_serverLamp, valid, "ONLINE");
            SetLamp(_responseLamp, valid, "VALID");
            SetLamp(_syncLamp, synced, "SYNCED");
            _broadcastLamp.Text = "● UNKNOWN";
            _broadcastLamp.ForeColor = Color.Silver;
            _status.Text = $"Server: {root.GetProperty("Server").GetString()}   ·   {state}" +
                (root.GetProperty("StepEnabled").GetBoolean() ? "   ·   CERTIFICATION STEP" : "   ·   MONITOR ONLY");

            string offset = valid
                ? root.GetProperty("OffsetMilliseconds").GetDouble().ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture) + " ms"
                : "—";
            _statistics.Text = $"Requests {root.GetProperty("RequestCount").GetInt64()}   |   " +
                $"Accepted {root.GetProperty("AcceptedCount").GetInt64()}   |   " +
                $"Rejected {root.GetProperty("RejectedCount").GetInt64()}   |   Offset {offset}";
            string? evt = root.GetProperty("LastEvent").ValueKind == JsonValueKind.String
                ? root.GetProperty("LastEvent").GetString() : null;
            if (string.IsNullOrWhiteSpace(evt) &&
                root.TryGetProperty("RecentCorrections", out var history) &&
                history.ValueKind == JsonValueKind.Array && history.GetArrayLength() > 0)
                evt = history[history.GetArrayLength() - 1].GetString();
            if (!string.IsNullOrWhiteSpace(evt)) _lastEvent.Text = "Event: " + evt;

            if (valid && root.GetProperty("EstimatedServerUtc").ValueKind == JsonValueKind.String)
            {
                // Approximate server clock; never use it to set Windows time.
                _serverEstimate = root.GetProperty("EstimatedServerUtc").GetDateTimeOffset();
                _estimateTimer.Restart();
            }
            else
            {
                _serverEstimate = null;
                _serverClock.Text = "--:--:--";
            }
        }
        catch (IOException)
        {
            _status.Text = "Waiting for AR Fast SNTP worker...";
            SetLamp(_serverLamp, false, "ONLINE");
            SetLamp(_responseLamp, false, "VALID");
            SetLamp(_syncLamp, false, "SYNCED");
        }
        catch (JsonException)
        {
            _status.Text = "Status snapshot not yet readable.";
        }
        catch (UnauthorizedAccessException)
        {
            _status.Text = "Cannot read status file; check service permissions.";
        }
    }
}
