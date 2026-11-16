using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace KioskAdmin;

/// <summary>Word-style ribbon pieces: tabs, groups and big round-icon buttons (teal and orange, built in code).</summary>
public static class Ribbon
{
    static SolidColorBrush Br(string hex)
    {
        var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        b.Freeze();
        return b;
    }

    static readonly Brush Teal = Br("#0B7F80"), TealSoft = Br("#E4F2F2"), Orange = Br("#FF7A1A"), DangerSoft = Br("#FCE9E6"),
        DangerText = Br("#A3281B"), Edge = Br("#CFDFE0"), GroupLine = Br("#DCE8E9"), GroupLabel = Br("#6A8387"),
        TextDark = Br("#17353A"), TabText = Br("#3E5A5F"), Hover = Br("#F0F8F8"), Clear = Brushes.Transparent;

    public enum Kind { Normal, Primary, Danger }

    // Segoe MDL2 Assets icon codes
    public const string Add = "\uE710", Delete = "\uE74D", Edit = "\uE70F", Sync = "\uE72C", Lock = "\uE72E", Unlock = "\uE785",
        Power = "\uE7E8", Up = "\uE74A", Down = "\uE74B", Folder = "\uE8B7", Picture = "\uE91B", Globe = "\uE774",
        Cancel = "\uE711", Check = "\uE73E", Settings = "\uE713", Send = "\uE724", Download = "\uE896", Cmd = "\uE756",
        App = "\uE737", List = "\uE8FD", Camera = "\uE722", Person = "\uE77B", AddPerson = "\uE8FA", Play = "\uE768",
        Flash = "\uE945", More = "\uE712", Key = "\uE192", Font = "\uE8D2", Puzzle = "\uEA86", Game = "\uE7FC", Share = "\uE72D";

    static Border Circle(string glyph, Brush bg, Brush fg) => new()
    {
        Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Background = bg, HorizontalAlignment = HorizontalAlignment.Center,
        Child = new TextBlock
        {
            Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 16, Foreground = fg,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        }
    };

    static Border Shell(Border circle, string label, Brush text, Action click)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 6, 0, 4) };
        sp.Children.Add(circle);
        sp.Children.Add(new TextBlock
        {
            Text = label, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = text, TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0)
        });
        var b = new Border { Width = 76, Background = Clear, CornerRadius = new CornerRadius(8), Child = sp, Cursor = Cursors.Hand };
        b.MouseEnter += (_, _) => b.Background = Hover;
        b.MouseLeave += (_, _) => b.Background = Clear;
        b.MouseLeftButtonUp += (_, _) => click();
        return b;
    }

    public static Border Button(string glyph, string label, Action click, Kind kind = Kind.Normal)
    {
        var circle = kind switch
        {
            Kind.Primary => Circle(glyph, Orange, Brushes.White),
            Kind.Danger => Circle(glyph, DangerSoft, DangerText),
            _ => Circle(glyph, TealSoft, Teal)
        };
        return Shell(circle, label, kind == Kind.Danger ? DangerText : TextDark, click);
    }

    /// <summary>A switch: the circle is filled teal while it is on.</summary>
    public static Border Toggle(string label, Func<bool> get, Action<bool> set)
    {
        var circle = Circle(Check, get() ? Teal : TealSoft, get() ? Brushes.White : Teal);
        var b = Shell(circle, label, TextDark, () => { });
        b.MouseLeftButtonUp += (_, _) =>
        {
            set(!get());
            circle.Background = get() ? Teal : TealSoft;
            ((TextBlock)circle.Child).Foreground = get() ? Brushes.White : Teal;
        };
        return b;
    }

    public static Border Group(string label, params UIElement[] buttons)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var x in buttons) row.Children.Add(x);
        var col = new StackPanel();
        col.Children.Add(row);
        col.Children.Add(new TextBlock { Text = label, FontSize = 10.5, Foreground = GroupLabel, HorizontalAlignment = HorizontalAlignment.Center });
        return new Border { Child = col, Padding = new Thickness(6, 0, 6, 0), BorderBrush = GroupLine, BorderThickness = new Thickness(0, 0, 1, 0) };
    }

    public static UIElement Row(params Border[] groups)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var g in groups) row.Children.Add(g);
        if (groups.Length > 0) groups[^1].BorderThickness = new Thickness(0);
        return row;
    }

    public static Border Tab(string text, bool teal, Action click)
    {
        var t = new Border
        {
            Padding = new Thickness(18, 8, 18, 8), CornerRadius = new CornerRadius(10, 10, 0, 0), Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 4, 0),
            BorderThickness = new Thickness(1, 1, 1, 0), BorderBrush = Clear, Background = Clear, Tag = teal ? "setup" : "tab",
            Child = new TextBlock { Text = text, FontSize = 13.5, FontWeight = FontWeights.Bold, Foreground = TabText }
        };
        t.MouseLeftButtonUp += (_, _) => click();
        SetActive(t, false);
        return t;
    }

    public static void SetActive(Border tab, bool on)
    {
        var label = (TextBlock)tab.Child;
        if ((string?)tab.Tag == "setup")
        {
            tab.Background = Teal; tab.BorderBrush = Teal; label.Foreground = Brushes.White;
            return;
        }
        tab.Background = on ? Brushes.White : Clear;
        tab.BorderBrush = on ? Edge : Clear;
        label.Foreground = on ? Teal : TabText;
    }

    public static Border Panel(UIElement child) => new()
    {
        Background = Brushes.White, BorderBrush = Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(0, 12, 12, 12),
        Padding = new Thickness(8, 4, 8, 2), Child = child, MinHeight = 98
    };
}
