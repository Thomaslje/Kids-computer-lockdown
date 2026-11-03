using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KioskShared;
using Microsoft.Win32;

namespace KioskAdmin;

public class ProcRow { public int Id { get; set; } public string Name { get; set; } = ""; public int MB { get; set; } }
public class ServiceRow
{
    public string Name { get; set; } = ""; public string DisplayName { get; set; } = "";
    public string Status { get; set; } = ""; public string StartType { get; set; } = "";
}
public class ProgRow { public string Name { get; set; } = ""; public string Version { get; set; } = ""; public string Publisher { get; set; } = ""; }

/// <summary>Remote console, process list, services, installed programs and a screenshot of one kiosk PC. Everything runs as SYSTEM.</summary>
public class RemoteToolsWindow : Window
{
    readonly byte[] _key;
    readonly ComboBox _pc = new() { MinWidth = 200, DisplayMemberPath = "Name", Margin = new Thickness(0, 0, 8, 0) };
    readonly TextBlock _msg = new() { Margin = new Thickness(8, 4, 8, 6), TextWrapping = TextWrapping.Wrap };
    readonly TabControl _tabs = new() { Margin = new Thickness(8, 0, 8, 0) };

    PcItem Pc => (PcItem)_pc.SelectedItem;

    static readonly (string Name, string Shell, string Command)[] Quick =
    {
        ("Pick a quick command...", "", ""),
        ("Network: IP and MAC addresses", "powershell", "ipconfig /all; Get-NetAdapter | Select-Object Name, MacAddress, Status, LinkSpeed | Format-Table -AutoSize | Out-String -Width 200"),
        ("Disk space", "powershell", "Get-PSDrive -PSProvider FileSystem | Select-Object Name, @{n='UsedGB';e={[math]::Round($_.Used/1GB,1)}}, @{n='FreeGB';e={[math]::Round($_.Free/1GB,1)}} | Format-Table -AutoSize | Out-String"),
        ("System info, uptime, graphics driver", "powershell", "Get-CimInstance Win32_OperatingSystem | Select-Object Caption, Version, BuildNumber, LastBootUpTime | Format-List | Out-String; Get-CimInstance Win32_VideoController | Select-Object Name, DriverVersion, DriverDate | Format-List | Out-String; whoami"),
        ("Who is logged in", "cmd", "query user"),
        ("Windows Update: check what is available", "powershell", "$s=New-Object -ComObject Microsoft.Update.Session; $r=$s.CreateUpdateSearcher().Search(\"IsInstalled=0 and Type='Software'\"); \"$($r.Updates.Count) update(s) available\"; $r.Updates | ForEach-Object { ' - ' + $_.Title }"),
        ("Windows Update: download and install all", "powershell", "$s=New-Object -ComObject Microsoft.Update.Session; $r=$s.CreateUpdateSearcher().Search(\"IsInstalled=0 and Type='Software'\"); if ($r.Updates.Count -eq 0) { 'No updates.'; return }\n$c=New-Object -ComObject Microsoft.Update.UpdateColl; foreach ($u in $r.Updates) { if (-not $u.EulaAccepted) { $u.AcceptEula() }; [void]$c.Add($u); ' - ' + $u.Title }\n$d=$s.CreateUpdateDownloader(); $d.Updates=$c; 'Downloading...'; [void]$d.Download()\n$i=$s.CreateUpdateInstaller(); $i.Updates=$c; 'Installing...'; $x=$i.Install(); \"Result code $($x.ResultCode); restart needed: $($x.RebootRequired)\""),
        ("Update group policy (gpupdate)", "cmd", "gpupdate /force"),
        ("Repair Windows files (sfc /scannow, slow)", "cmd", "sfc /scannow"),
        ("Clear temporary files", "powershell", "Remove-Item $env:TEMP\\*, C:\\Windows\\Temp\\* -Recurse -Force -ErrorAction SilentlyContinue; 'Done'"),
        ("Restart the kiosk agent", "powershell", "Restart-Service KioskAgent -Force"),
    };

    public RemoteToolsWindow(Window owner, byte[] key, IEnumerable<PcItem> pcs)
    {
        _key = key;
        Owner = owner; Title = "Remote tools (run as SYSTEM)";
        Width = 900; Height = 640; MinWidth = 700; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        foreach (var p in pcs) _pc.Items.Add(p);
        _pc.SelectedIndex = 0;

        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8) };
        top.Children.Add(new TextBlock { Text = "PC:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        top.Children.Add(_pc);

        _tabs.Items.Add(ConsoleTab());
        _tabs.Items.Add(ListTab<ProcRow>("Processes",
            "$x=@(Get-Process | Sort-Object WorkingSet64 -Descending | ForEach-Object { [pscustomobject]@{Id=$_.Id;Name=$_.ProcessName;MB=[int]($_.WorkingSet64/1MB)} }); ConvertTo-Json -InputObject $x -Compress",
            new[] { ("Id", "Id", 70.0), ("Name", "Name", 260.0), ("Memory (MB)", "MB", 100.0) },
            r => r.Name + " " + r.Id,
            ("Kill process", r => $"Stop-Process -Id {r.Id} -Force")));
        _tabs.Items.Add(ListTab<ServiceRow>("Services",
            "$x=@(Get-Service | ForEach-Object { [pscustomobject]@{Name=$_.Name;DisplayName=$_.DisplayName;Status=[string]$_.Status;StartType=[string]$_.StartType} }); ConvertTo-Json -InputObject $x -Compress",
            new[] { ("Name", "Name", 170.0), ("Display name", "DisplayName", 300.0), ("Status", "Status", 80.0), ("Start type", "StartType", 90.0) },
            r => r.Name + " " + r.DisplayName,
            ("Start", r => $"Start-Service -Name '{Q(r.Name)}'"),
            ("Stop", r => $"Stop-Service -Name '{Q(r.Name)}' -Force"),
            ("Restart", r => $"Restart-Service -Name '{Q(r.Name)}' -Force")));
        _tabs.Items.Add(ListTab<ProgRow>("Installed programs",
            "$k='HKLM:\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\*','HKLM:\\Software\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\*'; $x=@(Get-ItemProperty $k -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName } | Sort-Object DisplayName -Unique | ForEach-Object { [pscustomobject]@{Name=$_.DisplayName;Version=[string]$_.DisplayVersion;Publisher=[string]$_.Publisher} }); ConvertTo-Json -InputObject $x -Compress",
            new[] { ("Name", "Name", 380.0), ("Version", "Version", 120.0), ("Publisher", "Publisher", 220.0) },
            r => r.Name + " " + r.Publisher));
        _tabs.Items.Add(ScreenshotTab());

        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(_msg, Dock.Bottom);
        root.Children.Add(top); root.Children.Add(_msg); root.Children.Add(_tabs);
        Content = root;
    }

    static string Q(string s) => s.Replace("'", "''");

    // ---------- talking to the agent ----------

    /// <summary>Runs a command on the PC and returns all its output. onText gets new text as it arrives.</summary>
    async Task<(bool Ok, int Code, string Text)> ExecAsync(string shell, string command, int timeoutSeconds,
        Action<string>? onText, CancellationToken ct)
    {
        var pc = Pc;
        var sb = new System.Text.StringBuilder();
        var r = await Net.CallAsync(pc.Host, _key, "exec-start", Json.To(new ExecRequest(shell, command, timeoutSeconds)), 20000);
        if (!r.Ok) return (false, -1, r.Error ?? "error");
        var job = Json.From<InstallJob>(r.Data!)!.JobId;

        int offset = 0, failures = 0;
        while (true)
        {
            if (ct.IsCancellationRequested)
            {
                try { await Net.CallAsync(pc.Host, _key, "exec-stop", Json.To(new JobRef(job)), 10000); } catch { }
                return (false, -1, sb + "\n[stopped]");
            }
            try
            {
                r = await Net.CallAsync(pc.Host, _key, "exec-poll", Json.To(new ExecPoll(job, offset)), 15000);
                failures = 0;
            }
            catch
            {
                if (++failures > 20) return (false, -1, sb + "\n[lost contact with the PC]");
                await Task.Delay(1500); continue;
            }
            if (!r.Ok) return (false, -1, r.Error ?? "error");
            var st = Json.From<ExecStatus>(r.Data!)!;
            if (st.Output.Length > 0) { sb.Append(st.Output); onText?.Invoke(st.Output); }
            offset = st.NextOffset;
            if (st.Done) return (st.ExitCode == 0, st.ExitCode, sb.ToString());
            await Task.Delay(700);
        }
    }

    // ---------- console ----------

    TabItem ConsoleTab()
    {
        var shell = new ComboBox { Width = 110, Margin = new Thickness(0, 0, 8, 0) };
        shell.Items.Add("PowerShell"); shell.Items.Add("CMD"); shell.SelectedIndex = 0;
        var quick = new ComboBox { MinWidth = 300, Margin = new Thickness(0, 0, 8, 0) };
        foreach (var q in Quick) quick.Items.Add(q.Name);
        quick.SelectedIndex = 0;
        var command = new TextBox { AcceptsReturn = true, MinHeight = 54, MaxHeight = 120, TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas"), Margin = new Thickness(0, 6, 0, 6), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var output = new TextBox { IsReadOnly = true, FontFamily = new FontFamily("Consolas"), Background = Brushes.Black, Foreground = Brushes.Gainsboro,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, TextWrapping = TextWrapping.NoWrap };
        var run = new Button { Content = "Run", Width = 80, Padding = new Thickness(0, 3, 0, 3), Margin = new Thickness(0, 0, 8, 0) };
        var stop = new Button { Content = "Stop", Width = 80, Padding = new Thickness(0, 3, 0, 3), IsEnabled = false, Margin = new Thickness(0, 0, 8, 0) };
        var clear = new Button { Content = "Clear", Width = 80, Padding = new Thickness(0, 3, 0, 3) };
        CancellationTokenSource? cts = null;

        quick.SelectionChanged += (_, _) =>
        {
            var q = Quick[quick.SelectedIndex];
            if (q.Command == "") return;
            shell.SelectedIndex = q.Shell == "cmd" ? 1 : 0;
            command.Text = q.Command;
        };
        run.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(command.Text)) return;
            run.IsEnabled = false; stop.IsEnabled = true;
            cts = new CancellationTokenSource();
            var name = Pc.Name;
            output.AppendText($"\n===== {name}> {command.Text.Trim()}\n");
            output.ScrollToEnd();
            try
            {
                var res = await ExecAsync(shell.SelectedIndex == 1 ? "cmd" : "powershell", command.Text, 1800,
                    t => { output.AppendText(t); output.ScrollToEnd(); }, cts.Token);
                output.AppendText($"[exit code {res.Code}]\n");
                if (res.Text.Length == 0 && !res.Ok) output.AppendText(res.Text + "\n");
            }
            catch (Exception ex) { output.AppendText("Error: " + ex.Message + "\n"); }
            output.ScrollToEnd();
            run.IsEnabled = true; stop.IsEnabled = false;
        };
        stop.Click += (_, _) => cts?.Cancel();
        clear.Click += (_, _) => output.Clear();

        var row1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        row1.Children.Add(new TextBlock { Text = "Shell:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        row1.Children.Add(shell); row1.Children.Add(quick);
        var row2 = new StackPanel { Orientation = Orientation.Horizontal };
        row2.Children.Add(run); row2.Children.Add(stop); row2.Children.Add(clear);

        var dock = new DockPanel { Margin = new Thickness(8) };
        var head = new StackPanel();
        head.Children.Add(row1); head.Children.Add(command); head.Children.Add(row2);
        DockPanel.SetDock(head, Dock.Top);
        dock.Children.Add(head);
        var outBox = new Border { Margin = new Thickness(0, 8, 0, 0), Child = output };
        dock.Children.Add(outBox);
        return new TabItem { Header = "Console", Content = dock };
    }

    // ---------- list tabs (processes, services, programs) ----------

    TabItem ListTab<T>(string title, string psCommand, (string Header, string Prop, double Width)[] cols,
        Func<T, string> searchText, params (string Label, Func<T, string> Command)[] actions) where T : class
    {
        var all = new List<T>();
        var shown = new ObservableCollection<T>();
        var list = new ListView { ItemsSource = shown, SelectionMode = SelectionMode.Extended };
        var gv = new GridView();
        foreach (var c in cols)
            gv.Columns.Add(new GridViewColumn { Header = c.Header, Width = c.Width, DisplayMemberBinding = new System.Windows.Data.Binding(c.Prop) });
        list.View = gv;
        var filter = new TextBox { Width = 200, Margin = new Thickness(0, 0, 8, 0) };
        var refresh = new Button { Content = "Refresh", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 8, 0) };
        var info = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray };

        void Apply()
        {
            shown.Clear();
            var f = filter.Text.Trim();
            foreach (var r in all)
                if (f == "" || searchText(r).Contains(f, StringComparison.OrdinalIgnoreCase)) shown.Add(r);
            info.Text = $"{shown.Count} of {all.Count}";
        }
        async Task Load()
        {
            refresh.IsEnabled = false; info.Text = "Loading...";
            try
            {
                var res = await ExecAsync("powershell", psCommand, 120, null, CancellationToken.None);
                var text = res.Text;
                int i = text.IndexOf('[');
                all = res.Ok && i >= 0 ? Json.From<List<T>>(text[i..]) ?? new() : new();
                if (!res.Ok || i < 0) info.Text = "Could not read: " + text.Trim();
                else { Apply(); }
            }
            catch (Exception ex) { info.Text = "Could not read: " + ex.Message; }
            refresh.IsEnabled = true;
        }
        refresh.Click += async (_, _) => await Load();
        filter.TextChanged += (_, _) => Apply();

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        bar.Children.Add(refresh);
        bar.Children.Add(new TextBlock { Text = "Search:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        bar.Children.Add(filter);
        foreach (var a in actions)
        {
            var b = new Button { Content = a.Label, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 8, 0) };
            var act = a;
            b.Click += async (_, _) =>
            {
                var rows = list.SelectedItems.Cast<T>().ToList();
                if (rows.Count == 0) { info.Text = "Select a row first."; return; }
                b.IsEnabled = false;
                foreach (var r in rows)
                {
                    var res = await ExecAsync("powershell", act.Command(r), 120, null, CancellationToken.None);
                    if (!res.Ok) { info.Text = res.Text.Trim(); break; }
                }
                b.IsEnabled = true;
                await Load();
            };
            bar.Children.Add(b);
        }
        bar.Children.Add(info);

        var dock = new DockPanel { Margin = new Thickness(8) };
        DockPanel.SetDock(bar, Dock.Top);
        dock.Children.Add(bar); dock.Children.Add(list);
        var tab = new TabItem { Header = title, Content = dock };
        bool first = true;
        tab.Loaded += async (_, _) => { if (first) { first = false; await Load(); } };
        _pc.SelectionChanged += (_, _) => { all.Clear(); shown.Clear(); first = true; if (tab.IsSelected) { first = false; _ = Load(); } };
        return tab;
    }

    // ---------- screenshot ----------

    TabItem ScreenshotTab()
    {
        var img = new Image { Stretch = Stretch.Uniform };
        byte[]? png = null;
        var take = new Button { Content = "Take screenshot", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 8, 0) };
        var save = new Button { Content = "Save...", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 8, 0), IsEnabled = false };
        var info = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray };
        take.Click += async (_, _) =>
        {
            take.IsEnabled = false; info.Text = "Taking screenshot...";
            try
            {
                var r = await Net.CallAsync(Pc.Host, _key, "screenshot", null, 30000);
                if (!r.Ok) { info.Text = r.Error ?? "error"; }
                else
                {
                    png = Convert.FromBase64String(r.Data!);
                    var bi = new BitmapImage();
                    bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.StreamSource = new MemoryStream(png); bi.EndInit();
                    img.Source = bi; save.IsEnabled = true;
                    info.Text = $"{Pc.Name} at {DateTime.Now:HH:mm:ss}";
                }
            }
            catch (Exception ex) { info.Text = ex.Message; }
            take.IsEnabled = true;
        };
        save.Click += (_, _) =>
        {
            if (png == null) return;
            var dlg = new SaveFileDialog { Filter = "PNG|*.png", FileName = $"{Pc.Name}-{DateTime.Now:yyyyMMdd-HHmmss}.png" };
            if (dlg.ShowDialog(this) == true) File.WriteAllBytes(dlg.FileName, png);
        };
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        bar.Children.Add(take); bar.Children.Add(save); bar.Children.Add(info);
        var dock = new DockPanel { Margin = new Thickness(8) };
        DockPanel.SetDock(bar, Dock.Top);
        dock.Children.Add(bar); dock.Children.Add(img);
        return new TabItem { Header = "Screenshot", Content = dock };
    }
}
