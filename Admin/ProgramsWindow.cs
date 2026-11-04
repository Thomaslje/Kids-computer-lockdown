using System.Windows;
using System.Windows.Controls;

namespace KioskAdmin;

/// <summary>Programs the admin can start on the kiosk PCs (for example the NVIDIA Control Panel), without a tile.</summary>
public class ProgramsWindow : Window
{
    readonly AdminData _d;
    readonly ListBox _list = new() { MinHeight = 120, Margin = new Thickness(0, 4, 0, 8) };
    readonly TextBox _name = new(), _path = new(), _args = new();
    public SavedProgram? Chosen { get; private set; }

    public ProgramsWindow(Window owner, AdminData d, string pcNames)
    {
        _d = d;
        Owner = owner; Title = "Start a program on the kiosk PCs";
        Theme.Skin(this);
        SizeToContent = SizeToContent.WidthAndHeight; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;

        if (_d.Programs.Count == 0)
            _d.Programs.Add(new SavedProgram
            {
                Name = "NVIDIA Control Panel",
                Path = @"shell:AppsFolder\NVIDIACorp.NVIDIAControlPanel_56jybvy8sckqj!NVIDIACorp.NVIDIAControlPanel"
            });
        Refresh();

        var sp = new StackPanel { Margin = new Thickness(16), MinWidth = 460 };
        sp.Children.Add(Ui.Label("Start on: " + pcNames));
        sp.Children.Add(Ui.Label("Saved programs (pick one and press Start):"));
        sp.Children.Add(_list);

        sp.Children.Add(Ui.Label("Add or change a program. Name, then the full path of the .exe on the PC (or a shell:AppsFolder\\... address for Store apps), then optional arguments:"));
        sp.Children.Add(_name); sp.Children.Add(_path); sp.Children.Add(_args);
        _path.Margin = new Thickness(0, 4, 0, 4);
        _list.SelectionChanged += (_, _) =>
        {
            if (_list.SelectedItem is SavedProgram p) { _name.Text = p.Name; _path.Text = p.Path; _args.Text = p.Args; }
        };

        var save = new Button { Content = "Save / add", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 6, 6, 0) };
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_name.Text) || string.IsNullOrWhiteSpace(_path.Text)) return;
            var existing = _d.Programs.FirstOrDefault(p => p.Name == _name.Text.Trim());
            if (existing == null) { existing = new SavedProgram(); _d.Programs.Add(existing); }
            existing.Name = _name.Text.Trim(); existing.Path = _path.Text.Trim().Trim('"'); existing.Args = _args.Text.Trim();
            _d.Save(); Refresh(); _list.SelectedItem = existing;
        };
        var del = new Button { Content = "Delete", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 6, 6, 0) };
        del.Click += (_, _) =>
        {
            if (_list.SelectedItem is SavedProgram p) { _d.Programs.Remove(p); _d.Save(); Refresh(); }
        };
        var edit = new StackPanel { Orientation = Orientation.Horizontal };
        edit.Children.Add(save); edit.Children.Add(del);
        sp.Children.Add(edit);

        var start = new Button { Content = "Start on the PCs", Width = 120, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        start.Click += (_, _) =>
        {
            if (_list.SelectedItem is not SavedProgram p) { MessageBox.Show(this, "Pick a program in the list first."); return; }
            Chosen = p; DialogResult = true;
        };
        var cancel = new Button { Content = "Close", Width = 80, IsCancel = true };
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        row.Children.Add(start); row.Children.Add(cancel);
        sp.Children.Add(row);
        Content = sp;
    }

    void Refresh() { _list.ItemsSource = null; _list.ItemsSource = _d.Programs; }
}
