using System.Windows;
using System.Windows.Controls;
using KioskShared;

namespace KioskAdmin;

/// <summary>Pick Steam games straight from a kiosk PC's library (or type an AppID by hand).</summary>
public class SteamPickerWindow : Window
{
    public List<SteamGame> Selected { get; } = new();

    readonly ComboBox _pc = new() { MinWidth = 220 };
    readonly ListBox _list = new() { SelectionMode = SelectionMode.Extended, Height = 280, DisplayMemberPath = "Name" };
    readonly TextBlock _msg = new() { Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
    readonly TextBox _id = new() { Width = 100 };
    readonly TextBox _name = new() { Width = 200, Margin = new Thickness(8, 0, 0, 0) };
    readonly byte[] _key;

    public SteamPickerWindow(Window owner, List<string> hosts, byte[] key)
    {
        _key = key;
        Owner = owner;
        Title = "Add Steam games";
        Width = 520; SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _pc.ItemsSource = hosts;
        if (hosts.Count > 0) _pc.SelectedIndex = 0;
        var load = new Button { Content = "Load games from this PC", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
        load.Click += async (_, _) => await LoadAsync();

        var top = new StackPanel { Orientation = Orientation.Horizontal };
        top.Children.Add(_pc); top.Children.Add(load);

        var manual = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        manual.Children.Add(_id); manual.Children.Add(_name);

        var ok = new Button { Content = "Add selected", Width = 110, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        row.Children.Add(ok); row.Children.Add(cancel);
        ok.Click += (_, _) =>
        {
            Selected.AddRange(_list.SelectedItems.Cast<SteamGame>());
            if (_id.Text.Trim().All(char.IsDigit) && _id.Text.Trim() != "" && _name.Text.Trim() != "")
                Selected.Add(new SteamGame(_id.Text.Trim(), _name.Text.Trim()));
            DialogResult = true;
        };

        var sp = new StackPanel { Margin = new Thickness(16) };
        sp.Children.Add(Ui.Label("1. Pick a kiosk PC and load the games installed on it:"));
        sp.Children.Add(top);
        sp.Children.Add(_list);
        sp.Children.Add(_msg);
        sp.Children.Add(Ui.Label("Or add one by hand (Steam AppID and the name to show):"));
        sp.Children.Add(manual);
        sp.Children.Add(row);
        Content = sp;
    }

    async Task LoadAsync()
    {
        if (_pc.SelectedItem is not string host) { _msg.Text = "Add a kiosk PC in the main window first."; return; }
        _msg.Text = "Loading...";
        try
        {
            var r = await Net.CallAsync(host, _key, "scan", null, 20000);
            if (!r.Ok) { _msg.Text = "The PC said: " + r.Error; return; }
            var games = Json.From<List<SteamGame>>(r.Data!)!;
            _list.ItemsSource = games;
            _msg.Text = games.Count == 0
                ? "No Steam games found on that PC."
                : $"{games.Count} games found. Hold Ctrl to pick several.";
        }
        catch (Exception ex) { _msg.Text = "Could not reach the PC: " + ex.Message; }
    }
}
