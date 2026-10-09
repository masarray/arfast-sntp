using System.Net;
using System.Net.Sockets;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ARFastSNTP.Studio.Design;

namespace ARFastSNTP.Studio;

/// <summary>
/// Shared modal chrome: compact content sizing, a single action hierarchy,
/// consistent padding and keyboard handling. No fixed-height blank regions.
/// </summary>
internal static class PeerDialogChrome
{
    internal static void Configure(Window dialog, Window owner, string title, double width)
    {
        dialog.Title = title;
        dialog.Width = width;
        dialog.SizeToContent = SizeToContent.Height;
        dialog.CanResize = false;
        dialog.ShowInTaskbar = false;
        dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        dialog.Background = BrushResolver.Require(owner, "ArBrush.Canvas");
        dialog.FontFamily = owner.FontFamily;
    }

    internal static Button ActionButton(string text, bool primary = false)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = primary ? 106 : 82,
            Height = 32
        };
        button.Classes.Add("ar");
        if (primary) button.Classes.Add("primary");
        return button;
    }

    internal static StackPanel Actions(Button cancel, Button confirm)
    {
        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };
        footer.Children.Add(cancel);
        footer.Children.Add(confirm);
        return footer;
    }

    internal static StackPanel Body(double spacing = 10) =>
        new()
        {
            Spacing = spacing,
            Margin = new Thickness(18, 17, 18, 18)
        };
}

internal sealed class AddPeerDialog : Window
{
    internal AddPeerDialog(Window owner, IReadOnlyCollection<string> existing)
    {
        PeerDialogChrome.Configure(this, owner, "Add NTP Server", 374);
        var label = new TextBlock
        {
            Text = "Server IP Address",
            FontWeight = FontWeight.SemiBold,
            FontSize = 13
        };
        var ip = new TextBox
        {
            PlaceholderText = "192.16.1.33",
            MinHeight = 34,
            FontSize = 13
        };
        ip.Classes.Add("ar-input");
        var hint = new TextBlock
        {
            Text = "",
            IsVisible = false,
            FontSize = 11,
            Foreground = BrushResolver.Require(owner, "ArBrush.Warning")
        };
        var cancel = PeerDialogChrome.ActionButton("Cancel");
        var add = PeerDialogChrome.ActionButton("Add Server", primary: true);

        void Submit()
        {
            string value = ip.Text?.Trim() ?? "";
            if (!IPAddress.TryParse(value, out var address) ||
                address.AddressFamily != AddressFamily.InterNetwork ||
                address.GetAddressBytes()[0] is 0 or >= 224 ||
                !string.Equals(value, address.ToString(), StringComparison.Ordinal))
            {
                hint.Text = "Enter a valid IPv4 address.";
                hint.IsVisible = true;
                return;
            }
            if (existing.Contains(address.ToString()))
            {
                hint.Text = "This NTP server is already in the list.";
                hint.IsVisible = true;
                return;
            }
            Close(address.ToString());
        }
        cancel.Click += (_, _) => Close(null);
        add.Click += (_, _) => Submit();
        ip.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Submit(); e.Handled = true; }
            else if (e.Key == Key.Escape) { Close(null); e.Handled = true; }
        };
        ip.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty && hint.IsVisible)
                hint.IsVisible = false;
        };
        var body = PeerDialogChrome.Body();
        body.Children.Add(label);
        body.Children.Add(ip);
        body.Children.Add(hint);
        body.Children.Add(PeerDialogChrome.Actions(cancel, add));
        Content = body;
        Opened += (_, _) => ip.Focus();
    }
}

internal sealed class RemovePeerDialog : Window
{
    internal RemovePeerDialog(Window owner, string address)
    {
        PeerDialogChrome.Configure(this, owner, "Remove NTP Server", 374);
        var question = new TextBlock
        {
            Text = "Remove " + address + "?",
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        var detail = new TextBlock
        {
            Text = "Other NTP servers will remain active.",
            Foreground = BrushResolver.Require(owner, "ArBrush.TextSecondary"),
            TextWrapping = TextWrapping.Wrap
        };
        var cancel = PeerDialogChrome.ActionButton("Cancel");
        var remove = PeerDialogChrome.ActionButton("Remove");
        cancel.Click += (_, _) => Close(false);
        remove.Click += (_, _) => Close(true);
        var body = PeerDialogChrome.Body(10);
        body.Children.Add(question);
        body.Children.Add(detail);
        body.Children.Add(PeerDialogChrome.Actions(cancel, remove));
        Content = body;
    }
}
