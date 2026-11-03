using System.Windows;
using System.Windows.Controls;
using KioskShared;

namespace KioskAdmin;

/// <summary>Asks for the start password before the admin app opens.</summary>
public enum LockRole { Admin, User }

public class LockWindow : Window
{
    public LockRole Role { get; private set; } = LockRole.Admin;
    readonly PasswordBox _pw = new() { Margin = new Thickness(0, 6, 0, 8) };
    readonly TextBlock _msg = new() { Foreground = System.Windows.Media.Brushes.Firebrick, Margin = new Thickness(0, 0, 0, 6) };
    readonly Button _ok = new() { Content = "Open", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
    int _attempts;

    public LockWindow(string adminHash, string? userHash)
    {
        Title = "Kiosk Admin";
        SizeToContent = SizeToContent.WidthAndHeight; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; Topmost = true;

        var cancel = new Button { Content = "Exit", Width = 80, IsCancel = true };
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        row.Children.Add(_ok); row.Children.Add(cancel);
        var sp = new StackPanel { Margin = new Thickness(18), MinWidth = 300 };
        sp.Children.Add(new TextBlock { Text = "Enter your password:" });
        sp.Children.Add(_pw); sp.Children.Add(_msg); sp.Children.Add(row);
        Content = sp;
        Loaded += (_, _) => _pw.Focus();

        _ok.Click += async (_, _) =>
        {
            if (Check(_pw.Password, adminHash)) { Role = LockRole.Admin; DialogResult = true; return; }
            if (!string.IsNullOrEmpty(userHash) && Check(_pw.Password, userHash)) { Role = LockRole.User; DialogResult = true; return; }
            _attempts++;
            _pw.Clear();
            if (_attempts >= 5) { DialogResult = false; return; }
            _msg.Text = "Wrong password.";
            _ok.IsEnabled = false;
            await Task.Delay(1000 * _attempts);   // each wrong try waits a bit longer
            _ok.IsEnabled = true;
            _pw.Focus();
        };
    }

    static bool Check(string password, string stored)
    {
        try { return Passwords.Verify(password, stored); } catch { return false; }
    }
}
