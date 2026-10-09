using System.Net;
using System.Net.Sockets;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ARFastSNTP.Studio.Design;

namespace ARFastSNTP.Studio;

/// <summary>Short operator input, not a second installer or privileged wizard.</summary>
internal sealed class AddPeerDialog : Window
{
    internal AddPeerDialog(Window owner)
    {
        Title = "Add NTP Server";
        Width = 370; Height = 186; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = BrushResolver.Require(owner, "ArBrush.Canvas");
        FontFamily = owner.FontFamily;
        var ip = new TextBox { PlaceholderText = "e.g. 192.16.1.175", Text = "",
            MinHeight = 33 };
        var hint = new TextBlock { Text = "Enter an IPv4 address",
            Foreground = BrushResolver.Require(owner, "ArBrush.TextSecondary") };
        var cancel = new Button { Content = "Cancel", MinWidth = 75 };
        var add = new Button { Content = "Add Server", MinWidth = 100 };
        cancel.Click += (_, _) => Close(null);
        add.Click += (_, _) =>
        {
            string value = ip.Text?.Trim() ?? "";
            if (!IPAddress.TryParse(value, out var parsed) ||
                parsed.AddressFamily != AddressFamily.InterNetwork ||
                parsed.GetAddressBytes()[0] is 0 or >= 224)
            {
                hint.Text = "Enter a valid unicast IPv4 address.";
                return;
            }
            Close(parsed.ToString());
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        buttons.Children.Add(cancel); buttons.Children.Add(add);
        var body = new StackPanel { Spacing = 9, Margin = new Thickness(17) };
        body.Children.Add(new TextBlock { Text = "NTP Server IP Address",
            FontWeight = FontWeight.SemiBold });
        body.Children.Add(ip); body.Children.Add(hint); body.Children.Add(buttons);
        Content = body;
        Opened += (_, _) => ip.Focus();
    }
}

internal sealed class RemovePeerDialog : Window
{
    internal RemovePeerDialog(Window owner, string address)
    {
        Title = "Remove NTP Server";
        Width = 375; Height = 157; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = BrushResolver.Require(owner, "ArBrush.Canvas");
        FontFamily = owner.FontFamily;
        var text = new TextBlock { Text = "Remove " + address + " from NTP Servers?",
            TextWrapping = TextWrapping.Wrap };
        var cancel = new Button { Content = "Cancel", MinWidth = 80 };
        var remove = new Button { Content = "Remove", MinWidth = 88 };
        cancel.Click += (_, _) => Close(false);
        remove.Click += (_, _) => Close(true);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel); buttons.Children.Add(remove);
        var content = new StackPanel { Spacing = 19, Margin = new Thickness(18) };
        content.Children.Add(text); content.Children.Add(buttons);
        Content = content;
    }
}
