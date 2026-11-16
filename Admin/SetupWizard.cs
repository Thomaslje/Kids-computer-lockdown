using System.Windows;
using System.Windows.Controls;
using KioskShared;

namespace KioskAdmin;

/// <summary>What the wizard collected.</summary>
public sealed class WizardResult
{
    public bool JoinMode;
    public List<string> Hosts = new();
    public string ExitPassword = "", KioskPassword = "", AdminName = "", AdminPassword = "", JoinPassword = "";
    public bool BlockCtrlAltDel;
    public List<string> Tweaks = new();
    public string Title = "";
    public string StartPassword = "", FriendlyPassword = "";
    public string? JoinedKeyB64;     // join mode: the pairing key fetched from a PC
}

/// <summary>First-start wizard: pick default passwords, an admin account and tweaks, then Finish pushes them to every PC.</summary>
public sealed class SetupWizard : Window
{
    sealed record Page(string Title, string Help, UIElement Body, Func<string?> Validate);

    public WizardResult Result { get; } = new();

    readonly List<Page> _newPages = new(), _joinPages = new();
    List<Page> _pages => _modeJoin.IsChecked == true ? _joinPages : _newPages;
    readonly TextBlock _title = new() { FontSize = 20, FontWeight = FontWeights.SemiBold };
    readonly TextBlock _help = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 12), MaxWidth = 560 };
    readonly TextBlock _error = new() { Foreground = System.Windows.Media.Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, MaxWidth = 560, Margin = new Thickness(0, 8, 0, 0) };
    readonly ContentControl _body = new();
    readonly TextBlock _step = new() { Foreground = System.Windows.Media.Brushes.Gray, Margin = new Thickness(0, 0, 0, 4) };
    readonly Button _back = new() { Content = "Back", Width = 90, Margin = new Thickness(0, 0, 8, 0) };
    readonly Button _next = new() { Content = "Next", Width = 150, IsDefault = true };
    readonly Button _cancel = new() { Content = "Skip wizard", Width = 100, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
    int _index;
    readonly StackPanel _stepper = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };

    // fields
    readonly RadioButton _modeNew = new() { Content = "Set up my PCs now (first time, or PCs that have no admin app yet)", IsChecked = true, Margin = new Thickness(0, 4, 0, 4) };
    readonly RadioButton _modeJoin = new() { Content = "My PCs already work with another admin app: join them (needs their joining password)", Margin = new Thickness(0, 4, 0, 4) };
    readonly TextBox _hostsJoin = new() { AcceptsReturn = true, MinHeight = 90, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 2, 0, 4) };
    readonly TextBox _hosts = new() { AcceptsReturn = true, MinHeight = 90, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly PasswordBox _start1 = new(), _start2 = new(), _friend1 = new(), _friend2 = new(), _exit1 = new(), _exit2 = new(), _kiosk1 = new(), _kiosk2 = new(), _adminPw1 = new(), _adminPw2 = new(), _join1 = new(), _join2 = new(), _joinOld = new();
    readonly CheckBox _blockCad = new() { Content = "Block Ctrl+Alt+Del on the kids' PCs (also disables the Delete key there)", Margin = new Thickness(0, 10, 0, 0) };
    readonly TextBox _adminName = new();
    readonly TextBox _screenTitle = new();
    readonly List<(TweakInfo Info, CheckBox Box)> _tweakBoxes = new();
    readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap, MaxWidth = 560 };
    readonly TextBlock _joinStatus = new() { TextWrapping = TextWrapping.Wrap, MaxWidth = 560, Margin = new Thickness(0, 8, 0, 0) };
    bool _joined;

    public SetupWizard(Window owner, AdminData data)
    {
        Title = "Kiosk Admin setup";
        Owner = owner; SizeToContent = SizeToContent.Height; Width = 780; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        Theme.Skin(this);
        if (Theme.Get("OrangeButton") is Style ps) _next.Style = ps;

        foreach (var c in new Control[] { _exit1, _exit2, _kiosk1, _kiosk2, _adminPw1, _adminPw2, _join1, _join2, _joinOld, _start1, _start2, _friend1, _friend2, _adminName, _hosts, _screenTitle })
            c.Margin = new Thickness(0, 2, 0, 4);
        _blockCad.IsChecked = data.OptBlockCtrlAltDel;
        _screenTitle.Text = data.Config.Title;
        _hosts.Text = string.Join("\n", data.Pcs.Select(p => p.Host));

        BuildPages(data);
        _joinPages.Add(_newPages[0]);
        _joinPages.Add(JoinPage());

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(_cancel); buttons.Children.Add(_back); buttons.Children.Add(_next);
        var card = new StackPanel();
        card.Children.Add(_title); card.Children.Add(_help);
        card.Children.Add(_body); card.Children.Add(_error);
        var root = new StackPanel { Margin = new Thickness(24) };
        _stepper.Margin = new Thickness(6, 0, 6, 16);
        root.Children.Add(_stepper);
        root.Children.Add(Theme.Card(card, new Thickness(30, 24, 30, 26)));
        _step.HorizontalAlignment = HorizontalAlignment.Center; _step.Margin = new Thickness(0, 12, 0, 0);
        root.Children.Add(_step);
        root.Children.Add(buttons);
        Content = root;

        _back.Click += (_, _) => { _error.Text = ""; GoTo(_index - 1); };
        _next.Click += (_, _) => Next();
        _cancel.Click += (_, _) => DialogResult = false;
        GoTo(0);
    }

    static StackPanel Stack(params UIElement[] items)
    {
        var sp = new StackPanel();
        foreach (var i in items) sp.Children.Add(i);
        return sp;
    }

    void BuildPages(AdminData data)
    {
        // 0: welcome
        _newPages.Add(new Page("Welcome", "This wizard sets the defaults for your kids' PCs and, when you press Finish, sends them to every PC in your list. " +
            "You can run it again later from Setup > Setup wizard.", Stack(_modeNew, _modeJoin), () => null));

        // 1: PCs (setup mode) / join (join mode)
        _newPages.Add(new Page("Your PCs", "Type the name or IP address of every kids' PC, one per line. The kiosk software must already be installed on them (KioskSetup.exe with this admin app's pairing key).",
            Stack(_hosts), () =>
            {
                var hosts = ParseHosts(_hosts);
                return hosts.Count == 0 ? "Type at least one PC." : null;
            }));

        // 2: passwords
        _newPages.Add(new Page("Kiosk passwords",
            "The exit password lets you leave kiosk mode on a PC (Ctrl+Alt+Shift+K). The Kiosk account password is the Windows password of the kids' account; automatic login is kept working with it. Leave the Kiosk account password empty to keep the current one.",
            Stack(Ui.Label("Exit password (at least 4 characters)"), _exit1, Ui.Label("Repeat the exit password"), _exit2,
                  Ui.Label("Kiosk account password (optional)"), _kiosk1, Ui.Label("Repeat the Kiosk account password"), _kiosk2, _blockCad),
            () =>
            {
                if (_exit1.Password.Length < 4) return "The exit password must be at least 4 characters.";
                if (_exit1.Password != _exit2.Password) return "The two exit passwords are not the same.";
                if (_kiosk1.Password != _kiosk2.Password) return "The two Kiosk account passwords are not the same.";
                return null;
            }));

        // 3: admin account
        _newPages.Add(new Page("Administrator account",
            "Creates an administrator account on every PC, for you to use on the Windows desktop. Leave the name empty to skip this. If the account already exists on a PC, its password is changed.",
            Stack(Ui.Label("Account name"), _adminName, Ui.Label("Password"), _adminPw1, Ui.Label("Repeat the password"), _adminPw2),
            () =>
            {
                var n = _adminName.Text.Trim();
                if (n.Length == 0) return null;
                if (!System.Text.RegularExpressions.Regex.IsMatch(n, @"^[A-Za-z0-9][A-Za-z0-9 ._-]{0,19}$") || n.EndsWith('.') || n.EndsWith(' '))
                    return "Account name: 1-20 letters, digits, space . _ - (not ending in a dot or space).";
                if (new[] { "administrator", "guest", "defaultaccount", "wdagutilityaccount" }.Contains(n.ToLowerInvariant()))
                    return "That is a built-in Windows account. Pick another name.";
                if (_adminPw1.Password.Length == 0) return "Type a password for the administrator account.";
                if (_adminPw1.Password != _adminPw2.Password) return "The two passwords are not the same.";
                return null;
            }));

        // 4: joining password
        _newPages.Add(new Page("Joining password",
            "Once a PC has a joining password, another admin app can only take it over by knowing this password (otherwise it needs the long pairing key). Use something long, 10+ characters. Leave it empty to skip this.",
            Stack(Ui.Label("Joining password"), _join1, Ui.Label("Repeat the joining password"), _join2),
            () =>
            {
                if (_join1.Password.Length == 0 && _join2.Password.Length == 0) return null;
                if (_join1.Password.Length < 8) return "The joining password must be at least 8 characters.";
                if (_join1.Password != _join2.Password) return "The two joining passwords are not the same.";
                return null;
            }));

        // 5: passwords for this admin app
        _newPages.Add(new Page("Admin app passwords",
            "The start password is asked for every time this admin app opens (the full app). The simple password opens a simple view with only the PC list, Restart and Shut down, handy for someone you do not want to give full control. Leave both empty to skip; the simple password needs a start password.",
            Stack(Ui.Label("Start password (full admin)"), _start1, Ui.Label("Repeat the start password"), _start2,
                  Ui.Label("Simple password (simple view, optional)"), _friend1, Ui.Label("Repeat the simple password"), _friend2),
            () =>
            {
                if (_start1.Password.Length == 0 && _start2.Password.Length == 0 && _friend1.Password.Length == 0 && _friend2.Password.Length == 0) return null;
                if (_start1.Password.Length < 4) return "The start password must be at least 4 characters.";
                if (_start1.Password != _start2.Password) return "The two start passwords are not the same.";
                if (_friend1.Password.Length == 0 && _friend2.Password.Length == 0) return null;
                if (_friend1.Password.Length < 4) return "The simple password must be at least 4 characters.";
                if (_friend1.Password != _friend2.Password) return "The two simple passwords are not the same.";
                if (_friend1.Password == _start1.Password) return "The simple password must be different from the start password.";
                return null;
            }));

        // 6: tweaks
        var ticked = data.Tweaks ?? TweakCatalog.Defaults;
        var tweakPanel = new StackPanel();
        foreach (var t in TweakCatalog.All)
        {
            var cb = new CheckBox
            {
                Content = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap, MaxWidth = 520,
                    Inlines =
                    {
                        new System.Windows.Documents.Run(t.Title) { FontWeight = FontWeights.SemiBold },
                        new System.Windows.Documents.Run("\n" + t.Description) { Foreground = System.Windows.Media.Brushes.Gray }
                    }
                },
                IsChecked = ticked.Contains(t.Id), Margin = new Thickness(0, 4, 0, 4)
            };
            _tweakBoxes.Add((t, cb));
            tweakPanel.Children.Add(cb);
        }
        _newPages.Add(new Page("Tweaks", "Tick the Windows tweaks every PC should get. These are the same ones as on the Tweaks tab.",
            new ScrollViewer { Content = tweakPanel, MaxHeight = 330, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, () => null));

        // 7: kids' screen
        _newPages.Add(new Page("Kids' screen",
            "The heading shown at the top of the kids' screen. Tiles (Steam games, programs, Chrome) are added later under Kids' screen; any tiles you have already made are sent along with everything else.",
            Stack(Ui.Label("Heading"), _screenTitle), () => null));

        // 8: summary
        _newPages.Add(new Page("Ready", "Press Finish to send everything to the PCs. PCs that are off or have old software are listed afterwards; you can run the wizard again for them.",
            Stack(_summary), () => null));
    }

    // join mode page (replaces pages 1..6)
    Page JoinPage() => new("Join your PCs",
        "Type the address of at least one PC that already works with another admin app, and its joining password. This admin app then takes over that PC's pairing key and adds all the PCs you list. " +
        "Any PCs this admin app managed before with its own key stop answering.",
        Stack(Ui.Label("PC addresses, one per line"), _hostsJoin, Ui.Label("Joining password"), _joinOld, JoinButton(), _joinStatus),
        () => _joined ? null : "Press \"Join now\" first, and wait for it to say it worked.");

    Button JoinButton()
    {
        var b = new Button { Content = "Join now", Width = 120, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
        b.Click += async (_, _) =>
        {
            var hosts = ParseHosts(_hostsJoin);
            if (hosts.Count == 0 || _joinOld.Password.Length == 0) { _joinStatus.Text = "Type the PC address(es) and the joining password."; return; }
            b.IsEnabled = false; _joinStatus.Text = "Joining...";
            string? last = null;
            foreach (var h in hosts)
            {
                try
                {
                    Result.JoinedKeyB64 = await Join.JoinAsync(h, _joinOld.Password);
                    _joined = true;
                    _joinStatus.Text = $"Joined using {h}. Press Finish.";
                    b.IsEnabled = true;
                    return;
                }
                catch (Exception ex) { last = $"{h}: {ex.Message}"; if (ex.Message.StartsWith("Wrong")) break; }
            }
            _joined = false;
            _joinStatus.Text = "Could not join. " + last;
            b.IsEnabled = true;
        };
        return b;
    }

    static List<string> ParseHosts(TextBox tb) => tb.Text.Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(h => h.Trim()).Where(h => h.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    void GoTo(int index)
    {
        _index = Math.Clamp(index, 0, _pages.Count - 1);
        var p = _pages[_index];
        _step.Text = $"Step {_index + 1} of {_pages.Count}";
        BuildStepper();
        _title.Text = p.Title; _help.Text = p.Help; _body.Content = p.Body;
        _back.IsEnabled = _index > 0;
        _next.Content = _index == _pages.Count - 1 ? "Finish" : "Next";
        if (p.Title == "Ready") _summary.Text = BuildSummary();
    }

    void BuildStepper()
    {
        _stepper.Children.Clear();
        var teal = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#0B7F80")!;
        var orange = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#FF7A1A")!;
        var soft = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#E4F2F2")!;
        var line = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#CFDFE0")!;
        for (int i = 0; i < _pages.Count; i++)
        {
            bool done = i < _index, cur = i == _index;
            var dot = new Border
            {
                Width = 30, Height = 30, CornerRadius = new CornerRadius(15), ToolTip = _pages[i].Title,
                Background = cur ? orange : done ? teal : soft,
                Child = new TextBlock
                {
                    Text = done ? "\u2713" : (i + 1).ToString(), FontWeight = FontWeights.Bold, FontSize = 13,
                    Foreground = cur || done ? System.Windows.Media.Brushes.White : Theme.Muted,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            };
            _stepper.Children.Add(dot);
            if (i < _pages.Count - 1)
                _stepper.Children.Add(new Border { Width = 34, Height = 2, Background = done ? teal : line, Margin = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
        }
    }

    string BuildSummary()
    {
        if (_modeJoin.IsChecked == true) return "This admin app will take over the pairing key from the PCs and add them to its list.";
        var hosts = ParseHosts(_hosts);
        var lines = new List<string>
        {
            $"PCs: {string.Join(", ", hosts)}",
            "Exit password: set",
            _kiosk1.Password.Length > 0 ? "Kiosk account password: will be changed (automatic login is kept)" : "Kiosk account password: unchanged",
            _blockCad.IsChecked == true ? "Ctrl+Alt+Del: blocked" : "Ctrl+Alt+Del: not blocked",
            _adminName.Text.Trim().Length > 0 ? $"Administrator account: {_adminName.Text.Trim()}" : "Administrator account: none",
            _join1.Password.Length > 0 ? "Joining password: set" : "Joining password: not set",
            _start1.Password.Length > 0 ? (_friend1.Password.Length > 0 ? "Admin app: start password and simple password set" : "Admin app: start password set") : "Admin app: no start password change",
            $"Tweaks: {_tweakBoxes.Count(t => t.Box.IsChecked == true)} ticked",
            _screenTitle.Text.Trim().Length > 0 ? $"Kids' screen heading: {_screenTitle.Text.Trim()}" : "Kids' screen heading: unchanged"
        };
        return string.Join("\n", lines);
    }

    void Next()
    {
        _error.Text = "";
        var msg = _pages[_index].Validate();
        if (msg != null) { _error.Text = msg; return; }
        if (_index < _pages.Count - 1) { GoTo(_index + 1); return; }

        Result.JoinMode = _modeJoin.IsChecked == true;
        Result.Hosts = ParseHosts(Result.JoinMode ? _hostsJoin : _hosts);
        if (!Result.JoinMode)
        {
            Result.ExitPassword = _exit1.Password; Result.KioskPassword = _kiosk1.Password; Result.BlockCtrlAltDel = _blockCad.IsChecked == true;
            Result.AdminName = _adminName.Text.Trim(); Result.AdminPassword = _adminPw1.Password;
            Result.JoinPassword = _join1.Password;
            Result.StartPassword = _start1.Password; Result.FriendlyPassword = _friend1.Password;
            Result.Title = _screenTitle.Text.Trim();
            Result.Tweaks = _tweakBoxes.Where(t => t.Box.IsChecked == true).Select(t => t.Info.Id).ToList();
        }
        DialogResult = true;
    }
}
