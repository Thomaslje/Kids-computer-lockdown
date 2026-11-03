using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using KioskShared;

namespace KioskAdmin;

/// <summary>The friendly view: the PCs with Restart and Shut down, nothing else.</summary>
public class SimpleWindow : Window
{
    readonly AdminData _d;
    readonly ObservableCollection<PcItem> _pcs = new();
    readonly ListView _list = new() { SelectionMode = SelectionMode.Extended };
    readonly TextBlock _status = new() { Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(15) };
    readonly DispatcherTimer _fade = new() { Interval = TimeSpan.FromSeconds(15) };
    bool _refreshing;

    byte[] Key => Convert.FromBase64String(_d.KeyBase64);

    public SimpleWindow(AdminData d)
    {
        _d = d;
        Title = "Kiosk PCs";
        Width = 600; Height = 480; MinWidth = 420; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        foreach (var p in d.Pcs) _pcs.Add(new PcItem { Host = p.Host, Name = p.Host });
        _list.ItemsSource = _pcs;
        var gv = new GridView();
        gv.Columns.Add(new GridViewColumn { Header = "PC", DisplayMemberBinding = new Binding("Name"), Width = 220 });
        gv.Columns.Add(new GridViewColumn { Header = "Status", DisplayMemberBinding = new Binding("Status"), Width = 140 });
        gv.Columns.Insert(1, new GridViewColumn { Header = "TeamViewer ID", DisplayMemberBinding = new Binding("TeamViewer"), Width = 120 });
        _list.View = gv;
        var copyId = new MenuItem { Header = "Copy TeamViewer ID" };
        copyId.Click += (_, _) =>
        {
            if (_list.SelectedItem is PcItem p && p.TeamViewer != "") Clipboard.SetText(p.TeamViewer.Replace(" ", ""));
        };
        _list.ContextMenu = new ContextMenu { Items = { copyId } };

        var all = new Button { Content = "Select all", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0) };
        all.Click += (_, _) => _list.SelectAll();
        var restart = new Button { Content = "Restart", Padding = new Thickness(18, 6, 18, 6), Margin = new Thickness(0, 0, 8, 0) };
        restart.Click += async (_, _) => await SendAsync("restart", "Restart", "restart");
        var shutdown = new Button { Content = "Shut down", Padding = new Thickness(18, 6, 18, 6) };
        shutdown.Click += async (_, _) => await SendAsync("shutdown", "Shut down", "shut down");

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(all); buttons.Children.Add(restart); buttons.Children.Add(shutdown);

        var panel = new DockPanel { Margin = new Thickness(14) };
        var bottom = new StackPanel();
        bottom.Children.Add(buttons); bottom.Children.Add(_status);
        DockPanel.SetDock(bottom, Dock.Bottom);
        panel.Children.Add(bottom);
        panel.Children.Add(_list);
        Content = panel;

        _fade.Tick += (_, _) => { _fade.Stop(); _status.Text = ""; };
        _timer.Tick += async (_, _) => await RefreshAsync();
        Loaded += async (_, _) => { _timer.Start(); await RefreshAsync(); };
        Closed += (_, _) => _timer.Stop();
    }

    void Say(string text)
    {
        _status.Text = text;
        _fade.Stop();
        _fade.Start();
    }

    async Task RefreshAsync()
    {
        if (_refreshing) return;
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
                        pc.Name = info.PcName;
                        pc.TeamViewer = info.TeamViewerId;
                        pc.Status = "Online";
                    }
                    else pc.Status = "No answer";
                }
                catch { pc.Status = "Offline"; }
            }));
        }
        finally { _refreshing = false; }
    }

    async Task SendAsync(string cmd, string title, string verb)
    {
        var targets = _list.SelectedItems.Cast<PcItem>().ToList();
        if (targets.Count == 0) { Say("Pick one or more PCs in the list first."); return; }
        var names = string.Join(", ", targets.Select(t => t.Name));
        if (MessageBox.Show(this, $"{title} these PCs in 10 seconds?\n\n{names}", title,
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        var results = await Task.WhenAll(targets.Select(async pc =>
        {
            try { var r = await Net.CallAsync(pc.Host, Key, cmd, null, 8000); return (pc.Name, r.Ok); }
            catch { return (pc.Name, false); }
        }));
        var failed = results.Where(r => !r.Item2).Select(r => r.Name).ToList();
        Say(failed.Count == 0
            ? $"Done: {results.Length} PC(s) will {verb} in 10 seconds."
            : $"{results.Length - failed.Count} of {results.Length} will {verb}. No answer from: {string.Join(", ", failed)}.");
    }
}
