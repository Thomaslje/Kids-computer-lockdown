using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KioskShared;
using Microsoft.Win32;

namespace KioskLauncher;

public class MainWindow : Window
{
    // Colours of the KFUM/KFUK logo (red, blue, navy) plus a few friendly extras for tile accents
    const string Navy = "#3C3C50", Red = "#DD3221", Blue = "#5A96F0";
    static readonly string[] Palette = { Red, Blue, "#F5A623", "#4CAF7A", "#8A5AA5", "#2FB3C6" };

    readonly bool _windowed;
    readonly WrapPanel _panel = new() { HorizontalAlignment = HorizontalAlignment.Center };
    readonly TextBlock _title = new();
    readonly TextBlock _status = new();
    StackPanel _header = new();
    ScrollViewer _scroll = new();
    // your own logo, placed in the chosen corner (set from the admin app)
    readonly Image _logo = new()
    {
        IsHitTestVisible = false, Stretch = Stretch.Uniform, MaxWidth = 280, MaxHeight = 160,
        Margin = new Thickness(28, 24, 28, 24), Visibility = Visibility.Collapsed
    };
    readonly Canvas _overlay = new() { IsHitTestVisible = false };   // free text placed from the admin app
    readonly DispatcherTimer _reloadTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    FileSystemWatcher? _watcher;
    KeyboardBlocker? _blocker;
    KioskConfig _cfg = new();
    DateTime _busyUntil = DateTime.MinValue;
    readonly Dictionary<string, DateTime> _launchedAt = new();
    // Normal priority (the default DispatcherTimer priority is Background, which can starve while a game hogs the PC)
    readonly DispatcherTimer _clearStatus = new(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(1) };
    DateTime _statusUntil = DateTime.MinValue;
    bool _exiting;
    string _lastCommandId = "";

    public MainWindow(bool windowed)
    {
        _windowed = windowed;
        Title = "Kiosk";
        Background = new LinearGradientBrush(Color.FromRgb(0xE4, 0xF0, 0xFF), Color.FromRgb(0xFF, 0xFF, 0xFF), 90);
        if (windowed) { Width = 1100; Height = 700; }
        else { WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; WindowState = WindowState.Maximized; }

        _title.FontFamily = new FontFamily("Segoe UI");
        _title.FontSize = 52;
        _title.FontWeight = FontWeights.Bold;
        _title.Foreground = Hex(Navy);
        _title.HorizontalAlignment = HorizontalAlignment.Center;
        _title.TextAlignment = TextAlignment.Center;
        _title.TextWrapping = TextWrapping.Wrap;
        _title.Margin = new Thickness(40, 0, 40, 10);

        // three colour stripes under the title, like the logo
        var stripes = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 24) };
        foreach (var c in new[] { Red, Blue, Navy })
            stripes.Children.Add(new Border { Width = 90, Height = 8, CornerRadius = new CornerRadius(4), Background = Hex(c), Margin = new Thickness(4, 0, 4, 0) });
        var header = new StackPanel { Margin = new Thickness(0, 36, 0, 0) };
        _header = header;
        header.Children.Add(_title);
        header.Children.Add(stripes);

        _status.FontFamily = new FontFamily("Segoe UI");
        _status.FontSize = 24;
        _status.FontWeight = FontWeights.SemiBold;
        _status.Foreground = Hex(Navy);
        _status.HorizontalAlignment = HorizontalAlignment.Center;
        _status.VerticalAlignment = VerticalAlignment.Center;
        _status.TextAlignment = TextAlignment.Center;
        _status.TextWrapping = TextWrapping.Wrap;

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(40, 0, 40, 0),
            Content = _panel
        };
        _scroll = scroll;

        var bottom = new Grid { Margin = new Thickness(28, 8, 28, 0) };
        bottom.Children.Add(_status);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        // soft background bubbles in the logo colours
        foreach (var bubble in new[]
        {
            Bubble(Red,  340, 0.10, HorizontalAlignment.Left,  VerticalAlignment.Top,    new Thickness(-110, -110, 0, 0)),
            Bubble(Blue, 420, 0.16, HorizontalAlignment.Right, VerticalAlignment.Top,    new Thickness(0, -150, -140, 0)),
            Bubble(Blue, 300, 0.12, HorizontalAlignment.Left,  VerticalAlignment.Bottom, new Thickness(-90, 0, 0, -110)),
            Bubble(Red,  220, 0.08, HorizontalAlignment.Right, VerticalAlignment.Bottom, new Thickness(0, 0, 330, -80))
        })
        {
            Grid.SetRowSpan(bubble, 3);
            root.Children.Add(bubble);
        }
        Grid.SetRow(header, 0); Grid.SetRow(scroll, 1); Grid.SetRow(bottom, 2);
        root.Children.Add(header); root.Children.Add(scroll); root.Children.Add(bottom);
        Grid.SetRowSpan(_logo, 3);
        root.Children.Add(_logo);
        Grid.SetRowSpan(_overlay, 3);
        root.Children.Add(_overlay);   // last = on top
        _overlay.SizeChanged += (_, _) => RebuildTexts();
        Content = root;

        Loaded += OnLoaded;
        Closing += (_, e) => { if (!_windowed && !_exiting) e.Cancel = true; };
        KeyDown += OnKeyDown;
        _clearStatus.Tick += (_, _) => ExpireStatus();
        _clearStatus.Start();
        // coming back from a game: drop any old message and force a repaint
        Activated += (_, _) => { ExpireStatus(); _status.InvalidateVisual(); };
        _reloadTimer.Tick += (_, _) => { _reloadTimer.Stop(); LoadConfig(); };
    }

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_windowed)
        {
            Lockdown.Apply();
            try { _blocker = new KeyboardBlocker(); } catch (Exception ex) { Log.Write("Keyboard hook failed: " + ex); }
            EnsureSteam();
        }
        try { LoadConfig(); } catch (Exception ex) { Log.Write("LoadConfig failed: " + ex); }
        StartWatcher();
        Activate();
    }

    // ---------- configuration ----------

    void LoadConfig()
    {
        try
        {
            if (File.Exists(Paths.ConfigFile))
                _cfg = Json.From<KioskConfig>(File.ReadAllText(Paths.ConfigFile)) ?? new KioskConfig();
        }
        catch { /* file may be mid-write; the watcher will fire again */ }
        Rebuild();
        ApplyLogo();
        Task.Run(() => CloseCleanup.RunAll(_cfg));
    }

    void ApplyLogo()
    {
        var bmp = LoadImage(_cfg.LogoBase64);
        RenderOptions.SetBitmapScalingMode(_logo, BitmapScalingMode.HighQuality);
        _logo.Source = bmp;
        _logo.Visibility = bmp == null ? Visibility.Collapsed : Visibility.Visible;
        var c = _cfg.LogoCorner;
        _logo.HorizontalAlignment = c is LogoCorner.TopLeft or LogoCorner.BottomLeft ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        _logo.VerticalAlignment = c is LogoCorner.TopLeft or LogoCorner.TopRight ? VerticalAlignment.Top : VerticalAlignment.Bottom;
    }

    void StartWatcher()
    {
        try
        {
            if (!Directory.Exists(Paths.DataDir)) return;
            _watcher = new FileSystemWatcher(Paths.DataDir, "*.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            void Changed(object s, FileSystemEventArgs a) =>
                Dispatcher.BeginInvoke(() =>
                {
                    if (a.Name == "command.json") CheckCommand();
                    else if (a.Name == "config.json") { _reloadTimer.Stop(); _reloadTimer.Start(); }
                });
            _watcher.Changed += Changed;
            _watcher.Created += Changed;
            _watcher.Renamed += (s, a) => Changed(s, a);
        }
        catch { }
    }

    // ---------- remote commands (sent from the admin app via the agent) ----------

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern int GetSystemMetrics(int index);

    void CheckCommand()
    {
        try
        {
            if (!File.Exists(Paths.CommandFile)) return;
            var c = Json.From<KioskCommand>(File.ReadAllText(Paths.CommandFile));
            if (c == null || c.Id == _lastCommandId) return;
            _lastCommandId = c.Id;
            // ignore leftovers from before a restart
            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - c.Ts > 60) return;
            Log.Write("Remote command: " + c.Cmd);
            if (c.Cmd == "unlock") ExitKiosk();
            else if (c.Cmd == "launchapp" && !string.IsNullOrWhiteSpace(c.Path))
            {
                try
                {
                    // a tap on Alt lets the new program take the foreground even though nobody clicked in the launcher
                    keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero);
                    var psi = new ProcessStartInfo(c.Path) { UseShellExecute = true, Arguments = c.Args ?? "" };
                    if (File.Exists(c.Path)) psi.WorkingDirectory = Path.GetDirectoryName(c.Path) ?? "";
                    Process.Start(psi);
                    ShowStatus("Starting a program...");
                }
                catch (Exception ex) { Log.Write("launchapp failed: " + ex.Message); }
            }
            else if (c.Cmd == "front")
            {
                // an admin tool was started on this screen: step back so it is visible
                var h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                SetWindowPos(h, (IntPtr)1, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);   // HWND_BOTTOM, no move/size/activate
            }
            else if (c.Cmd == "screenshot" && !string.IsNullOrWhiteSpace(c.Path))
            {
                try
                {
                    int w = GetSystemMetrics(0), h = GetSystemMetrics(1);
                    using var bmp = new System.Drawing.Bitmap(w, h);
                    using (var g = System.Drawing.Graphics.FromImage(bmp)) g.CopyFromScreen(0, 0, 0, 0, bmp.Size);
                    var tmp = c.Path + ".tmp";
                    bmp.Save(tmp, System.Drawing.Imaging.ImageFormat.Png);
                    File.Move(tmp, c.Path, true);
                }
                catch (Exception ex) { Log.Write("screenshot failed: " + ex.Message); }
            }
            else if (c.Cmd == "reloadui")
            {
                // exit code 1 = "restart me": the supervisor starts the (new) UI again
                _exiting = true;
                _blocker?.Dispose();
                Application.Current.Shutdown(1);
            }
        }
        catch (Exception e) { Log.Write("CheckCommand failed: " + e.Message); }
    }

    // ---------- tiles ----------

    static bool Available(Tile t) => t.Type switch
    {
        TileType.Steam => SteamScanner.IsInstalled(t.SteamAppId ?? ""),
        TileType.Exe => ExePathResolver.Resolve(t.ExePath) != null,
        TileType.Chrome => ChromeLocator.Find() != null,
        _ => false
    };

    void Rebuild()
    {
        _title.Text = _cfg.Title;
        // no title text = no title and no stripes; the tiles move up so you can use your own screen text instead
        bool hasTitle = !string.IsNullOrWhiteSpace(_cfg.Title);
        _header.Visibility = hasTitle ? Visibility.Visible : Visibility.Collapsed;
        _scroll.Margin = new Thickness(40, hasTitle ? 0 : 40, 40, 0);
        _panel.Children.Clear();
        foreach (var t in _cfg.Tiles.Where(Available))
            _panel.Children.Add(MakeTile(t));
        if (_panel.Children.Count == 0)
            _status.Text = "Nothing to show yet.";
        else if (_status.Text == "Nothing to show yet.")
            _status.Text = "";
        RebuildTexts();
    }

    void RebuildTexts()
    {
        _overlay.Children.Clear();
        double w = _overlay.ActualWidth, h = _overlay.ActualHeight;
        if (w < 10 || h < 10) return;
        foreach (var t in _cfg.Texts)
        {
            if (string.IsNullOrWhiteSpace(t.Text)) continue;
            try
            {
                var tb = new TextBlock
                {
                    Text = t.Text, FontFamily = new FontFamily("Segoe UI"),
                    FontSize = Math.Max(8, t.Size * h / 1080),
                    FontWeight = t.Bold ? FontWeights.Bold : FontWeights.Normal,
                    Foreground = Hex(t.Color)
                };
                Canvas.SetLeft(tb, t.X * w);
                Canvas.SetTop(tb, t.Y * h);
                _overlay.Children.Add(tb);
            }
            catch (Exception ex) { Log.Write("Bad screen text: " + ex.Message); }
        }
    }

    static SolidColorBrush Hex(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;

    static System.Windows.Shapes.Ellipse Bubble(string hex, double size, double opacity,
        HorizontalAlignment h, VerticalAlignment v, Thickness margin) => new()
    {
        Width = size, Height = size, Fill = Hex(hex), Opacity = opacity,
        HorizontalAlignment = h, VerticalAlignment = v, Margin = margin, IsHitTestVisible = false
    };

    FrameworkElement MakeTile(Tile t)
    {
        const double W = 280, ImgH = 131, H = 215, R = 26;
        var accent = Hex(Palette[t.Name.Sum(c => (int)c) % Palette.Length]);

        // top part: game picture, program icon, or a big letter
        var top = new Grid { Width = W, Height = ImgH };
        var bmp = LoadImage(t.ImageBase64);
        if (bmp != null && t.Type == TileType.Steam)
        {
            top.Children.Add(new Image { Source = bmp, Stretch = Stretch.UniformToFill });
        }
        else if (bmp != null)
        {
            top.Background = Hex("#EAF2FF");
            var icon = new Image { Source = bmp, Width = 80, Height = 80, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
            top.Children.Add(icon);
        }
        else
        {
            top.Background = accent;
            var letter = string.IsNullOrWhiteSpace(t.Name) ? "?" : t.Name.Trim()[0].ToString().ToUpper();
            top.Children.Add(new TextBlock
            {
                Text = letter, FontSize = 72, FontWeight = FontWeights.Bold, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            });
        }

        var label = new Grid { Height = H - ImgH - 8, Background = Brushes.White };
        label.Children.Add(new TextBlock
        {
            Text = t.Name, FontFamily = new FontFamily("Segoe UI"), FontSize = 22, FontWeight = FontWeights.Bold,
            Foreground = Hex(Navy), TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center, MaxHeight = 62, Margin = new Thickness(12, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center
        });

        var content = new StackPanel { Width = W, Height = H, Clip = new RectangleGeometry(new Rect(0, 0, W, H), R - 2, R - 2) };
        content.Children.Add(top);
        content.Children.Add(label);
        content.Children.Add(new Border { Height = 8, Background = accent });

        var scale = new ScaleTransform(1, 1);
        var card = new Border
        {
            Background = Brushes.White, CornerRadius = new CornerRadius(R), Margin = new Thickness(16),
            Cursor = Cursors.Hand, Child = content,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = scale,
            Effect = new DropShadowEffect { Color = Color.FromRgb(0x3C, 0x3C, 0x50), Opacity = 0.22, BlurRadius = 18, ShadowDepth = 5 }
        };

        void Zoom(double to)
        {
            var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(120));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
        }
        card.MouseEnter += (_, _) => Zoom(1.06);
        card.MouseLeave += (_, _) => Zoom(1.0);
        card.MouseLeftButtonDown += (_, _) => Zoom(0.97);
        card.MouseLeftButtonUp += (_, _) => { Zoom(1.06); Launch(t); };
        return card;
    }

    static BitmapImage? LoadImage(string? base64)
    {
        if (string.IsNullOrEmpty(base64)) return null;
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.StreamSource = new MemoryStream(Convert.FromBase64String(base64));
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch { return null; }
    }

    // ---------- starting things ----------

    void ShowStatus(string text)
    {
        _status.Text = text;
        _statusUntil = DateTime.UtcNow.AddSeconds(8);
    }

    // Messages from ShowStatus disappear after 8 seconds; "Nothing to show yet." is not one of them.
    void ExpireStatus()
    {
        if (_statusUntil != DateTime.MinValue && DateTime.UtcNow >= _statusUntil)
        {
            _statusUntil = DateTime.MinValue;
            _status.Text = _panel.Children.Count == 0 ? "Nothing to show yet." : "";
        }
    }

    // Tiles with "close first" (for example Roblox): if the program is still running, close it and start it fresh.
    // Further clicks while it is starting are ignored, so nobody clicks five times thinking nothing happens.
    bool LaunchWithClose(Tile t)
    {
        if (string.IsNullOrWhiteSpace(t.CloseFirst)) return false;
        if (DateTime.UtcNow < _busyUntil) { ShowStatus($"{t.Name} is starting, please wait..."); return true; }
        _busyUntil = DateTime.UtcNow.AddSeconds(20);
        ShowStatus($"Starting {t.Name}, please wait...");
        var names = t.CloseFirst!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _ = Task.Run(() =>
        {
            bool closed = CloseProcesses(names);
            Dispatcher.BeginInvoke(() =>
            {
                if (!closed)
                {
                    _busyUntil = DateTime.MinValue;
                    ShowStatus($"Could not close {t.Name}, please try again.");
                    return;
                }
                StartTile(t, 20);
            });
        });
        return true;
    }

    /// <summary>Closes every running copy of the named programs. Returns false if one would not go away.</summary>
    static bool CloseProcesses(string[] names)
    {
        foreach (var raw in names)
        {
            var name = raw.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? raw[..^4] : raw;
            try
            {
                foreach (var p in Process.GetProcessesByName(name))
                {
                    using (p)
                    {
                        Log.Write($"Closing {name} (pid {p.Id}) before starting it again");
                        try { p.Kill(true); } catch (Exception ex) { Log.Write("Kill failed: " + ex.Message); }
                        p.WaitForExit(5000);
                    }
                }
            }
            catch (Exception ex) { Log.Write("CloseProcesses failed for " + name + ": " + ex.Message); }
        }
        // check that they are really gone (give them a moment)
        for (int i = 0; i < 20; i++)
        {
            bool any = false;
            foreach (var raw in names)
            {
                var name = raw.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? raw[..^4] : raw;
                var left = Process.GetProcessesByName(name);
                if (left.Length > 0) any = true;
                foreach (var p in left) p.Dispose();
            }
            if (!any) return true;
            Thread.Sleep(250);
        }
        return false;
    }

    void Launch(Tile t)
    {
        if (LaunchWithClose(t)) return;
        // Never start a game twice. (The browser is left alone: Chrome reuses its own running instance.)
        if (t.Type != TileType.Chrome)
        {
            bool running = false;
            var window = IntPtr.Zero;
            try { running = RunningCheck.IsRunning(t, out window); }
            catch (Exception ex) { Log.Write("Running check failed: " + ex.Message); }

            Log.Write($"Launch '{t.Name}': running={running}");
            if (running)
            {
                _launchedAt.Remove(t.Id);
                RunningCheck.Focus(window);
                ShowStatus($"{t.Name} is already running.");
                return;
            }
            // started a moment ago but no process visible yet (Steam is slow to start games)
            if (_launchedAt.TryGetValue(t.Id, out var at) && DateTime.UtcNow - at < TimeSpan.FromSeconds(45))
            {
                ShowStatus($"{t.Name} is starting, please wait...");
                return;
            }
        }

        if (DateTime.UtcNow < _busyUntil) return; // ignore double clicks
        StartTile(t, 8);
    }

    void StartTile(Tile t, int lockSeconds)
    {
        _busyUntil = DateTime.UtcNow.AddSeconds(lockSeconds);
        ShowStatus($"Starting {t.Name}...");
        CloseCleanup.Watch(t);
        try
        {
            switch (t.Type)
            {
                case TileType.Steam:
                    EnsureSteam();
                    Process.Start(new ProcessStartInfo($"steam://rungameid/{t.SteamAppId}") { UseShellExecute = true });
                    break;
                case TileType.Exe:
                {
                    var exe = ExePathResolver.Resolve(t.ExePath) ?? throw new FileNotFoundException("The program was not found on this PC");
                    Log.Write($"Starting {exe}");
                    Process.Start(new ProcessStartInfo(exe)
                    {
                        UseShellExecute = true,
                        WorkingDirectory = Path.GetDirectoryName(exe) ?? "",
                        Arguments = t.Arguments ?? ""
                    });
                    break;
                }
                case TileType.Chrome:
                    Process.Start(new ProcessStartInfo(ChromeLocator.Find()!, "--no-first-run") { UseShellExecute = true });
                    break;
            }
        }
        catch (Exception ex)
        {
            _busyUntil = DateTime.MinValue;
            ShowStatus("Could not start: " + ex.Message);
            return;
        }
        if (t.Type != TileType.Chrome) _launchedAt[t.Id] = DateTime.UtcNow;
    }

    static void EnsureSteam()
    {
        try
        {
            if (Process.GetProcessesByName("steam").Length > 0) return;
            var sp = SteamScanner.SteamPath();
            var exe = sp == null ? null : Path.Combine(sp, "steam.exe");
            if (exe != null && File.Exists(exe))
                Process.Start(new ProcessStartInfo(exe, "-silent -nofriendsui") { UseShellExecute = true });
        }
        catch { }
    }

    // ---------- hidden admin exit: Ctrl + Alt + Shift + K ----------

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var all = ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift;
        if (key == Key.K && (Keyboard.Modifiers & all) == all)
        {
            e.Handled = true;
            if (AskPassword()) ExitKiosk();
            else _status.Text = "Wrong password.";
        }
    }

    bool AskPassword()
    {
        var pb = new PasswordBox { Width = 260, Margin = new Thickness(0, 8, 0, 8) };
        var ok = new Button { Content = "OK", Width = 80, IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right };
        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock { Text = "Admin password" });
        stack.Children.Add(pb);
        stack.Children.Add(ok);
        var dlg = new Window
        {
            Owner = this, Title = "Admin", Content = stack, SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.ToolWindow, Topmost = true
        };
        ok.Click += (_, _) => dlg.DialogResult = true;
        dlg.Loaded += (_, _) => pb.Focus();
        return dlg.ShowDialog() == true && Passwords.Verify(pb.Password, _cfg.ExitPasswordHash);
    }

    void ExitKiosk()
    {
        _exiting = true;
        _blocker?.Dispose();
        if (!_windowed) Lockdown.Remove();
        Application.Current.Shutdown(100); // supervisor sees 100 and starts the normal desktop
    }
}
