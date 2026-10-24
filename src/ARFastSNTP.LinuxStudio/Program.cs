using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

namespace ARFastSNTP.LinuxStudio;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args) =>
        AppBuilder.Configure<StudioApp>().UsePlatformDetect()
            .WithInterFont().StartWithClassicDesktopLifetime(args);
}

internal sealed class StudioApp : Application
{
    public override void OnFrameworkInitializationCompleted()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        if (ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime app)
            app.MainWindow = new StudioWindow();
        base.OnFrameworkInitializationCompleted();
    }
}

internal sealed class StudioWindow : Window
{
    private readonly TextBlock _state = new() { FontSize = 17 };
    private readonly TextBlock _details = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _peers = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _address = new() { Watermark = "SNTP server IPv4 address", Width = 250 };
    private readonly Button _add = new() { Content = "Add server", MinWidth = 116 };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };

    public StudioWindow()
    {
        Title = "AR Fast SNTP — Linux";
        Width = 540;
        MinWidth = 470;
        Height = 360;
        MinHeight = 320;
        Background = Brush.Parse("#141A23");
        var root = new StackPanel { Spacing = 14, Margin = new Thickness(22) };
        root.Children.Add(new TextBlock { Text = "AR Fast SNTP", FontSize = 21 });
        root.Children.Add(new TextBlock { Text = "Simple time synchronization · Linux x64",
            FontSize = 12, Foreground = Brush.Parse("#9BA7B8") });
        root.Children.Add(_state);
        root.Children.Add(_details);
        var input = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9 };
        input.Children.Add(_address);
        input.Children.Add(_add);
        root.Children.Add(input);
        root.Children.Add(new TextBlock { Text = "NTP sources", FontSize = 14 });
        root.Children.Add(_peers);
        root.Children.Add(new TextBlock {
            Text = "Changes require system authorization. The background service monitors network recovery.",
            FontSize = 11, TextWrapping = TextWrapping.Wrap,
            Foreground = Brush.Parse("#9BA7B8") });
        Content = new ScrollViewer { Content = root };
        _add.Click += async (_, _) => await AddAsync();
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        Closed += (_, _) => _timer.Stop();
        Refresh();
    }

    private void Refresh()
    {
        try
        {
            string path = "/var/lib/arfast-sntp/status.json";
            if (!File.Exists(path))
            {
                _state.Text = "Service not ready";
                _details.Text = "Check: systemctl status arfast-sntp";
                _peers.Text = "Add a server IP address after installation.";
                return;
            }
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            _state.Text = root.GetProperty("State").GetString() ?? "Waiting";
            string? selected = root.GetProperty("SelectedServer").GetString();
            DateTimeOffset updated = root.GetProperty("UpdatedUtc").GetDateTimeOffset();
            _details.Text = "Source: " + (selected ?? "None") + "  ·  Updated " +
                updated.ToLocalTime().ToString("HH:mm:ss") + (DateTimeOffset.UtcNow - updated >
                TimeSpan.FromSeconds(15) ? "  ·  Stale status" : "");
            _peers.Text = string.Join(Environment.NewLine,
                root.GetProperty("Peers").EnumerateArray().Select(x =>
                    x.GetProperty("Address").GetString() + "  —  " + x.GetProperty("State").GetString()));
            if (_peers.Text.Length == 0) _peers.Text = "No NTP sources configured.";
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _state.Text = "Status unavailable";
            _details.Text = ex.Message;
        }
    }

    private async Task AddAsync()
    {
        string value = _address.Text?.Trim() ?? "";
        if (!IPAddress.TryParse(value, out var ip) ||
            ip.AddressFamily != AddressFamily.InterNetwork ||
            ip.ToString() != value || ip.GetAddressBytes()[0] is 0 or >= 224)
        {
            _details.Text = "Enter a valid unicast IPv4 address.";
            return;
        }
        _add.IsEnabled = false;
        try
        {
            var process = new ProcessStartInfo("pkexec")
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            process.ArgumentList.Add("/opt/arfast-sntp/ARFastSNTP.Linux");
            process.ArgumentList.Add("--add");
            process.ArgumentList.Add(value);
            using var child = Process.Start(process) ?? throw new IOException("Could not start authorization.");
            string stderr = await child.StandardError.ReadToEndAsync();
            await child.WaitForExitAsync();
            _details.Text = child.ExitCode == 0
                ? "Server added. Background synchronization will begin automatically."
                : "Unable to add server: " + stderr.Trim();
            Refresh();
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _details.Text = "Authorization failed: " + ex.Message;
        }
        finally { _add.IsEnabled = true; }
    }
}
