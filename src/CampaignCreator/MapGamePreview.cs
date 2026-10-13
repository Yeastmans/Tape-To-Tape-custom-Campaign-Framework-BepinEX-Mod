using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CampaignCore;

namespace CampaignCreator;

/// <summary>Read-only draft preview. Uses game artwork and shared tiling math, but not native Unity transforms.</summary>
public sealed class MapGamePreview : UserControl
{
    private readonly Canvas scene = new() { Height = 500, ClipToBounds = true };
    private readonly ScrollViewer viewport = new() { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
    private readonly LayoutTransformControl scaled;
    private readonly Slider zoom = new() { Minimum = .03, Maximum = 2, Value = .6, Width = 140 };
    private readonly TextBlock summary = new() { Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
    private readonly MapNodeArtwork artwork = new();
    private readonly Dictionary<string, Bitmap> textures = new();
    private bool fit = true, settingZoom;
    private MapLayout? layout;
    private int gameplayAct;
    private IReadOnlyDictionary<string, string>? teams;
    private MapTeamPreview? teamPreview;
    public int SceneryAct { get; private set; }
    public int HalfParts { get; private set; }
    public int ConnectionCount { get; private set; }
    public int NodeCount { get; private set; }

    public MapGamePreview()
    {
        Name = "GameMapPreview";
        scaled = new LayoutTransformControl { Child = scene, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        viewport.Content = scaled;
        var shell = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 10, Margin = new Thickness(0, 12, 0, 0) };
        var toolbar = new DockPanel();
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var fitButton = new Button { Content = "Fit full map" }; fitButton.Click += (_, _) => { fit = true; Fit(); };
        controls.Children.Add(fitButton); controls.Children.Add(zoom);
        ToolTip.SetTip(zoom, "Preview zoom — scroll to explore a long map");
        zoom.PropertyChanged += (_, e) => { if (e.Property == Slider.ValueProperty) { if (!settingZoom) fit = false; scaled.LayoutTransform = new ScaleTransform(zoom.Value, zoom.Value); } };
        DockPanel.SetDock(controls, Dock.Right); toolbar.Children.Add(controls); toolbar.Children.Add(summary);
        shell.Children.Add(toolbar); Grid.SetRow(viewport, 1); shell.Children.Add(viewport);
        var note = new TextBlock { Text = "Game textures • live unsaved layout • estimated placement. In game, full arena and badge bounds reserve column spacing and vertical margins at native scale. Random/template arenas and native decorations resolve there.", Foreground = Brush.Parse("#A5B7CA"), FontSize = 12, TextWrapping = TextWrapping.Wrap };
        Grid.SetRow(note, 2); shell.Children.Add(note); Content = shell;
        viewport.SizeChanged += (_, _) => { if (fit) Fit(); };
        AttachedToVisualTree += (_, _) => Refresh();
        DetachedFromVisualTree += (_, _) => { scene.Children.Clear(); artwork.Dispose(); foreach (var texture in textures.Values) texture.Dispose(); textures.Clear(); };
    }

    public void Update(MapLayout? map, int act, IReadOnlyDictionary<string, string> teamNames, MapTeamPreview? identity = null)
    {
        layout = map; gameplayAct = act; teams = teamNames; teamPreview = identity;
        // Hidden previews need no GPU resources; they render the latest draft on attachment.
        if (TopLevel.GetTopLevel(this) != null) Refresh();
    }

    // act-N backgrounds come from the player's install (StudioArt); the transition and ending seams are
    // the existing bundled patches (CustomCampaignFramework/Assets/MapSeams), approved for this release.
    private Bitmap? Texture(string key)
    {
        if (!textures.TryGetValue(key, out var texture))
        {
            if (key.StartsWith("act-") && key.IndexOf('-', 4) < 0)
            {
                if (StudioArt.Bitmap("map-preview", key + ".png") is not { } background) return null;
                textures[key] = texture = background;
            }
            else
            {
                using var stream = AssetLoader.Open(new Uri("avares://T2T Campaign Studio/Assets/MapPreview/" + key + ".png"));
                textures[key] = texture = new Bitmap(stream);
            }
        }
        return texture;
    }

    private void Fit()
    {
        if (viewport.Bounds.Width <= 0 || scene.Width <= 0) return;
        settingZoom = true;
        zoom.Value = Math.Clamp(Math.Min((viewport.Bounds.Width - 24) / scene.Width, (viewport.Bounds.Height - 24) / 500), zoom.Minimum, zoom.Maximum);
        scaled.LayoutTransform = new ScaleTransform(zoom.Value, zoom.Value);
        settingZoom = false; viewport.Offset = default;
    }

    private void Refresh()
    {
        scene.Children.Clear(); NodeCount = ConnectionCount = 0;
        if (layout == null) return;
        SceneryAct = Math.Clamp(layout.LookAct == 0 ? gameplayAct : layout.LookAct, 1, 3);
        // Preview-space calibration is deliberately approximate: Unity's template scale,
        // prefab anchor and sprite sockets are only available in the running game.
        float AuthoredX(MapLayoutNode n) => (float)MapNodePlacement.Position(n).X;
        var regular = layout.Nodes.Where(n => n.Type != "Boss").ToArray();
        float start = regular.Length == 0 ? 100 : regular.Min(AuthoredX);
        float right = regular.Length == 0 ? start : regular.Max(AuthoredX);
        var boss = layout.Nodes.FirstOrDefault(n => n.Type == "Boss");
        var spacing = new MapRouteSpacing(start, Math.Max(boss == null ? right + 270 : AuthoredX(boss), right + 1), 1840, 500, -1);
        HalfParts = Math.Clamp(spacing.Repeats + 2, 1, 258);
        var tiles = new MapHalfTiles(2000, HalfParts);
        scene.Width = tiles.EndingStart + tiles.TileWidth;
        scene.Children.Add(new Scenery { Width = scene.Width, Height = 500, Tiles = tiles, BackgroundImage = Texture($"act-{SceneryAct}"), Loop = Texture($"act-{SceneryAct}-transition"), Ending = Texture($"act-{SceneryAct}-a-ending") });
        // Match native graph Y direction and global centring. Do not compress a
        // tall invalid map to make it appear safe; the editor asks for a repair.
        var points = layout.Nodes.ToDictionary(n => n.Id, n => new Point(n.Type == "Boss" ? scene.Width - 160 : spacing.Position(AuthoredX(n)), n.Type == "Boss" ? 260 : MapPlacementBounds.PreviewY(layout, n)));
        foreach (var node in layout.Nodes)
            foreach (var id in node.Outgoing.Distinct())
                if (points.TryGetValue(id, out var end))
                {
                    var from = points[node.Id];
                    scene.Children.Add(new Line { StartPoint = from, EndPoint = end, Stroke = Brush.Parse("#C9223545"), StrokeThickness = 6 });
                    scene.Children.Add(new Line { StartPoint = from, EndPoint = end, Stroke = Brush.Parse("#FFF4CE"), StrokeThickness = 2, StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 3, 3 } });
                    ConnectionCount++;
                }
        foreach (var node in layout.Nodes)
        {
            bool isBoss = node.Type == "Boss";
            var point = points[node.Id];
            var image = artwork.Get(node, gameplayAct);
            var stack = new StackPanel { Width = 156, Spacing = 3, Tag = "preview-node:" + node.Id };
            if (image != null)
            {
                var arena = new Grid();
                arena.Children.Add(new Image { Source = image, Height = isBoss ? 108 : 82, Stretch = Stretch.Uniform });
                var logo = teamPreview?.Logo(node.Team);
                if (logo != null) arena.Children.Add(new Image { Name = "PreviewTeamLogo", Tag = node.Team, Source = logo, Width = 42, Height = 42, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top });
                stack.Children.Add(arena);
            }
            else stack.Children.Add(new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(16), Background = Brush.Parse("#D9223545"), BorderBrush = Brushes.White, BorderThickness = new Thickness(2), Child = new TextBlock { Text = string.IsNullOrEmpty(node.Type) ? "+" : "•", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White, FontSize = 21 } });
            string label = string.IsNullOrEmpty(node.Type) ? "Choose node type" : FieldControls.Friendly(node.Type);
            if (!string.IsNullOrEmpty(node.Team) && teams != null && teams.TryGetValue(node.Team, out var team)) label += "\n" + team;
            if (node.Type is "Elite" or "Boss" && string.IsNullOrEmpty(node.Stadium)) label += "\nArena decided in game";
            stack.Children.Add(new Border { Background = Brush.Parse("#DC132234"), CornerRadius = new CornerRadius(7), Padding = new Thickness(7, 4), Child = new TextBlock { Text = label, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Foreground = isBoss ? Brush.Parse("#FFD68A") : Brushes.White, FontSize = 12 } });
            ToolTip.SetTip(stack, $"Layer {node.Layer + 1} · {label}\n{teamPreview?.Description(node.Team)}");
            Canvas.SetLeft(stack, point.X - 78); Canvas.SetTop(stack, point.Y - (image == null ? 16 : isBoss ? 82 : 62));
            scene.Children.Add(stack); NodeCount++;
        }
        summary.Text = $"Act {SceneryAct} scenery  ·  {layout.Nodes.Select(n => n.Layer).Distinct().Count()} layers  ·  {HalfParts} middle section{(HalfParts == 1 ? "" : "s")}";
        if (MapPlacementBounds.Validate(layout).Count > 0)
            summary.Text += "  ·  Placement needs repair in Node builder";
        if (fit) Fit();
    }

    private sealed class Scenery : Control
    {
        // BackgroundImage is null until the art cache has been built from the game.
        public required Bitmap? BackgroundImage, Loop, Ending;
        public MapHalfTiles Tiles;
        public override void Render(DrawingContext context)
        {
            base.Render(context);
            if (BackgroundImage == null || Loop == null || Ending == null) return;
            void Strip(double source, double target, double width)
            {
                var size = BackgroundImage.Size;
                context.DrawImage(BackgroundImage, new Rect(source / 2000 * size.Width, 0, width / 2000 * size.Width, size.Height), new Rect(target, 0, width, 500));
            }
            void Seam(Bitmap bitmap, double boundary)
            {
                var rect = new Rect(Tiles.SeamStart((float)boundary), 0, Tiles.TileWidth, 500);
                var feather = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative), GradientStops = new GradientStops { new(Colors.Transparent, 0), new(Colors.White, .02), new(Colors.White, .98), new(Colors.Transparent, 1) } };
                using (context.PushOpacityMask(feather, rect)) context.DrawImage(bitmap, new Rect(bitmap.Size), rect);
            }
            Strip(0, 0, 500);
            for (int i = 0; i < Tiles.Parts; i++) Strip(Tiles.SourceStart(i), Tiles.Destination(i), 500);
            Strip(1500, Tiles.EndingStart, 500);
            for (int i = 0; i < Tiles.Parts; i++) if (Tiles.NeedsLoopSeam(i)) Seam(Loop, Tiles.Destination(i));
            if (Tiles.EndsWithA) Seam(Ending, Tiles.EndingStart);
        }
    }
}
