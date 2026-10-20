using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using Avalonia.VisualTree;
using CampaignCore;

namespace CampaignCreator;

public sealed class MapEditorView : UserControl
{
    private string campaignDir;
    private readonly List<MapLayout> catalog;
    private readonly List<MapLayout> sourceCatalog;
    private readonly Dictionary<string, string> teamNames;
    private readonly StackPanel tabs = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
    private readonly ComboBox mapLook = new() { Name = "MapLook", ItemsSource = new[] { "Act 1", "Act 2", "Act 3" }, Width = 120 };
    private bool refreshingLook;
    private readonly ComboBox generation = new() { Name = "MapGeneration", ItemsSource = new[] { "Custom layout", "Vanilla random" }, Width = 190 };
    private bool refreshingGeneration;
    private readonly TextBox mapName = new() { Name = "MapName", Watermark = "Use the game's act name", Width = 280 };
    private readonly TextBlock introAct = new() { Name = "MapIntroAct", VerticalAlignment = VerticalAlignment.Center, Foreground = StudioTheme.Muted };
    private bool refreshingName;
    private readonly List<MapDraft> drafts = new();
    private readonly string contentRoot;
    private readonly Func<string> selectedTemplate;
    private string defaultTemplate;
    private sealed class MapDraft
    {
        public MapLayout? Layout;
        public int Act, OriginalIndex;
        public bool Inherited;
        public bool NeedsSpacingSave;
        public Stack<string> Undo = new(), Redo = new();
    }
    private readonly Canvas canvas = new() { Width = 1500, Height = 700, Background = StudioTheme.Background };
    private readonly ScrollViewer mapViewport = new();
    private readonly LayoutTransformControl scaledCanvas = new();
    private readonly TextBlock zoomLabel = new() { Width = 44, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center };
    private double builderZoom = .8;
    private bool fitBuilder = true;
    private bool draggingNode;
    private readonly MapGamePreview gamePreview = new();
    private Window? detailsDialog;
    private readonly StackPanel inspector = new() { Spacing = StudioTheme.SpaceMd };
    private readonly TextBlock message = new() { TextWrapping = TextWrapping.Wrap };
    private Stack<string> undo = new(), redo = new();
    private MapLayout? map;
    private MapLayoutNode? selected;
    private bool dirty, connecting;
    private string? sequenceError;
    private Point connectionCursor;
    private Avalonia.Controls.Shapes.Path? connectionPreview;
    private int currentStage;
    private readonly Dictionary<string, Button> tiles = new();
    private int[] acts => drafts.Select(d => d.Act).ToArray();
    private readonly MapNodeArtwork artwork = new();
    private MapTeamPreview teamPreview;
    public MapLayout? CurrentLayout => map;
    public int MapCount => drafts.Count;
    public IReadOnlyList<MapLayout> RewardLayouts => drafts.Select(d => d.Layout!).ToList();
    public Func<IReadOnlyList<MapLayout>, string?>? RewardValidation { get; set; }
    public void SelectMap(int index)
    {
        if (index >= 0 && index < drafts.Count) LoadStage(index);
    }
    public bool HasUnsavedChanges => dirty;
    public Dictionary<string, List<string>> AssignedOpponents => OpponentAssignments.FromLayouts(
        drafts.Select((draft, index) => (index + 1, draft.Layout!)));
    /// <summary>Every node that takes an opponent team (Elite/Boss), across all maps, in play order.</summary>
    public List<MatchNode> MatchNodes() => OpponentAssignments.MatchNodes(drafts.Select((d, i) => (i + 1, d.Act, d.Layout!)));
    /// <summary>Assigns a team to a match node from outside the builder; the change is saved with the maps.</summary>
    public void AssignTeam(MatchNode target, string team)
    {
        int mapIndex = target.Map - 1;
        if (mapIndex < 0 || mapIndex >= drafts.Count) return;
        var draft = drafts[mapIndex];
        var node = draft.Layout!.Nodes.FirstOrDefault(n => n.Type is "Elite" or "Boss" && n.Layer == target.Layer && draft.Layout.NativeIndex(n) == target.Native);
        if (node == null || node.Team.Equals(team, StringComparison.OrdinalIgnoreCase)) return;
        RefreshTeams();
        if (mapIndex == currentStage) Checkpoint();
        else { draft.Undo.Push(draft.Layout.Serialize()); draft.Redo.Clear(); draft.Inherited = false; dirty = true; }
        node.Team = team;
        if (mapIndex == currentStage) { Draw(); if (selected == node) Inspect(); Validate(); }
    }
    public MapEditorView(string contentRoot, string campaignDirectory, Func<string>? selectedTemplate = null)
    {
        campaignDir = campaignDirectory;
        this.contentRoot = contentRoot;
        this.selectedTemplate = selectedTemplate ?? (() => SquadTemplateCatalog.ResolveCampaign(campaignDir));
        defaultTemplate = this.selectedTemplate();
        teamPreview = new MapTeamPreview(contentRoot, campaignDirectory);
        DetachedFromVisualTree += (_, _) => { connecting = false; artwork.Dispose(); teamPreview.Dispose(); };

        Background = StudioTheme.Background;
        Focusable = true;
        catalog = MapCatalog.LoadBaseMaps(System.IO.Path.Combine(contentRoot, "_game_maps.txt"));
        sourceCatalog = MapCatalog.LoadSourceMaps(System.IO.Path.Combine(contentRoot, "_game_maps.txt"));
        if (sourceCatalog.Count == 0) sourceCatalog = catalog;
        teamNames = CampaignNavigation.Load(campaignDir).Where(n => n.Context.StartsWith("Opponent ") && n.FilePath != null)
            .ToDictionary(n => System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(n.FilePath))!, n => n.DisplayName, StringComparer.OrdinalIgnoreCase);
        var configFile = System.IO.Path.Combine(campaignDir, "campaign.txt");
        var config = new CampaignDocument(File.Exists(configFile) ? File.ReadAllText(configFile) : "");
        if (!CampaignActSequence.TryParse(config.Get("Act Sequence", CampaignActSequence.Default), out var sequence))
        {
            sequenceError = CampaignActSequence.ValidationMessage + " Correct Act Sequence in campaign.txt, then reopen this campaign.";
            message.Text = sequenceError;
            Content = new Border { Name = "MapSequenceError", Margin = new Thickness(16), Child = message };
            return;
        }
        for (int i = 0; i < sequence.Length; i++)
        {
            string file = System.IO.Path.Combine(campaignDir, "map_layouts", $"map-{i + 1}.txt");
            var layout = File.Exists(file) ? MapLayout.Parse(File.ReadAllText(file)) : MapCatalog.TemplateMap(catalog, defaultTemplate, sequence[i]);
            MapCatalog.HydrateNativePools(layout, catalog);
            if (!File.Exists(file)) layout.VanillaRandom = true;
            if (layout.LookAct == 0) layout.LookAct = sequence[i];
            var beforeX = layout.Nodes.ToDictionary(n => n.Id, n => n.X);
            // Existing off-screen maps remain unchanged until the user chooses
            // Fit node placement. Opening the editor must not silently repair a file.
            if (MapPlacementBounds.Validate(layout).Count == 0 && MapNodePlacement.ArrangeLayers(layout)) dirty = true;
            bool spacingAdjusted = File.Exists(file) && layout.Nodes.Any(n => Math.Abs(n.X - beforeX[n.Id]) > .001);
            drafts.Add(new MapDraft { Layout = layout, Act = sequence[i], OriginalIndex = i + 1, Inherited = !File.Exists(file), NeedsSpacingSave = spacingAdjusted });
        }
        NormalizeCampaign();
        foreach (var draft in drafts)
            if (MapPlacementBounds.Validate(draft.Layout!).Count == 0) MapNodePlacement.ArrangeLayers(draft.Layout!);
        var shell = new Grid { Margin = new Thickness(16), RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = StudioTheme.SpaceMd };
        var top = new DockPanel();
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var lookSettings = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 0, 0) };
        lookSettings.Children.Add(new TextBlock { Text = "Map look", Foreground = StudioTheme.Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,10,0) });
        lookSettings.Children.Add(mapLook);
        mapLook.SelectionChanged += (_, _) => {
            if (refreshingLook || map == null || mapLook.SelectedIndex < 0) return;
            int act = mapLook.SelectedIndex + 1;
            if (map.LookAct == act) return;
            Checkpoint(); map.LookAct = act; Draw();
            message.Text = $"Map {currentStage + 1} look: Act {act}. Save maps to keep the change.";
        };
        actions.Children.Add(Action("+ Add map", AddMap));
        actions.Children.Add(Action("Import base-game map", OpenImport));
        var fitPlacement = Action("Fit node placement", FitNodePlacement);
        fitPlacement.Name = "FitNodePlacement";
        ToolTip.SetTip(fitPlacement, "Move this draft's nodes inside the visible game map area. Save maps applies the change and keeps a backup.");
        actions.Children.Add(fitPlacement);
        DockPanel.SetDock(actions, Dock.Right); top.Children.Add(actions);
        top.Children.Add(new ScrollViewer { Content = tabs, ClipToBounds = true, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        var heading = new StackPanel { Spacing = 8 };
        heading.Children.Add(top);
        var identity = new WrapPanel { Orientation = Orientation.Horizontal };
        identity.Children.Add(new TextBlock { Text = "Generation", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        identity.Children.Add(generation);
        generation.Margin = new Thickness(0, 0, 16, 0);
        generation.SelectionChanged += (_, _) => { if (!refreshingGeneration && generation.SelectedIndex >= 0) SetVanillaRandom(generation.SelectedIndex == 1); };
        ToolTip.SetTip(generation, "Both modes use this editable layout. Vanilla random starts from the imported map's event pools and opponents. Each random node has its own choices and weights.");
        var nameSettings = new StackPanel { Orientation = Orientation.Horizontal };
        nameSettings.Children.Add(new TextBlock { Text = "Map name", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        nameSettings.Children.Add(mapName); identity.Children.Add(nameSettings);
        introAct.Margin = new Thickness(16, 0, 0, 0); identity.Children.Add(introAct);
        identity.Children.Add(lookSettings);
        ToolTip.SetTip(mapName, "Shown on this map's opening wipe. Leave blank to keep the game's act name.");
        mapName.TextChanged += (_, _) => {
            if (refreshingName || map == null || map.Name == (mapName.Text ?? "")) return;
            Checkpoint(); map.Name = mapName.Text ?? ""; RefreshTabs();
            message.Text = "Map name changed. Save maps to keep the change.";
        };
        heading.Children.Add(identity);
        shell.Children.Add(heading);
        var body = new Grid();
        scaledCanvas.Child = canvas;
        scaledCanvas.HorizontalAlignment = HorizontalAlignment.Left;
        scaledCanvas.VerticalAlignment = VerticalAlignment.Top;
        scaledCanvas.LayoutTransform = new ScaleTransform(builderZoom, builderZoom);
        mapViewport.Name = "NodeBuilderViewport";
        mapViewport.Content = scaledCanvas;
        mapViewport.HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        mapViewport.VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        mapViewport.SizeChanged += (_, _) => SetBuilderZoom(builderZoom, fitBuilder);
        var views = new TabControl { Name = "MapViewTabs", ItemsSource = new[] {
            new TabItem { Header = "Node builder", Content = mapViewport },
            new TabItem { Header = "Game preview", Content = gamePreview }
        } };
        body.Children.Add(views);
        Grid.SetRow(body, 1); shell.Children.Add(body);
        var footer = new DockPanel();
        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        controls.Children.Add(Action("Undo", Undo)); controls.Children.Add(Action("Redo", Redo));
        var saveButton = Action("Save maps", SaveMap); StudioTheme.Primary(saveButton); controls.Children.Add(saveButton);
        var zoomControls = new StackPanel { Orientation = Orientation.Horizontal };
        var zoomOut = Action("−", () => SetBuilderZoom(builderZoom / 1.2)); zoomOut.Name = "NodeZoomOut";
        var zoomIn = Action("+", () => SetBuilderZoom(builderZoom * 1.2)); zoomIn.Name = "NodeZoomIn";
        ToolTip.SetTip(zoomOut, "Zoom out"); ToolTip.SetTip(zoomIn, "Zoom in");
        zoomControls.Children.Add(zoomOut); zoomControls.Children.Add(zoomLabel); zoomControls.Children.Add(zoomIn);
        zoomControls.Children.Add(Action("Fit", () => SetBuilderZoom(1, true)));
        views.SelectionChanged += (_, _) => { zoomControls.IsVisible = views.SelectedIndex == 0; if (views.SelectedIndex != 0) CancelConnection(); };
        controls.Children.Add(zoomControls); DockPanel.SetDock(controls, Dock.Right); footer.Children.Add(controls); footer.Children.Add(message);
        Grid.SetRow(footer, 2); shell.Children.Add(footer); Content = shell;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && connecting) { CancelConnection(); e.Handled = true; }
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.S) { SaveMap(); e.Handled = true; }
        };
        AttachedToVisualTree += (_, _) => { RefreshTemplateDefaults(); RefreshTeams(); Draw(); };
        EnableNodeDragging();
        LoadStage(0);
    }
    private Window DialogOwner => TopLevel.GetTopLevel(this) as Window ?? throw new InvalidOperationException("Map editor must be attached to a window.");
    public void RefreshTemplateDefaults()
    {
        if (sequenceError != null) return;
        string template = selectedTemplate();
        if (SquadTemplatePolicy.Identity(template) == SquadTemplatePolicy.Identity(defaultTemplate)) return;
        defaultTemplate = template;
        foreach (var draft in drafts.Where(d => d.Inherited))
        {
            draft.Layout = MapCatalog.TemplateMap(catalog, template, draft.Act);
            draft.Layout.VanillaRandom = true;
            MapNodePlacement.ArrangeLayers(draft.Layout);
        }
        NormalizeCampaign();
        LoadStage(currentStage);
    }
    private void SetBuilderZoom(double requested, bool fitWidth = false)
    {
        fitBuilder = fitWidth;
        if (mapViewport.Bounds.Width <= 24 || mapViewport.Bounds.Height <= 24) return;
        double fit = Math.Min((mapViewport.Bounds.Height - 24) / canvas.Height,
            (mapViewport.Bounds.Width - 24) / canvas.Width);
        builderZoom = Math.Clamp(fitWidth ? fit : requested, Math.Min(.01, fit), 8);
        scaledCanvas.LayoutTransform = new ScaleTransform(builderZoom, builderZoom);
        zoomLabel.Text = $"{builderZoom:P0}";
        if (fitWidth) mapViewport.Offset = default;
    }
    public void SetCampaignDirectory(string directory) { campaignDir = directory; teamPreview.Dispose(); teamPreview = new MapTeamPreview(contentRoot, directory); RefreshTeams(); }
    private void RefreshTeams()
    {
        teamNames.Clear();
        foreach (var team in CampaignNavigation.Load(campaignDir).Where(n => n.Context.StartsWith("Opponent ") && n.FilePath != null))
            teamNames[System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(team.FilePath))!] = team.DisplayName;
    }
    private static MapLayout NewMap(int act)
    {
        var first = new MapLayoutNode { Type = "", Layer = 0 };
        var boss = new MapLayoutNode { Type = "Boss", Layer = 1 };
        MapNodePlacement.Set(first, new Point(MapNodePlacement.LayerCenterLeft(0), MapNodePlacement.MinY));
        MapNodePlacement.Set(boss, new Point(MapNodePlacement.LayerCenterLeft(1), MapNodePlacement.MinY));
        return new MapLayout { BaseSquad = "Basic Squad", BaseMap = act - 1, LookAct = act, Nodes = [first, boss] };
    }
    private void RefreshTabs()
    {
        tabs.Children.Clear();
        for (int i = 0; i < drafts.Count; i++)
        {
            int index = i;
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            string name = drafts[i].Layout?.Name.Trim() ?? "";
            var select = Action($"Map {i + 1}" + (name.Length > 0 ? " · " + name : ""), () => LoadStage(index));
            select.MaxWidth = 250; select.ClipToBounds = true; ToolTip.SetTip(select, select.Content);
            select.Classes.Add("map-select"); select.Foreground = i == currentStage ? StudioTheme.Accent : StudioTheme.Muted; select.Margin = new Thickness(0);
            var close = Action("×", () => RemoveMap(index)); close.Tag = index;
            close.Classes.Add("map-close"); close.Margin = new Thickness(0);
            Avalonia.Automation.AutomationProperties.SetName(close, $"Remove Map {i + 1}");
            ToolTip.SetTip(close, $"Remove Map {i + 1}"); close.IsEnabled = drafts.Count > 1;
            header.Children.Add(select); header.Children.Add(close);
            var tab = new Border { Child = header, Classes = { "map-tab" } };
            if (i == currentStage) tab.Classes.Add("active");
            tabs.Children.Add(tab);
        }
    }
    private void NormalizeCampaign()
    {
        foreach (var draft in drafts)
        {
            var layout = draft.Layout!;
            if (!layout.Nodes.Any(n => n.Layer == 0 && n.Type != "Boss"))
            {
                foreach (var node in layout.Nodes) node.Layer++;
                var entry = new MapLayoutNode { Type = "", Layer = 0 };
                MapNodePlacement.Set(entry, new Point(MapNodePlacement.LayerCenterLeft(0), MapNodePlacement.MinY));
                layout.Nodes.Add(entry);
            }
            if (layout.Nodes.Any(n => n.Type == "Boss")) continue;
            var boss = new MapLayoutNode { Type = "Boss", Layer = layout.Nodes.Count == 0 ? 0 : layout.Nodes.Max(n => n.Layer) + 1 };
            double x = layout.Nodes.Count == 0 ? 600 : layout.Nodes.Max(n => NodePosition(n).X) + 245;
            MapNodePlacement.Set(boss, new Point(x, 200)); layout.Nodes.Add(boss);
        }
    }
    private void AddMap()
    {
        drafts.Add(new MapDraft { Layout = NewMap(1), Act = 1 }); dirty = true; LoadStage(drafts.Count - 1);
    }
    private void RemoveMap(int index)
    {
        if (drafts.Count <= 1) return;
        drafts.RemoveAt(index); dirty = true;
        LoadStage(index < currentStage ? currentStage - 1 : Math.Min(currentStage, drafts.Count - 1));
        message.Text = "Map removed from draft. Save maps to keep the change.";
    }
    internal static string NodeName(string type) => type switch {
        "Elite" => "Match", "NULL" => "Random event", "FanNumber1" => "Fan", "GeneralManager" => "GM",
        "Boss" => "Boss", "" => "Choose node type", _ => FieldControls.Friendly(type) };
    private void OpenNodePicker(int layer, bool newLayer, MapLayoutNode? placeholder = null)
    {
        if (map == null) return;
        if (placeholder == null && !newLayer && (map.InLayer(layer).Count() >= MapPlacementBounds.MaxNodesPerLayer || map.InLayer(layer).Any(n => n.Type == "Boss"))) return;
        var dialog = new Window { Title = "Choose a node", Width = 450, Height = 550, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Background };
        var choices = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (string kind in MapLayout.NodeTypes.Where(t => t != "Boss"))
        {
            string type = kind;
            var button = new Button { Content = NodeName(type), Tag = "node-choice:" + type, Width = 185, Margin = new Thickness(4), Padding = new Thickness(10,12) };
            button.Click += (_, _) => {
                if (placeholder != null && map.Nodes.Contains(placeholder)) { Checkpoint(); placeholder.Type = type; selected = placeholder; Draw(); Inspect(); Validate(); }
                else AddLayerNode(layer, newLayer, type);
                dialog.Close();
            };
            choices.Children.Add(button);
        }
        var cancel = new Button { Content = "Cancel" }; cancel.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel { Margin = new Thickness(20), Spacing = StudioTheme.SpaceMd, Children = {
            new TextBlock { Text = newLayer ? "Add a layer" : $"Layer {layer + 1} - choose a node", FontSize = 23, FontWeight = FontWeight.Bold },
            new TextBlock { Text = "Add choices freely. Layers with three or more nodes use closer row spacing." }, choices, cancel } };
        _ = dialog.ShowDialog(DialogOwner);
    }
    private void AddLayerNode(int layer, bool newLayer, string type)
    {
        if (map == null || type == "Boss" || (type.Length > 0 && !MapLayout.NodeTypes.Contains(type))) return;
        if (!newLayer && (map.InLayer(layer).Count() >= MapPlacementBounds.MaxNodesPerLayer || map.InLayer(layer).Any(n => n.Type == "Boss"))) return;
        var working = MapLayout.Parse(map.Serialize());
        var added = new MapLayoutNode { Layer = layer, Type = type };
        double nextY = newLayer || !working.InLayer(layer).Any() ? MapNodePlacement.MinY
            : working.InLayer(layer).Max(n => NodePosition(n).Y) + MapNodePlacement.VerticalSpacing;
        if (newLayer)
        {
            if (layer <= 0 || layer > working.Nodes.Max(n => n.Layer)) return;
            // Existing links across this boundary cannot skip the inserted layer.
            var nextIds = working.InLayer(layer).Select(n => n.Id).ToHashSet();
            foreach (var previous in working.InLayer(layer - 1)) previous.Outgoing.RemoveAll(nextIds.Contains);
            foreach (var node in working.Nodes.Where(n => n.Layer >= layer)) { node.Layer++; node.X += MapNodePlacement.NativeLayerPitch; }
        }
        var existing = working.InLayer(layer).ToArray();
        double spacing = MapNodePlacement.VerticalSpacing;
        var positions = existing.SelectMany(n => new[] { MapNodePlacement.Position(n).Y - spacing, MapNodePlacement.Position(n).Y + spacing })
            .Concat(new[] { nextY, MapNodePlacement.MinY, MapNodePlacement.MaxY })
            .Where(y => y >= MapNodePlacement.MinY && y <= MapNodePlacement.MaxY)
            .Select(y => new Point(MapNodePlacement.LayerCenterLeft(layer), y))
            .Where(p => existing.All(n => !MapNodePlacement.Overlaps(p, n)))
            .OrderBy(p => Math.Abs(p.Y - nextY)).ToArray();
        if (existing.Length >= 2 || positions.Length == 0)
        {
            // Repack a crowded layer as one undoable edit. Preserve the existing
            // top-to-bottom order, with the added choice in the last row.
            var rows = existing.OrderBy(n => n.Y).Append(added).ToArray();
            for (int i = 0; i < rows.Length; i++) rows[i].Y = MapPlacementBounds.RowY(rows.Length, i);
            added.X = MapNodePlacement.NativeStartX + layer * MapNodePlacement.NativeLayerPitch;
        }
        else MapNodePlacement.Set(added, positions[0]);
        working.Nodes.Add(added); Checkpoint(); map = working; drafts[currentStage].Layout = map;
        selected = added; connecting = false; Draw(); Inspect(); message.Text = "Node added. Select connections to the next layer when ready.";
    }
    private void RemoveLayer(int layer)
    {
        if (map == null || layer <= 0 || map.InLayer(layer).Any(n => n.Type == "Boss")) return;
        var ids = map.InLayer(layer).Select(n => n.Id).ToHashSet();
        if (ids.Count == 0) return;
        Checkpoint();
        map.Nodes.RemoveAll(n => ids.Contains(n.Id));
        foreach (var node in map.Nodes)
        {
            node.Outgoing.RemoveAll(ids.Contains);
            if (node.Layer > layer) { node.Layer--; node.X -= MapNodePlacement.NativeLayerPitch; }
        }
        selected = null; connecting = false; Draw(); Inspect();
        message.Text = "Layer removed. Undo restores its nodes and connections.";
    }
    private void OpenImport()
    {
        var dialog = new Window { Title = "Import map", Width = 530, Height = 330, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Background };
        var source = new ComboBox { Name = "ImportSource", ItemsSource = new[] { "Base game maps", "Custom maps" }, HorizontalAlignment = HorizontalAlignment.Stretch };
        var choice = new ComboBox { Name = "ImportMap", HorizontalAlignment = HorizontalAlignment.Stretch };
        var hint = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var options = new List<(string Label, MapLayout Layout)>();
        var import = new Button { Content = "Import map", IsEnabled = false };
        source.SelectionChanged += (_, _) =>
        {
            options.Clear();
            if (source.SelectedIndex == 0)
                options.AddRange(catalog.Select(m => ($"{m.BaseSquad} · Map {m.BaseMap + 1} · Variant {m.Template + 1}", m)));
            else
            {
                string campaigns = System.IO.Path.Combine(contentRoot, "campaigns");
                if (Directory.Exists(campaigns)) foreach (string dir in Directory.GetDirectories(campaigns).OrderBy(d => d))
                {
                    if (System.IO.Path.GetFullPath(dir).Equals(System.IO.Path.GetFullPath(campaignDir), StringComparison.OrdinalIgnoreCase)) continue;
                    string layouts = System.IO.Path.Combine(dir, "map_layouts");
                    if (!Directory.Exists(layouts)) continue;
                    string configPath = System.IO.Path.Combine(dir, "campaign.txt");
                    string name = File.Exists(configPath) ? new CampaignDocument(File.ReadAllText(configPath)).Get("Name", System.IO.Path.GetFileName(dir)) : System.IO.Path.GetFileName(dir);
                    foreach (string file in Directory.GetFiles(layouts, "map-*.txt").OrderBy(f => f))
                    {
                        try { options.Add(($"{name} · Map {System.IO.Path.GetFileNameWithoutExtension(file)[4..]}", MapLayout.Parse(File.ReadAllText(file)))); }
                        catch { /* A broken source map must not prevent importing other maps. */ }
                    }
                }
            }
            choice.ItemsSource = options.Select(o => o.Label).ToArray(); choice.SelectedIndex = options.Count > 0 ? 0 : -1;
            hint.Text = options.Count == 0 ? "No maps available from this source." : "Replaces the selected map tab. You can undo the import. Assign your opponents to its match nodes afterward.";
            import.IsEnabled = options.Count > 0;
        };
        import.Click += (_, _) =>
        {
            if (choice.SelectedIndex < 0 || choice.SelectedIndex >= options.Count) return;
            Checkpoint(); map = MapLayout.Parse(options[choice.SelectedIndex].Layout.Serialize());
            if (source.SelectedIndex == 0) map.VanillaRandom = true;
            if (source.SelectedIndex == 1) foreach (var node in map.Nodes) node.Team = "";
            drafts[currentStage].Layout = map; NormalizeCampaign(); if (map.LookAct == 0) map.LookAct = Math.Clamp(map.BaseMap + 1, 1, 3); RefreshMapLook(); RefreshGeneration();
            // Preserve imported positions until an explicit fit action when the
            // source exceeds the native visible envelope. Save validation blocks it.
            bool visible = MapPlacementBounds.Validate(map).Count == 0;
            if (visible) MapNodePlacement.ArrangeLayers(map);
            dirty = true; selected = map.Nodes.FirstOrDefault(); connecting = false;
            Draw(); Inspect(); Validate(); dialog.Close();
        };
        var cancel = new Button { Content = "Cancel" }; cancel.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel { Margin = new Thickness(24), Spacing = StudioTheme.SpaceLg, Children = {
            new TextBlock { Text = "Import map", FontSize = 24, FontWeight = FontWeight.Bold }, source, choice, hint,
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = StudioTheme.SpaceMd, Children = { import, cancel } } } };
        source.SelectedIndex = 0;
        _ = dialog.ShowDialog(DialogOwner);
    }
    private Button Action(string label, System.Action action)
    {
        var button = new Button { Content = label, Margin = new Thickness(0, 0, 8, 4), Padding = StudioTheme.ButtonPadding };
        button.Click += (_, _) => { try { action(); } catch (Exception ex) { message.Text = ex.Message; } }; return button;
    }
    private void LoadStage(int index)
    {
        if (index < 0 || index >= drafts.Count) return;
        currentStage = index; map = drafts[index].Layout; undo = drafts[index].Undo; redo = drafts[index].Redo;
        selected = null; connecting = false; canvas.Width = 1500; canvas.Height = 700;
        bool visible = MapPlacementBounds.Validate(map!).Count == 0;
        if (visible) MapNodePlacement.ArrangeLayers(map!);
        RefreshTabs(); RefreshMapLook(); Draw(); Inspect(); RefreshGeneration();
        message.Text = !visible
            ? "Map placement needs attention. " + string.Join("  ", MapPlacementBounds.Validate(map!).Take(2))
            : drafts[index].NeedsSpacingSave
            ? "This map used older node spacing. Save maps to update the in-game route, then start a new run."
            : "Click + on a layer to add a node. Three or more choices share closer row spacing.";
        if (map!.VanillaRandom) message.Text = NativeGenerationMessage;
    }
    private const string NativeGenerationMessage = "Vanilla random · one editable layout. Each random node rolls from its own choices on a new run. Open that node's Random event options to edit them.";
    public void SetVanillaRandom(bool enabled)
    {
        if (map == null || map.VanillaRandom == enabled) return;
        Checkpoint(); map.VanillaRandom = enabled;
        selected = null; connecting = false;
        RefreshGeneration(); Draw(); Inspect();
        message.Text = enabled ? NativeGenerationMessage : "Custom layout · edit this fixed route, then save maps.";
    }
    private void RefreshGeneration()
    {
        refreshingGeneration = true;
        generation.SelectedIndex = map?.VanillaRandom == true ? 1 : 0;
        refreshingGeneration = false;
    }
    private void RefreshMapLook()
    {
        refreshingLook = true; mapLook.SelectedIndex = (map!.LookAct > 0 ? map.LookAct : drafts[currentStage].Act) - 1; refreshingLook = false;
        refreshingName = true; mapName.Text = map.Name; refreshingName = false;
        introAct.Text = $"ACT {currentStage + 1} · Number follows campaign order";
        RefreshTabs();
    }
    private string Snapshot()
    {
        var state = new CampaignDocument(map!.Serialize()); state.Set("Editor Act", drafts[currentStage].Act.ToString()); return state.ToString();
    }
    private void Checkpoint() { if (map == null) return; drafts[currentStage].Inherited = false; undo.Push(Snapshot()); redo.Clear(); dirty = true; }
    public void FitNodePlacement()
    {
        if (map == null) return;
        var capacity = MapPlacementBounds.CapacityErrors(map);
        if (capacity.Count > 0) { message.Text = string.Join("  ", capacity); return; }
        Checkpoint();
        MapNodePlacement.ArrangeLayers(map);
        Draw(); Inspect(); Validate();
        message.Text = "Node placement fitted to the visible map area in this draft. Save maps applies it with a backup; Undo restores the previous positions.";
    }
    public void Undo() { if (map == null || undo.Count == 0) return; redo.Push(Snapshot()); Restore(undo.Pop()); }
    public void Redo() { if (map == null || redo.Count == 0) return; undo.Push(Snapshot()); Restore(redo.Pop()); }
    private void Restore(string text) { string? id = selected?.Id; map = MapLayout.Parse(text); drafts[currentStage].Layout = map; NormalizeCampaign(); drafts[currentStage].Act = int.TryParse(new CampaignDocument(text).Get("Editor Act"), out int restoredAct) ? Math.Clamp(restoredAct, 1, 3) : drafts[currentStage].Act; RefreshMapLook(); RefreshGeneration(); selected = map.Nodes.FirstOrDefault(n => n.Id == id); dirty = true; connecting = false; Draw(); Inspect(); Validate(); }
    public void SaveMap() => TrySaveMap(out _);
    public bool TrySaveMap(out string error)
    {
        error = "";
        // A malformed import must stay intact for correction, including when
        // campaign.txt was changed after this editor loaded its valid drafts.
        string configFile = System.IO.Path.Combine(campaignDir, "campaign.txt");
        var config = new CampaignDocument(File.Exists(configFile) ? File.ReadAllText(configFile) : "");
        if (sequenceError != null || !CampaignActSequence.TryParse(config.Get("Act Sequence", CampaignActSequence.Default), out _))
        {
            error = sequenceError ?? CampaignActSequence.ValidationMessage;
            message.Text = error;
            return false;
        }
        for (int i = 0; i < drafts.Count; i++)
        {
            var errors = drafts[i].Layout == null ? null : ValidateLayout(drafts[i].Layout!);
            if (errors == null || errors.Count > 0) { error = $"Map {i + 1} not saved: " + (errors == null ? "No layout." : string.Join("  ", errors.Take(4))); message.Text = error; return false; }
        }
        string? reserve = RewardValidation != null ? RewardValidation(RewardLayouts)
            : string.Join("\n", CampaignRewardBudget.Calculate(contentRoot, campaignDir, layouts: RewardLayouts).SelectMany(b => b.Errors));
        if (!string.IsNullOrWhiteSpace(reserve)) { error = reserve; message.Text = error; return false; }
        string folder = System.IO.Path.Combine(campaignDir, "map_layouts"); Directory.CreateDirectory(folder);
        // Keep a recoverable copy before renumbering or removing saved layouts.
        string backup = System.IO.Path.Combine(campaignDir, "map_backups", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff"));
        Directory.CreateDirectory(backup);
        foreach (string file in Directory.GetFiles(folder, "map-*.txt")) File.Copy(file, System.IO.Path.Combine(backup, System.IO.Path.GetFileName(file)));
        if (File.Exists(configFile)) File.Copy(configFile, System.IO.Path.Combine(backup, "campaign.txt"));
        string assignments = System.IO.Path.Combine(campaignDir, "assignments.txt");
        if (File.Exists(assignments))
        {
            File.Copy(assignments, System.IO.Path.Combine(backup, "assignments.txt"));
            var remapped = File.ReadAllLines(assignments).Select(line =>
            {
                var match = System.Text.RegularExpressions.Regex.Match(line, @"^(\s*Map )(\d+)( /.*)$");
                if (!match.Success) return line;
                int index = drafts.FindIndex(d => d.OriginalIndex == int.Parse(match.Groups[2].Value));
                return index < 0 ? null : match.Groups[1].Value + (index + 1) + match.Groups[3].Value;
            }).Where(line => line != null);
            new CampaignDocument(string.Join(Environment.NewLine, remapped)).Save(assignments);
        }
        for (int i = 0; i < drafts.Count; i++)
        {
            var layout = drafts[i].Layout!;
            layout.BaseSquad = MapLayout.ResolveBaseSquad(layout.BaseSquad, sourceCatalog.Select(m => m.BaseSquad))!;
            new CampaignDocument(layout.Serialize()).Save(System.IO.Path.Combine(folder, $"map-{i + 1}.txt"));
        }
        config.Set("Act Sequence", string.Join(",", acts)); config.Save(configFile);
        foreach (string file in Directory.GetFiles(folder, "map-*.txt"))
            if (int.TryParse(System.IO.Path.GetFileNameWithoutExtension(file)[4..], out int n) && n > drafts.Count) File.Delete(file);
        for (int i = 0; i < drafts.Count; i++) { drafts[i].OriginalIndex = i + 1; drafts[i].Inherited = false; drafts[i].NeedsSpacingSave = false; }
        dirty = false; message.Text = "Maps saved. Changes apply to new runs.";
        return true;
    }
    private List<string> ValidateLayout(MapLayout layout)
        => layout.Validate(teamNames.Keys).Concat(MapPlacementBounds.Validate(layout)).Concat(MapCatalog.ValidateSource(layout, sourceCatalog))
            .Concat(layout.Nodes.Where(n => n.Type is "Elite" or "Boss" && n.Stadium.Length > 0
                && !NodeSettingsWindow.Stadiums.Any(s => s.Id == n.Stadium))
                .Select(n => $"Stadium '{n.Stadium}' is unavailable. Choose a current stadium."))
            .Distinct().ToList();
    private void Validate() { if (map == null) return; var errors = ValidateLayout(map); message.Text = errors.Count == 0 ? map.VanillaRandom ? NativeGenerationMessage : "Unsaved map · all nodes connect from entry to boss." : string.Join("  ", errors.Take(3)); }
    private double BuilderRowScale => Math.Max(1, ((map?.Nodes.GroupBy(n => n.Layer).Select(g => g.Count()).DefaultIfEmpty(1).Max() ?? 1) - 1)
        * (MapNodePlacement.Height + MapNodePlacement.Gap) / ((MapPlacementBounds.PairBottomY - MapPlacementBounds.PairTopY) * MapNodePlacement.Units));
    private Point NodePosition(MapLayoutNode n)
    {
        var position = MapNodePlacement.Position(n);
        // Keep invalid imported nodes reachable in the editor while preserving
        // their stored coordinates until the user explicitly fits or drags them.
        double y = double.IsFinite(position.Y) ? Math.Clamp(position.Y, MapNodePlacement.MinY, MapNodePlacement.MaxY) : MapNodePlacement.MinY;
        return new Point(position.X, MapNodePlacement.MinY + (y - MapNodePlacement.MinY) * BuilderRowScale);
    }
    private void EnableNodeDragging()
    {
        MapLayoutNode? dragged = null;
        Button? pressed = null;
        Point pointerStart = default, nodeStart = default;
        bool moved = false, gesture = false;
        canvas.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (connecting && e.GetCurrentPoint(canvas).Properties.IsRightButtonPressed) { CancelConnection(); e.Handled = true; return; }
            if (connecting || map == null || !e.GetCurrentPoint(canvas).Properties.IsLeftButtonPressed) return;
            var control = e.Source as Visual;
            pressed = control as Button ?? control?.GetVisualAncestors().OfType<Button>().FirstOrDefault();
            if (pressed?.Tag is not string id || !tiles.TryGetValue(id, out var tile) || tile != pressed) { pressed = null; return; }
            dragged = map.Nodes.First(n => n.Id == id);
            pointerStart = e.GetPosition(canvas); nodeStart = NodePosition(dragged); moved = false; gesture = false;
            e.Pointer.Capture(canvas); e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        canvas.AddHandler(PointerMovedEvent, (_, e) =>
        {
            if (connecting)
            {
                connectionCursor = e.GetPosition(canvas);
                UpdateConnectionPreview();
                return;
            }
            if (dragged == null || map == null) return;
            var delta = e.GetPosition(canvas) - pointerStart;
            if (!gesture && ((Vector)delta).Length < 5) return;
            gesture = true;
            draggingNode = true;
            var wanted = nodeStart + delta;
            wanted = new Point(wanted.X, MapNodePlacement.MinY + (wanted.Y - MapNodePlacement.MinY) / BuilderRowScale);
            var next = MapNodePlacement.DragPosition(dragged, wanted, map.Nodes);
            if (((Vector)(next - MapNodePlacement.Position(dragged))).Length < .01) return;
            if (!moved) { Checkpoint(); moved = true; }
            MapNodePlacement.Set(dragged, next);
            selected = dragged; Draw();
            message.Text = "Drag within this layer. Other nodes block movement. Undo restores the position.";
            e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        canvas.AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if (dragged == null) return;
            var click = gesture ? null : pressed;
            dragged = null; pressed = null; e.Pointer.Capture(null); e.Handled = true;
            draggingNode = false;
            SetBuilderZoom(builderZoom, fitBuilder);
            click?.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        canvas.PointerCaptureLost += (_, _) => { dragged = null; pressed = null; draggingNode = false; };
    }
    private void Draw()
    {
        gamePreview.Update(map, drafts[currentStage].Act, teamNames, teamPreview);
        canvas.Children.Clear(); tiles.Clear(); connectionPreview = null;
        canvas.Cursor = connecting ? new Cursor(StandardCursorType.Cross) : null;
        if (map == null || map.Nodes.Count == 0) return;
        var groups = map.Nodes.GroupBy(n => n.Layer).OrderBy(g => g.Key).ToArray();
        canvas.Width = Math.Max(1000, 68 + groups.Length * MapNodePlacement.LayerPitch);
        double LayerHeight(IEnumerable<MapLayoutNode> nodes) => nodes.Max(n => NodePosition(n).Y)
            + MapNodePlacement.Height + (nodes.Any(n => n.Type == "Boss") ? 24 : 76) - 18;
        canvas.Height = groups.Max(g => LayerHeight(g)) + 36;
        if (!draggingNode) SetBuilderZoom(builderZoom, fitBuilder);
        foreach (var group in groups)
        {
            int layer = group.Key; bool bossLayer = group.Any(n => n.Type == "Boss"); double x = 38 + layer * MapNodePlacement.LayerPitch;
            var lane = new Border { Tag = "layer-box:" + layer, Width = MapNodePlacement.LaneWidth, Height = LayerHeight(group), CornerRadius = new CornerRadius(16), Background = Brush.Parse(bossLayer ? "#211C2D" : "#131F30"), BorderBrush = Brush.Parse(bossLayer ? "#3D304D" : "#223249"), BorderThickness = new Thickness(1), IsHitTestVisible = false };
            Canvas.SetLeft(lane, x); Canvas.SetTop(lane, 18); canvas.Children.Add(lane);
            var title = new TextBlock { Text = bossLayer ? "FINAL MATCH" : $"LAYER {layer + 1:00}", FontSize = 17, FontWeight = FontWeight.SemiBold, Foreground = Brush.Parse(bossLayer ? "#D6B6EF" : "#D6E4F5") };
            Canvas.SetLeft(title, x + 16); Canvas.SetTop(title, 38); canvas.Children.Add(title);
            var count = new TextBlock { Text = bossLayer ? "Boss" : $"{group.Count()} nodes", FontSize = 15, Foreground = Brush.Parse("#8FA4BE") };
            Canvas.SetLeft(count, x + MapNodePlacement.LaneWidth - 85); Canvas.SetTop(count, 40); canvas.Children.Add(count);
            var remove = new Button { Content = "×", Tag = "remove-layer:" + layer, Width = 28, Height = 28, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), FontSize = 20, CornerRadius = new CornerRadius(6), Background = Brushes.Transparent, Foreground = Brush.Parse("#9CAEC5"), IsEnabled = layer > 0 && !bossLayer };
            ToolTip.SetTip(remove, layer == 0 ? "Layer 1 stays in place" : bossLayer ? "Keep the final Boss layer" : "Remove this layer and its nodes");
            remove.Click += (_, _) => RemoveLayer(layer);
            Canvas.SetLeft(remove, x + MapNodePlacement.LaneWidth - 36); Canvas.SetTop(remove, 33); canvas.Children.Add(remove);
            if (!bossLayer)
            {
                var plus = Action("+ Add node", () => AddLayerNode(layer, false, ""));
                plus.Tag = "layer-plus:" + layer; plus.Width = 228; plus.FontSize = 16; plus.IsEnabled = group.Count() < MapPlacementBounds.MaxNodesPerLayer;
                plus.HorizontalContentAlignment = HorizontalAlignment.Center; plus.Background = Brush.Parse("#20364A"); plus.Foreground = Brush.Parse("#8FE0D3");
                Canvas.SetLeft(plus, MapNodePlacement.LayerCenterLeft(layer)); Canvas.SetTop(plus, group.Max(n => NodePosition(n).Y) + MapNodePlacement.Height + 14); canvas.Children.Add(plus);
            }
            else
            {
                var caption = new TextBlock { Text = "The finish of this map", FontSize = 15, Foreground = Brush.Parse("#AA94BF") };
                Canvas.SetLeft(caption, x + 16); Canvas.SetTop(caption, 76); canvas.Children.Add(caption);
            }
            if (layer == 0) continue;
            var insert = Action("+ Insert layer", () => AddLayerNode(layer, true, ""));
            insert.Tag = "insert-layer:" + layer; insert.Width = 76; insert.Height = 118; insert.Padding = new Thickness(4); insert.Margin = new Thickness(0);
            insert.Background = Brush.Parse("#FFD166"); insert.Foreground = Brush.Parse("#241B08");
            insert.BorderBrush = Brush.Parse("#FFE7A8"); insert.BorderThickness = new Thickness(2);
            insert.CornerRadius = new CornerRadius(12); insert.ZIndex = 1;
            insert.Content = new StackPanel { Spacing = 2, Children = {
                new TextBlock { Text = "+", FontSize = 34, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center },
                new TextBlock { Text = "Insert\nlayer", FontSize = 17, FontWeight = FontWeight.Bold, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center }
            } }; insert.HorizontalContentAlignment = HorizontalAlignment.Center;
            ToolTip.SetTip(insert, "Add a layer before this one");
            Canvas.SetLeft(insert, x - 79); Canvas.SetTop(insert, 30); canvas.Children.Add(insert);
        }
        foreach (var node in map.Nodes) foreach (var id in node.Outgoing)
        {
            var target = map.Nodes.FirstOrDefault(n => n.Id == id); if (target == null) continue;
            var a = NodePosition(node) + new Vector(MapNodePlacement.Width + 12, MapNodePlacement.Height / 2);
            var b = NodePosition(target) + new Vector(-12, MapNodePlacement.Height / 2);
            double bend = Math.Max(30, (b.X - a.X) * .5);
            var geometry = Geometry.Parse(FormattableString.Invariant($"M {a.X},{a.Y} C {a.X + bend},{a.Y} {b.X - bend},{b.Y} {b.X},{b.Y}"));
            canvas.Children.Add(new Avalonia.Controls.Shapes.Path { Data = geometry, Stroke = Brush.Parse(node == selected ? "#86EDD3" : "#526C86"), StrokeThickness = node == selected ? 3 : 2, IsHitTestVisible = false });
        }
        foreach (var node in map.Nodes)
        {
            var n = node; bool isSource = connecting && selected == n;
            bool linked = connecting && selected != null && selected.Outgoing.Contains(n.Id);
            bool candidate = connecting && selected != null && n.Layer == selected.Layer + 1 && !linked;
            string colour = n == selected ? "#86EDD3" : candidate ? linked ? "#86EDD3" : "#689EC4" : n.Type == "Boss" ? "#735285" : "#344A65";
            string name = n.Team.Length > 0 ? teamNames.GetValueOrDefault(n.Team, "Choose team") : NodeName(n.Type);
            var contents = new StackPanel { Spacing = 3 };
            contents.Children.Add(new TextBlock { Text = name, FontSize = 19, FontWeight = FontWeight.SemiBold, Foreground = Brush.Parse("#F0F5FB"), TextTrimming = TextTrimming.CharacterEllipsis });
            var picture = artwork.Get(n, map.LookAct > 0 ? map.LookAct : acts[currentStage]);
            var stadiumArt = new Grid();
            stadiumArt.Children.Add(picture != null
                ? new Image { Source = picture, Height = 66, Width = 198, Stretch = Stretch.Uniform, Tag = MapNodeArtwork.Region(n, map.LookAct > 0 ? map.LookAct : acts[currentStage]), IsHitTestVisible = false }
                : new TextBlock { Text = n.Type.Length == 0 ? "+" : "?", FontSize = 44, Height = 66, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Brush.Parse("#9ACEC9"), IsHitTestVisible = false });
            var logo = teamPreview.Logo(n.Team);
            if (logo != null) stadiumArt.Children.Add(new Image { Name = "NodeTeamLogo", Tag = n.Team, Source = logo, Width = 38, Height = 38, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false });
            contents.Children.Add(stadiumArt);
            contents.Children.Add(new TextBlock { Text = isSource ? "Choose a target" : candidate ? "Click to connect" : n.Type.Length == 0 ? "Pick an event" : "Drag · edit", MaxWidth = 110, HorizontalAlignment = HorizontalAlignment.Left, FontSize = 12, Foreground = Brush.Parse(candidate || isSource ? "#A4E2DA" : "#8FA6BE") });
            var tile = new Button { Content = contents, Tag = n.Id, Width = MapNodePlacement.Width, Height = MapNodePlacement.Height, Padding = new Thickness(12,8), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(n == selected || candidate ? 2 : 1), BorderBrush = Brush.Parse(colour), Background = Brush.Parse(n == selected ? "#224447" : n.Type == "Boss" ? "#35273E" : "#213249"), Opacity = connecting && !candidate && !isSource ? .75 : 1 };
            if (candidate)
            {
                tile.RenderTransformOrigin = RelativePoint.Center;
                tile.RenderTransform = new ScaleTransform(1.06, 1.06);
                tile.BorderBrush = Brush.Parse("#86EDD3");
                tile.Background = Brush.Parse("#234C49");
                tile.ZIndex = 2;
            }
            var p = NodePosition(n); Canvas.SetLeft(tile, p.X); Canvas.SetTop(tile, p.Y); tiles[n.Id] = tile; canvas.Children.Add(tile);
            if (n.Team.Length > 0) ToolTip.SetTip(tile, name + "\n" + teamPreview.Description(n.Team));
            tile.Click += (_, _) => {
                if (connecting && selected != null)
                {
                    if (candidate) ConnectNodes(selected, n);
                    else if (linked) message.Text = "Already connected. Use Disconnect to sever incoming paths.";
                    else message.Text = "Choose a node in the next layer, or press Esc to finish.";
                }
                else if (n.Type.Length == 0) OpenNodePicker(n.Layer, false, n);
                else OpenNodeDetails(n);
            };
            if ((!connecting || isSource) && map.Nodes.Any(next => next.Layer == n.Layer + 1))
            {
                var output = ConnectionBubble(isSource ? "Cancel" : "Connect", "connect-from:" + n.Id, isSource ? "#86EDD3" : "#7595B3");
                output.Width = 86; output.Height = 28; output.FontSize = 13; output.CornerRadius = new CornerRadius(6); output.ZIndex = 3;
                ToolTip.SetTip(output, "Start a connection to the next layer");
                output.Click += (_, _) => {
                    if (connecting && selected == n) { CancelConnection(); return; }
                    selected = n; connecting = true;
                    connectionCursor = NodePosition(n) + new Vector(MapNodePlacement.Width, MapNodePlacement.Height / 2);
                    Focus(); Draw(); Inspect();
                    message.Text = "Click a highlighted node to finish the path. Disconnect severs incoming paths. Esc or right-click cancels.";
                };
                Canvas.SetLeft(output, p.X + MapNodePlacement.Width - output.Width - 8); Canvas.SetTop(output, p.Y + MapNodePlacement.Height - output.Height - 6); canvas.Children.Add(output);
            }
            int incoming = map.Nodes.Count(source => source.Outgoing.Contains(n.Id));
            if (incoming > 0 && !connecting)
            {
                var disconnect = ConnectionBubble("Disconnect", "disconnect-to:" + n.Id, "#FFB3A7");
                disconnect.Width = 104; disconnect.FontSize = 13; disconnect.ZIndex = 4;
                ToolTip.SetTip(disconnect, $"Sever {incoming} incoming path(s). Outgoing paths stay connected. Undo restores them.");
                disconnect.Click += (_, _) => DisconnectIncoming(n);
                Canvas.SetLeft(disconnect, p.X - 108);
                Canvas.SetTop(disconnect, p.Y + MapNodePlacement.Height / 2 + (candidate ? 30 : -18));
                canvas.Children.Add(disconnect);
            }
        }
        if (connecting && selected != null)
        {
            connectionPreview = new Avalonia.Controls.Shapes.Path { Name = "ConnectionCursorPath", Stroke = Brush.Parse("#86EDD3"), StrokeThickness = 4, StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 3, 2 }, IsHitTestVisible = false, ZIndex = 5 };
            canvas.Children.Add(connectionPreview);
            UpdateConnectionPreview();
        }
    }
    private void UpdateConnectionPreview()
    {
        if (!connecting || selected == null || connectionPreview == null) return;
        var a = NodePosition(selected) + new Vector(MapNodePlacement.Width, MapNodePlacement.Height / 2);
        var b = connectionCursor;
        double bend = Math.Max(30, Math.Abs(b.X - a.X) * .5);
        connectionPreview.Data = Geometry.Parse(FormattableString.Invariant($"M {a.X},{a.Y} C {a.X + bend},{a.Y} {b.X - bend},{b.Y} {b.X},{b.Y}"));
    }
    private void CancelConnection()
    {
        if (!connecting) return;
        connecting = false; Draw(); Inspect(); message.Text = "Connection cancelled.";
    }
    private void OpenNodeDetails(MapLayoutNode node)
    {
        if (detailsDialog != null) return;
        selected = node; Draw(); Inspect();
        var dialog = new Window { Title = NodeName(node.Type) + " settings", Width = 460, Height = 610, MinHeight = 420, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Brush.Parse("#131F30") };
        detailsDialog = dialog;
        var layout = new DockPanel { Margin = new Thickness(24), LastChildFill = true };
        var done = Action("Done", () => dialog.Close()); done.HorizontalAlignment = HorizontalAlignment.Right;
        DockPanel.SetDock(done, Dock.Bottom); layout.Children.Add(done);
        var scroll = new ScrollViewer { Content = inspector }; layout.Children.Add(scroll); dialog.Content = layout;
        dialog.Closed += (_, _) => { scroll.Content = null; detailsDialog = null; };
        _ = dialog.ShowDialog(DialogOwner);
    }
    private static Button ConnectionBubble(string label, string tag, string colour) => new()
    {
        Content = label, Tag = tag, Width = 36, Height = 36, MinWidth = 0, MinHeight = 0,
        Padding = new Thickness(0), CornerRadius = new CornerRadius(18), FontSize = 24,
        HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
        Background = StudioTheme.Background, BorderBrush = Brush.Parse(colour), BorderThickness = new Thickness(3), Foreground = Brush.Parse(colour)
    };
    private void ConnectNodes(MapLayoutNode source, MapLayoutNode target)
    {
        if (target.Layer != source.Layer + 1 || source.Outgoing.Contains(target.Id)) return;
        Checkpoint(); source.Outgoing.Add(target.Id);
        connecting = false;
        Draw(); Inspect(); Validate();
    }
    private void DisconnectIncoming(MapLayoutNode target)
    {
        if (map == null) return;
        var sources = map.Nodes.Where(n => n.Outgoing.Contains(target.Id)).ToArray();
        if (sources.Length == 0) return;
        Checkpoint();
        foreach (var source in sources) source.Outgoing.RemoveAll(id => id == target.Id);
        connecting = false;
        Draw(); Inspect(); Validate();
    }
    private async Task EditRandomNode(MapLayoutNode node)
    {
        try
        {
            if (map == null || node.Type != "NULL") return;
            var result = await new RandomNodeSettingsWindow(node).ShowDialog<Dictionary<string, decimal>?>(detailsDialog ?? DialogOwner);
            if (result == null || !map.Nodes.Contains(node)) return;
            var current = node.RandomWeights ?? node.NativeRandomWeights;
            if (current != null && result.Where(p => p.Value > 0).OrderBy(p => p.Key)
                .SequenceEqual(current.Where(p => p.Value > 0).OrderBy(p => p.Key))) return;
            Checkpoint(); node.RandomWeights = result; selected = node; Draw(); Inspect(); Validate();
        }
        catch (Exception ex) { message.Text = "Could not edit random node: " + ex.Message; }
    }
    private Control TeamAndStadiumFields(MapLayoutNode node)
    {
        var choices = teamNames.ToList();
        if (node.Team.Length > 0 && !teamNames.ContainsKey(node.Team)) choices.Add(new(node.Team, "Missing team: " + node.Team));
        var teams = new ComboBox
        {
            Name = "NodeTeam", ItemsSource = choices, SelectedIndex = choices.FindIndex(t => t.Key.Equals(node.Team, StringComparison.OrdinalIgnoreCase)),
            PlaceholderText = "Choose an opponent team", HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<KeyValuePair<string, string>>((t, _) => new TextBlock { Text = t.Value })
        };
        var stadiums = NodeSettingsWindow.Stadiums.ToList();
        if (node.Stadium.Length > 0 && !stadiums.Any(s => s.Id == node.Stadium)) stadiums.Add(new(node.Stadium, "Unavailable stadium: " + node.Stadium, ""));
        var stadium = new ComboBox
        {
            Name = "NodeStadium", ItemsSource = stadiums, SelectedItem = stadiums.FirstOrDefault(s => s.Id == node.Stadium),
            PlaceholderText = "Choose a stadium", HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<StadiumChoice>((s, _) => new TextBlock { Text = s?.Name })
        };
        var image = new Image { Name = "NodeStadiumPreview", Height = 170, Stretch = Stretch.Uniform };
        var logo = new Image { Name = "TeamLogoPreview", Width = 64, Height = 64, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        var description = new TextBlock { Name = "TeamDescriptionPreview", TextWrapping = TextWrapping.Wrap };
        void Preview()
        {
            image.Source = artwork.Get(node, acts[currentStage]);
            logo.Source = teamPreview.Logo(node.Team);
            description.Text = teamPreview.Description(node.Team);
        }
        teams.SelectionChanged += (_, _) =>
        {
            if (teams.SelectedItem is not KeyValuePair<string, string> team || !teamNames.ContainsKey(team.Key) || team.Key == node.Team) return;
            Checkpoint(); node.Team = team.Key; Preview(); Draw(); Validate();
        };
        stadium.SelectionChanged += (_, _) =>
        {
            if (stadium.SelectedItem is not StadiumChoice choice || choice.Id == node.Stadium) return;
            Checkpoint(); node.Stadium = choice.Id; Preview(); Draw(); Validate();
        };
        Preview();
        return new StackPanel { Spacing = StudioTheme.SpaceMd, Children = {
            new TextBlock { Text = "Team" }, teams,
            new TextBlock { Text = "Stadium" }, stadium,
            new Grid { Children = { image, logo } }, description
        } };
    }
    private void Inspect()
    {
        inspector.Children.Clear();
        if (selected == null || map == null) return;
        var n = selected;
        inspector.Children.Add(new TextBlock { Text = NodeName(n.Type), FontSize = 27, FontWeight = FontWeight.SemiBold });
        inspector.Children.Add(new TextBlock { Text = $"Layer {n.Layer + 1} / Node {map.NativeIndex(n) + 1}", Foreground = Brush.Parse("#8FA6BE"), Margin = new Thickness(0,0,0,12) });
        var types = new ComboBox { ItemsSource = n.Type == "Boss" ? new[] { "Boss" } : MapLayout.NodeTypes.Where(t => t != "Boss").ToArray(), SelectedItem = n.Type, IsEnabled = n.Type != "Boss", HorizontalAlignment = HorizontalAlignment.Stretch };
        types.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<string>((type, _) => new TextBlock { Text = NodeName(type ?? "") });
        types.SelectionChanged += (_, _) =>
        {
            if (types.SelectedItem is not string type || type == n.Type) return;
            Checkpoint(); n.Type = type;
            // Event nodes have no opponent picker. Do not retain a hidden match
            // assignment that makes the converted event impossible to save.
            if (type is not "Elite" and not "Boss") { n.Team = ""; n.Stadium = ""; }
            Draw(); Inspect(); Validate();
        };
        inspector.Children.Add(new TextBlock { Text = "Node type" }); inspector.Children.Add(types);
        if (n.Type is "Elite" or "Boss")
        {
            if (n.Team.Length > 0 && !teamNames.ContainsKey(n.Team)) inspector.Children.Add(new TextBlock { Text = "The saved opponent is missing. Choose a campaign team.", TextWrapping = TextWrapping.Wrap });
            inspector.Children.Add(TeamAndStadiumFields(n));
        }
        if (n.Type == "NULL") inspector.Children.Add(Action("Random event options...", async () => await EditRandomNode(n)));
        if (n.Type is "Elite" or "Boss" or "Challenge")
            inspector.Children.Add(new MatchPeriodFields(n, Checkpoint, Validate));
        if (n.Type != "Boss") inspector.Children.Add(Action("Remove node", () => { Checkpoint(); map.Remove(n); selected = null; detailsDialog?.Close(); Draw(); Inspect(); Validate(); }));
        void Count(string label, int value, int min, int max, Action<int> set)
        {
            inspector.Children.Add(new TextBlock { Text = label });
            var number = new ComboBox { Name = label.Replace(" ", ""), ItemsSource = Enumerable.Range(min, max - min + 1).ToArray(), SelectedItem = value, HorizontalAlignment = HorizontalAlignment.Stretch };
            number.SelectionChanged += (_, _) => { if (number.SelectedItem is not int v || v == value) return; Checkpoint(); value = v; set(value); Validate(); }; inspector.Children.Add(number);
        }
        // Native RewardCounter reads the authored reward counts only for Elite matches.
        if (n.Type == "Elite")
        {
            inspector.Children.Add(new TextBlock { Text = PoolChoiceGuidance.MatchRewards, TextWrapping = TextWrapping.Wrap });
            Count("Talent rewards", n.TalentRewards, 0, 10, v => n.TalentRewards = v);
            Count("Relic rewards", n.RelicRewards, 0, 10, v => n.RelicRewards = v);
        }
        if (n.Type == "GeneralManager")
        {
            inspector.Children.Add(new TextBlock { Text = "Recruit role" });
            var role = new ComboBox { Name = "RecruitRole", ItemsSource = new[] { GoalieFreeAgents.Skaters, GoalieFreeAgents.Goalies }, SelectedItem = n.GmRole, HorizontalAlignment = HorizontalAlignment.Stretch };
            role.SelectionChanged += (_, _) => { if (role.SelectedItem is not string value || value == n.GmRole) return; Checkpoint(); n.GmRole = value; Validate(); };
            inspector.Children.Add(role);
            Count("Players to recruit", n.GmChoices, 1, 20, v => n.GmChoices = v);
        }
    }
}

