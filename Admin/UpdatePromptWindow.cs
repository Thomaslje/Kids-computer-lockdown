using System.Windows;
using System.Windows.Controls;
using KioskShared;

namespace KioskAdmin;

public enum UpdateChoice { Update, Later, Skip }

/// <summary>"A new version is available" with the release notes and three choices.</summary>
public class UpdatePromptWindow : Window
{
    public UpdateChoice Choice { get; private set; } = UpdateChoice.Later;

    public UpdatePromptWindow(Window? owner, ReleaseInfo rel)
    {
        Title = "New version available";
        Theme.Skin(this);
        Width = 520; Height = 440;
        WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
        if (owner != null) Owner = owner;
        ResizeMode = ResizeMode.NoResize;

        var root = new DockPanel { Margin = new Thickness(16) };
        var head = new TextBlock
        {
            Text = $"A new version is available: {rel.Tag}\nThis version: {BuildInfo.ReleaseTag}",
            FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10)
        };
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        Button B(string text, UpdateChoice c, bool def = false)
        {
            var b = new Button { Content = text, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(6, 0, 0, 0), IsDefault = def };
            b.Click += (_, _) => { Choice = c; DialogResult = true; };
            return b;
        }
        buttons.Children.Add(B("Don't remind me about this version", UpdateChoice.Skip));
        buttons.Children.Add(B("No, open the program", UpdateChoice.Later));
        buttons.Children.Add(B("Yes, update and open", UpdateChoice.Update, true));
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var notes = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(rel.Body) ? "(No change notes were published with this release.)" : rel.Body.Trim(),
            IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var box = new GroupBox { Header = "What's new", Content = notes };
        root.Children.Add(box);
        Content = root;
    }
}
