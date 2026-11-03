using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KioskLauncher;

/// <summary>The KFUM/KFUK Esbjerg logo, drawn from vector shapes (no image file needed, sharp at any size).</summary>
static class Logo
{
    static readonly (string Fill, string Data)[] Shapes =
    {
        ("#DD3221", "M44,37.2 L64.1,1.9 L52.6,1.9 L38.3,27.1 Z"),
        ("#3C3C50", "M32.5,37.2 L38.3,47.2 L44,37.2 L38.3,27.1 Z"),
        ("#3C3C50", "M46.9,12 L52.6,1.9 L23.9,1.9 L29.7,12 Z"),
        ("#5A96F0", "M23.9,1.9 L23.9,1.9 L12.4,1.9 L32.5,37.2 L38.3,27.1 Z"),
        ("#3C3C50", "M66.5,24.9 L68.3,24.9 L68.3,28.4 L71.5,24.9 L73.7,24.9 L70.4,28.3 L73.8,32.9 L71.7,32.9 L69.2,29.5 L68.3,30.5 L68.3,32.9 L66.5,32.9 Z"),
        ("#3C3C50", "M77.6,24.9 L83.7,24.9 L83.7,26.5 L79.4,26.5 L79.4,28.2 L83.2,28.2 L83.2,29.8 L79.4,29.8 L79.4,32.9 L77.6,32.9 Z"),
        ("#3C3C50", "M88,29.4v-4.6h1.8v4.5c0,1.3,0.7,2,1.7,2c1.1,0,1.7-0.6,1.7-1.9v-4.6H95v4.5c0,2.4-1.4,3.6-3.5,3.6 S88,31.7,88,29.4 Z"),
        ("#3C3C50", "M66.5,13.3 L68.3,13.3 L68.3,16.8 L71.5,13.3 L73.7,13.3 L70.4,16.6 L73.8,21.2 L71.7,21.2 L69.2,17.9 L68.3,18.8 L68.3,21.2 L66.5,21.2 Z"),
        ("#3C3C50", "M99.8,24.9 L101.6,24.9 L101.6,28.4 L104.9,24.9 L107,24.9 L103.8,28.3 L107.2,32.9 L105.1,32.9 L102.6,29.5 L101.6,30.5 L101.6,32.9 L99.8,32.9 Z"),
        ("#3C3C50", "M77.7,13.3 L83.8,13.3 L83.8,14.9 L79.4,14.9 L79.4,16.6 L83.2,16.6 L83.2,18.1 L79.4,18.1 L79.4,21.2 L77.7,21.2 Z"),
        ("#3C3C50", "M88,17.7v-4.6h1.8v4.5c0,1.3,0.7,2,1.7,2s1.7-0.6,1.7-1.9v-4.6H95v4.5c0,2.4-1.4,3.6-3.5,3.6 C89.3,21.2,88,20,88,17.7 Z"),
        ("#3C3C50", "M99.3,13.3 L101.2,13.3 L103.3,16.6 L105.4,13.3 L107.3,13.3 L107.3,21.2 L105.6,21.2 L105.6,16 L103.3,19.5 L103.3,19.5 L101,16.1 L101,21.2 L99.3,21.2 Z"),
    };

    static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;

    /// <summary>Returns the logo scaled to the given width (the logo is 120 x 57 units).</summary>
    public static FrameworkElement Create(double width)
    {
        var canvas = new Canvas { Width = 120, Height = 57 };
        foreach (var (fill, data) in Shapes)
            canvas.Children.Add(new System.Windows.Shapes.Path { Fill = Brush(fill), Data = Geometry.Parse(data) });

        canvas.Children.Add(new System.Windows.Shapes.Rectangle
        {
            Fill = Brush("#3C3C50"), Width = 41, Height = 1.6
        });
        Canvas.SetLeft(canvas.Children[canvas.Children.Count - 1], 66.3);
        Canvas.SetTop(canvas.Children[canvas.Children.Count - 1], 36.4);

        var text = new TextBlock
        {
            Text = "Esbjerg", FontFamily = new FontFamily("Segoe UI Semibold, Segoe UI"),
            FontSize = 7.3466, Foreground = Brush("#3C3C50")
        };
        Canvas.SetLeft(text, 83.83);
        Canvas.SetTop(text, 37.5);   // baseline of the original text is at y = 45.49
        canvas.Children.Add(text);

        return new Viewbox { Width = width, Height = width * 57 / 120, Child = canvas };
    }
}
