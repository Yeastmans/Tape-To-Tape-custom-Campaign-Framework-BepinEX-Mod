using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using CampaignCore;

namespace CampaignCreator;

internal sealed partial class TeamEditorView : UserControl
{
    private static readonly IBrush Mint = StudioTheme.Accent, Muted = StudioTheme.Muted;
    private readonly Dictionary<string, Func<string>> readers = new(StringComparer.OrdinalIgnoreCase);
    internal EditorAttention Attention { get; } = new();
    private readonly TabControl tabs = new();
    internal int SelectedTab { get => tabs.SelectedIndex; set => tabs.SelectedIndex = Math.Clamp(value, 0, Math.Max(0, tabs.Items.Count - 1)); }
    private readonly CampaignDocument doc;
    private readonly GameData data;
    private readonly Window owner;
    private readonly Action changed;
    private readonly Action<string, Func<string>> register;
    private readonly TeamGoalieEquipment goalieEquipment;
    private readonly Dictionary<string, string> equipment = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<EquipmentStylePicker> stylePickers = [];
    private readonly TeamUniform uniform = new();
    private readonly TeamUniform goalieUniform = new() { Goalie = true };
    private readonly StackPanel middleParts = new() { Name = "SkaterEquipment", Width = 210, Spacing = StudioTheme.SpaceMd };
    private readonly Canvas middleOverlay = new() { ClipToBounds = true };
    private readonly Viewbox middleCustomizers;
    private readonly List<Bitmap> images = [];
    private bool away;
    private Bitmap? logoBitmap;
    private string logoSource = "";
    private readonly Grid diagram = new() { ColumnDefinitions = new ColumnDefinitions("180,*,180"), ColumnSpacing = StudioTheme.SpaceMd };
    private readonly StackPanel leftParts = new() { Spacing = StudioTheme.SpaceSm };
    private readonly StackPanel rightParts = new() { Name = "GoalieEquipment", Spacing = StudioTheme.SpaceSm };
    private readonly TextBlock message;
    private readonly bool defaultsOnly;
    internal string Value(string key) => readers.TryGetValue(key, out var get) ? get() : doc.Get(key);
    internal string DefaultLookValue(string key) => FieldSchema.GoalieAssets.Contains(key) ? goalieEquipment.Get(key) : Value(key);
    private Action? refreshTemplateRoster;
    private Action? refreshTemplateOptions;
    private Action? refreshSquadBehaviors;
    private Action? validateSquadBehaviors;
    private Action? validateStartingRoster;
    private readonly HashSet<string> removedRosterPlayers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> rosterPositionChanges = new(StringComparer.OrdinalIgnoreCase);
    private SquadPoolsEditor? playerPools;
    internal bool HasEquipmentChanges => goalieEquipment.HasChanges || removedRosterPlayers.Count > 0 || rosterPositionChanges.Count > 0 || playerPools?.HasChanges == true;
    internal void SaveEquipment()
    {
        validateSquadBehaviors?.Invoke();
        validateStartingRoster?.Invoke();
        playerPools?.Save();
        goalieEquipment.Save();
        RosterLines.SavePositions(rosterPositionChanges.Where(p => !removedRosterPlayers.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value));
        rosterPositionChanges.Clear();
        foreach (string file in removedRosterPlayers.ToArray())
        {
            if (File.Exists(file))
            {
                string folder = Path.GetDirectoryName(file)!;
                while (!Path.GetFileName(folder).Equals("players", StringComparison.OrdinalIgnoreCase)) folder = Path.GetDirectoryName(folder) ?? throw new IOException("Player is outside the roster.");
                string backup = Path.Combine(Path.GetDirectoryName(folder)!, ".removed-roster", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(backup); File.Move(file, Path.Combine(backup, Path.GetFileName(file)));
            }
            removedRosterPlayers.Remove(file);
        }
    }

    internal TeamEditorView(Window owner, CampaignDocument document, GameData data, string root, string file,
        Action changed, Action<string, Func<string>> register, Action save, Action back, Action import,
        Action<string> openPlayer, Action<string, bool> addPlayer, Action addSquad, TextBlock status, Action? export = null, bool defaultsOnly = false,
        Action<string[]>? bulkImportPlayers = null)
    {
        this.owner = owner; this.defaultsOnly = defaultsOnly;
        bool customSquad = IsCustomSquad(file);
        doc = document; this.data = data; this.changed = changed; this.register = register;
        goalieEquipment = new TeamGoalieEquipment(file, defaultsOnly ? document : null);
        middleCustomizers = new Viewbox { Child = middleParts, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly };
        middleOverlay.Children.Add(middleCustomizers);
        diagram.LayoutUpdated += (_, _) => PositionMiddleCustomizers();
        Background = StudioTheme.Background; Foreground = Brushes.White;
        var shell = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(20, 12) };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0,0,0,8) }; StudioTheme.Header(header);
        header.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = StudioTheme.SpaceLg, VerticalAlignment = VerticalAlignment.Center, Children = {
            new StackPanel { Children = { Label("TAPE TO TAPE",12,Mint), Label("Campaign Studio",11,Muted) } },
            new TextBlock { Text = customSquad ? "Custom squad editor" : "Opponent team editor", FontSize = 26, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center } } });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = StudioTheme.SpaceMd, VerticalAlignment = VerticalAlignment.Top };
        actions.Children.Add(ActionButton(customSquad ? "Back to custom squads" : "Back to opponents", back)); actions.Children.Add(ActionButton("Import team", import)); actions.Children.Add(PageHelpButton.Create(owner)); Grid.SetColumn(actions,1); header.Children.Add(actions); shell.Children.Add(header);
        Grid.SetRow(tabs,1); shell.Children.Add(tabs);
        var look = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = StudioTheme.SpaceXs, Margin = new Thickness(0,6,0,0) };
        var fillLook = ActionButton("Fill look", async () => await FillLook(root, status));
        fillLook.Name = "FillLook";
        fillLook.HorizontalAlignment = HorizontalAlignment.Right;
        // The team logo is part of the look (player teams, opponents and the default look alike).
        var lookBar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = StudioTheme.SpaceMd };
        var logoLabel = Label("TEAM LOGO"); logoLabel.VerticalAlignment = VerticalAlignment.Center;
        lookBar.Children.Add(new StackPanel { Name = "TeamLogo", Orientation = Orientation.Horizontal, Spacing = StudioTheme.SpaceMd,
            VerticalAlignment = VerticalAlignment.Center, Children = { logoLabel, Logo(root) } });
        Grid.SetColumn(fillLook, 2); lookBar.Children.Add(fillLook);
        look.Children.Add(lookBar);
        // One left-aligned column grid, so every settings row lines up under name, city and abbreviation.
        // Custom squad rows: identity / squad template / description + mascot / squad icon + zamboni head + transition,
        // then goal celebration. Opponent rows: identity / description + transition.
        double[] widths = [300, 240, 100, 420];
        var identity = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(",", widths)), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto"),
            ColumnSpacing = StudioTheme.SpaceMd, RowSpacing = StudioTheme.SpaceMd, HorizontalAlignment = HorizontalAlignment.Left };
        void Place(Control control, int row, int column, int span = 1)
        {
            // A fixed width makes long text wrap instead of widening the shared columns.
            control.Width = widths.Skip(column).Take(span).Sum() + StudioTheme.SpaceMd * (span - 1);
            control.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetRow(control, row); Grid.SetColumn(control, column); Grid.SetColumnSpan(control, span); identity.Children.Add(control);
        }
        int i = 0;
        foreach (var (key,label) in new[] { ("Team Name","TEAM NAME"), ("City","CITY"), ("Abbreviation","ABBREV.") })
            Place(new StackPanel { Spacing = 3, Children = { Label(label), Field(key) } }, 0, i++);
        var description = new TextBox { Tag = "Description", Text = Value("Description"), Watermark = "Describe this team for the map and match preview", TextWrapping = TextWrapping.Wrap, MinHeight = 36, MaxHeight = 64 };
        Track("Description", () => description.Text ?? "");
        description.TextChanged += (_, _) => changed();
        Place(new StackPanel { Spacing = 3, Children = { Label("DESCRIPTION"), description } }, customSquad ? 2 : 1, 0, 2);
        if (customSquad)
        {
            Place(SquadMascotPicker(), 2, 2, 2);
            Place(SquadIconPicker(), 3, 0);
            Place(SquadHeadPicker("Zamboni Head", "ZAMBONI HEAD", "Leave unselected to use the squad icon."), 3, 1, 2);
        }
        uniform.ColorValue = token => DiagramColor(token, false);
        goalieUniform.ColorValue = token => DiagramColor(token, true);
        uniform.NumberText = goalieUniform.NumberText = () => FieldSchema.NumberText(27, Value("Number Style"));
        uniform.Logo = goalieUniform.Logo = Crest;
        DetachedFromVisualTree += (_, _) => { logoBitmap?.Dispose(); logoBitmap = null; };
        uniform.SkinValue = part => DiagramSkin(part, false);
        goalieUniform.SkinValue = part => DiagramSkin(part, true);
        var art = new Grid { ColumnDefinitions = new ColumnDefinitions("*,180,*"), ColumnSpacing = StudioTheme.SpaceSm };
        void AddUniform(string name, TeamUniform preview, int column)
        {
            var panel = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = StudioTheme.SpaceXs };
            var title = Label(name, 12, Mint); title.HorizontalAlignment = HorizontalAlignment.Center; panel.Children.Add(title);
            var fit = new Viewbox { Stretch = Stretch.Uniform, Child = preview };
            Grid.SetRow(fit, 1); panel.Children.Add(fit); Grid.SetColumn(panel, column); art.Children.Add(panel);
        }
        AddUniform("SKATER", uniform, 0); AddUniform("GOALIE", goalieUniform, 2);
        var previews = new Grid(); previews.Children.Add(art); previews.Children.Add(middleOverlay);
        Grid.SetColumn(previews,1); diagram.Children.Add(previews);
        var left = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = StudioTheme.SpaceSm };
        var views = new StackPanel { Spacing = StudioTheme.SpaceXs };
        var kit = new TabStrip { Name = "TeamKitTabs", ItemsSource = new[] { "Home", "Away" }, SelectedIndex = 0 };
        kit.SelectionChanged += (_,_) => { away = kit.SelectedIndex == 1; BuildParts(); RefreshUniform(); };
        views.Children.Add(kit);
        var facing = new TabStrip { Name = "TeamFacingTabs", ItemsSource = new[] { "Front", "Back" }, SelectedIndex = 0 };
        facing.SelectionChanged += (_, _) => { uniform.Back = goalieUniform.Back = facing.SelectedIndex == 1; RefreshUniform(); };
        views.Children.Add(facing);
        left.Children.Add(views);
        var leftFit = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = leftParts, VerticalAlignment = VerticalAlignment.Top };
        Grid.SetRow(leftFit,1); left.Children.Add(leftFit); diagram.Children.Add(left);
        var rightFit = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = rightParts, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(rightFit,2); diagram.Children.Add(rightFit);
        BuildParts(); RefreshUniform();
        var board = Card(diagram,12); Grid.SetRow(board, 1); look.Children.Add(board);
        if (defaultsOnly)
        {
            header.Children.Clear();
            header.Children.Add(Label("Default team look", 26, Brushes.White));
            tabs.ItemsSource = new[] { new TabItem { Header = "Team look", Content = look } };
            message = status;
            var defaultFooter = new StackPanel { Spacing = StudioTheme.SpaceSm };
            defaultFooter.Children.Add(status);
            var defaultActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = StudioTheme.SpaceMd };
            defaultActions.Children.Add(ActionButton("Cancel", back));
            defaultActions.Children.Add(PageHelpButton.Create(owner));
            var saveDefault = ActionButton("Save default", save);
            saveDefault.Name = "SaveDefaultLook";
            defaultActions.Children.Add(saveDefault);
            defaultFooter.Children.Add(defaultActions);
            Grid.SetRow(defaultFooter, 2); shell.Children.Add(defaultFooter);
            Content = shell;
            return;
        }
        var settings = new StackPanel { Spacing = StudioTheme.SpaceMd, HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = 1100 };
        // Keep every existing field accessible, including legacy and mod-specific settings.
        var reserved = new HashSet<string>(new[] { "Team Name", "City", "Abbreviation", "Logo From", "Team Relics", "Relics" }.Concat(FieldSchema.TeamColors).Concat(FieldSchema.EquipmentColors()).Concat(new[] { "Body", "Body Away", "Socks", "Socks Away", "Helmet", "Helmet Away", "Gloves", "Gloves Away", "Pants", "Pants Away", "Skates", "Skates Away", "Stick", "Stick Color", "Stick Away Color" }), StringComparer.OrdinalIgnoreCase);
        reserved.Add("Squad Template"); reserved.Add("Template Squad");
        reserved.Add("Starting Relics Mode");
        reserved.UnionWith(new[] {VanillaCopyData.OpponentId,VanillaCopyData.SquadId,"Vanilla Opponent Roles","Vanilla Starting Relics"});
        reserved.Add("Starting Bench Positions");
        reserved.Add(SquadPlayerPools.ModeKey);
        reserved.UnionWith(SquadBehaviors.All.Select(b => b.Key));
        reserved.Add("Description");
        reserved.Add("Opponent Two Lines");
        reserved.UnionWith(new[] { "No Bench Bonus", "Disable Bench Bonus", "Bench Size", "Transition Primary", "Transition Secondary", "Transition Tertiary" });
        reserved.Add(SquadTransitionColors.InheritedMarker);
        var inheritedTransitions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (customSquad)
        {
            inheritedTransitions.UnionWith(doc.Get(SquadTransitionColors.InheritedMarker).Split('|', StringSplitOptions.RemoveEmptyEntries));
            foreach (string key in SquadTransitionColors.Keys) if (string.IsNullOrWhiteSpace(Value(key))) inheritedTransitions.Add(key);
            Track(SquadTransitionColors.InheritedMarker, () => string.Join("|", SquadTransitionColors.Keys.Where(inheritedTransitions.Contains)));
        }
        void ApplyTemplateTransitions(string templateName)
        {
            foreach (string key in SquadTransitionColors.Keys.Where(inheritedTransitions.Contains))
            {
                equipment[key] = SquadTransitionColors.Get(templateName, key);
                Track(key, () => equipment[key]);
            }
        }
        if (customSquad) ApplyTemplateTransitions(TemplateForTeam(file)?.Name ?? "Basic Squad");
        var transitionPreview = new TeamTransitionPreview { Width = 180, Height = 45, Read = Value, ClipToBounds = true };
        var transitionButton = ActionButton("Customise transition colors", async () => await AppearanceColorsDialog.Show(owner, "Transition",
            ["Transition Primary", "Transition Secondary", "Transition Tertiary"], false, Value,
            (key, value) => { inheritedTransitions.Remove(key); equipment[key] = value; Track(key, () => equipment[key]); },
            () => { changed(); transitionPreview.InvalidateVisual(); }, showKit: false));
        transitionButton.VerticalAlignment = VerticalAlignment.Center; transitionButton.HorizontalAlignment = HorizontalAlignment.Left;
        // The button sits in front of a small preview rectangle.
        Place(new StackPanel { Spacing = 3, Children = { Label("TRANSITION COLORS"),
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = StudioTheme.SpaceMd, Children = { transitionButton, transitionPreview } } } },
            customSquad ? 3 : 1, customSquad ? 3 : 2, customSquad ? 1 : 2);
        reserved.UnionWith(new[] { "Goal Sound", "Goal Sound Data", "Goal Sound Volume" });
        if (customSquad) reserved.UnionWith(new[] { "Mascot", "Squad Head", "Zamboni Head" });
        // Bench Head is no longer offered; a saved value stays in the file untouched.
        reserved.Add("Bench Head");
        reserved.UnionWith(new[] { "Opponent Preset", "Opponent Effects", "Opponent Offense", "Opponent Defense", "Match Puck", "Match Surface", "Golf Fairway", "Tycoons Sponsors", "Canada Curses" });
        reserved.UnionWith(RelicOpponentInteractions.All.Select(i => i.SettingKey));
        reserved.UnionWith(new[] { "No Effects Bench", "No Effects Superstars", "Glasses" });
        reserved.Add("Number Style"); // Team look → Name & numbers
        // Remaining fields flow in name-width columns instead of stretching across the page.
        var extras = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var key in doc.Fields.Keys.Concat(new[] { "Description", "Squad Template", "Squad Head", "Stat Scale", "Bench Size", "Bench Head", "No Bench Bonus", "Transition Primary", "Transition Secondary", "Transition Tertiary" }).Distinct(StringComparer.OrdinalIgnoreCase).Where(k => !UpgradeCleanup.Retired(k) && !reserved.Contains(k) && !k.StartsWith("Bicep", StringComparison.OrdinalIgnoreCase))) extras.Children.Add(new StackPanel { Spacing = 3, Width = 300, Margin = new Thickness(0, 0, StudioTheme.SpaceMd, StudioTheme.SpaceSm), Children = { Label(key.ToUpperInvariant()), Field(key) } });
        if (extras.Children.Count > 0) settings.Children.Add(extras);
        if (customSquad)
        {
            var template = new ComboBox { Name = "PlayerTeamSquadTemplate", ItemsSource = SquadTemplateCatalog.All,
                SelectedItem = TemplateForTeam(file) ?? SquadTemplateCatalog.Find("Basic Squad"), Width = widths[0], HorizontalAlignment = HorizontalAlignment.Left };
            Track("Squad Template", () => (template.SelectedItem as SquadTemplateOption)?.Name ?? "Basic Squad");
            var templateDetails = new TextBlock { Text = (template.SelectedItem as SquadTemplateOption)?.Details, TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Muted };
            template.SelectionChanged += (_, _) => { templateDetails.Text = (template.SelectedItem as SquadTemplateOption)?.Details; ApplyTemplateTransitions((template.SelectedItem as SquadTemplateOption)?.Name ?? "Basic Squad"); transitionPreview.InvalidateVisual(); resetInjuriesForTemplate?.Invoke(); refreshTemplateRoster?.Invoke(); refreshTemplateOptions?.Invoke(); refreshSquadBehaviors?.Invoke(); changed(); };
            var templateBlock = new StackPanel { Spacing = 3, Children = { Label("SQUAD TEMPLATE"), template, templateDetails } };
            Place(templateBlock, 1, 0, 3);
            templateBlock.Children.Add(Label("This preset supplies defaults for this player team. Change individual rules in Squad behaviors.", 11));
        }
        settings.Children.Insert(0, identity);
        if (customSquad) settings.Children.Insert(1, InjuryControls());
        if (export != null) actions.Children.Add(ActionButton("Export to Play Now", export));

        var roster = Roster(file, openPlayer, addPlayer, bulkImportPlayers);
        var lookTab = new TabItem { Header = "Team look", Content = look };
        var rosterTab = new TabItem { Header = "Team roster", Content = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = roster, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center } };
        var teamTabs = new List<TabItem> { lookTab, rosterTab };
        if (customSquad) teamTabs.Add(new TabItem { Header = "Squad behaviors", Content = BehaviorControls(file) });
        if (customSquad)
        {
            playerPools = new SquadPoolsEditor(owner, root, Path.GetDirectoryName(file)!, doc, openPlayer, changed, Track);
            teamTabs.Add(new TabItem { Header = "Player pools", Content = playerPools });
        }
        if (!customSquad) teamTabs.Add(new TabItem { Header = "Opponent settings", Content = new OpponentSettingsView(doc, changed, Track) });
        if (customSquad) Place(GoalSoundControls(), 4, 0, 3);
        else settings.Children.Add(GoalSoundControls());
        teamTabs.Insert(0, new TabItem { Header = "Team settings", Content = new ScrollViewer { Content = settings, Margin = new Thickness(12, 8) } });
        tabs.ItemsSource = teamTabs;
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), ColumnSpacing = StudioTheme.SpaceMd, Margin = new Thickness(0,8,0,0) }; StudioTheme.Footer(footer);
        message = new TextBlock { Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        message.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(TextBlock.Text)) { Source = status }); footer.Children.Add(message);
        var saveButton = ActionButton(customSquad ? "Save player team" : "Save opponent", save); StudioTheme.Primary(saveButton); saveButton.FontWeight = FontWeight.Bold;
        var guide = ActionButton("Colour guide", async () => {
            var dialog = new Window { Title = "Equipment colour guide", Width = 760, Height = Math.Min(690,owner.Height-40), WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = StudioTheme.Surface };
            var body = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = StudioTheme.SpaceMd, Margin = new Thickness(20) };
            body.Children.Add(new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = EquipmentColorGuide.Content() });
            var done = ActionButton("Done",()=>dialog.Close()); Grid.SetRow(done,1); body.Children.Add(done);dialog.Content = body; await dialog.ShowDialog(owner);
        });
        Grid.SetColumn(guide,1); footer.Children.Add(guide);
        Grid.SetColumn(saveButton,3); footer.Children.Add(saveButton); Grid.SetRow(footer,2); shell.Children.Add(footer); Content = Attention.Wrap(shell, saveButton);
        DetachedFromVisualTree += (_,_) => { foreach (var image in images) image.Dispose(); images.Clear(); };
    }
    private static TextBlock Label(string text, double size = 12, IBrush? brush = null) => new() { Text = text, FontSize = size, Foreground = brush ?? Muted, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private static Button ActionButton(string text, Action action)
    {
        var b = new Button { Content = text, Padding = StudioTheme.ButtonPadding, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        b.Click += (_,_) => action(); return b;
    }
    private static Border Card(Control child, double padding = 20) => new() { Background = StudioTheme.Surface, BorderBrush = StudioTheme.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(padding), Child = child };
    private void Track(string key, Func<string> read) { readers[key] = read; register(key, read); }
    private Control Field(string key)
    {
        var result = FieldControls.Create(key, Value(key), "skater", data, () => { changed(); RefreshUniform(); }, _ => { });
        result.control.Tag = key; result.control.HorizontalAlignment = HorizontalAlignment.Stretch; Track(key,result.read); return result.control;
    }
    private void BuildParts()
    {
        leftParts.Children.Clear(); rightParts.Children.Clear(); middleParts.Children.Clear(); stylePickers.Clear();
        void Part(string name, string? skin, string[] colors, StackPanel destination, bool goalie = false, bool teamPaletteOnly = false)
        {
            var stack = new StackPanel { Width = 210, Spacing = 3, Children = { Label(name.ToUpperInvariant(),11) } };
            var colorsButton = ActionButton("Customise colors", async () => await ColorsDialog(name,colors));
            if (skin == null) stack.Children.Add(colorsButton);
            else
            {
                string role = goalie ? "goalie" : "skater";
                var catalog = data.Assets.Values(role, skin);
                if (catalog.Count == 0 && skin.EndsWith(" Away")) catalog = data.Assets.Values(role, HomeSkin(skin));
                if (!goalie && !equipment.ContainsKey(skin))
                {
                    equipment[skin] = Value(skin);
                    Track(skin, () => equipment[skin]);
                }
                string ReadSkin()
                {
                    string value = goalie ? goalieEquipment.Get(skin) : equipment[skin];
                    try { return data.Assets.Resolve(role, skin, value); } catch { return value; }
                }
                void WriteSkin(string value)
                {
                    if (goalie) goalieEquipment.Set(skin, value);
                    else
                    {
                        equipment[skin] = value;
                        if (skin is "Body" or "Body Away")
                        {
                            string sleeveKey = skin == "Body" ? "Bicep" : "Bicep Away";
                            string? sleeves = EquipmentStylePicker.MatchingSleeves(value, data.Assets.Values("skater", sleeveKey));
                            if (sleeves != null) { equipment[sleeveKey] = sleeves; Track(sleeveKey, () => equipment[sleeveKey]); }
                        }
                    }
                    foreach (var picker in stylePickers) picker.Refresh();
                    changed(); RefreshUniform();
                }
                bool socks = skin is "Socks" or "Socks Away";
                bool nativeStyle = !goalie && !defaultsOnly && EditorRequirements.HasNativeTeamEquipment(doc) && !socks;
                var picker = new EquipmentStylePicker(catalog, ReadSkin, WriteSkin, colorsButton,
                    customLabel: "Custom color", allowNoHelmet: !goalie && skin is "Helmet" or "Helmet Away", teamPaletteOnly: teamPaletteOnly, // the colorable style: tinted by this team's kit
                    emptyLabel: socks ? "Same as jersey" : nativeStyle ? "Native player style" : null,
                    emptyHint: socks ? "Socks follow the jersey style." : nativeStyle ? "Keeps each player's native equipment." : null);
                stylePickers.Add(picker); stack.Children.Add(picker);
                if (!goalie && name == "Jersey")
                {
                    string sleeveKey = skin == "Body" ? "Bicep" : "Bicep Away";
                    if (!equipment.ContainsKey(sleeveKey)) equipment[sleeveKey] = Value(sleeveKey);
                    Track(sleeveKey, () => equipment[sleeveKey]);
                    var sleeves = data.Assets.Values("skater", sleeveKey);
                    if (sleeves.Count == 0) sleeves = data.Assets.Values("skater", "Bicep");
                    picker.AddSleeves(sleeves, () => {
                        string value = equipment[sleeveKey];
                        if (value.Length == 0 && sleeveKey.EndsWith(" Away")) value = Value("Bicep");
                        try { return data.Assets.Resolve("skater", sleeveKey, value); } catch (ArgumentException) { return value; }
                    }, value => { equipment[sleeveKey] = value; changed(); RefreshUniform(); });
                }
            }
            destination.Children.Add(stack);
        }
        string a = away ? " Away" : "";
        string[] Colors(string piece) => new[] { piece+a+" Color", piece+a+" Secondary Color", piece+a+" Tertiary Color" };
        var skateColors = new[] { "Skates"+a+" Color", "Laces"+a+" Color", "Blade"+a+" Color" };
        var jerseyColors = away ? new[] { "Away Primary", "Away Secondary", "Away Accent" } : new[] { "Jersey Primary", "Jersey Secondary", "Jersey Accent" };
        var pantsColors = Colors("Pants");
        Part("Name & numbers", null, new[] { "Number"+a+" Color", "Number"+a+" Secondary Color", away ? "Number Color Away" : "Number Color Home" }, leftParts);
        ((StackPanel)leftParts.Children[^1]).Children.Add(NumberStylePicker());
        // Base game: the stick (style and colors) is set on each player, not the team. The default
        // look is where new players' starting stick comes from, so only it shows one.
        if (defaultsOnly) Part("Stick (new players)", "Stick", PlayerColorModel.StickKeys, leftParts);
        Part("Gloves", "Gloves"+a, Colors("Gloves")[..2], leftParts);
        // Base game: skates are colors only (no skate style in either customizer).
        Part("Skates", null, skateColors, leftParts);
        Part("Helmet", "Helmet"+a, Colors("Helmet")[..1], middleParts);
        Part("Jersey", "Body"+a, jerseyColors, middleParts);
        Part("Pants", "Pants"+a, pantsColors, middleParts);
        Part("Socks", "Socks"+a, Colors("Socks"), middleParts);
        Part("Goalie mask", "Helmet Skin", Colors("Helmet")[1..], rightParts, true);
        Part("Goalie jersey", "Skin"+a, jerseyColors, rightParts, true);
        Part("Goalie pants", away ? "Goalie Pants Away" : "Goalie Pants Skin", pantsColors[..1], rightParts, true);
        Part("Catcher", away ? "Glove Away" : "Glove Skin", Colors("Gloves")[..2], rightParts, true);
        Part("Goalie pads", away ? "Pads Away" : "Pads Skin", Colors("Socks"), rightParts, true);
        Part("Blocker", away ? "Blocker Away" : "Blocker Skin", [], rightParts, true, true);
        Part("Goalie stick", away ? "Stick Away" : "Stick Skin", [], rightParts, true, true);

    }
    // Default for both kits; a player's explicit native number style takes precedence.
    private ComboBox NumberStylePicker()
    {
        const string key = "Number Style";
        if (!equipment.ContainsKey(key)) { equipment[key] = Value(key); Track(key, () => equipment[key]); }
        var picker = new ComboBox { Name = "NumberStyle", Tag = key, ItemsSource = FieldSchema.NumberStyles,
            SelectedItem = FieldSchema.NumberStyles[FieldSchema.NumberStyleIndex(equipment[key])], HorizontalAlignment = HorizontalAlignment.Stretch };
        ToolTip.SetTip(picker, "Default jersey number style: 27, XXVII or 00027. Players set to Inherit team follow this; an explicit player style takes precedence.");
        picker.SelectionChanged += (_, _) =>
        {
            if (picker.SelectedItem is not string style || style == FieldSchema.NumberStyles[FieldSchema.NumberStyleIndex(equipment[key])]) return;
            equipment[key] = style; changed(); RefreshUniform();
        };
        return picker;
    }
    private static string HomeSkin(string key) => key switch {
        "Glove Away" => "Glove Skin", "Blocker Away" => "Blocker Skin", "Pads Away" => "Pads Skin", "Stick Away" => "Stick Skin",
        _ => key.Replace(" Away", "")
    };
    private string DiagramSkin(string part, bool goalie)
    {
        if (!goalie && part == "socks") return AppearanceContract.SockStyle(Value, away);
        string a=away?" Away":"";
        string key=goalie ? part switch {
            "jersey"=>"Skin"+a,"pants"=>away?"Goalie Pants Away":"Goalie Pants Skin","helmet"=>"Helmet Skin","gloves"=>away?"Glove Away":"Glove Skin",
            "socks"=>away?"Pads Away":"Pads Skin","blocker"=>away?"Blocker Away":"Blocker Skin","stick"=>away?"Stick Away":"Stick Skin",_=>""
        } : part switch {"jersey" or "socks"=>"Body"+a,"sleeves"=>"Bicep"+a,"helmet"=>"Helmet"+a,"gloves"=>"Gloves"+a,"pants"=>"Pants"+a,"skates"=>"Skates"+a,"stick"=>"Stick",_=>""};
        // Base game: no skate style (standard skate) and no team stick (each player's own;
        // the default look sets new players' stick).
        if (!goalie && part == "skates") return AppearanceContract.StandardSkates;
        if (!goalie && part == "stick") return defaultsOnly && Value("Stick").Length > 0 ? Value("Stick") : AppearanceContract.DefaultSkin("skater", "Stick");
        string value=goalie?goalieEquipment.Get(key):Value(key);
        return value;
    }
    private void PositionMiddleCustomizers()
    {
        // Keep the helmet picker between the helmets, with the shared clothing
        // controls below it in the reserved gap between the two figures.
        var skaterEdge = uniform.TranslatePoint(new Point(317, 50), middleOverlay);
        var goalieEdge = goalieUniform.TranslatePoint(new Point(109, 50), middleOverlay);
        if (skaterEdge is not { } left || goalieEdge is not { } right || right.X <= left.X || middleParts.DesiredSize.Height <= 0) return;
        double width = Math.Min(180, Math.Max(1, right.X - left.X - 16));
        double scale = Math.Min(width / 210, Math.Max(1, middleOverlay.Bounds.Height - 16) / middleParts.DesiredSize.Height);
        double height = middleParts.DesiredSize.Height * scale;
        double helmetHeight = middleParts.Children[0].DesiredSize.Height * scale;
        middleCustomizers.Width = 210 * scale;
        middleCustomizers.Height = height;
        Canvas.SetLeft(middleCustomizers, (left.X + right.X - middleCustomizers.Width) / 2);
        Canvas.SetTop(middleCustomizers, Math.Clamp((left.Y + right.Y - helmetHeight) / 2, 0, Math.Max(0, middleOverlay.Bounds.Height - height)));
    }
    private Control Logo(string root)
    {
        var picker = LogoPicker.Create(root, Value("Logo From"), () => {
            foreach (string key in doc.Fields.Keys.Where(k => k.StartsWith("Export Logo ", StringComparison.OrdinalIgnoreCase))) Track(key, () => "");
            changed(); RefreshUniform();
        }, defaultsOnly ? "No logo: new teams start without one" : "No logo selected", inline: true);
        Track("Logo From", picker.read);
        return picker.control;
    }
    private Task ColorsDialog(string part, string[] keys) => AppearanceColorsDialog.Show(owner, part, keys, away, Value,
        (key, value) => { equipment[key] = value; Track(key, () => equipment[key]); },
        () => { changed(); RefreshUniform(); });

    private string DiagramField(string token)
    {
        int channel = token[^1] - '0'; string part = token[..^1]; string a = away ? " Away" : "";
        return part switch {
            "jersey" => (away ? "Away" : "Jersey") + new[] { " Primary", " Secondary", " Accent" }[channel],
            "numbers" => channel == 2 ? (away ? "Number Color Away" : "Number Color Home") : "Number"+a+(channel == 0 ? " Color" : " Secondary Color"),
            "skates" => new[] { "Skates", "Blade", "Laces" }[channel]+a+" Color",
            "stick" => "Stick"+a+new[] { " Color", " Secondary Color", " Tertiary Color" }[channel],
            _ => char.ToUpperInvariant(part[0])+part[1..]+a+new[] { " Color", " Secondary Color", " Tertiary Color" }[channel]
        };
    }
    private string DiagramColor(string token, bool goalie = false)
    {
        if (token.StartsWith("bicep")) token = "jersey" + token[^1];
        if (goalie && token.StartsWith("blocker")) token = "skates" + token[^1];
        if (token.StartsWith("stick"))
        {
            // No team stick: the default look's new-player stick colors, else the default ones.
            int c = token[^1] - '0';
            string own = defaultsOnly ? Value(PlayerColorModel.StickKeys[c]) : "";
            string stick = own.Length > 0 ? own : AppearanceContract.DefaultColor("stick", c);
            return FieldSchema.TryRgb(stick, out byte sr, out byte sg, out byte sb) ? $"#{sr:X2}{sg:X2}{sb:X2}" : "";
        }
        string key = DiagramField(token);
        string value = Value(key);
        return FieldSchema.TryRgb(value, out byte r, out byte g, out byte b) ? $"#{r:X2}{g:X2}{b:X2}" : "";
    }    private Bitmap? Crest()
    {
        string source = Value("Logo From");
        if (source != logoSource) { logoSource = source; logoBitmap?.Dispose(); logoBitmap = null; try { logoBitmap = LogoExport.Preview(data.Root, source); } catch { } }
        return logoBitmap;
    }
    private void RefreshUniform()
    {
        uniform.Abbreviation = goalieUniform.Abbreviation = Value("Abbreviation") is { Length: > 0 } ab ? ab : "TEAM";
        uniform.TeamName = goalieUniform.TeamName = Value("Team Name");
        uniform.InvalidateVisual(); goalieUniform.InvalidateVisual();
    }
}



