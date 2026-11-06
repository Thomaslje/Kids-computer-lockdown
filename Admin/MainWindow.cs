using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Threading;
using KioskShared;
using Microsoft.Win32;

namespace KioskAdmin;

public class MainWindow : Window
{
    static readonly HttpClient Http = new();

    readonly AdminData _d = AdminData.Load();
    readonly ObservableCollection<PcItem> _pcs = new();
    readonly ListView _pcList = new() { SelectionMode = SelectionMode.Extended };
    readonly ListBox _tileList = new() { MinHeight = 200 };
    readonly TextBox _titleBox = new();
    readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(15) };
    bool _refreshing;
    bool _busy;
    readonly TextBlock _statOnline = new(), _statOffline = new(), _statOld = new();
    readonly Border _banner = new();
    readonly TextBlock _bannerText = new() { FontSize = 14, TextWrapping = TextWrapping.Wrap };
    ReleaseInfo? _pendingRelease;

    byte[] Key => Convert.FromBase64String(_d.KeyBase64);

    public MainWindow()
    {
        Title = $"Kiosk Admin   (build {BuildInfo.Revision})";
        Width = 1180; Height = 780; MinWidth = 980; MinHeight = 600;
        Theme.Skin(this);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        foreach (var p in _d.Pcs) _pcs.Add(new PcItem { Host = p.Host, Name = p.Host });
        _pcList.ItemsSource = _pcs;
        var gv = new GridView();
        gv.Columns.Add(new GridViewColumn { Header = "PC", DisplayMemberBinding = new Binding("Name"), Width = 130 });
        gv.Columns.Add(new GridViewColumn { Header = "Address", DisplayMemberBinding = new Binding("Host"), Width = 110 });
        gv.Columns.Add(Theme.StatusColumn(120));
        gv.Columns.Add(new GridViewColumn { Header = "TeamViewer ID", DisplayMemberBinding = new Binding("TeamViewer"), Width = 110 });
        gv.Columns.Add(new GridViewColumn { Header = "Setup v.", DisplayMemberBinding = new Binding("Version"), Width = 60 });
        gv.Columns.Add(new GridViewColumn { Header = "Software", DisplayMemberBinding = new Binding("Software"), Width = 100 });
        _pcList.View = gv;
        var copyId = new MenuItem { Header = "Copy TeamViewer ID" };
        copyId.Click += (_, _) =>
        {
            if (_pcList.SelectedItem is PcItem p && p.TeamViewer != "") Clipboard.SetText(p.TeamViewer.Replace(" ", ""));
        };
        _pcList.ContextMenu = new ContextMenu { Items = { copyId } };

        _titleBox.Text = _d.Config.Title;
        RefreshTiles();

        // PCs view: action buttons
        var pcButtons = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        pcButtons.Children.Add(Btn("Push to selected", async (_, _) => await PushAsync(_pcList.SelectedItems.Cast<PcItem>().ToList()), "PrimaryButton"));
        pcButtons.Children.Add(Btn("Push to all", async (_, _) => await PushAsync(_pcs.ToList())));
        pcButtons.Children.Add(Btn("Unlock to desktop", async (_, _) => await RemoteAsync("unlock", "Unlock",
            "leave kiosk mode and show the normal Windows desktop. Restart the PC to lock it again")));
        pcButtons.Children.Add(Btn("Restart", async (_, _) => await RemoteAsync("restart", "Restart", "restart in 10 seconds")));
        pcButtons.Children.Add(Btn("Shut down", async (_, _) => await RemoteAsync("shutdown", "Shut down", "shut down in 10 seconds"), "DangerButton"));
        pcButtons.Children.Add(Btn("Add PC...", (_, _) => AddPc()));
        pcButtons.Children.Add(Btn("Remove", (_, _) => RemovePc()));
        pcButtons.Children.Add(Btn("Check now", async (_, _) => await RefreshAsync()));
        pcButtons.Children.Add(Btn("Update software...", async (_, _) => await UpdateSoftwareAsync()));
        pcButtons.Children.Add(Btn("Kiosk options...", async (_, _) => await OptionsAsync()));
        var toolsBtn = Btn("Tools \u25BE", (_, _) => { });
        var toolsMenu = new ContextMenu();
        void Item(string text, Func<Task> act)
        {
            var mi = new MenuItem { Header = text };
            mi.Click += async (_, _) => await act();
            toolsMenu.Items.Add(mi);
        }
        Item("Create the Share folder (no password)...", ShareSetupAsync);
        Item("Send a file to the PCs...", SendFileAsync);
        Item("Install a program silently...", InstallProgramAsync);
        Item("Start a program on the kiosk PCs...", StartProgramAsync);
        toolsBtn.ContextMenu = toolsMenu;
        toolsBtn.Click += (_, _) => { toolsMenu.PlacementTarget = toolsBtn; toolsMenu.IsOpen = true; };
        pcButtons.Children.Add(toolsBtn);
        var adminMenu = new ContextMenu();
        var onScreen = new MenuItem { Header = "Open on the PC's screen (as admin)" };
        void Screen(string name, string path, string args = "")
        {
            var mi = new MenuItem { Header = name };
            mi.Click += async (_, _) => await AdminLaunchAsync(name, path, args);
            onScreen.Items.Add(mi);
        }
        Screen("Command Prompt", "cmd.exe", "/k cd /d C:\\");
        Screen("PowerShell", "powershell.exe", "-NoExit -NoLogo -Command Set-Location C:\\");
        Screen("Task Manager", "taskmgr.exe");
        Screen("Registry Editor", "regedit.exe");
        Screen("Device Manager", "mmc.exe", "devmgmt.msc");
        Screen("Services", "mmc.exe", "services.msc");
        Screen("Event Viewer", "mmc.exe", "eventvwr.msc");
        Screen("Computer Management (disks, users)", "mmc.exe", "compmgmt.msc");
        Screen("Task Scheduler", "mmc.exe", "taskschd.msc");
        Screen("Programs and Features", "control.exe", "appwiz.cpl");
        Screen("Network connections", "control.exe", "ncpa.cpl");
        Screen("Resource Monitor", "resmon.exe");
        Screen("System Information", "msinfo32.exe");
        var other = new MenuItem { Header = "Other program..." };
        other.Click += async (_, _) => await AdminLaunchOtherAsync();
        onScreen.Items.Add(new Separator()); onScreen.Items.Add(other);
        adminMenu.Items.Add(onScreen);
        var gh = new MenuItem { Header = "Update software from GitHub..." };
        gh.Click += async (_, _) => await UpdateFromGitHubAsync();
        var ghSet = new MenuItem { Header = "GitHub settings..." };
        ghSet.Click += (_, _) => GitHubSettings();

        var updMenu = new ContextMenu();
        var checkAtStart = new MenuItem { Header = "Check for a new admin app version at start", IsCheckable = true, IsChecked = _d.CheckForUpdates };
        checkAtStart.Click += (_, _) => { _d.CheckForUpdates = checkAtStart.IsChecked; _d.Save(); };
        var autoApp = new MenuItem { Header = "Install admin app updates automatically (no question)", IsCheckable = true, IsChecked = _d.AutoUpdateApp };
        autoApp.Click += (_, _) => { _d.AutoUpdateApp = autoApp.IsChecked; _d.Save(); };
        var checkNow = new MenuItem { Header = "Check for an admin app update now" };
        checkNow.Click += async (_, _) => await CheckAppUpdateAsync(true);
        var kioskAuto = new MenuItem
        {
            Header = "Kiosk PCs update themselves from GitHub when idle (push the setup to apply)",
            IsCheckable = true, IsChecked = _d.Config.AutoUpdate
        };
        kioskAuto.Click += (_, _) => { _d.Config.AutoUpdate = kioskAuto.IsChecked; _d.Save(); _status.Text = "Saved. Use \"Push to selected PCs\" to send this to the PCs."; };
        var newKey = new MenuItem { Header = "Create update signing key..." };
        newKey.Click += (_, _) => CreateSigningKey();
        updMenu.Items.Add(gh); updMenu.Items.Add(ghSet); updMenu.Items.Add(new Separator()); updMenu.Items.Add(checkNow); updMenu.Items.Add(checkAtStart); updMenu.Items.Add(autoApp);
        updMenu.Items.Add(new Separator()); updMenu.Items.Add(kioskAuto);
        updMenu.Items.Add(new Separator()); updMenu.Items.Add(newKey);
        var remote = new MenuItem { Header = "Console, processes, services, programs, screenshot..." };
        remote.Click += (_, _) => OpenRemoteTools();
        adminMenu.Items.Add(remote);

        var uninstallBtn = Btn("Uninstall kiosk...", async (_, _) => await UninstallAsync());
        pcButtons.Children.Add(uninstallBtn);

        // passwords menu
        var pwMenu = new ContextMenu();
        void PwItem(string text, Action act) { var mi = new MenuItem { Header = text }; mi.Click += (_, _) => act(); pwMenu.Items.Add(mi); }
        PwItem("Friendly password (simple view)...", () => SetFriendlyPassword());
        PwItem("Start password (admin)...", () => SetStartPassword());
        PwItem("Exit password (kiosk)...", () => SetPassword());
        PwItem("Pairing key...", () => ShowKey());

        // PCs view
        _banner.Visibility = Visibility.Collapsed;
        _banner.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#FFF1E3")!;
        _banner.BorderBrush = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#FFC999")!;
        _banner.BorderThickness = new Thickness(1.5);
        _banner.CornerRadius = new CornerRadius(14);
        _banner.Padding = new Thickness(16, 8, 8, 0);
        _banner.Margin = new Thickness(0, 0, 0, 14);
        var bannerRow = new DockPanel();
        var bannerLater = Btn("Later", (_, _) => _banner.Visibility = Visibility.Collapsed);
        var bannerGo = Btn("Update now", async (_, _) => { if (_pendingRelease != null) await InstallAppUpdateAsync(_pendingRelease); }, "OrangeButton");
        DockPanel.SetDock(bannerLater, Dock.Right); DockPanel.SetDock(bannerGo, Dock.Right);
        bannerRow.Children.Add(bannerLater); bannerRow.Children.Add(bannerGo);
        _bannerText.VerticalAlignment = VerticalAlignment.Center; _bannerText.Margin = new Thickness(0, 0, 0, 8);
        bannerRow.Children.Add(_bannerText);
        _banner.Child = bannerRow;

        var stats = new UniformGrid { Rows = 1, Margin = new Thickness(0, 0, 0, 14) };
        var s1 = Theme.Stat("PCs online", _statOnline); s1.Margin = new Thickness(0, 0, 14, 0);
        var s2 = Theme.Stat("Offline", _statOffline); s2.Margin = new Thickness(0, 0, 14, 0);
        var s3 = Theme.Stat("Not on the newest software", _statOld);
        stats.Children.Add(s1); stats.Children.Add(s2); stats.Children.Add(s3);

        var listCard = Theme.Card(_pcList);
        var pcsView = new Grid();
        pcsView.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        pcsView.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        pcsView.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        pcsView.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_banner, 0); Grid.SetRow(stats, 1); Grid.SetRow(pcButtons, 2); Grid.SetRow(listCard, 3);
        pcsView.Children.Add(_banner); pcsView.Children.Add(stats); pcsView.Children.Add(pcButtons); pcsView.Children.Add(listCard);

        // Kids' screen view
        var tileButtons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        tileButtons.Children.Add(Btn("+ Steam game...", async (_, _) => await AddSteamAsync(), "PrimaryButton"));
        tileButtons.Children.Add(Btn("+ Program...", (_, _) => AddProgram(), "PrimaryButton"));
        tileButtons.Children.Add(Btn("+ Chrome", (_, _) => AddChrome(), "PrimaryButton"));
        tileButtons.Children.Add(Btn("Rename...", (_, _) => RenameTile()));
        tileButtons.Children.Add(Btn("Close first...", (_, _) => EditCloseFirst()));
        tileButtons.Children.Add(Btn("Delete when closed...", (_, _) => EditDeleteOnClose()));
        tileButtons.Children.Add(Btn("Change path...", (_, _) => EditPath()));
        tileButtons.Children.Add(Btn("Set image...", (_, _) => SetImage()));
        tileButtons.Children.Add(Btn("Screen text...", (_, _) => EditScreenText()));
        tileButtons.Children.Add(Btn("Up", (_, _) => MoveTile(-1)));
        tileButtons.Children.Add(Btn("Down", (_, _) => MoveTile(1)));
        tileButtons.Children.Add(Btn("Remove", (_, _) => RemoveTile(), "DangerButton"));

        var titleRow = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var logoBtn = Btn("Logo...", (_, _) => LogoSettings());
        logoBtn.Margin = new Thickness(10, 0, 0, 0);
        DockPanel.SetDock(logoBtn, Dock.Right);
        titleRow.Children.Add(logoBtn);
        titleRow.Children.Add(new TextBlock { Text = "Title on the kids' screen:  ", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold });
        titleRow.Children.Add(_titleBox);

        var tilesPanel = new DockPanel { Margin = new Thickness(18) };
        DockPanel.SetDock(titleRow, Dock.Top);
        DockPanel.SetDock(tileButtons, Dock.Bottom);
        tilesPanel.Children.Add(titleRow);
        tilesPanel.Children.Add(tileButtons);
        tilesPanel.Children.Add(_tileList);
        var tilesView = new Grid { Visibility = Visibility.Collapsed };
        tilesView.Children.Add(Theme.Card(tilesPanel));

        // sidebar
        var navPcs = Theme.Nav("PCs", Theme.IconMonitor);
        var navTiles = Theme.Nav("Kids' screen", Theme.IconGrid);
        var navAdmin = Theme.Nav("Admin tools", Theme.IconTool);
        var navTweaks = Theme.Nav("Tweaks", Theme.IconZap);
        var navUpd = Theme.Nav("Updates", Theme.IconRefresh);
        var navPw = Theme.Nav("Passwords and key", Theme.IconLock);

        var heading = new TextBlock { FontSize = 26, FontWeight = FontWeights.Bold, Foreground = Theme.Text };
        var subheading = new TextBlock { FontSize = 14, Foreground = Theme.Muted, Margin = new Thickness(0, 2, 0, 16) };
        void Show(bool pcs)
        {
            pcsView.Visibility = pcs ? Visibility.Visible : Visibility.Collapsed;
            tilesView.Visibility = pcs ? Visibility.Collapsed : Visibility.Visible;
            navPcs.Tag = pcs ? "active" : null;
            navTiles.Tag = pcs ? null : "active";
            heading.Text = pcs ? "Kids' PCs" : "What the kids see";
            subheading.Text = pcs ? "Everything you manage, in one place" : "The tiles on the kids' screen, in this order. Use \"Push to selected\" on the PCs page to send changes.";
        }
        void Open(System.Windows.Controls.ContextMenu m, UIElement target)
        {
            m.PlacementTarget = target;
            m.Placement = System.Windows.Controls.Primitives.PlacementMode.Right;
            m.IsOpen = true;
        }
        navPcs.Click += (_, _) => Show(true);
        navTiles.Click += (_, _) => Show(false);
        navAdmin.Click += (_, _) => Open(adminMenu, navAdmin);
        navTweaks.Click += async (_, _) => await TweaksAsync();
        navUpd.Click += (_, _) => Open(updMenu, navUpd);
        navPw.Click += (_, _) => Open(pwMenu, navPw);
        Show(true);

        var brandRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 4, 0, 24) };
        try
        {
            brandRow.Children.Add(new System.Windows.Controls.Image
            {
                Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/icon.png")),
                Width = 40, Height = 40, Margin = new Thickness(0, 0, 12, 0)
            });
        }
        catch { }
        var brandText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        brandText.Children.Add(new TextBlock { Text = "Kiosk Admin", FontSize = 17, FontWeight = FontWeights.Bold, Foreground = System.Windows.Media.Brushes.White });
        brandText.Children.Add(new TextBlock { Text = "Build " + BuildInfo.ReleaseTag, FontSize = 12, Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#9FD3D3")! });
        brandRow.Children.Add(brandText);

        var navStack = new StackPanel();
        foreach (var n in new[] { navPcs, navTiles, navAdmin, navTweaks, navUpd, navPw }) navStack.Children.Add(n);

        var sideBottom = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        var secureNote = new TextBlock
        {
            Text = "All updates are signature checked.", TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = new Thickness(8, 0, 8, 10),
            Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#CDEBEB")!
        };
        sideBottom.Children.Add(secureNote);
        if (!string.IsNullOrEmpty(_d.LockHash))
        {
            var lockApp = new Button { Content = "Lock app", Style = Theme.Get("NavButton"), HorizontalContentAlignment = HorizontalAlignment.Center, Margin = new Thickness(0) };
            lockApp.Click += (_, _) =>
            {
                _d.Config.Title = _titleBox.Text; _d.Save();
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true }); } catch { }
                Application.Current.Shutdown();
            };
            sideBottom.Children.Add(lockApp);
        }

        var side = new DockPanel { Margin = new Thickness(16, 20, 16, 18) };
        DockPanel.SetDock(brandRow, Dock.Top);
        DockPanel.SetDock(sideBottom, Dock.Bottom);
        side.Children.Add(brandRow);
        side.Children.Add(sideBottom);
        side.Children.Add(navStack);
        var sideBorder = new Border
        {
            Child = side,
            Background = new System.Windows.Media.LinearGradientBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#083A3D"),
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0B6163"), 90)
        };

        // status messages disappear on their own (not the "working..." ones, which are replaced by the result)
        _status.Foreground = Theme.Good;
        _status.FontWeight = FontWeights.SemiBold;
        _status.Margin = new Thickness(0, 10, 0, 0);
        var fade = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        fade.Tick += (_, _) => { fade.Stop(); _status.Text = ""; };
        System.ComponentModel.DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock))
            .AddValueChanged(_status, (_, _) =>
            {
                fade.Stop();
                if (_status.Text.Length > 0 && !_status.Text.EndsWith("...")) fade.Start();
            });

        var views = new Grid();
        views.Children.Add(pcsView);
        views.Children.Add(tilesView);

        var main = new DockPanel { Margin = new Thickness(28, 24, 28, 18) };
        var head = new StackPanel();
        head.Children.Add(heading); head.Children.Add(subheading);
        DockPanel.SetDock(head, Dock.Top);
        DockPanel.SetDock(_status, Dock.Bottom);
        main.Children.Add(head);
        main.Children.Add(_status);
        main.Children.Add(views);

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(sideBorder, 0); Grid.SetColumn(main, 1);
        root.Children.Add(sideBorder); root.Children.Add(main);
        Content = root;

        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();
        Loaded += async (_, _) => { await CheckAppUpdateAsync(false); await RefreshAsync(); };
        Closing += (_, _) => { _d.Config.Title = _titleBox.Text; _d.Save(); };
    }

    static Button Btn(string text, RoutedEventHandler click, string? style = null)
    {
        var b = new Button { Content = text };
        if (style != null && Theme.Get(style) is Style st) b.Style = st;
        b.Click += click;
        return b;
    }

    void UpdateStats()
    {
        int online = _pcs.Count(p => p.Status == "Online");
        _statOnline.Text = $"{online} / {_pcs.Count}";
        _statOffline.Text = (_pcs.Count - online).ToString();
        _statOld.Text = _pcs.Count(p => p.Status == "Online" && p.Software != BuildInfo.Revision).ToString();
    }

    void ShowBanner(ReleaseInfo rel)
    {
        _pendingRelease = rel;
        _bannerText.Text = $"A new version is available: {rel.Tag}.";
        _banner.Visibility = Visibility.Visible;
    }

    // ---------- PCs ----------

    void AddPc()
    {
        var host = Ui.Prompt(this, "Add kiosk PC", "Computer name or IP address of the kids' PC:");
        if (string.IsNullOrWhiteSpace(host) || _d.Pcs.Any(p => p.Host.Equals(host, StringComparison.OrdinalIgnoreCase))) return;
        _d.Pcs.Add(new PcEntry { Host = host });
        _pcs.Add(new PcItem { Host = host, Name = host });
        _d.Save();
        UpdateStats();
        _ = RefreshAsync();
    }

    void RemovePc()
    {
        foreach (var pc in _pcList.SelectedItems.Cast<PcItem>().ToList())
        {
            _pcs.Remove(pc);
            _d.Pcs.RemoveAll(p => p.Host == pc.Host);
        }
        _d.Save();
        UpdateStats();
    }

    async Task RefreshAsync()
    {
        if (_refreshing || _busy) return;
        _refreshing = true;
        try
        {
            await Task.WhenAll(_pcs.ToList().Select(async pc =>
            {
                try
                {
                    var r = await Net.CallAsync(pc.Host, Key, "ping", null, 4000);
                    if (r.Ok)
                    {
                        var info = Json.From<PingInfo>(r.Data!)!;
                        pc.Name = info.PcName; pc.Status = "Online"; pc.Version = info.ConfigVersion.ToString();
                        pc.Software = string.IsNullOrEmpty(info.Revision) ? "older" : info.Revision;
                        pc.TeamViewer = info.TeamViewerId;
                    }
                    else pc.Status = r.Error ?? "Error";
                }
                catch (SocketException) { pc.Status = "Offline"; }
                catch (OperationCanceledException) { pc.Status = "No answer"; }
                catch { pc.Status = "Wrong key / no reply"; }
            }));
        }
        finally { _refreshing = false; UpdateStats(); }
    }

    async Task UninstallAsync()
    {
        var targets = _pcList.SelectedItems.Cast<PcItem>().ToList();
        if (targets.Count == 0) { _status.Text = "Select one or more PCs in the list first."; return; }

        var tooOld = targets.Where(t => !AgentAtLeast(t.Software, "2026-10-25-l")).ToList();
        var names = string.Join(", ", targets.Select(t => t.Name));
        var remove = new CheckBox
        {
            Content = "Also delete the Kiosk Windows account and its files (cannot be undone)",
            Margin = new Thickness(0, 10, 0, 4)
        };
        if (!Ui.Ask(this, "Uninstall kiosk",
                Ui.Label("Remove the kiosk software from: " + names),
                Ui.Label("The PC is cleaned up and restarts to the normal Windows login in about a minute. " +
                         "It is also removed from this list (add it again after a new install)."),
                remove)) return;

        bool removeAccount = remove.IsChecked == true;
        if (removeAccount && MessageBox.Show(this,
                $"Delete the Kiosk account and everything saved in it on: {names}?\n\nThis cannot be undone.",
                "Uninstall kiosk", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        var send = targets.Except(tooOld).ToList();
        _status.Text = "Uninstalling...";
        var json = Json.To(new UninstallRequest(removeAccount));
        var results = await Task.WhenAll(send.Select(async pc =>
        {
            try
            {
                var r = await Net.CallAsync(pc.Host, Key, "uninstall", json, 30000);
                if (r.Ok) Dispatcher.Invoke(() => { _pcs.Remove(pc); _d.Pcs.RemoveAll(p => p.Host == pc.Host); });
                return (Pc: pc.Name, Ok: r.Ok, Err: r.Error);
            }
            catch (Exception ex) { return (Pc: pc.Name, Ok: false, Err: ex.Message); }
        }));

        _d.Save();
        int ok = results.Count(r => r.Ok);
        var failed = results.Where(r => !r.Ok).Select(r => $"{r.Pc} ({r.Err})")
            .Concat(tooOld.Select(t => $"{t.Name} (software too old: use Update software... first)")).ToList();
        _status.Text = $"Uninstall started on {ok} of {targets.Count} PCs." +
                       (failed.Count > 0 ? " Failed: " + string.Join(", ", failed) + "." : "") +
                       (ok > 0 ? " The PCs were removed from this list and will restart in about a minute." : "");
    }

    async Task RemoteAsync(string cmd, string title, string what)
    {
        var targets = _pcList.SelectedItems.Cast<PcItem>().ToList();
        if (targets.Count == 0) { _status.Text = "Select one or more PCs in the list first."; return; }
        var names = string.Join(", ", targets.Select(t => t.Name));
        var ask = $"{title}: {names}?\n\nThe PC will {what}.";
        if (MessageBox.Show(this, ask, title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        _status.Text = $"{title}...";
        var results = await Task.WhenAll(targets.Select(async pc =>
        {
            try
            {
                var r = await Net.CallAsync(pc.Host, Key, cmd, null, 8000);
                return (Pc: pc.Name, Ok: r.Ok, Err: r.Error);
            }
            catch (Exception ex) { return (Pc: pc.Name, Ok: false, Err: ex.Message); }
        }));

        int ok = results.Count(r => r.Ok);
        var failed = results.Where(r => !r.Ok).Select(r => $"{r.Pc} ({r.Err})");
        _status.Text = $"{title}: sent to {ok} of {results.Length} PCs." + (ok < results.Length ? " Failed: " + string.Join(", ", failed) : "");
    }

    // ---------- kiosk options (password, Ctrl+Alt+Del) ----------

    sealed record OptRes(PcItem Pc, bool Ok, string Err, bool Restart, string Notes);

    // PCs whose agent is older than this admin build would silently ignore the password, so they are refused
    static bool AgentAtLeast(string software, string minimumRevision) =>
        software.Length > 0 && char.IsDigit(software[0]) && string.CompareOrdinal(software, minimumRevision) >= 0;
    static bool AgentKnowsPassword(string software) => AgentAtLeast(software, "2026-10-25-c");

    // ---------- tools: share folder, files, installs, start a program ----------

    const string ToolsRev = "2026-10-27-c";

    List<PcItem>? PickPcs(out List<PcItem> tooOld, string minRev = ToolsRev)
    {
        var targets = _pcList.SelectedItems.Cast<PcItem>().ToList();
        tooOld = new();
        if (targets.Count == 0) { _status.Text = "Select one or more PCs in the list first."; return null; }
        tooOld = targets.Where(t => !AgentAtLeast(t.Software, minRev)).ToList();
        return targets.Except(tooOld).ToList();
    }

    string TooOldText(List<PcItem> tooOld) =>
        tooOld.Count == 0 ? "" : " Skipped (software too old, use Update software... first): " + string.Join(", ", tooOld.Select(t => t.Name)) + ".";

    const string AdminRev = "2026-10-27-d";

    async Task AdminLaunchAsync(string name, string path, string args)
    {
        var send = PickPcs(out var tooOld, AdminRev);
        if (send == null) return;
        if (send.Count == 0) { _status.Text = "Those PCs need Update software... first."; return; }
        var json = Json.To(new LaunchAdminRequest(path, args));
        _status.Text = $"Opening {name}...";
        var results = await Task.WhenAll(send.Select(async pc =>
        {
            try
            {
                var r = await Net.CallAsync(pc.Host, Key, "launch-admin", json, 20000);
                return (pc.Name, r.Ok, r.Error ?? "");
            }
            catch (Exception ex) { return (pc.Name, false, ex.Message); }
        }));
        var failed = results.Where(r => !r.Item2).Select(r => $"{r.Name} ({r.Item3})").ToList();
        _status.Text = $"{name} opened on {results.Length - failed.Count} of {results.Length} PCs." +
                       (failed.Count > 0 ? " Failed: " + string.Join(", ", failed) + "." : "") + TooOldText(tooOld);
    }

    async Task AdminLaunchOtherAsync()
    {
        var text = Ui.Prompt(this, "Open a program as admin", "Full path of the program, then any arguments (for example: C:\\Windows\\System32\\notepad.exe C:\\file.txt):");
        if (string.IsNullOrWhiteSpace(text)) return;
        text = text.Trim();
        string path, args;
        if (text.StartsWith('"')) { var end = text.IndexOf('"', 1); if (end < 0) end = text.Length - 1; path = text[1..end]; args = text[(end + 1)..].Trim(); }
        else { var sp = text.IndexOf(' '); path = sp < 0 ? text : text[..sp]; args = sp < 0 ? "" : text[(sp + 1)..].Trim(); }
        await AdminLaunchAsync(path, path, args);
    }

    void OpenRemoteTools()
    {
        var send = PickPcs(out var tooOld, AdminRev);
        if (send == null) return;
        if (send.Count == 0) { _status.Text = "Those PCs need Update software... first."; return; }
        if (tooOld.Count > 0) _status.Text = TooOldText(tooOld).Trim();
        new RemoteToolsWindow(this, Key, send).Show();
    }

    async Task ShareSetupAsync()
    {
        var send = PickPcs(out var tooOld);
        if (send == null) return;
        if (send.Count == 0) { _status.Text = "Those PCs need Update software... first."; return; }
        if (MessageBox.Show(this,
                "Create C:\\Share on " + string.Join(", ", send.Select(t => t.Name)) + " and share it as \"Share\" WITHOUT a password?\n\n" +
                "Anyone on the network can then read and write in that folder (and the PC accepts password-free guest logins). " +
                "Use this only on a network you trust.", "Share folder", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        _status.Text = "Setting up Share...";
        var results = await Task.WhenAll(send.Select(async pc =>
        {
            try
            {
                var r = await Net.CallAsync(pc.Host, Key, "share-setup", null, 60000);
                if (!r.Ok) return $"{pc.Name}: failed ({r.Error})";
                var res = Json.From<OptionsResult>(r.Data!)!;
                return $"{pc.Name}: " + string.Join(" ", res.Notes);
            }
            catch (Exception ex) { return $"{pc.Name}: failed ({ex.Message})"; }
        }));
        _status.Text = string.Join("  ", results) + TooOldText(tooOld) + "  The PCs are reached as \\\\PCNAME\\Share.";
    }

    /// <summary>Sends one file to a PC in pieces. Returns the path it ended up at on the PC.</summary>
    async Task<string> UploadAsync(PcItem pc, string file, string dest, string id)
    {
        var info = new FileInfo(file);
        var r = await Net.CallAsync(pc.Host, Key, "upload-start", Json.To(new UploadStart(id, info.Name, info.Length, dest)), 30000);
        if (!r.Ok) throw new Exception(r.Error ?? "error");

        const int chunk = 3 * 1024 * 1024;
        var buf = new byte[chunk];
        await using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        long pos = 0;
        int lastPct = -1;
        while (true)
        {
            int n = await fs.ReadAsync(buf.AsMemory(0, chunk));
            if (n == 0) break;
            var data = Json.To(new UploadChunk(id, pos, Convert.ToBase64String(buf, 0, n)));
            for (int attempt = 1; ; attempt++)   // the offset makes a repeated piece harmless
            {
                try
                {
                    r = await Net.CallAsync(pc.Host, Key, "upload-chunk", data, 60000);
                    if (!r.Ok) throw new Exception(r.Error ?? "error");
                    break;
                }
                catch when (attempt < 3) { await Task.Delay(1000); }
            }
            pos += n;
            int pct = info.Length == 0 ? 100 : (int)(pos * 100 / info.Length);
            if (pct != lastPct) { lastPct = pct; pc.Status = $"Sending {pct}%"; }
        }
        r = await Net.CallAsync(pc.Host, Key, "upload-finish", Json.To(new UploadFinish(id)), 60000);
        if (!r.Ok) throw new Exception(r.Error ?? "error");
        return r.Data ?? "";
    }

    async Task SendFileAsync()
    {
        var send = PickPcs(out var tooOld);
        if (send == null) return;
        if (send.Count == 0) { _status.Text = "Those PCs need Update software... first."; return; }

        var dlg = new OpenFileDialog { Title = "Pick the file(s) to send to C:\\Share on the PCs", Multiselect = true };
        if (dlg.ShowDialog(this) != true) return;
        var files = dlg.FileNames;

        _busy = true;
        try
        {
            var results = await Task.WhenAll(send.Select(async pc =>
            {
                try
                {
                    foreach (var f in files) await UploadAsync(pc, f, "share", Guid.NewGuid().ToString("N"));
                    pc.Status = "Online";
                    return (pc.Name, true, "");
                }
                catch (Exception ex) { pc.Status = "Online"; return (pc.Name, false, ex.Message); }
            }));
            var failed = results.Where(r => !r.Item2).Select(r => $"{r.Name} ({r.Item3})").ToList();
            _status.Text = $"Sent {files.Length} file(s) to C:\\Share on {results.Length - failed.Count} of {results.Length} PCs." +
                           (failed.Count > 0 ? " Failed: " + string.Join(", ", failed) + "." : "") + TooOldText(tooOld);
        }
        finally { _busy = false; }
    }

    async Task InstallProgramAsync()
    {
        var send = PickPcs(out var tooOld);
        if (send == null) return;
        if (send.Count == 0) { _status.Text = "Those PCs need Update software... first."; return; }

        var fromAdmin = new RadioButton { Content = "A file on this PC (it is sent to the kiosk PCs first)", IsChecked = true, Margin = new Thickness(0, 6, 0, 2) };
        var fileBox = new TextBox { Margin = new Thickness(18, 2, 0, 2), IsReadOnly = true };
        var browse = new Button { Content = "Browse...", Margin = new Thickness(18, 2, 0, 6), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 2, 10, 2) };
        browse.Click += (_, _) =>
        {
            var dlg = new OpenFileDialog { Title = "Pick the installer", Filter = "Installers (*.exe;*.msi)|*.exe;*.msi|All files|*.*", InitialDirectory = _d.InstallFolder ?? "" };
            if (dlg.ShowDialog(this) == true) { fileBox.Text = dlg.FileName; _d.InstallFolder = Path.GetDirectoryName(dlg.FileName); }
        };
        var onPc = new RadioButton { Content = "A file that is already on the kiosk PCs (full path there)", Margin = new Thickness(0, 6, 0, 2) };
        var pcPath = new TextBox { Margin = new Thickness(18, 2, 0, 6), Text = "C:\\Share\\" };

        var args = new ComboBox { IsEditable = true, Text = _d.InstallArgs, Margin = new Thickness(0, 2, 0, 6) };
        foreach (var a in new[] { "/S", "/s", "/silent", "/quiet /norestart", "/VERYSILENT /NORESTART", "-s -noreboot", "-s -noreboot -noeula", "/qn /norestart" })
            args.Items.Add(a);
        var timeout = new TextBox { Text = "60", Width = 60, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 6) };
        var del = new CheckBox { Content = "Delete the installer from the PC afterwards (sent files only)", IsChecked = true, Margin = new Thickness(0, 4, 0, 0) };
        var onScreen = new CheckBox
        {
            Content = "Show the installer on the PC's screen (use this if nothing happens in the background)",
            Margin = new Thickness(0, 6, 0, 0)
        };

        if (!Ui.Ask(this, "Install a program silently",
                Ui.Label("Install on: " + string.Join(", ", send.Select(t => t.Name))),
                Ui.Label("Which installer?"), fromAdmin, fileBox, browse, onPc, pcPath,
                Ui.Label("Silent switch / arguments (depends on the installer: /S, /s, /silent, /quiet ... NVIDIA: -s -noreboot -noeula; .msi files default to /qn /norestart):"), args,
                Ui.Label("Give up after (minutes):"), timeout, del, onScreen)) return;

        string file = fileBox.Text;
        string remotePath = pcPath.Text.Trim();
        bool upload = fromAdmin.IsChecked == true;
        if (upload && !File.Exists(file)) { _status.Text = "Pick an installer file first."; return; }
        if (!upload && (remotePath.Length < 4 || remotePath.EndsWith("\\"))) { _status.Text = "Type the full path of the installer on the PCs."; return; }
        if (!int.TryParse(timeout.Text, out var minutes)) minutes = 60;
        _d.InstallArgs = args.Text.Trim();
        _d.Save();

        if (onScreen.IsChecked == true)
        {
            var oldForScreen = send.Where(t => !AgentAtLeast(t.Software, "2026-10-27-e")).ToList();
            tooOld = tooOld.Concat(oldForScreen).ToList();
            send = send.Except(oldForScreen).ToList();
            if (send.Count == 0) { _status.Text = "Those PCs need Update software... first." + TooOldText(tooOld); return; }
        }

        _busy = true;
        var restart = new List<PcItem>();
        try
        {
            var results = await Task.WhenAll(send.Select(async pc =>
            {
                try
                {
                    string? uploadId = null;
                    if (upload)
                    {
                        uploadId = Guid.NewGuid().ToString("N");
                        await UploadAsync(pc, file, "install", uploadId);
                    }
                    pc.Status = "Installing...";
                    var req = new InstallRequest(uploadId, upload ? null : remotePath, _d.InstallArgs, minutes, del.IsChecked == true, onScreen.IsChecked == true);
                    var r = await Net.CallAsync(pc.Host, Key, "install-start", Json.To(req), 30000);
                    if (!r.Ok) throw new Exception(r.Error ?? "error");
                    var job = Json.From<InstallJob>(r.Data!)!.JobId;

                    var until = DateTime.UtcNow.AddMinutes(minutes + 2);
                    while (DateTime.UtcNow < until)
                    {
                        await Task.Delay(5000);
                        try
                        {
                            r = await Net.CallAsync(pc.Host, Key, "install-status", Json.To(new JobRef(job)), 15000);
                        }
                        catch { continue; }   // the PC may be busy or restarting for a moment
                        if (!r.Ok) throw new Exception(r.Error ?? "error");
                        var st = Json.From<InstallStatus>(r.Data!)!;
                        if (!st.Done) continue;
                        if (st.RestartNeeded) lock (restart) restart.Add(pc);
                        pc.Status = "Online";
                        return (pc.Name, st.Success, st.Message);
                    }
                    pc.Status = "Online";
                    return (pc.Name, false, "no answer from the PC");
                }
                catch (Exception ex) { pc.Status = "Online"; return (pc.Name, false, ex.Message); }
            }));
            _status.Text = string.Join("  ", results.Select(r => $"{r.Name}: {(r.Item2 ? "OK" : "FAILED")} - {r.Item3}.")) + TooOldText(tooOld);
        }
        finally { _busy = false; }

        if (restart.Count > 0 &&
            MessageBox.Show(this, "These PCs need a restart to finish the install:\n\n" + string.Join(", ", restart.Select(p => p.Name)) +
                "\n\nRestart them now (10 second countdown)?", "Restart needed", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            await Task.WhenAll(restart.Select(async pc => { try { await Net.CallAsync(pc.Host, Key, "restart", null, 8000); } catch { } }));
            _status.Text += " Restart sent.";
        }
    }

    async Task StartProgramAsync()
    {
        var send = PickPcs(out var tooOld);
        if (send == null) return;
        if (send.Count == 0) { _status.Text = "Those PCs need Update software... first."; return; }

        var win = new ProgramsWindow(this, _d, string.Join(", ", send.Select(t => t.Name)));
        var shown = win.ShowDialog();
        _d.Save();
        if (shown != true || win.Chosen == null) return;

        var json = Json.To(new LaunchRequest(win.Chosen.Path, win.Chosen.Args));
        _status.Text = $"Starting {win.Chosen.Name}...";
        var results = await Task.WhenAll(send.Select(async pc =>
        {
            try
            {
                var r = await Net.CallAsync(pc.Host, Key, "launch", json, 15000);
                return (pc.Name, r.Ok, r.Error ?? "");
            }
            catch (Exception ex) { return (pc.Name, false, ex.Message); }
        }));
        var failed = results.Where(r => !r.Item2).Select(r => $"{r.Name} ({r.Item3})").ToList();
        _status.Text = $"{win.Chosen.Name} sent to {results.Length - failed.Count} of {results.Length} PCs (it opens on the kids' screen if someone is logged in)." +
                       (failed.Count > 0 ? " Failed: " + string.Join(", ", failed) + "." : "") + TooOldText(tooOld);
    }

    async Task TweaksAsync()
    {
        var targets = _pcList.SelectedItems.Cast<PcItem>().ToList();
        if (targets.Count == 0) { _status.Text = "Select one or more PCs in the list first."; return; }

        var ticked = _d.Tweaks ?? TweakCatalog.Defaults;
        var boxes = TweakCatalog.All.Select(t => (Info: t, Box: new CheckBox
        {
            Content = new TextBlock
            {
                Inlines =
                {
                    new System.Windows.Documents.Run(t.Title) { FontWeight = FontWeights.SemiBold },
                    new System.Windows.Documents.Run("\n" + t.Description) { Foreground = System.Windows.Media.Brushes.Gray }
                }
            },
            IsChecked = ticked.Contains(t.Id), Margin = new Thickness(0, 5, 0, 5)
        })).ToList();

        var fields = new List<UIElement> { Ui.Label("Send to: " + string.Join(", ", targets.Select(t => t.Name))) };
        fields.AddRange(boxes.Select(b => (UIElement)b.Box));
        if (!Ui.Ask(this, "Tweaks", fields.ToArray())) return;

        var ids = boxes.Where(b => b.Box.IsChecked == true).Select(b => b.Info.Id).ToList();
        _d.Tweaks = ids;
        _d.Save();
        if (ids.Count == 0) { _status.Text = "No tweaks ticked, nothing sent."; return; }

        var tooOld = targets.Where(t => !AgentAtLeast(t.Software, "2026-10-27-a")).ToList();
        var send = targets.Except(tooOld).ToList();
        var json = Json.To(new TweaksRequest(ids));
        _status.Text = "Sending tweaks...";

        var results = await Task.WhenAll(send.Select(async pc =>
        {
            try
            {
                var r = await Net.CallAsync(pc.Host, Key, "tweaks", json, 60000);
                if (!r.Ok) return new OptRes(pc, false, r.Error ?? "error", false, "");
                var res = Json.From<OptionsResult>(r.Data!)!;
                return new OptRes(pc, true, "", res.RestartNeeded, string.Join(" ", res.Notes));
            }
            catch (Exception ex) { return new OptRes(pc, false, ex.Message, false, ""); }
        }));

        var ok = results.Where(r => r.Ok).ToList();
        var failed = results.Where(r => !r.Ok).Select(r => $"{r.Pc.Name} ({r.Err})")
            .Concat(tooOld.Select(t => $"{t.Name} (its software is too old: use Update software... first)")).ToList();
        var notes = string.Join(" ", ok.Where(r => r.Notes != "").Select(r => $"{r.Pc.Name}: {r.Notes}"));
        _status.Text = $"Tweaks applied on {ok.Count} of {targets.Count} PCs." +
                       (failed.Count > 0 ? " Failed: " + string.Join(", ", failed) + "." : "") +
                       (notes != "" ? " " + notes : "");

        var needRestart = ok.Where(r => r.Restart).Select(r => r.Pc).ToList();
        if (needRestart.Count > 0 &&
            MessageBox.Show(this, "These PCs must restart for some tweaks to take effect:\n\n" +
                string.Join(", ", needRestart.Select(p => p.Name)) + "\n\nRestart them now (10 second countdown)?",
                "Restart needed", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            await Task.WhenAll(needRestart.Select(async pc =>
            {
                try { await Net.CallAsync(pc.Host, Key, "restart", null, 8000); } catch { }
            }));
            _status.Text += " Restart sent.";
        }
    }

    async Task OptionsAsync()
    {
        var targets = _pcList.SelectedItems.Cast<PcItem>().ToList();
        if (targets.Count == 0) { _status.Text = "Select one or more PCs in the list first."; return; }

        var pw = new TextBox { Margin = new Thickness(0, 4, 0, 8) };
        var block = new CheckBox
        {
            Content = "Block Ctrl+Alt+Del (turns off the Delete keys on the PC; needs a restart)",
            IsChecked = _d.OptBlockCtrlAltDel, Margin = new Thickness(0, 4, 0, 4)
        };
        var names = string.Join(", ", targets.Select(t => t.Name));
        if (!Ui.Ask(this, "Kiosk options",
                Ui.Label("Apply to: " + names),
                Ui.Label("New password for the Kiosk Windows account (leave empty to keep the current one). Automatic login keeps working with it:"),
                pw, block)) return;

        var opts = new KioskOptions(string.IsNullOrEmpty(pw.Text) ? null : pw.Text, block.IsChecked == true);
        _d.OptBlockCtrlAltDel = opts.BlockCtrlAltDel;
        _d.Save();

        // PCs that can't take the password yet
        var tooOld = opts.Password == null ? new List<PcItem>() : targets.Where(t => !AgentKnowsPassword(t.Software)).ToList();
        var send = targets.Except(tooOld).ToList();
        var json = Json.To(opts);
        _status.Text = "Applying options...";

        var results = await Task.WhenAll(send.Select(async pc =>
        {
            try
            {
                var r = await Net.CallAsync(pc.Host, Key, "setoptions", json, 30000);
                if (!r.Ok) return new OptRes(pc, false, r.Error ?? "error", false, "");
                var res = Json.From<OptionsResult>(r.Data!)!;
                return new OptRes(pc, true, "", res.RestartNeeded, string.Join(" ", res.Notes));
            }
            catch (Exception ex) { return new OptRes(pc, false, ex.Message, false, ""); }
        }));

        var ok = results.Where(r => r.Ok).ToList();
        var failed = results.Where(r => !r.Ok).Select(r => $"{r.Pc.Name} ({r.Err})")
            .Concat(tooOld.Select(t => $"{t.Name} (its software is too old: use Update software... first)")).ToList();
        var notes = string.Join(" ", ok.Where(r => r.Notes != "").Select(r => $"{r.Pc.Name}: {r.Notes}"));
        _status.Text = $"Options applied on {ok.Count} of {targets.Count} PCs." +
                       (failed.Count > 0 ? " Failed: " + string.Join(", ", failed) + "." : "") +
                       (notes != "" ? " " + notes : "");

        var needRestart = ok.Where(r => r.Restart).Select(r => r.Pc).ToList();
        if (needRestart.Count > 0 &&
            MessageBox.Show(this, "These PCs must restart for the change to take effect:\n\n" +
                string.Join(", ", needRestart.Select(p => p.Name)) + "\n\nRestart them now (10 second countdown)?",
                "Restart needed", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            await Task.WhenAll(needRestart.Select(async pc =>
            {
                try { await Net.CallAsync(pc.Host, Key, "restart", null, 8000); } catch { }
            }));
            _status.Text += " Restart sent.";
        }
    }

    // ---------- software updates ----------

    async Task UpdateSoftwareAsync()
    {
        var targets = _pcList.SelectedItems.Cast<PcItem>().ToList();
        if (targets.Count == 0) { _status.Text = "Select one or more PCs in the list first."; return; }

        var dlg = new OpenFolderDialog
        {
            Title = "Pick the 'kiosk' folder made by Build.ps1 (dist\\kiosk)",
            InitialDirectory = _d.UpdateFolder ?? ""
        };
        if (dlg.ShowDialog(this) != true) return;
        var folder = dlg.FolderName;
        if (!File.Exists(Path.Combine(folder, "Agent", "KioskAgent.exe")) ||
            !File.Exists(Path.Combine(folder, "Launcher", "KioskLauncher.exe")))
        {
            MessageBox.Show(this, "That folder must contain the Agent and Launcher folders (it is dist\\kiosk after running Build.ps1).", "Update software");
            return;
        }
        _d.UpdateFolder = folder;
        _d.Save();
        await RunUpdateAsync(targets, folder, true);
    }

    async Task RunUpdateAsync(List<PcItem> targets, string folder, bool confirm)
    {
        var names = string.Join(", ", targets.Select(t => t.Name));
        if (confirm && MessageBox.Show(this, $"Send the new software to: {names}?\n\nOnly changed files are sent. The kids' screen restarts for a moment.",
                "Update software", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        _busy = true;
        try
        {
            _status.Text = "Reading the new build...";
            var local = await Task.Run(() => FileHasher.Hash(folder));
            var results = await Task.WhenAll(targets.Select(pc => UpdateOneAsync(pc, folder, local)));
            var failed = results.Where(r => !r.Ok).Select(r => $"{r.Pc} ({r.Msg})").ToList();
            var okLines = results.Where(r => r.Ok).Select(r => $"{r.Pc}: {r.Msg}");
            _status.Text = failed.Count == 0
                ? "Update done. " + string.Join("; ", okLines)
                : $"{results.Length - failed.Count} of {results.Length} updated. Failed: " + string.Join(", ", failed);
        }
        finally { _busy = false; }
        await Task.Delay(8000);   // the agent restarts itself after an update
        await RefreshAsync();
    }

    bool GitHubSettings()
    {
        var repo = new TextBox { Text = GitHubUpdate.Repo(_d), Margin = new Thickness(0, 4, 0, 4) };
        var token = new PasswordBox { Margin = new Thickness(0, 4, 0, 4) };
        if (!Ui.Ask(this, "GitHub settings",
                Ui.Label("Repository, written owner/name:"), repo,
                Ui.Label(string.IsNullOrEmpty(_d.GitHubToken)
                    ? "Access token (only needed if the repository is private; otherwise leave empty):"
                    : "Access token (leave empty to keep the saved one):"), token)) return false;

        var r = repo.Text.Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(r, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$"))
        {
            MessageBox.Show(this, "Write the repository as owner/name.", "GitHub settings");
            return false;
        }
        if (token.Password.Length > 0) _d.GitHubToken = GitHubUpdate.Protect(token.Password.Trim());
        _d.GitHubRepo = r;
        _d.Save();
        _status.Text = "GitHub settings saved.";
        return true;
    }

    async Task UpdateFromGitHubAsync()
    {
        var targets = _pcList.SelectedItems.Cast<PcItem>().ToList();
        if (targets.Count == 0) { _status.Text = "Select one or more PCs in the list first."; return; }

        _busy = true;
        try
        {
            _status.Text = "Checking GitHub...";
            var repo = GitHubUpdate.Repo(_d);
            var token = GitHubUpdate.Token(_d);
            var rel = await Releases.LatestAsync(Http, repo, token);
            var notes = string.IsNullOrWhiteSpace(rel.Body) ? "" : "\n\nWhat's new:\n" + (rel.Body.Length > 1200 ? rel.Body[..1200] + "..." : rel.Body);
            var signed = BuildInfo.SigningPublicKey != "" ? "" : "\n\nWARNING: this admin app has no signing key built in, so the download cannot be checked.";
            if (MessageBox.Show(this,
                    $"Latest release on GitHub: {rel.Tag}\n\nSend it to: {string.Join(", ", targets.Select(t => t.Name))}?\n\nOnly changed files are sent. The kids' screen restarts for a moment.{notes}{signed}",
                    "Update from GitHub", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) { _status.Text = ""; return; }
            _status.Text = "Downloading " + rel.Tag + "...";
            var work = GitHubUpdate.NewWorkFolder();
            var (zip, _) = await Releases.DownloadVerifiedAsync(Http, repo, token, rel, GitHubUpdate.KioskAsset, work, requireSignature: false);
            var folder = GitHubUpdate.ExtractKiosk(zip);
            _busy = false;
            await RunUpdateAsync(targets, folder, false);
        }
        catch (Exception ex) { _status.Text = "GitHub: " + ex.Message; }
        finally { _busy = false; }
    }

    /// <summary>Looks for a newer admin app; asks (or just installs) and restarts into the new version.</summary>
    async Task CheckAppUpdateAsync(bool manual)
    {
        if (!manual && !_d.CheckForUpdates) return;
        try
        {
            if (manual) _status.Text = "Checking for an update...";
            var rel = await AppUpdater.CheckAsync(Http, _d, manual);
            if (rel == null) { if (manual) _status.Text = BuildInfo.ReleaseTag == "dev" ? "This is a local build; there is nothing to compare." : "You have the newest version."; return; }
            if (!manual && rel.Tag == _d.SkippedTag) return;

            var choice = UpdateChoice.Update;
            if (manual || !_d.AutoUpdateApp)
            {
                var win = new UpdatePromptWindow(IsLoaded ? this : null, rel);
                win.ShowDialog();
                choice = win.Choice;
            }
            if (choice == UpdateChoice.Skip) { _d.SkippedTag = rel.Tag; _d.Save(); _status.Text = ""; return; }
            if (choice != UpdateChoice.Update) { _status.Text = ""; ShowBanner(rel); return; }
            await InstallAppUpdateAsync(rel);
        }
        catch (Exception ex) { _status.Text = "Update: " + ex.Message; }
    }

    async Task InstallAppUpdateAsync(ReleaseInfo rel)
    {
        _busy = true;
        try
        {
            if (await AppUpdater.UpdateAsync(Http, _d, rel, t => _status.Text = t))
                Application.Current.Shutdown();
        }
        catch (Exception ex) { _status.Text = "Update: " + ex.Message; }
        finally { _busy = false; }
    }

    void CreateSigningKey()
    {
        if (MessageBox.Show(this,
                "This creates the key pair that protects your updates.\n\n" +
                "- The PRIVATE key goes into a GitHub secret named SIGNING_KEY (you copy it once).\n" +
                "- The PUBLIC key is saved as signing-public-key.txt, which you add to the repository.\n\n" +
                "If you already have a key, do not make a new one: apps built with the old key will refuse updates signed with the new one.\n\nContinue?",
                "Update signing key", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        var (priv, pub) = Signing.NewKeyPair();
        var box = new TextBox { Text = priv, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Height = 90, Margin = new Thickness(0, 4, 0, 4) };
        var copy = new Button { Content = "Copy private key", Padding = new Thickness(10, 4, 10, 4), HorizontalAlignment = HorizontalAlignment.Left };
        copy.Click += (_, _) => { Clipboard.SetText(priv); copy.Content = "Copied"; };
        var save = new Button { Content = "Save signing-public-key.txt...", Padding = new Thickness(10, 4, 10, 4), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
        save.Click += (_, _) =>
        {
            var dlg = new SaveFileDialog { FileName = "signing-public-key.txt", Filter = "Text|*.txt" };
            if (dlg.ShowDialog(this) == true) { File.WriteAllText(dlg.FileName, pub); save.Content = "Saved"; }
        };
        Ui.Ask(this, "Update signing key",
            Ui.Label("1. Copy the private key and paste it into the GitHub secret SIGNING_KEY. Keep it secret; it is not stored anywhere else."), box, copy,
            Ui.Label("2. Save the public key file and put it in the root of your repository."), save);
    }

    async Task<(string Pc, bool Ok, string Msg)> UpdateOneAsync(PcItem pc, string folder, Dictionary<string, string> local)
    {
        try
        {
            pc.Status = "Comparing...";
            var r = await Net.CallAsync(pc.Host, Key, "filehashes", null, 60000);
            if (!r.Ok) return (pc.Name, false, r.Error ?? "error");
            var remote = Json.From<Dictionary<string, string>>(r.Data!)!;

            var changed = local
                .Where(kv => !remote.TryGetValue(kv.Key, out var h) || !h.Equals(kv.Value, StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Key).ToList();
            if (changed.Count == 0) { pc.Status = "Online"; return (pc.Name, true, "already up to date"); }

            var zip = await Task.Run(() => BuildZip(folder, changed));
            var id = Guid.NewGuid().ToString("N");

            r = await Net.CallAsync(pc.Host, Key, "update-start", Json.To(new UpdateStart(id, zip.Length)), 30000);
            if (!r.Ok) return (pc.Name, false, r.Error ?? "error");

            const int chunk = 3 * 1024 * 1024;
            for (int pos = 0, n = 0; pos < zip.Length; pos += chunk, n++)
            {
                int len = Math.Min(chunk, zip.Length - pos);
                r = await Net.CallAsync(pc.Host, Key, "update-chunk",
                    Json.To(new UpdateChunk(id, n, Convert.ToBase64String(zip, pos, len))), 60000);
                if (!r.Ok) return (pc.Name, false, r.Error ?? "error");
                pc.Status = $"Uploading {(pos + len) * 100L / zip.Length}%";
            }

            pc.Status = "Installing...";
            r = await Net.CallAsync(pc.Host, Key, "update-apply", Json.To(new UpdateApply(id)), 120000);
            if (!r.Ok) { pc.Status = "Update failed"; return (pc.Name, false, r.Error ?? "error"); }

            pc.Status = "Restarting...";
            return (pc.Name, true, $"{changed.Count} file(s) updated");
        }
        catch (Exception ex)
        {
            pc.Status = "Update failed";
            return (pc.Name, false, ex.Message);
        }
    }

    static byte[] BuildZip(string folder, List<string> relativePaths)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
            foreach (var rel in relativePaths)
                zip.CreateEntryFromFile(Path.Combine(folder, rel.Replace('/', '\\')), rel, CompressionLevel.Optimal);
        return ms.ToArray();
    }

    // ---------- tiles ----------

    void RefreshTiles(int select = -1)
    {
        _tileList.ItemsSource = null;
        _tileList.ItemsSource = _d.Config.Tiles;
        if (select >= 0 && select < _d.Config.Tiles.Count) _tileList.SelectedIndex = select;
    }

    Tile? SelectedTile()
    {
        if (_tileList.SelectedItem is Tile t) return t;
        _status.Text = "Select a tile in the list first.";
        return null;
    }

    static async Task<string?> DownloadSteamHeader(string appId)
    {
        try
        {
            var bytes = await Http.GetByteArrayAsync($"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/header.jpg");
            return Convert.ToBase64String(bytes);
        }
        catch { return null; }
    }

    async Task AddSteamAsync()
    {
        var dlg = new SteamPickerWindow(this, _d.Pcs.Select(p => p.Host).ToList(), Key);
        if (dlg.ShowDialog() != true) return;
        _status.Text = "Adding games...";
        foreach (var g in dlg.Selected)
        {
            if (_d.Config.Tiles.Any(t => t.SteamAppId == g.AppId)) continue;
            var img = await DownloadSteamHeader(g.AppId);
            _d.Config.Tiles.Add(new Tile { Name = g.Name, Type = TileType.Steam, SteamAppId = g.AppId, ImageBase64 = img });
        }
        _d.Save();
        RefreshTiles(_d.Config.Tiles.Count - 1);
        _status.Text = "Games added. Remember to push the setup.";
    }

    void EditScreenText()
    {
        var dlg = new ScreenTextWindow(this, _d.Config.Texts, _d.Config.Tiles.Count, _titleBox.Text);
        if (dlg.ShowDialog() != true) return;
        _d.Config.Texts = dlg.Result;
        _titleBox.Text = dlg.TitleText;
        _d.Config.Title = dlg.TitleText;
        _d.Save();
        _status.Text = "Screen text saved. Push the setup to show it on the kids' PCs.";
    }

    /// <summary>
    /// Paths into a user's AppData and into version folders (Roblox "version-1a2b3c...", Squirrel apps "app-1.2.3", "1.2.3.4")
    /// break when the program updates or on another account. This offers to turn them into %LOCALAPPDATA% and *.
    /// </summary>
    string OfferUpdateProofPath(string exe)
    {
        var smart = exe;
        smart = System.Text.RegularExpressions.Regex.Replace(smart, @"^[A-Za-z]:\\Users\\[^\\]+\\AppData\\Local\\", @"%LOCALAPPDATA%\", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        smart = System.Text.RegularExpressions.Regex.Replace(smart, @"^[A-Za-z]:\\Users\\[^\\]+\\AppData\\Roaming\\", @"%APPDATA%\", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        smart = System.Text.RegularExpressions.Regex.Replace(smart, @"\\version-[0-9a-fA-F]{6,}\\", @"\version-*\");
        smart = System.Text.RegularExpressions.Regex.Replace(smart, @"\\app-\d+(\.\d+)+\\", @"\app-*\", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        smart = System.Text.RegularExpressions.Regex.Replace(smart, @"\\\d+(\.\d+){2,}\\", @"\*\");
        if (smart == exe) return exe;

        var ans = MessageBox.Show(this,
            "This path contains a folder that changes when the program updates (or belongs to one user account).\n\n" +
            "Use this update-proof path instead?\n\n" + smart + "\n\n(Yes = the tile keeps working after updates; the newest version is started.)",
            "Add program", MessageBoxButton.YesNo, MessageBoxImage.Question);
        return ans == MessageBoxResult.Yes ? smart : exe;
    }

    void EditPath()
    {
        var t = SelectedTile();
        if (t == null) return;
        if (t.Type != TileType.Exe) { _status.Text = "Only program tiles have a path."; return; }
        var text = Ui.Prompt(this, "Change path", "Path of the .exe on the kids' PCs. * stands for a folder that changes (for example a version folder), %LOCALAPPDATA% for the Kiosk account's AppData:", t.ExePath ?? "");
        if (string.IsNullOrWhiteSpace(text)) return;
        var exe = OfferUpdateProofPath(text.Trim().Trim('"'));
        if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !(Path.IsPathRooted(exe) || exe.StartsWith('%')))
        {
            MessageBox.Show(this, "The path must end in .exe and start with a drive letter or %.", "Change path");
            return;
        }
        t.ExePath = exe;
        _d.Save();
        _status.Text = "Path changed. Push the setup to apply it.";
    }

    const string RobloxNames = "RobloxPlayerBeta, RobloxPlayerLauncher, RobloxCrashHandler";

    const string RobloxLogin = @"%LOCALAPPDATA%\Roblox\LocalStorage\*";

    void EditDeleteOnClose()
    {
        var t = SelectedTile();
        if (t == null) return;
        if (t.Type == TileType.Steam) { _status.Text = "\"Delete when closed\" works for programs and Chrome, not for Steam games."; return; }

        var box = new TextBox
        {
            Text = t.DeleteOnClose ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Height = 110, Width = 520, Margin = new Thickness(0, 6, 0, 6)
        };
        var preset = Btn("Fill in the Roblox login", (_, _) =>
        {
            if (!box.Text.Contains(RobloxLogin)) box.Text = (box.Text.TrimEnd() + "\r\n" + RobloxLogin).Trim();
        });
        preset.HorizontalAlignment = HorizontalAlignment.Left;

        if (!Ui.Ask(this, "Delete when closed: " + t.Name,
                Ui.Label("Files or folders to delete from the Kiosk account when this program has been closed for a while " +
                         "(and when the kiosk starts). One per line. You can use % variables like %LOCALAPPDATA% and * in the last part. " +
                         "Only things inside the Kiosk account's own profile can be deleted. Leave empty to turn this off."),
                box, preset,
                Ui.Label("Roblox keeps the saved login in the LocalStorage folder (I believe the file is RobloxCookies.dat). " +
                         "Test it on one PC first. Log in, close Roblox, wait a minute, then check that the login is gone.")))
            return;
        t.DeleteOnClose = string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim();
        _d.Save();
        _status.Text = t.DeleteOnClose == null ? "Delete when closed is off for this tile. Push the setup to apply it."
                                               : "Delete when closed is set. Push the setup to apply it.";
    }

    void EditCloseFirst()
    {
        var t = SelectedTile();
        if (t == null) return;
        var initial = t.CloseFirst ?? "";
        if (initial == "" && (t.Name + t.ExePath).Contains("roblox", StringComparison.OrdinalIgnoreCase)) initial = RobloxNames;
        var text = Ui.Prompt(this, "Close first",
            "Programs to close before this tile starts (names without .exe, separated by commas). If one of them is still running when " +
            "the tile is clicked, it is closed and the program starts fresh. Leave empty to turn this off:", initial);
        if (text == null) return;
        t.CloseFirst = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        _d.Save();
        _status.Text = t.CloseFirst == null ? "Close first is off for this tile. Push the setup to apply it."
                                            : "Close first is set. Push the setup to apply it.";
    }

    void AddProgram()
    {
        string pathText = "", nameText = "";
        while (true)
        {
            var path = new TextBox { Text = pathText, Width = 460, Margin = new Thickness(0, 4, 0, 4) };
            var name = new TextBox { Text = nameText, Width = 460, Margin = new Thickness(0, 4, 0, 4) };
            path.Loaded += (_, _) => { path.Focus(); path.SelectAll(); };
            if (!Ui.Ask(this, "Add program",
                    Ui.Label("Path to the .exe on the kids' PCs (paste it, for example C:\\Games\\Minecraft\\Minecraft.exe). You may use * for a folder that changes with updates and %LOCALAPPDATA% for the Kiosk account's AppData:"), path,
                    Ui.Label("Name to show on the tile (leave empty to use the file name):"), name)) return;

            // "Copy as path" in Windows Explorer adds quotes: remove them
            var exe = path.Text.Trim().Trim('"').Trim();
            pathText = path.Text; nameText = name.Text;

            exe = OfferUpdateProofPath(exe);

            if (exe.Length == 0 || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !(Path.IsPathRooted(exe) || exe.StartsWith('%')))
            {
                MessageBox.Show(this, "Please paste the full path to an .exe file, starting with a drive letter, for example C:\\Games\\Game\\Game.exe", "Add program");
                continue;
            }

            var tileName = name.Text.Trim();
            if (tileName.Length == 0) tileName = Path.GetFileNameWithoutExtension(exe);
            _d.Config.Tiles.Add(new Tile
            {
                Name = tileName, Type = TileType.Exe, ExePath = exe,
                CloseFirst = exe.Contains("roblox", StringComparison.OrdinalIgnoreCase) ? RobloxNames : null,
                // the program is not on this PC, so no icon can be read: use "Set image..." for a picture
                ImageBase64 = !ExePathResolver.IsPattern(exe) && File.Exists(exe) ? Ui.IconOfExe(exe) : null
            });
            _d.Save();
            RefreshTiles(_d.Config.Tiles.Count - 1);
            _status.Text = "Program added. Remember to push the setup. Use \"Set image...\" if you want a picture on the tile.";
            return;
        }
    }

    void AddChrome()
    {
        var name = Ui.Prompt(this, "Browser tile", "Name to show on the tile:", "Internet");
        if (string.IsNullOrWhiteSpace(name)) return;
        _d.Config.Tiles.Add(new Tile { Name = name, Type = TileType.Chrome, ImageBase64 = Ui.IconOfExe(ChromeLocator.Find() ?? "") });
        _d.Save();
        RefreshTiles(_d.Config.Tiles.Count - 1);
    }

    void RenameTile()
    {
        if (SelectedTile() is not { } t) return;
        var name = Ui.Prompt(this, "Rename", "New name:", t.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        t.Name = name;
        _d.Save();
        RefreshTiles(_tileList.SelectedIndex);
    }

    void SetImage()
    {
        if (SelectedTile() is not { } t) return;
        var dlg = new OpenFileDialog { Filter = "Pictures|*.png;*.jpg;*.jpeg;*.bmp;*.gif", Title = "Pick a picture for the tile" };
        if (dlg.ShowDialog(this) != true) return;
        var b64 = Ui.ImageFileToPngBase64(dlg.FileName);
        if (b64 == null) { _status.Text = "Could not read that picture."; return; }
        t.ImageBase64 = b64;
        _d.Save();
    }

    void LogoSettings()
    {
        string? b64 = _d.Config.LogoBase64;
        var preview = new Image { Height = 90, Stretch = System.Windows.Media.Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 6) };
        var note = new TextBlock { Foreground = Theme.Muted, Margin = new Thickness(0, 0, 0, 4) };
        void Refresh()
        {
            preview.Source = Ui.FromBase64(b64);
            note.Text = b64 == null ? "No logo on the kids' screen." : "This logo is shown on the kids' screen.";
        }
        Refresh();

        var pick = Btn("Choose a picture...", (_, _) =>
        {
            var dlg = new OpenFileDialog { Filter = "Pictures|*.png;*.jpg;*.jpeg;*.bmp;*.gif", Title = "Pick your logo (a PNG with a transparent background looks best)" };
            if (dlg.ShowDialog(this) != true) return;
            var conv = Ui.ImageFileToPngBase64(dlg.FileName);
            if (conv == null) { MessageBox.Show(this, "Could not read that picture.", "Logo"); return; }
            b64 = conv; Refresh();
        });
        var remove = Btn("Remove logo", (_, _) => { b64 = null; Refresh(); }, "DangerButton");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(pick); buttons.Children.Add(remove);

        var corners = new (string Text, LogoCorner Value)[]
        {
            ("Lower right corner (default)", LogoCorner.BottomRight), ("Lower left corner", LogoCorner.BottomLeft),
            ("Upper right corner", LogoCorner.TopRight), ("Upper left corner", LogoCorner.TopLeft)
        };
        var radios = corners.Select(c => new RadioButton
        {
            Content = c.Text, GroupName = "logoCorner", Margin = new Thickness(0, 3, 0, 3), IsChecked = _d.Config.LogoCorner == c.Value
        }).ToList();

        if (!Ui.Ask(this, "Logo on the kids' screen",
                Ui.Label("Pick a logo and where it sits on the screen."), note, preview, buttons,
                Ui.Label("Place it in:"), radios[0], radios[1], radios[2], radios[3])) return;

        _d.Config.LogoBase64 = b64;
        _d.Config.LogoCorner = corners[Math.Max(0, radios.FindIndex(r => r.IsChecked == true))].Value;
        _d.Save();
        _status.Text = "Logo saved. Use \"Push to selected\" on the PCs page to send it to the kids' PCs.";
    }

    void MoveTile(int delta)
    {
        int i = _tileList.SelectedIndex, j = i + delta;
        if (i < 0 || j < 0 || j >= _d.Config.Tiles.Count) return;
        (_d.Config.Tiles[i], _d.Config.Tiles[j]) = (_d.Config.Tiles[j], _d.Config.Tiles[i]);
        _d.Save();
        RefreshTiles(j);
    }

    void RemoveTile()
    {
        if (SelectedTile() is not { } t) return;
        _d.Config.Tiles.Remove(t);
        _d.Save();
        RefreshTiles();
    }

    // ---------- password, key, push ----------

    void SetPassword()
    {
        var a = new PasswordBox { Margin = new Thickness(0, 4, 0, 4) };
        var b = new PasswordBox { Margin = new Thickness(0, 4, 0, 4) };
        if (!Ui.Ask(this, "Exit password",
                Ui.Label("Password that lets you leave kiosk mode (Ctrl+Alt+Shift+K on the kiosk PC):"), a,
                Ui.Label("Repeat it:"), b)) return;
        if (a.Password.Length < 4 || a.Password != b.Password)
        {
            MessageBox.Show(this, "The passwords must match and be at least 4 characters.", "Exit password");
            return;
        }
        _d.Config.ExitPasswordHash = Passwords.Hash(a.Password);
        _d.Save();
        _status.Text = "Exit password set. Push the setup to apply it.";
    }

    void SetStartPassword()
    {
        bool has = !string.IsNullOrEmpty(_d.LockHash);
        var cur = new PasswordBox { Margin = new Thickness(0, 4, 0, 4) };
        var a = new PasswordBox { Margin = new Thickness(0, 4, 0, 4) };
        var b = new PasswordBox { Margin = new Thickness(0, 4, 0, 4) };
        var fields = new List<UIElement>();
        if (has) { fields.Add(Ui.Label("Current password:")); fields.Add(cur); }
        fields.Add(Ui.Label(has ? "New password (leave both empty to remove the lock):" : "Password needed to open this admin app:"));
        fields.Add(a);
        fields.Add(Ui.Label("Repeat it:")); fields.Add(b);
        if (!Ui.Ask(this, "Start password", fields.ToArray())) return;

        if (has)
        {
            bool good;
            try { good = Passwords.Verify(cur.Password, _d.LockHash); } catch { good = false; }
            if (!good) { MessageBox.Show(this, "The current password is wrong.", "Start password"); return; }
        }
        if (a.Password.Length == 0 && b.Password.Length == 0)
        {
            if (!has) return;
            _d.LockHash = null; _d.UserLockHash = null; _d.Save();   // without the admin lock there is no friendly one either
            _status.Text = "The start password was removed (and the friendly password with it).";
            return;
        }
        if (a.Password.Length < 4 || a.Password != b.Password)
        {
            MessageBox.Show(this, "The passwords must match and be at least 4 characters.", "Start password");
            return;
        }
        _d.LockHash = Passwords.Hash(a.Password);
        _d.Save();
        _status.Text = "Start password set. It is asked for the next time the admin app opens.";
    }

    void SetFriendlyPassword()
    {
        if (string.IsNullOrEmpty(_d.LockHash))
        {
            MessageBox.Show(this, "Set the admin Start password first (button \"Start password...\"). The friendly password is the second password that opens the simple view.", "Friendly password");
            return;
        }
        bool has = !string.IsNullOrEmpty(_d.UserLockHash);
        var a = new PasswordBox { Margin = new Thickness(0, 4, 0, 4) };
        var b = new PasswordBox { Margin = new Thickness(0, 4, 0, 4) };
        if (!Ui.Ask(this, "Friendly password",
                Ui.Label("Anyone who types this password when the admin app opens sees only the PC list with Restart and Shut down." +
                         (has ? " Leave both empty to remove it." : "")),
                a, Ui.Label("Repeat it:"), b)) return;

        if (a.Password.Length == 0 && b.Password.Length == 0)
        {
            if (!has) return;
            _d.UserLockHash = null; _d.Save();
            _status.Text = "The friendly password was removed.";
            return;
        }
        if (a.Password.Length < 4 || a.Password != b.Password)
        {
            MessageBox.Show(this, "The passwords must match and be at least 4 characters.", "Friendly password");
            return;
        }
        bool same;
        try { same = Passwords.Verify(a.Password, _d.LockHash); } catch { same = false; }
        if (same)
        {
            MessageBox.Show(this, "That is the admin password. The friendly password must be a different one.", "Friendly password");
            return;
        }
        _d.UserLockHash = Passwords.Hash(a.Password);
        _d.Save();
        _status.Text = "Friendly password set. Next time the admin app opens, it opens the simple view.";
    }

    void ShowKey()
    {
        var cmd = $".\\Install-Kiosk.ps1 -Key \"{_d.KeyBase64}\"";
        var tb = new TextBox { Text = cmd, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Width = 520, Height = 70 };
        var copyKey = Btn("Copy key only", (_, _) => Clipboard.SetText(_d.KeyBase64));
        var copyCmd = Btn("Copy install command", (_, _) => Clipboard.SetText(cmd));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        row.Children.Add(copyKey); row.Children.Add(copyCmd);
        Ui.Ask(this, "Pairing key",
            Ui.Label("This key links this admin app to the kiosk PCs. Every kiosk PC is installed with the same key. Keep it private."),
            tb, row);
    }

    async Task PushAsync(List<PcItem> targets)
    {
        if (string.IsNullOrEmpty(_d.Config.ExitPasswordHash))
        {
            MessageBox.Show(this, "Set an exit password first (button \"Exit password...\").", "Push");
            return;
        }
        if (targets.Count == 0) { _status.Text = "No PCs to push to. Add or select a PC."; return; }

        _d.Config.Title = _titleBox.Text;
        _d.Config.Version++;
        _d.Save();
        var json = Json.To(_d.Config);
        _status.Text = $"Pushing setup v{_d.Config.Version} to {targets.Count} PC(s)...";

        var results = await Task.WhenAll(targets.Select(async pc =>
        {
            try
            {
                var r = await Net.CallAsync(pc.Host, Key, "push", json, 30000);
                if (r.Ok) { pc.Status = "Online"; pc.Version = _d.Config.Version.ToString(); }
                return (Pc: pc.Host, Ok: r.Ok, Err: r.Error);
            }
            catch (Exception ex) { return (Pc: pc.Host, Ok: false, Err: ex.Message); }
        }));

        int ok = results.Count(r => r.Ok);
        var failed = results.Where(r => !r.Ok).Select(r => $"{r.Pc} ({r.Err})");
        _status.Text = $"Pushed to {ok} of {results.Length} PCs." + (ok < results.Length ? " Failed: " + string.Join(", ", failed) : "");
        var oldPcs = targets.Where(t => !AgentAtLeast(t.Software, "2026-10-25-g")).Select(t => t.Name).ToList();
        if (_d.Config.Texts.Count > 0 && oldPcs.Count > 0)
            _status.Text += " The screen text will not show on " + string.Join(", ", oldPcs) + " until you run Update software... on them.";
    }
}
