using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace KioskAdmin;

/// <summary>Small helpers for building dialogs and images in code.</summary>
public static class Ui
{
    public static TextBlock Label(string text) =>
        new() { Text = text, Margin = new Thickness(0, 8, 0, 2), TextWrapping = TextWrapping.Wrap, MaxWidth = 520 };

    public static bool Ask(Window owner, string title, params UIElement[] fields)
    {
        var sp = new StackPanel { Margin = new Thickness(16), MinWidth = 340 };
        foreach (var f in fields) sp.Children.Add(f);
        var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        row.Children.Add(ok); row.Children.Add(cancel);
        sp.Children.Add(row);
        var w = new Window
        {
            Title = title, Owner = owner, Content = sp, SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false
        };
        ok.Click += (_, _) => w.DialogResult = true;
        return w.ShowDialog() == true;
    }

    public static string? Prompt(Window owner, string title, string label, string initial = "")
    {
        var tb = new TextBox { Text = initial, Margin = new Thickness(0, 6, 0, 0) };
        tb.Loaded += (_, _) => { tb.Focus(); tb.SelectAll(); };
        return Ask(owner, title, Label(label), tb) ? tb.Text.Trim() : null;
    }

    public static string? IconOfExe(string exe)
    {
        try
        {
            if (!File.Exists(exe)) return null;
            using var ic = System.Drawing.Icon.ExtractAssociatedIcon(exe);
            if (ic == null) return null;
            using var bmp = ic.ToBitmap();
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return Convert.ToBase64String(ms.ToArray());
        }
        catch { return null; }
    }

    public static string? ImageFileToPngBase64(string file)
    {
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.UriSource = new Uri(file);
            bi.DecodePixelWidth = 460;
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.EndInit();
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bi));
            using var ms = new MemoryStream();
            enc.Save(ms);
            return Convert.ToBase64String(ms.ToArray());
        }
        catch { return null; }
    }
}
