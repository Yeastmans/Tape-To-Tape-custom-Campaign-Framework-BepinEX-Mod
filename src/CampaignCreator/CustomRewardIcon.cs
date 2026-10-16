using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace CampaignCreator;

/// <summary>The same named card is previewed in Studio and saved as a game-ready PNG.</summary>
internal static class CustomRewardIcon
{
    internal static async Task<byte[]?> PickNative(Window owner)
    {
        string? index = StudioArt.Path("relic-icons", "index.json");
        if (index == null) throw new InvalidOperationException("Read game artwork from Home to load the base-game relic icons, then reopen this picker.");
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(index));
        var entries = json.RootElement.EnumerateArray().Select(e => (Name: e.GetProperty("name").GetString() ?? "Relic", File: e.GetProperty("file").GetString() ?? "")).OrderBy(e => e.Name).ToArray();
        var dialog = new Window { Title = "Choose a base-game relic icon", Width = 720, Height = 600, MinWidth = 420, MinHeight = 360, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var search = new TextBox { Watermark = "Search relic icons", Name = "NativeRelicIconSearch" };
        var rows = new WrapPanel { Orientation = Avalonia.Layout.Orientation.Horizontal };
        var previews = new List<IDisposable>();
        void Refresh() {
            rows.Children.Clear();
            foreach (var bitmap in previews) bitmap.Dispose(); previews.Clear();
            foreach (var e in entries.Where(e => e.Name.Contains(search.Text ?? "", StringComparison.OrdinalIgnoreCase))) {
                if (Path.GetFileName(e.File) != e.File || StudioArt.Path("relic-icons", e.File) is not { } path) continue;
                var preview = Preview(e.Name, ReadPng(path), 120, 90);
                if (preview is Image { Source: IDisposable bitmap }) previews.Add(bitmap);
                var button = new Button { Width = 152, Margin = new Thickness(4), Content = new StackPanel { Children = {
                    preview, new TextBlock { Text = e.Name, TextWrapping = TextWrapping.Wrap, MaxWidth = 126 }
                } } };
                button.Click += (_, _) => dialog.Close(ReadPng(path)); rows.Children.Add(button);
            }
        }
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 12, Margin = new Thickness(16) };
        layout.Children.Add(search); var scroll = new ScrollViewer { Content = rows, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); layout.Children.Add(scroll);
        var cancel = new Button { Content = "Cancel" }; cancel.Click += (_, _) => dialog.Close(null); Grid.SetRow(cancel, 2); layout.Children.Add(cancel);
        search.TextChanged += (_, _) => Refresh(); dialog.Content = layout; Refresh();
        try { return await dialog.ShowDialog<byte[]?>(owner); }
        finally { foreach (var bitmap in previews) bitmap.Dispose(); }
    }
    internal const int Width = 512, Height = 256;
    internal static byte[] ReadPng(string path)
    {
        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new ArgumentException("Choose a PNG image no larger than 4 MB.");
        var bytes = File.ReadAllBytes(path);
        CampaignCore.CustomRewardDefinition.ValidatePng(bytes);
        using var stream = new MemoryStream(bytes);
        using var decoded = new Bitmap(stream); // Validate the complete image before saving it.
        return bytes;
    }

    internal static Control Preview(string name, byte[]? png, double width = 160, double height = 80)
    {
        if (png is { Length: > 0 })
        {
            using var stream = new MemoryStream(png);
            return new Image { Source = Bitmap.DecodeToWidth(stream, Math.Min(512, (int)Math.Ceiling(width * 2))), Width = width, Height = height, Stretch = Stretch.Uniform };
        }
        return new Border {
            Width = width, Height = height, Background = Brush.Parse("#203747"),
            BorderBrush = Brush.Parse("#69D9C0"), BorderThickness = new Thickness(2), Padding = new Thickness(8),
            Child = new TextBlock { Text = string.IsNullOrWhiteSpace(name) ? "Item name" : name,
                FontSize = 14, FontWeight = FontWeight.Bold, Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }
        };
    }

    internal static byte[] NamedRectangle(string name)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(Width, Height), new Vector(96, 96));
        using (var drawing = bitmap.CreateDrawingContext())
        {
            drawing.DrawRectangle(Brush.Parse("#203747"), new Pen(Brush.Parse("#69D9C0"), 6), new Rect(3, 3, Width - 6, Height - 6));
            FormattedText Text(double size) => new(name, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Arial", FontStyle.Normal, FontWeight.Bold), size, Brushes.White) {
                    MaxTextWidth = Width - 40, TextAlignment = TextAlignment.Center
                };
            var label = Text(42);
            for (double size = 40; label.Height > Height - 40 && size >= 16; size -= 2) label = Text(size);
            drawing.DrawText(label, new Point(20, (Height - label.Height) / 2));
        }
        using var output = new MemoryStream(); bitmap.Save(output); return output.ToArray();
    }
}
