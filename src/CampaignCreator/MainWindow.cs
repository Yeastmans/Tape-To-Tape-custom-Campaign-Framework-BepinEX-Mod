using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using CampaignCore;

namespace CampaignCreator;

public sealed record CampaignFile(string RelativePath, string DisplayName, string Context);

public sealed partial class MainWindow : Window
{
    private string root;
    private GameData data;
    private readonly ComboBox campaigns = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TreeView files = new();
    private readonly TextBox search = new() { Watermark = "Search players and files…" };
    private readonly TextBlock heading = new() { Text = "Your campaign workspace", FontSize = 26, FontWeight = FontWeight.SemiBold };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock previewLabel = new() { Text = "Select an appearance field to inspect its game asset.", TextWrapping = TextWrapping.Wrap };
    private readonly Image preview = new() { Height = 300, Stretch = Stretch.Uniform };
    private readonly TabControl tabs = new();
    private readonly Dictionary<string, Func<string>> editors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> teamTabs = new(StringComparer.OrdinalIgnoreCase);
    private CampaignDocument? document;
    private string? path;
    private string role = "skater";
    private bool dirty, loading;
    private Bitmap? bitmap;
    private List<CampaignNode> paths = [];
    private CampaignWorkspace? campaignWorkspace;
    private Control standardEditorShell = null!;

    private void OpenCampaignWorkspace(string directory)
    {
        if (campaignWorkspace is { IsNew: true } working && working.WorkingDirectory == directory) { Content = working; return; }
        campaignWorkspace = new CampaignWorkspace(this, root, directory,
            ShowHome,
            file => { OpenFile(file); if (!Path.GetFileName(file).Equals("campaign.txt", StringComparison.OrdinalIgnoreCase)) Content = editorShell; },
            async () => await ChooseContent("team", true));
        Content = campaignWorkspace;
    }

    public MainWindow(string[] args)
    {
        Title = "T2T Campaign Studio · Beta 1.0"; Width = 1320; Height = 860; MinWidth = 960; MinHeight = 620;
        Icon = new WindowIcon(typeof(MainWindow).Assembly.GetManifestResourceStream("CampaignStudioIcon")!);
        Background = StudioTheme.Background;
        FontSize = 14;
        FontFamily = new FontFamily("Segoe UI");
        Opened += (_, _) =>
        {
            var screen = Screens.ScreenFromWindow(this);
            if (screen == null) return;
            double availableWidth = screen.WorkingArea.Width / screen.Scaling - 24;
            double availableHeight = screen.WorkingArea.Height / screen.Scaling - 48;
            MinWidth = Math.Min(MinWidth, availableWidth); MinHeight = Math.Min(MinHeight, availableHeight);
            Width = Math.Min(Width, availableWidth); Height = Math.Min(Height, availableHeight);
        };
        root = FindRoot(args); data = new GameData(root);
        var shell = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = StudioTheme.PagePadding };
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 24) }; StudioTheme.Header(top);
        top.Children.Add(new StackPanel { Spacing = StudioTheme.SpaceXs, Children = { new TextBlock { Text = "TAPE TO TAPE", Foreground = StudioTheme.Accent, FontWeight = FontWeight.Bold }, heading } });
        var topActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = StudioTheme.SpaceMd };
        topActions.Children.Add(Button("Home", ShowHome));
        topActions.Children.Add(Button("Back to team", () =>
        {
            DiscardEditorChangesForNavigation();
            string? parent = path == null ? null : Path.GetDirectoryName(Path.GetDirectoryName(path));
            string? teamFile = parent == null ? null : Path.Combine(parent, "team.txt");
            if (teamFile != null && File.Exists(teamFile)) OpenFile(teamFile);
            else ShowHome();
        }));
        topActions.Children.Add(Button("Campaign overview", () => { DiscardEditorChangesForNavigation(); if (campaignWorkspace is { IsNew: true } working) { Content = working; return; } if (campaigns.SelectedItem is string c) OpenCampaignWorkspace(Path.Combine(root, "campaigns", c)); }));
        topActions.Children.Add(Button("Open content folder", async () => await PickRoot()));
        Grid.SetColumn(topActions, 1); top.Children.Add(topActions);
        shell.Children.Add(top);
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("260,*,280"), ColumnSpacing = StudioTheme.SpaceXl };
        Grid.SetRow(body, 1); shell.Children.Add(body);
        var navigation = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,*"), RowSpacing = StudioTheme.SpaceMd };
        navigation.Children.Add(new TextBlock { Text = "CAMPAIGN", Foreground = StudioTheme.Muted });
        Grid.SetRow(campaigns, 1); navigation.Children.Add(campaigns);
        Grid.SetRow(search, 2); navigation.Children.Add(search);
        var mapButton = Button("Edit campaign map", async () => await EditMap()); Grid.SetRow(mapButton, 3); navigation.Children.Add(mapButton);
        Grid.SetRow(files, 4); navigation.Children.Add(files); body.Children.Add(navigation);
        files.ItemTemplate = new FuncTreeDataTemplate<CampaignNode>((value, _) => new StackPanel { Spacing = 3, Children = {
            new TextBlock { Text = value?.DisplayName, TextTrimming = TextTrimming.CharacterEllipsis },
            new TextBlock { Text = value?.Context, FontSize = 11, Foreground = StudioTheme.Muted, TextTrimming = TextTrimming.CharacterEllipsis }
        }}, value => value.Children);
        files.Styles.Add(new Avalonia.Styling.Style(selector => selector.OfType<TreeViewItem>()) { Setters = { new Setter(TreeViewItem.IsExpandedProperty, new Avalonia.Data.Binding(nameof(CampaignNode.IsExpanded)) { Mode = Avalonia.Data.BindingMode.TwoWay }) } });
        Grid.SetColumn(tabs, 1); body.Children.Add(tabs);
        var side = new StackPanel { Spacing = StudioTheme.SpaceLg, Children = {
            new TextBlock { Text = "GAME ASSET", FontWeight = FontWeight.Bold }, preview, previewLabel,
            new TextBlock { Text = "Piece preview only. Recolouring depends on the selected skin; baked-in colours may stay fixed. Check full-player appearance in game.", Foreground = StudioTheme.Muted, TextWrapping = TextWrapping.Wrap },
            new Separator(), Button("Add team as custom squad", async () => await AddSquad()),
            new TextBlock { Text = "Adds a selectable squad to this campaign. Its template supplies the maps and game rules.", TextWrapping = TextWrapping.Wrap, Foreground = StudioTheme.Muted }
        }};
        var previewScroll = new ScrollViewer { Content = side, ClipToBounds = true };
        Grid.SetColumn(previewScroll, 2); body.Children.Add(previewScroll);
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = StudioTheme.SpaceMd, Margin = new Thickness(0, 20, 0, 0) }; StudioTheme.Footer(footer);
        footer.Children.Add(status);
        var save = Button("Save changes", Save); StudioTheme.Primary(save); Grid.SetColumn(save, 1); footer.Children.Add(save);
        var export = Button("Export player to Play Now", Export); Grid.SetColumn(export, 2); footer.Children.Add(export);
        Grid.SetRow(footer, 2); shell.Children.Add(footer); editorShell = standardEditorShell = shell;
        campaigns.SelectionChanged += async (_, _) => { if (!loading) await LoadCampaign(); };
        search.TextChanged += (_, _) => FilterFiles();
        files.SelectionChanged += (_, _) =>
        {
            if (loading || files.SelectedItem is not CampaignNode file || file.FilePath == null) return;
            if (dirty) { status.Text = "Save your changes before opening another file."; return; }
            OpenFile(file.FilePath);
        };
        UnsavedChangesGuard.Attach(this, () => dirty || campaignWorkspace?.HasUnsavedChanges == true || OwnedWindows.OfType<MapEditorWindow>().Any(w => w.HasUnsavedChanges));
        Closed += (_, _) => bitmap?.Dispose();
        LoadCampaigns();
        ShowHome();
        InitializePageHelp();
        Opened += async (_, _) => await StartStudio();
    }
    private static string FindRoot(string[] args)
    {
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--content") return Path.GetFullPath(args[i + 1]);
        return CampaignCore.UserContentPaths.EnsureRoot();
    }
    private Button Button(string text, Action action)
    {
        var b = new Button { Content = text, Padding = StudioTheme.ButtonPadding, HorizontalAlignment = HorizontalAlignment.Stretch };
        b.Click += (_, _) => { try { action(); } catch (Exception ex) { status.Text = ex.Message; } }; return b;
    }
    private void LoadCampaigns()
    {
        loading = true;
        string dir = Path.Combine(root, "campaigns");
        campaigns.ItemsSource = Directory.Exists(dir) ? Directory.GetDirectories(dir).Select(Path.GetFileName).Order().ToArray() : [];
        campaigns.SelectedIndex = 0; loading = false;
        _ = LoadCampaign();
    }
    private async Task PickRoot()
    {
        if (dirty) { status.Text = "Save your changes before changing folders."; return; }
        var selected = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose Custom Campaigns Mod content folder" });
        string? local = selected.FirstOrDefault()?.TryGetLocalPath();
        if (local == null) return;
        if (!Directory.Exists(Path.Combine(local, "campaigns"))) { status.Text = "Choose the content folder containing campaigns/."; return; }
        root = local; data = new GameData(root); LoadCampaigns();
    }
    private async Task LoadCampaign()
    {
        if (dirty) { status.Text = "Save your changes before changing campaign."; return; }
        var name = campaigns.SelectedItem?.ToString(); if (name == null) return;
        string dir = Path.Combine(root, "campaigns", name);
        document = null; path = null; editors.Clear(); tabs.ItemsSource = null;
        heading.Text = name;
        var found = await Task.Run(() => CampaignNavigation.Load(dir));
        if (campaigns.SelectedItem?.ToString() != name) return;
        paths = found; FilterFiles(); status.Text = $"{name} · {paths.SelectMany(p => p.Descendants()).Count(p => p.FilePath != null)} items";
    }
    private void FilterFiles()
    {
        loading = true;
        var filtered = CampaignNavigation.Filter(paths, search.Text ?? "");
        if (!string.IsNullOrEmpty(search.Text)) foreach (var node in filtered.SelectMany(n => n.Descendants())) node.IsExpanded = node.Children.Count > 0;
        files.ItemsSource = filtered;
        files.SelectedItem = filtered.SelectMany(n => n.Descendants()).FirstOrDefault(n => n.FilePath == path);
        loading = false;
    }
    private static CampaignFile DescribeFile(string campaignRoot, string file)
    {
        var config = new CampaignDocument(File.ReadAllText(file));
        string fallback = Path.GetFileNameWithoutExtension(file).Replace('_', ' ');
        string name = config.Get("Name", config.Get("Team Name", fallback)).Trim();
        if (name.Length == 0) name = fallback;
        string parent = Path.GetDirectoryName(file)!;
        string context = "Campaign settings";
        if (Path.GetFileName(parent).Equals("players", StringComparison.OrdinalIgnoreCase))
        {
            string teamFolder = Path.GetDirectoryName(parent)!;
            string teamFile = Path.Combine(teamFolder, "team.txt");
            string team = File.Exists(teamFile) ? new CampaignDocument(File.ReadAllText(teamFile)).Get("Team Name", Path.GetFileName(teamFolder)) : Path.GetFileName(teamFolder);
            string position = config.Get("Position");
            if (position.Length == 0 && fallback.Contains(" - ")) position = fallback.Split(" - ", 2)[0];
            context = team + (position.Length == 0 ? " · Player" : " · " + position);
        }
        else if (config.Fields.ContainsKey("Team Name")) context = file.Contains("player_teams") ? "Custom squad" : "Opponent team";
        else if (Path.GetFileName(parent) == "draft_pool") context = "Starting bench";
        else if (Path.GetFileName(parent) == "free_agents") context = "Free agents";
        else if (Path.GetFileName(parent) == "superstars") context = "Superstars";
        if (fallback.Equals("campaign", StringComparison.OrdinalIgnoreCase)) name = "Campaign settings";
        return new CampaignFile(Path.GetRelativePath(campaignRoot, file), name, context);
    }
    private static string FriendlyAsset(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Choose equipment";
        string leaf = value[(value.LastIndexOf('/') + 1)..];
        if (leaf.Equals("Customization_colors", StringComparison.OrdinalIgnoreCase)) return "Team colours";
        if (leaf.EndsWith("_Helmet", StringComparison.OrdinalIgnoreCase)) return leaf[..^7].Replace('_', ' ') + " (helmet compatible)";
        return leaf.Replace('_', ' ');
    }
    private void DiscardEditorChangesForNavigation()
    {
        dirty = false;
        homeDraft = null;
        campaigns.IsEnabled = files.IsEnabled = true;
        status.Text = "";
    }
    private void ReturnToTeamSection(string file)
    {
        DiscardEditorChangesForNavigation();
        string? directory = Directory.GetParent(Path.GetDirectoryName(file)!)?.Parent?.FullName;
        if (directory == null || !File.Exists(Path.Combine(directory, "campaign.txt"))) { ShowHome(); return; }
        OpenCampaignWorkspace(directory);
        campaignWorkspace?.SelectTeamSection(TeamEditorView.IsCustomSquad(file));
    }
    private string? playerReturnTeam;
    private string? latestPlayerTeam;
    private void OpenFile(string file)
    {
        // Navigation follows the entry point, not merely the player's folder on disk.
        playerReturnTeam = Content is TeamEditorView ? path
            : Content is PlayerEditorView && string.Equals(path, file, StringComparison.OrdinalIgnoreCase) ? playerReturnTeam : null;
        string? returnTeam = playerReturnTeam;
        if (Content == editorShell && editorShell is TeamEditorView previousTeam && path != null)
            teamTabs[Path.GetFullPath(path)] = previousTeam.SelectedTab;
        if (Path.GetFileName(file).Equals("campaign.txt", StringComparison.OrdinalIgnoreCase)) { OpenCampaignWorkspace(Path.GetDirectoryName(file)!); return; }
        path = file; document = new CampaignDocument(File.ReadAllText(file)); editors.Clear(); loading = true;
        data.LoadCustomRewards(file);
        role = file.Contains("goalie", StringComparison.OrdinalIgnoreCase) || document.Get("Position").Equals("Goalie", StringComparison.OrdinalIgnoreCase) || document.Fields.ContainsKey("Catching") || document.Fields.ContainsKey("Goalie Skill") ? "goalie" : "skater";
        heading.Text = document.Get("Name", document.Get("Team Name", Path.GetFileNameWithoutExtension(file)));
        if (Path.GetFileName(file).Equals("team.txt", StringComparison.OrdinalIgnoreCase) || document.Fields.ContainsKey("Team Name"))
        {
            if (TeamEditorView.IsCustomSquad(file)) latestPlayerTeam = file;
            editorShell = new TeamEditorView(this, document, data, root, file, MarkDirty,
                (key, read) => editors[key] = read, Save, () => ReturnToTeamSection(file), async () => await ImportTeam(),
                playerFile => { if (dirty) { status.Text = "Save your team before editing a player."; return; } OpenFile(playerFile); },
                async (position, importPlayer) => { if (dirty) { status.Text = "Save your team before adding a player."; return; } await AddRosterPlayer(file, position, importPlayer); },
                async () => await AddSquad(), status, Export,
                bulkImportPlayers: async slots => await BulkImportRosterPlayers(file, slots));
            if (teamTabs.TryGetValue(Path.GetFullPath(file), out int selectedTab))
                ((TeamEditorView)editorShell).SelectedTab = selectedTab;
            loading = false; dirty = false; status.Text = heading.Text; RefreshAttention(); Content = editorShell; return;
        }
        editorShell = standardEditorShell;
        var groups = new Dictionary<string, StackPanel> { ["Details"] = new() { Spacing = StudioTheme.SpaceMd }, ["Appearance"] = new() { Spacing = StudioTheme.SpaceMd }, ["Attributes"] = new() { Spacing = StudioTheme.SpaceMd } };
        var fields = document.Fields;
        bool player = !fields.ContainsKey("Team Name") && !Path.GetFileName(file).Equals("campaign.txt", StringComparison.OrdinalIgnoreCase)
            && (fields.ContainsKey("Name") || file.Contains(Path.DirectorySeparatorChar + "players" + Path.DirectorySeparatorChar));
        if (player)
        {
            editorShell = new PlayerEditorView(this, document, data, file, role, MarkDirty,
                (key, read) => editors[key] = read, Save,
                returnTeam == null ? null : () => {
                    var pendingDraft = homeDraft;
                    DiscardEditorChangesForNavigation();
                    if (File.Exists(returnTeam)) { homeDraft = pendingDraft; OpenFile(returnTeam); } else ShowHome();
                }, ShowHome, Export, status, returnTeam ?? latestPlayerTeam);
            loading = false; dirty = false; status.Text = heading.Text; RefreshAttention(); Content = editorShell; return;
        }
        if (fields.ContainsKey("Team Name")) foreach (var key in FieldSchema.TeamColors.Concat(FieldSchema.EquipmentColors())) fields.TryAdd(key, "");
        if (fields.ContainsKey("Team Name") && file.Contains("player_teams")) fields.TryAdd("Squad Template", "");
        foreach (var (key, value) in fields)
        {
            if (key.Equals("Glasses", StringComparison.OrdinalIgnoreCase)) continue;
            string group = FieldSchema.Kind(key, value) == FieldKind.Color || data.Assets.Values(role, key).Count > 0 || key.Contains("Skin") ? "Appearance"
                : new[] { "Speed", "Accuracy", "Checking", "Shot Power", "Talents", "Ability" }.Contains(key) ? "Attributes" : "Details";
            var row = new StackPanel { Spacing = 5 }; row.Children.Add(new TextBlock { Text = EquipmentColorGuide.FieldLabel(key, role), Foreground = Brush.Parse("#BDCADB") });
            var editor = key == "Ability" && OpponentAbilityPolicy.IsOpponentPlayer(file)
                ? OpponentAbilityPicker.Create(value, data, MarkDirty)
                : FieldControls.Create(key, value, role, data, MarkDirty, selected => Preview(key, selected));
            editor.control.Tag = key; editors[key] = editor.read; row.Children.Add(editor.control);
            groups[group].Children.Add(row);
        }
        tabs.ItemsSource = groups.Select(g => new TabItem { Header = g.Key, Content = new ScrollViewer { Content = g.Value, Margin = new Thickness(0, 16, 16, 0) } }).ToArray();
        tabs.SelectedIndex = 0; loading = false; dirty = false; status.Text = heading.Text;
        Preview("Face", document.Get("Face"));
        Content = editorShell;
    }
    private async Task ImportTeam()
    {
        try
        {
            string? source = await ContentImport.Pick(this, root, "team"); if (source == null) return;
            if (dirty && !await ConfirmTeamImport()) return;
            var imported = new CampaignDocument(File.ReadAllText(source));
            string name = imported.Get("Team Name", Path.GetFileName(Path.GetDirectoryName(source)!));
            if (path != null && TeamEditorView.IsCustomSquad(path) && CreatorContent.PlayerCampaign(path) == null)
            {
                string libraryTeam = CreatorContent.Create(root, "player-team", name, Path.GetDirectoryName(source)!, null, null);
                OpenFile(libraryTeam); status.Text = "Imported player team to library"; return;
            }
            // Resolve from the open team, including home-screen drafts that do
            // not have a selected campaign. Keep custom squads in player_teams.
            string campaign = campaignWorkspace?.WorkingDirectory
                ?? Directory.GetParent(Path.GetDirectoryName(path!)!)?.Parent?.FullName
                ?? throw new InvalidOperationException("Open a team before importing.");
            bool squad = TeamEditorView.IsCustomSquad(path!);
            string template = imported.Get("Squad Template");
            if (SquadTemplateCatalog.Find(template) == null) template = SquadTemplateCatalog.ResolveCampaign(campaign);
            string created = squad
                ? Path.Combine(SquadCopy.Create(Path.GetDirectoryName(source)!, campaign, name,
                    template), "team.txt")
                : CreatorContent.Create(root, "team", name, Path.GetDirectoryName(source)!, campaign, null);
            if (homeDraft != null) homeDraft = (squad ? "squad" : "team", created);
            paths = CampaignNavigation.Load(campaign); FilterFiles(); OpenFile(created); status.Text = "Imported · " + name;
        }
        catch (Exception ex) { status.Text = ex.Message; }
    }
    private void MarkDirty()
    {
        if (!loading && document != null && (editors.Any(pair => pair.Value() != document.Get(pair.Key)) || editorShell is TeamEditorView teamView && teamView.HasEquipmentChanges))
        {
            dirty = true; campaigns.IsEnabled = files.IsEnabled = false;
            status.Text = "Unsaved changes · save before switching files";
        }
        if (!loading && document != null) RefreshAttention();
    }
    private CampaignDocument ReadEditor()
    {
        if (document == null) throw new InvalidOperationException("Open a player or team first.");
        var next = new CampaignDocument(document.ToString()); foreach (var (key, get) in editors)
        { string value = get(); if (value != document.Get(key)) next.Set(key, value); } return next;
    }
    private async void Save()
    {
        if (path == null) return;
        var next = ReadEditor();
        if (!ValidateEditor(next)) return;
        // A new starting power or squad rule can invalidate an otherwise valid
        // custom reward reserve. Check the proposed document before any writes.
        if (path.Contains(Path.DirectorySeparatorChar + "player_teams" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                string? parent = Path.GetDirectoryName(path);
                while (parent != null && !File.Exists(Path.Combine(parent, "campaign.txt"))) parent = Path.GetDirectoryName(parent);
                if (parent != null)
                {
                    var changes = new Dictionary<string, CampaignDocument>(StringComparer.OrdinalIgnoreCase) { [Path.GetFullPath(path)] = next };
                    var errors = CampaignRewardBudget.Calculate(root, parent, changedFiles: changes).SelectMany(b => b.Errors).ToArray();
                    if (errors.Length > 0) { status.Text = string.Join("\n", errors) + " Update the campaign reward pools before applying this change."; return; }
                }
            }
            catch (Exception ex) { status.Text = ex.Message; return; }
        }
        if (editorShell is PlayerEditorView && next.Get(FreeAgentContent.Marker).Length == 0 && next.Get(StartingPoolEditor.BaseMarker).Length == 0)
        {
            try { CreatorContent.ValidatePlayerName(path, next.Get("Name"), path); }
            catch (ArgumentException ex) { status.Text = ex.Message; return; }
        }
        if (editorShell is TeamEditorView teamView)
        {
            try { teamView.SaveEquipment(); }
            catch (Exception ex) { status.Text = ex.Message; return; }
        }
        try { CustomRewardCatalog.PreserveLoadout(data, next, path, editorShell is PlayerEditorView ? role == "goalie" : null); next.Save(path); }
        catch (Exception ex) { status.Text = ex.Message; return; }
        document = next;
        if (homeDraft is { } draft && draft.File == path)
        {
            if (draft.Kind is "squad" or "team" or "skater" or "goalie" or "player")
            {
                try
                {
                    bool player = draft.Kind is "skater" or "goalie" or "player";
                    string saved = CreatorContent.Create(root, player ? "player" : draft.Kind == "squad" ? "player-team" : "team", next.Get(player ? "Name" : "Team Name"), player ? path : Path.GetDirectoryName(path)!, null, null);
                    homeDraft = null; dirty = false; campaignWorkspace = null;
                    campaigns.IsEnabled = files.IsEnabled = true;
                    OpenFile(saved); status.Text = player ? "Saved player to library" : "Saved team to library";
                }
                catch (Exception ex) { status.Text = ex.Message; }
                return;
            }
            await ChooseContent(draft.Kind, true, draftSource: draft.File);
            return;
        }
        dirty = false;
        campaigns.IsEnabled = files.IsEnabled = true;
        status.Text = "Saved · " + next.Get("Name", next.Get("Team Name", heading.Text ?? ""));
        heading.Text = next.Get("Name", next.Get("Team Name", heading.Text ?? ""));
        var expanded = paths.SelectMany(n => n.Descendants()).Where(n => n.IsExpanded).Select(n => n.FilePath ?? n.DisplayName).ToHashSet();
        paths = CampaignNavigation.Load(campaignWorkspace?.WorkingDirectory ?? Path.Combine(root, "campaigns", campaigns.SelectedItem?.ToString() ?? ""));
        foreach (var node in paths.SelectMany(n => n.Descendants())) node.IsExpanded = expanded.Contains(node.FilePath ?? node.DisplayName);
        FilterFiles();
    }
    private void Preview(string field, string value)
    {
        preview.Source = null; bitmap?.Dispose(); bitmap = null;
        previewLabel.Text = field + " · " + FriendlyAsset(value);
        try { value = data.Assets.Resolve(role, field, value); } catch { return; }
        if (data.Previews.TryGetValue(role + ":" + field + ":" + value, out var image) && File.Exists(image))
        { bitmap = new Bitmap(image); preview.Source = bitmap; }
    }
    private async void Export()
    {
        if (teamExportPending) return;
        try
        {
        var exportDocument = ReadEditor();
        if (!ValidateEditor(exportDocument)) return;
        var report = new ExportReport();
        if (editorShell is TeamEditorView teamView)
        {
            if (teamView.HasEquipmentChanges) { status.Text = "Save the team's equipment changes before exporting its roster."; return; }
            teamExportPending = true;
            try
            {
                var choice = await ChoosePlayNowExport(exportDocument.Get("Team Name"));
                if (choice == null) return;
                string teamResult;
                if (choice.Target == null)
                    teamResult = ExportTranslation.ExportTeam(exportDocument, path!, data, root, report);
                else
                {
                    var package = ExportTranslation.BuildTeam(exportDocument, path!, new GameData(root), root, report);
                    teamResult = ExportTranslation.WriteUpdate(package, choice.Target,
                        Path.Combine(GameSavePaths.Root, "TeamDataModels"),
                        Path.Combine(ExportTranslation.RuntimeRoot(root), "play_now_overrides"), root);
                }
                status.Text = (choice.Target == null ? "Exported new team and roster" : "Updated selected team and roster") + " · restart the game to load " + Path.GetFileName(teamResult);
                ShowExportReport(report);
            }
            finally { teamExportPending = false; }
            return;
        }
        if (editorShell is PlayerEditorView playerView) playerView.ApplyExportAppearance(exportDocument);
        var result = PlayerExport.Export(exportDocument, data, root, role, path, report);
        status.Text = "Exported " + Path.GetFileName(result) + " · restart the game to load it.";
        ShowExportReport(report);
        }
        catch (Exception ex) { status.Text = "Export failed: " + ex.Message; }
    }
    private async Task EditMap()
    {
        if (dirty) { status.Text = "Save your player or team edits before opening the map."; return; }
        if (campaigns.SelectedItem is not string campaign) return;
        await new MapEditorWindow(root, Path.Combine(root, "campaigns", campaign)).ShowDialog(this);
    }
    private async Task AddSquad()
    {
        try
        {
            if (path == null || document == null || !document.Fields.ContainsKey("Team Name")) { status.Text = "Open an existing team's team.txt first."; return; }
            if (dirty) { status.Text = "Save this team before adding it as a squad."; return; }
            var dialog = new Window { Title = "Add custom squad", Width = 440, Height = 300, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var name = new TextBox { Text = document.Get("Team Name") };
            string squadCampaign = Path.Combine(root, "campaigns", campaigns.SelectedItem!.ToString()!);
            string basis = SquadTemplateCatalog.ResolveCampaign(squadCampaign);
            var template = new ComboBox { ItemsSource = SquadTemplateCatalog.All, SelectedItem = SquadTemplateCatalog.Find(basis), HorizontalAlignment = HorizontalAlignment.Stretch };
            var message = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var add = new Button { Content = "Add selectable squad" };
            add.Click += (_, _) =>
            {
                try
                {
                    string label = (name.Text ?? "").Trim();
                    if (label.Length == 0 || label.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || label == "." || label == "..") throw new ArgumentException("Enter a valid squad name.");
                    SquadCopy.Create(Path.GetDirectoryName(path)!, Path.Combine(root, "campaigns", campaigns.SelectedItem!.ToString()!), label,
                        (template.SelectedItem as SquadTemplateOption)?.Name ?? throw new ArgumentException("Choose a team template."));
                    dialog.Close(); status.Text = "Added " + label + " · reopen squad selection in game."; _ = LoadCampaign();
                }
                catch (Exception ex) { message.Text = ex.Message; }
            };
            dialog.Content = new StackPanel { Margin = StudioTheme.PagePadding, Spacing = StudioTheme.SpaceMd, Children = { new TextBlock { Text = "Squad name" }, name, new TextBlock { Text = "Inherit maps and rules from" }, template, add, message } };
            await dialog.ShowDialog(this);
        }
        catch (Exception ex) { status.Text = ex.Message; }
    }
}

