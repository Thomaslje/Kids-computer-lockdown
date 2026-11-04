using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;

namespace KioskAdmin;

/// <summary>Colours, button and list styles for the whole admin app (teal with orange accents).</summary>
public static class Theme
{
    static SolidColorBrush Br(string hex)
    {
        var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        b.Freeze();
        return b;
    }

    public static readonly Brush Bg = Br("#EEF6F6");
    public static readonly Brush Text = Br("#0F2A2E");
    public static readonly Brush Muted = Br("#4F6B6F");
    public static readonly Brush Line = Br("#D6E7E7");
    public static readonly Brush Good = Br("#0B6B4C");

    // 24x24 line icons (the same ones as in the design)
    public const string IconMonitor = "M4,3 h16 a2,2 0 0 1 2,2 v10 a2,2 0 0 1 -2,2 h-16 a2,2 0 0 1 -2,-2 v-10 a2,2 0 0 1 2,-2 z M8,21 h8 M12,17 v4";
    public const string IconGrid = "M3,3 h7 v7 h-7 z M14,3 h7 v7 h-7 z M14,14 h7 v7 h-7 z M3,14 h7 v7 h-7 z";
    public const string IconTool = "M14.7,6.3 a1,1 0 0 0 0,1.4 l1.6,1.6 a1,1 0 0 0 1.4,0 l3.77,-3.77 a6,6 0 0 1 -7.94,7.94 l-6.91,6.91 a2.12,2.12 0 0 1 -3,-3 l6.91,-6.91 a6,6 0 0 1 7.94,-7.94 l-3.76,3.76 z";
    public const string IconZap = "M13,2 L3,14 H12 L11,22 L21,10 H12 Z";
    public const string IconRefresh = "M23,4 V10 H17 M1,20 V14 H7 M3.51,9 A9,9 0 0 1 18.36,5.64 L23,10 M1,14 L5.64,18.36 A9,9 0 0 0 20.49,15";
    public const string IconLock = "M5,11 h14 a2,2 0 0 1 2,2 v7 a2,2 0 0 1 -2,2 h-14 a2,2 0 0 1 -2,-2 v-7 a2,2 0 0 1 2,-2 z M7,11 V7 a5,5 0 0 1 10,0 v4";

    const string Xaml = """
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <Style TargetType="Button">
    <Setter Property="Foreground" Value="#0F3A3E"/>
    <Setter Property="Background" Value="#FFFFFF"/>
    <Setter Property="BorderBrush" Value="#B9D6D6"/>
    <Setter Property="BorderThickness" Value="1.5"/>
    <Setter Property="Padding" Value="14,7"/>
    <Setter Property="Margin" Value="0,0,8,8"/>
    <Setter Property="FontWeight" Value="SemiBold"/>
    <Setter Property="Cursor" Value="Hand"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="Button">
          <Border x:Name="Bd" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                  BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="10" Padding="{TemplateBinding Padding}">
            <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalAlignment="Center" RecognizesAccessKey="True"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Bd" Property="Opacity" Value="0.88"/></Trigger>
            <Trigger Property="IsPressed" Value="True"><Setter TargetName="Bd" Property="Opacity" Value="0.7"/></Trigger>
            <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Bd" Property="BorderBrush" Value="#FF7A1A"/></Trigger>
            <Trigger Property="IsEnabled" Value="False"><Setter TargetName="Bd" Property="Opacity" Value="0.45"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key="PrimaryButton" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
    <Setter Property="Background" Value="#0B7F80"/>
    <Setter Property="BorderBrush" Value="#0B7F80"/>
    <Setter Property="Foreground" Value="#FFFFFF"/>
  </Style>
  <Style x:Key="OrangeButton" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
    <Setter Property="Background" Value="#FF7A1A"/>
    <Setter Property="BorderBrush" Value="#FF7A1A"/>
    <Setter Property="Foreground" Value="#2B1200"/>
  </Style>
  <Style x:Key="DangerButton" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
    <Setter Property="Background" Value="#FFF6F5"/>
    <Setter Property="BorderBrush" Value="#F5CFC9"/>
    <Setter Property="Foreground" Value="#A3281B"/>
  </Style>
  <Style x:Key="NavButton" TargetType="Button">
    <Setter Property="Foreground" Value="#CDEBEB"/>
    <Setter Property="Background" Value="Transparent"/>
    <Setter Property="Padding" Value="14,11"/>
    <Setter Property="FontWeight" Value="SemiBold"/>
    <Setter Property="HorizontalContentAlignment" Value="Left"/>
    <Setter Property="Cursor" Value="Hand"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="Button">
          <Border x:Name="Bd" Background="{TemplateBinding Background}" CornerRadius="10" Padding="{TemplateBinding Padding}">
            <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalAlignment="Center"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Bd" Property="Background" Value="#26FFFFFF"/></Trigger>
            <Trigger Property="Tag" Value="active">
              <Setter TargetName="Bd" Property="Background" Value="#FF7A1A"/>
              <Setter Property="Foreground" Value="#2B1200"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style TargetType="TextBox">
    <Setter Property="Background" Value="#FFFFFF"/>
    <Setter Property="BorderBrush" Value="#B9D6D6"/>
    <Setter Property="BorderThickness" Value="1.5"/>
    <Setter Property="Padding" Value="6,5"/>
  </Style>
  <Style TargetType="PasswordBox">
    <Setter Property="BorderBrush" Value="#B9D6D6"/>
    <Setter Property="BorderThickness" Value="1.5"/>
    <Setter Property="Padding" Value="6,5"/>
  </Style>
  <Style TargetType="ListView">
    <Setter Property="Background" Value="#FFFFFF"/>
    <Setter Property="BorderThickness" Value="0"/>
  </Style>
  <Style TargetType="ListViewItem">
    <Setter Property="Foreground" Value="#0F2A2E"/>
    <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ListViewItem">
          <Border x:Name="Bd" Background="Transparent" BorderBrush="#E3EFEF" BorderThickness="0,0,0,1" Padding="8,11">
            <GridViewRowPresenter Content="{TemplateBinding Content}"
                                  Columns="{Binding View.Columns, RelativeSource={RelativeSource AncestorType=ListView}}"
                                  VerticalAlignment="Center"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Bd" Property="Background" Value="#F0F8F8"/></Trigger>
            <Trigger Property="IsSelected" Value="True"><Setter TargetName="Bd" Property="Background" Value="#D5EFEF"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style TargetType="GridViewColumnHeader">
    <Setter Property="Background" Value="#F3F9F9"/>
    <Setter Property="Foreground" Value="#4F6B6F"/>
    <Setter Property="FontWeight" Value="SemiBold"/>
    <Setter Property="Padding" Value="8,9"/>
    <Setter Property="HorizontalContentAlignment" Value="Left"/>
    <Setter Property="BorderThickness" Value="0"/>
  </Style>
  <Style TargetType="ListBox">
    <Setter Property="Background" Value="#FFFFFF"/>
    <Setter Property="BorderBrush" Value="#E3EFEF"/>
  </Style>
  <Style TargetType="ListBoxItem">
    <Setter Property="Foreground" Value="#0F2A2E"/>
    <Setter Property="Padding" Value="12,10"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ListBoxItem">
          <Border x:Name="Bd" Background="Transparent" CornerRadius="8" Margin="3,2" Padding="{TemplateBinding Padding}">
            <ContentPresenter/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Bd" Property="Background" Value="#F0F8F8"/></Trigger>
            <Trigger Property="IsSelected" Value="True"><Setter TargetName="Bd" Property="Background" Value="#D5EFEF"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
</ResourceDictionary>
""";

    /// <summary>Loads the styles. If anything goes wrong the app simply keeps the plain Windows look.</summary>
    public static void Apply(Application app)
    {
        try { app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(Xaml)); }
        catch { }
    }

    public static Style? Get(string key) => Application.Current?.TryFindResource(key) as Style;

    public static void Skin(Window w)
    {
        w.Background = Bg;
        w.Foreground = Text;
        w.FontFamily = new FontFamily("Segoe UI");
    }

    public static Button Nav(string text, string iconPath)
    {
        var icon = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(iconPath), StrokeThickness = 2, Width = 18, Height = 18, Stretch = Stretch.Uniform,
            StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center
        };
        icon.SetBinding(System.Windows.Shapes.Shape.StrokeProperty,
            new Binding("Foreground") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1) });
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(icon);
        row.Children.Add(new TextBlock { Text = text, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
        return new Button { Content = row, Style = Get("NavButton"), Margin = new Thickness(0, 0, 0, 4) };
    }

    public static Border Card(UIElement child, Thickness? padding = null) => new()
    {
        Background = Brushes.White, BorderBrush = Line, BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(14), Padding = padding ?? new Thickness(0), Child = child,
        Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.08, Color = Colors.Black }
    };

    public static Border Stat(string label, TextBlock value)
    {
        value.FontSize = 28; value.FontWeight = FontWeights.Bold; value.Foreground = Text;
        var sp = new StackPanel { Margin = new Thickness(18, 14, 18, 14) };
        sp.Children.Add(new TextBlock { Text = label, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Muted });
        sp.Children.Add(value);
        return Card(sp);
    }

    /// <summary>The Status column: a coloured dot and the text.</summary>
    public static GridViewColumn StatusColumn(double width)
    {
        var dot = new FrameworkElementFactory(typeof(System.Windows.Shapes.Ellipse));
        dot.SetValue(FrameworkElement.WidthProperty, 9.0);
        dot.SetValue(FrameworkElement.HeightProperty, 9.0);
        dot.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
        dot.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        dot.SetBinding(System.Windows.Shapes.Shape.FillProperty, new Binding("Status") { Converter = new StatusBrushConverter() });
        var label = new FrameworkElementFactory(typeof(TextBlock));
        label.SetBinding(TextBlock.TextProperty, new Binding("Status"));
        var row = new FrameworkElementFactory(typeof(StackPanel));
        row.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        row.AppendChild(dot);
        row.AppendChild(label);
        return new GridViewColumn { Header = "Status", Width = width, CellTemplate = new DataTemplate { VisualTree = row } };
    }
}

public class StatusBrushConverter : IValueConverter
{
    static readonly Brush Green = new SolidColorBrush(Color.FromRgb(0x12, 0xB7, 0x6A));
    static readonly Brush Grey = new SolidColorBrush(Color.FromRgb(0x98, 0xA2, 0xB3));
    static readonly Brush Orange = new SolidColorBrush(Color.FromRgb(0xFF, 0x7A, 0x1A));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => (value as string) switch
    {
        "Online" => Green,
        "Offline" or "No answer" or "Checking..." => Grey,
        _ => Orange
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
