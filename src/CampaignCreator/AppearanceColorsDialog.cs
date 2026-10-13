using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CampaignCore;

namespace CampaignCreator;

internal static class AppearanceColorsDialog
{
    private static readonly IBrush Mint = StudioTheme.Accent, Muted = StudioTheme.Muted;
    private static TextBlock Label(string text, double size = 12, IBrush? brush = null) => new() { Text = text, FontSize = size, Foreground = brush ?? Muted, TextWrapping = TextWrapping.Wrap };
    private static Button ActionButton(string text, Action action)
    {
        var button = new Button { Content = text, Padding = StudioTheme.ButtonPadding };
        button.Click += (_,_) => action(); return button;
    }
    private static string ColorLabel(string key, string part)
    {
        if (part == "Transition") return key.Replace("Transition ", "");
        string k=AppearanceContract.TeamKey(key).Replace(" Away","");
        if (part == "Blocker") return k switch {
            "Skates Color" => "Blocker colour 1", "Blade Color" => "Blocker colour 2", "Laces Color" => "Blocker colour 3", _ => EquipmentColorGuide.Channel(key)
        };
        if (part is "Helmet" or "Goalie mask") return k switch {
            "Helmet Color" => "Helmet colour", "Helmet Secondary Color" => "Design", "Helmet Tertiary Color" => "Cage",
            _ => EquipmentColorGuide.Channel(key)
        };
        return EquipmentColorGuide.Channel(key);
    }
    internal static async Task Show(Window owner, string part, string[] keys, bool away, Func<string,string> read, Action<string,string> write, Action refresh, bool showKit = true)
    {
        var dialog = new Window { Title = part + " colors", Width = Math.Min(610, Math.Max(380, owner.Width-40)), Height = Math.Min(640, Math.Max(360, owner.Height-50)), WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = StudioTheme.Surface };
        var panel = new StackPanel { Spacing = StudioTheme.SpaceLg, Margin = new Thickness(24), Children = { Label(part + " colors" + (showKit ? " · " + (away ? "Away" : "Home") : ""),24,Brushes.White) } };
        var values = keys.ToDictionary(k => k, read);
        string selected = keys[0]; bool selecting = false;
        var channels = new StackPanel { Orientation = Orientation.Horizontal, Spacing = StudioTheme.SpaceMd };
        var swatches = new Dictionary<string, Button>();
        var samples = new Dictionary<string, Border>();
        var label = Label(ColorLabel(selected, part),14,Brushes.White);
        var color = new ColorView { IsAlphaEnabled = false, IsAlphaVisible = false, IsColorPreviewVisible = false, IsAccentColorsVisible = false, IsColorPaletteVisible = false, IsColorModelVisible = false, HorizontalAlignment = HorizontalAlignment.Stretch };
        Color ReadColor(string key)
        {
            if (FieldSchema.TryRgb(values[key], out var r, out var g, out var b)) return Color.FromRgb(r,g,b);
            return Colors.White;
        }
        void Select(string key)
        {
            selecting = true; selected = key; color.Color = ReadColor(key); label.Text = ColorLabel(key, part);
            foreach (var pair in swatches) { pair.Value.BorderBrush = pair.Key == key ? Mint : Muted; pair.Value.BorderThickness = new Thickness(pair.Key == key ? 2 : 1); }
            selecting = false;
        }
        foreach (var key in keys)
        {
            var swatch = ActionButton("", () => Select(key));
            var sample = new Border { Height = 30, MinWidth = 100, CornerRadius = new CornerRadius(4), Background = FieldSchema.TryRgb(values[key], out _, out _, out _) ? new SolidColorBrush(ReadColor(key)) : UnsetColor.Brush("") };
            swatch.Content = new StackPanel { Spacing = 6, Children = { Label(ColorLabel(key, part), 12, Brushes.White), sample } };
            samples[key] = sample;
            swatch.FontSize = 12; swatch.Padding = new Thickness(8,6); ToolTip.SetTip(swatch,key); swatches[key] = swatch; channels.Children.Add(swatch);
        }
        color.ColorChanged += (_,e) => {
            if (selecting) return;
            values[selected] = FieldSchema.Rgb(e.NewColor.R,e.NewColor.G,e.NewColor.B);
            samples[selected].Background = new SolidColorBrush(e.NewColor);
            write(selected, values[selected]); refresh();
        };
        Select(selected); panel.Children.Add(channels); panel.Children.Add(label); panel.Children.Add(color);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = StudioTheme.SpaceMd };
        var done = ActionButton("Done", () => dialog.Close()); StudioTheme.Primary(done); actions.Children.Add(done);
        panel.Children.Add(actions); dialog.Content = new ScrollViewer { Content = panel }; await dialog.ShowDialog(owner);
    }

}
