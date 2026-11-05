using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace CampaignCreator;

// Images returned by load belong to the dialog; borrowed images must not be disposed.
internal static class PreviewGridPicker
{
    internal sealed record Choice(string Id, string Label);

    internal static Bitmap? FileImage(string? path) => path != null && File.Exists(path) ? new Bitmap(path) : null;

    internal static Bitmap? StadiumImage(string id)
    {
        var choice = NodeSettingsWindow.Stadiums.FirstOrDefault(s => s.Id == id);
        return choice == null ? null : StudioArt.Bitmap("node-images", "regions", choice.Image + ".png");
    }

    internal static (Control control, Func<string> read) Create(string title, IEnumerable<Choice> choices,
        string value, Func<string, IImage?> load, Action changed, Action<string>? preview = null, bool ownsImages = true,
        Func<string, IEnumerable<Choice>>? choicesForSelection = null, Func<string, bool>? canSelect = null)
    {
        var all = choices.ToArray();
        string saved = all.FirstOrDefault(c => c.Id.Equals(value, StringComparison.OrdinalIgnoreCase))?.Id ?? "";
        var button = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
        void Refresh() => button.Content = title + " · " + (all.FirstOrDefault(c => c.Id == saved)?.Label ?? "Nothing selected");
        Refresh();
        button.Click += async (_, _) => {
            if (TopLevel.GetTopLevel(button) is not Window owner) return;
            if (choicesForSelection != null) all = choicesForSelection(saved).ToArray();
            string? selected = await Show(owner, title, all, saved, load, ownsImages, canSelect);
            if (selected == null || selected == saved) return;
            saved = selected; Refresh(); changed(); preview?.Invoke(saved);
        };
        return (button, () => saved);
    }

    internal static async Task<string?> Show(Window owner, string title, IEnumerable<Choice> choices,
        string selected, Func<string, IImage?> load, bool ownsImages = true, Func<string, bool>? canSelect = null)
    {
        var dialog = new Window { Title = title, Width = 800, Height = 620, MinWidth = 360, MinHeight = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = StudioTheme.Surface };
        var search = new TextBox { Watermark = "Search options…" };
        var grid = new WrapPanel { Orientation = Orientation.Horizontal };
        var feedback = new TextBlock { IsVisible = false, TextWrapping = TextWrapping.Wrap, Foreground = StudioTheme.Muted };
        var tiles = new List<(Choice choice, Button tile)>();
        foreach (var choice in choices)
        {
            var unavailable = new TextBlock { Text = choice.Id.Length == 0 ? "—" : "Preview unavailable",
                TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            var thumbnail = new ContentControl { Height = 104 };
            var tile = new Button { Width = 168, Height = 166, Margin = new Thickness(4),
                BorderThickness = new Thickness(choice.Id == selected ? 3 : 1),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = new StackPanel { Spacing = 6, Children = {
                    thumbnail,
                    new TextBlock { Text = choice.Label, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis }
                } } };
            ToolTip.SetTip(tile, choice.Label);
            bool showing = false;
            IImage? image = null;
            void ShowThumbnail(bool visible)
            {
                if (visible == showing) return;
                showing = visible;
                if (visible) {
                    try { image = load(choice.Id); } catch (Exception) { image = null; }
                    thumbnail.Content = image == null ? (Control)unavailable : new Image { Source = image, Height = 104, Stretch = Stretch.Uniform };
                } else {
                    thumbnail.Content = null;
                    if (ownsImages && image is IDisposable owned) owned.Dispose();
                    image = null;
                }
            }
            var viewport = new Rect();
            void UpdateVisibility() => ShowThumbnail(tile.IsVisible && new Rect(tile.Bounds.Size).Intersects(viewport));
            tile.EffectiveViewportChanged += (_, e) => { viewport = e.EffectiveViewport; UpdateVisibility(); };
            tile.PropertyChanged += (_, e) => { if (e.Property == Visual.IsVisibleProperty) UpdateVisibility(); };
            tile.DetachedFromVisualTree += (_, _) => ShowThumbnail(false);
            tile.Click += (_, _) => {
                if (canSelect == null || canSelect(choice.Id)) { dialog.Close(choice.Id); return; }
                feedback.Text = $"The artwork for {choice.Label} could not be loaded. Choose another head or repair its project.";
                feedback.IsVisible = true;
            };
            tiles.Add((choice, tile)); grid.Children.Add(tile);
        }
        var empty = new TextBlock { Text = "No matching options.", IsVisible = tiles.Count == 0 };
        search.TextChanged += (_, _) => {
            foreach (var (choice, tile) in tiles) tile.IsVisible = (choice.Label + " " + choice.Id).Contains(search.Text ?? "", StringComparison.OrdinalIgnoreCase);
            empty.IsVisible = !tiles.Any(t => t.tile.IsVisible);
        };
        var cancel = new Button { Content = "Cancel", HorizontalAlignment = HorizontalAlignment.Right };
        cancel.Click += (_, _) => dialog.Close();
        dialog.KeyDown += (_, e) => { if (e.Key == Key.Escape) { dialog.Close(); e.Handled = true; } };
        var body = new Grid { Margin = new Thickness(18), RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 12 };
        body.Children.Add(search);
        var scroll = new ScrollViewer { Name = "PreviewGridScroll", HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = new StackPanel { Children = { grid, empty } } };
        var footer = new StackPanel { Spacing = 6, Children = { feedback, cancel } };
        Grid.SetRow(scroll, 1); body.Children.Add(scroll); Grid.SetRow(footer, 2); body.Children.Add(footer);
        dialog.Content = body;
        dialog.Opened += (_, _) => search.Focus();
        try { return await dialog.ShowDialog<string?>(owner); }
        finally { dialog.Content = null; }
    }
}
