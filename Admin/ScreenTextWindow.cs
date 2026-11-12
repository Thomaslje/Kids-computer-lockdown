using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using KioskShared;

namespace KioskAdmin;

/// <summary>
/// Place your own text on the kiosk screen: a small picture of the screen where text boxes can be dragged around.
/// Positions are stored as fractions of the screen, sizes relative to a 1080 pixel high screen.
/// </summary>
public class ScreenTextWindow : Window
{
    const double RefW = 1920, RefH = 1080;          // the screen we draw as a guide
    const double StageW = 800, StageH = 450;        // size of the preview on this PC
    const double S = StageW / RefW;                 // preview scale

    static readonly (string Name, string Hex)[] Palette =
    {
        ("Navy", "#3C3C50"), ("Red", "#DD3221"), ("Blue", "#5A96F0"),
        ("Orange", "#F5A623"), ("Green", "#4CAF7A"), ("Black", "#000000")
    };

    static SolidColorBrush B(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;

    public List<ScreenText> Result { get; }

    readonly Canvas _stage = new()
    {
        Width = StageW, Height = StageH, ClipToBounds = true,
        Background = new LinearGradientBrush(Color.FromRgb(0xE4, 0xF0, 0xFF), Color.FromRgb(0xFF, 0xFF, 0xFF), 90)
    };
    readonly Dictionary<ScreenText, Border> _boxes = new();
    readonly TextBox _text = new()
    {
        AcceptsReturn = true, Height = 90, TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
    };
    readonly Slider _size = new() { Minimum = 16, Maximum = 160, Margin = new Thickness(0, 4, 0, 4) };
    readonly TextBlock _sizeLabel = new();
    readonly ComboBox _color = new() { Margin = new Thickness(0, 4, 0, 4) };
    readonly CheckBox _bold = new() { Content = "Bold", Margin = new Thickness(0, 4, 0, 0) };
    readonly StackPanel _edit = new() { IsEnabled = false };

    readonly TextBox _titleBox = new() { Margin = new Thickness(0, 4, 0, 12) };
    readonly List<UIElement> _guideItems = new();
    int _tileCount;

    /// <summary>The big title at the top of the kiosk screen (empty = no title).</summary>
    public string TitleText { get; private set; }

    ScreenText? _sel, _drag;
    Point _grab;
    bool _loading;


    public ScreenTextWindow(Window owner, List<ScreenText> existing, int tileCount, string title)
    {
        Result = existing.Select(t => t.Clone()).ToList();
        TitleText = title ?? "";
        _tileCount = tileCount;
        Owner = owner;
        Title = "Screen text";
        Theme.Skin(this);
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        DrawGuide(TitleText, _tileCount);
        foreach (var t in Result) AddBox(t);
        _stage.MouseLeftButtonDown += (_, _) => Select(null);

        // ---- side panel ----
        _color.ItemsSource = Palette.Select(c => c.Name).ToList();
        _text.TextChanged += (_, _) => { if (Editing(out var t)) { t.Text = _text.Text; Refresh(t); } };
        _size.ValueChanged += (_, _) =>
        {
            _sizeLabel.Text = $"Size: {Math.Round(_size.Value)}";
            if (Editing(out var t)) { t.Size = Math.Round(_size.Value); Refresh(t); }
        };
        _color.SelectionChanged += (_, _) =>
        {
            if (Editing(out var t) && _color.SelectedIndex >= 0) { t.Color = Palette[_color.SelectedIndex].Hex; Refresh(t); }
        };
        _bold.Click += (_, _) => { if (Editing(out var t)) { t.Bold = _bold.IsChecked == true; Refresh(t); } };

        _edit.Children.Add(new TextBlock { Text = "Text:" });
        _edit.Children.Add(_text);
        _edit.Children.Add(_sizeLabel);
        _edit.Children.Add(_size);
        _edit.Children.Add(new TextBlock { Text = "Colour:", Margin = new Thickness(0, 6, 0, 0) });
        _edit.Children.Add(_color);
        _edit.Children.Add(_bold);

        var add = new Button { Content = "+ Add text", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 8, 0) };
        add.Click += (_, _) =>
        {
            var t = new ScreenText { Text = "New text" };
            Result.Add(t);
            AddBox(t);
            Select(t);
            _text.Focus(); _text.SelectAll();
        };
        var del = new Button { Content = "Delete", Padding = new Thickness(10, 4, 10, 4) };
        del.Click += (_, _) =>
        {
            if (_sel == null) return;
            _stage.Children.Remove(_boxes[_sel]);
            _boxes.Remove(_sel);
            Result.Remove(_sel);
            Select(null);
        };
        var addRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        addRow.Children.Add(add); addRow.Children.Add(del);

        var hint = new TextBlock
        {
            Text = "Drag a text on the screen to move it. The tiles, title and logo are only a rough guide to where things appear.",
            TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, Margin = new Thickness(0, 12, 0, 0)
        };
        _titleBox.Text = TitleText;
        _titleBox.TextChanged += (_, _) => { TitleText = _titleBox.Text; RedrawGuide(); };
        var side = new StackPanel { Width = 250, Margin = new Thickness(16, 0, 0, 0) };
        side.Children.Add(new TextBlock { Text = "Big title at the top (empty = no title):", TextWrapping = TextWrapping.Wrap });
        side.Children.Add(_titleBox);
        side.Children.Add(addRow); side.Children.Add(_edit); side.Children.Add(hint);

        var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        ok.Click += (_, _) =>
        {
            Result.RemoveAll(t => string.IsNullOrWhiteSpace(t.Text));
            DialogResult = true;
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(ok); buttons.Children.Add(cancel);

        var stageFrame = new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Child = _stage, VerticalAlignment = VerticalAlignment.Top };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(stageFrame); row.Children.Add(side);
        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(row); root.Children.Add(buttons);
        Content = root;

        Select(Result.FirstOrDefault());
    }

    // ---- the guide drawn behind the texts (approximate layout of the real kiosk screen) ----

    void AddGuide(UIElement el)
    {
        _stage.Children.Insert(_guideItems.Count, el);   // always underneath the texts
        _guideItems.Add(el);
    }

    void RedrawGuide()
    {
        foreach (var el in _guideItems) _stage.Children.Remove(el);
        _guideItems.Clear();
        DrawGuide(TitleText, _tileCount);
    }

    void Box(double x, double y, double w, double h, double radius, string fill, string? stroke)
    {
        var r = new System.Windows.Shapes.Rectangle
        {
            Width = w * S, Height = h * S, RadiusX = radius * S, RadiusY = radius * S,
            Fill = B(fill), IsHitTestVisible = false
        };
        if (stroke != null) { r.Stroke = B(stroke); r.StrokeThickness = 1; }
        Canvas.SetLeft(r, x * S); Canvas.SetTop(r, y * S);
        AddGuide(r);
    }

    void DrawGuide(string title, int tileCount)
    {
        bool hasTitle = !string.IsNullOrWhiteSpace(title);
        if (hasTitle)
        {
            var tb = new TextBlock
            {
                Text = title, Width = StageW, TextAlignment = TextAlignment.Center, FontWeight = FontWeights.Bold,
                FontSize = 52 * S, Foreground = B("#3C3C50"), Opacity = 0.35, IsHitTestVisible = false,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Canvas.SetTop(tb, 36 * S);
            AddGuide(tb);

            double sx = (RefW - 3 * 98) / 2 + 4;
            Box(sx, 122, 90, 8, 4, "#DD3221", null);
            Box(sx + 98, 122, 90, 8, 4, "#5A96F0", null);
            Box(sx + 196, 122, 90, 8, 4, "#3C3C50", null);
        }
        double tilesTop = hasTitle ? 160 : 40;

        int n = Math.Min(Math.Max(tileCount, 0), 15);
        int cols = Math.Max(1, Math.Min(n, 5));
        double startX = (RefW - cols * 312) / 2;
        for (int i = 0; i < n; i++)
            Box(startX + (i % 5) * 312 + 16, tilesTop + (i / 5) * 247 + 16, 280, 215, 26, "#FFFFFF", "#C8D6EE");

        Box(RefW - 28 - 222, RefH - 24 - 127, 222, 127, 20, "#FFFFFF", "#C8D6EE");
        var logo = new TextBlock { Text = "logo", Foreground = Brushes.Gray, FontSize = 14, IsHitTestVisible = false };
        Canvas.SetLeft(logo, (RefW - 28 - 222 + 90) * S); Canvas.SetTop(logo, (RefH - 24 - 127 + 50) * S);
        AddGuide(logo);
    }

    // ---- texts ----

    void Refresh(ScreenText t)
    {
        var tb = (TextBlock)_boxes[t].Child;
        tb.Text = t.Text;
        tb.FontSize = t.Size * S;
        tb.FontWeight = t.Bold ? FontWeights.Bold : FontWeights.Normal;
        try { tb.Foreground = B(t.Color); } catch { }
    }

    void AddBox(ScreenText t)
    {
        var box = new Border
        {
            Child = new TextBlock { FontFamily = new FontFamily("Segoe UI") },
            Background = Brushes.Transparent, Cursor = Cursors.SizeAll
        };
        _boxes[t] = box;
        Refresh(t);
        Canvas.SetLeft(box, t.X * StageW);
        Canvas.SetTop(box, t.Y * StageH);
        _stage.Children.Add(box);

        box.MouseLeftButtonDown += (_, e) =>
        {
            Select(t);
            _drag = t;
            var p = e.GetPosition(_stage);
            _grab = new Point(p.X - Canvas.GetLeft(box), p.Y - Canvas.GetTop(box));
            box.CaptureMouse();
            e.Handled = true;
        };
        box.MouseMove += (_, e) =>
        {
            if (_drag != t || !box.IsMouseCaptured) return;
            var p = e.GetPosition(_stage);
            double x = Math.Clamp(p.X - _grab.X, 0, Math.Max(0, StageW - box.ActualWidth));
            double y = Math.Clamp(p.Y - _grab.Y, 0, Math.Max(0, StageH - box.ActualHeight));
            Canvas.SetLeft(box, x); Canvas.SetTop(box, y);
            t.X = x / StageW; t.Y = y / StageH;
        };
        box.MouseLeftButtonUp += (_, _) =>
        {
            if (_drag != t) return;
            box.ReleaseMouseCapture();
            _drag = null;
        };
    }

    bool Editing(out ScreenText t)
    {
        t = _sel!;
        return !_loading && _sel != null;
    }

    void Select(ScreenText? t)
    {
        _sel = t;
        foreach (var kv in _boxes)
            kv.Value.Background = kv.Key == t ? new SolidColorBrush(Color.FromArgb(0x40, 0x5A, 0x96, 0xF0)) : Brushes.Transparent;

        _loading = true;
        _edit.IsEnabled = t != null;
        if (t != null)
        {
            _text.Text = t.Text;
            _size.Value = Math.Clamp(t.Size, _size.Minimum, _size.Maximum);
            _sizeLabel.Text = $"Size: {Math.Round(_size.Value)}";
            _color.SelectedIndex = Array.FindIndex(Palette, c => c.Hex.Equals(t.Color, StringComparison.OrdinalIgnoreCase));
            _bold.IsChecked = t.Bold;
        }
        _loading = false;
    }
}
