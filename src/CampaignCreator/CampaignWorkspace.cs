using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CampaignCore;

namespace CampaignCreator;

/// <summary>Campaign-level navigation; player tools only appear after opening a roster entry.</summary>
public sealed partial class CampaignWorkspace : UserControl
{
    private readonly Window owner;
    private readonly string root;
    private string directory;
    private bool isNew;
    private readonly Action<string>? created;
    private readonly TextBox campaignName = new() { Watermark = "Name your campaign", FontSize = 26, MaxLength = 80 };
    public string WorkingDirectory => directory;
    public bool IsNew => isNew;
    private const int OpponentsSection = 1, MapSection = 2, CustomSquadsSection = 3, FreeAgentsSection = 4, RewardPoolsSection = 5;
    public void SelectTeamSection(bool customSquad) => ShowSection(customSquad ? CustomSquadsSection : OpponentsSection);
    private readonly GameData data;
    private readonly Action<string> open;
    private readonly TextBlock message = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Dictionary<string, Func<string>> fields = new(StringComparer.OrdinalIgnoreCase);
    private CampaignDocument settings, rewards;
    private readonly Dictionary<string, RewardPoolDefinition> rewardPools = new();
    private bool rewardDirty;
    private bool freeAgentsDirty;
    private NativePlayerPoolEditor? nativeFreeAgents;
    private FreeAgentTalentWeightsEditor? freeAgentTalentWeights;
    private HashSet<string> selectedBaseAgents = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> selectedCustomAgents = new(StringComparer.OrdinalIgnoreCase);
    private readonly ContentControl navigation = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private Control[] sections = [];
    private int currentSection;
    internal int CurrentSection => currentSection;
    private MapEditorView? mapEditor;
    private readonly List<Action> refreshRosters = new();
    private readonly List<Action> refreshRewardCounts = new();
    private IReadOnlyList<RewardBudget> RewardBudgets(IEnumerable<MapLayout>? layouts = null) => CampaignRewardBudget.Calculate(root, directory, rewardPools,
        layouts ?? mapEditor?.RewardLayouts, fields.TryGetValue("Act Sequence", out var sequence) ? sequence() : settings.Get("Act Sequence", "1,2,3"));
    private MapEditorView EnsureMapEditor()
    {
        if (mapEditor == null)
        {
            mapEditor = new MapEditorView(root, directory, () => SelectedTemplate);
            mapEditor.RewardValidation = layouts => string.Join("\n", RewardBudgets(layouts).SelectMany(b => b.Errors));
        }
        return mapEditor;
    }
    public bool HasUnsavedChanges => isNew || rewardDirty || freeAgentsDirty || nativeFreeAgents?.HasChanges == true || StartingPoolsDirty || mapEditor?.HasUnsavedChanges == true || fields.Any(f => f.Value() != settings.Get(f.Key));
    public bool CanLeave(bool withinCampaign = false)
    {
        if (isNew && withinCampaign) { SaveWorkingCopy(); return true; }
        if (!HasUnsavedChanges) return true;
        message.Text = "Save campaign changes before leaving this campaign.";
        return false;
    }
    public CampaignWorkspace(Window owner, string root, string directory, Action back, Action<string> open, Func<Task> newTeam, bool isNew = false, Action<string>? created = null, string initialName = "")
    {
        this.owner = owner; this.root = root; this.directory = directory; this.open = open;
        this.isNew = isNew; this.created = created;
        data = new GameData(root);
        campaignName.Text = initialName;
        settings = new CampaignDocument(isNew && !File.Exists(Path.Combine(directory,"campaign.txt")) ? "# Campaign Settings\nAct Sequence = 1,2,3\nMaps = Basic\n" : File.ReadAllText(Path.Combine(directory, "campaign.txt")));
        if (isNew) { Directory.CreateDirectory(directory); settings.Save(Path.Combine(directory, "campaign.txt")); }
        string rewardFile = Path.Combine(directory, "reward_pools.txt");
        rewards = new CampaignDocument(File.Exists(rewardFile) ? File.ReadAllText(rewardFile) : "# Campaign reward pools\n");
        var shell = new Grid { Margin = new Thickness(32), RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = StudioTheme.SpaceXl };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") }; StudioTheme.Header(header);
        header.Children.Add(new StackPanel { Spacing = 6, Children = {
            new TextBlock { Text = "TAPE TO TAPE  /  CAMPAIGN", Foreground = StudioTheme.Accent, FontWeight = FontWeight.Bold },
            isNew ? campaignName : new TextBlock { Text = Path.GetFileName(directory), FontSize = 30, FontWeight = FontWeight.SemiBold }
        }});
        var headerActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = StudioTheme.SpaceMd };
        var backButton = ActionButton("Back", () =>
        {
            if (currentSection != 0) ShowSection(0);
            else back();
        });
        backButton.Name = "BackToCampaign";
        headerActions.Children.Add(backButton);
        headerActions.Children.Add(PageHelpButton.Create(owner));

        Grid.SetColumn(headerActions, 1); header.Children.Add(headerActions); shell.Children.Add(header);
        sections = [
            ScrollPage(OverviewPage()),
            ScrollPage(RosterPage(false, newTeam)),
            MapsPage(),
            ScrollPage(CustomSquadsPage(newTeam)),
            ScrollPage(FreeAgentsPage(), reserveScrollbar: true),
            RewardsPage()
        ];
        ShowSection(0);
        Grid.SetRow(navigation, 1); shell.Children.Add(navigation);
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = StudioTheme.SpaceLg }; StudioTheme.Footer(footer);
        message.Text = isNew ? "Name your campaign above, then save to create it." : "Changes apply to this campaign only."; footer.Children.Add(message);
        var save = ActionButton("Save campaign", Save); StudioTheme.Primary(save);
        Grid.SetColumn(save, 1); footer.Children.Add(save); Grid.SetRow(footer, 2); shell.Children.Add(footer); Content = shell;
    }
    private void ShowSection(int index)
    {
        navigation.Content = sections[index];
        currentSection = index;
        if (index == RewardPoolsSection) foreach (var refresh in refreshRewardCounts) refresh();
    }
    private static ScrollViewer ScrollPage(Control body, bool reserveScrollbar = false) => new()
    {
        Content = body,
        AllowAutoHide = !reserveScrollbar,
        Name = reserveScrollbar ? "FreeAgentsPageScroll" : null
    };
    private static StackPanel Page(string title, string description) => new() { Spacing = StudioTheme.SpaceLg, Children = {
        new TextBlock { Text = title, FontSize = 24, FontWeight = FontWeight.SemiBold },
        new TextBlock { Text = description, FontSize = 14, Foreground = StudioTheme.Muted, TextWrapping = TextWrapping.Wrap }
    }};
    private Button ActionButton(string label, Action action)
    {
        var button = new Button { Content = label, Padding = StudioTheme.ButtonPadding };
        button.Click += (_, _) => { try { action(); } catch (Exception ex) { message.Text = ex.Message; } };
        return button;
    }
    private void AddField(StackPanel page, string key, string? label = null)
    {
        string value = settings.Get(key);
        // Native map aliases are valid saved values, even when the catalog uses full squad names.
        var choices = data;
        if (key == "Maps" && value.Length > 0 && !choices.Templates.Contains(value) && choices.Templates.Contains(MapCatalog.ResolveSquad(value))) choices.Templates.Add(value);
        var field = FieldControls.Create(key, value, "skater", choices, () => { if (HasUnsavedChanges) message.Text = "Unsaved campaign changes"; }, _ => { });
        field.control.Tag = key; fields[key] = field.read;
        var row = new StackPanel { Spacing = StudioTheme.SpaceSm, MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Stretch, Children = {
            new TextBlock { Text = label ?? key, FontSize = 14, Foreground = Brush.Parse("#BDCADB") }, field.control
        }};
        row.HorizontalAlignment = HorizontalAlignment.Left; row.Width = 720;
        page.Children.Add(row);
    }
    private Control MapsPage()
    {
        var host = new ContentControl { Margin = new Thickness(0, 12, 0, 0), HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        host.AttachedToVisualTree += (_, _) => host.Content = EnsureMapEditor();
        return host;
    }
    private Control RosterPage(bool custom, Func<Task> newTeam)
    {
        var page = Page(custom ? "Player teams" : "Opponents", custom ? "Click a team to edit its settings, look and roster." : "Green teams are assigned to map nodes. Red teams still need a node assignment.");
        var list = new StackPanel { Name = custom ? "CampaignPlayerTeamList" : "OpponentList", Spacing = StudioTheme.SpaceMd };
        void Refresh()
        {
            list.Children.Clear();
            var all = CampaignNavigation.Load(directory);
            var assignments = custom ? new Dictionary<string, List<string>>() : mapEditor?.AssignedOpponents ?? OpponentAssignments.Load(directory);
            var matchNodes = custom ? [] : CurrentMatchNodes();
            var teams = custom ? all.FirstOrDefault(n => n.DisplayName == "Custom Squads")?.Children ?? [] : all.Where(n => n.Context.StartsWith("Opponent ")).ToList();
            if (teams.Count == 0) list.Children.Add(new Border { Padding = new Thickness(24), Background = StudioTheme.Surface, CornerRadius = new CornerRadius(8), Child = new TextBlock { Text = custom ? "No player teams yet. Add a player team to start editing." : "No opponents yet. Create your first opponent team to start building the campaign.", TextWrapping = TextWrapping.Wrap } });
            string TeamName(string folder) => teams.FirstOrDefault(t => t.FilePath != null && Path.GetFileName(Path.GetDirectoryName(t.FilePath)!).Equals(folder, StringComparison.OrdinalIgnoreCase))?.DisplayName ?? folder;
            foreach (var team in teams)
            {
                if (!custom)
                {
                    var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = StudioTheme.SpaceMd };
                    string key = team.FilePath == null ? "" : Path.GetFileName(Path.GetDirectoryName(team.FilePath)!);
                    bool nativeBound = team.FilePath != null && new CampaignDocument(File.ReadAllText(team.FilePath)).Get(VanillaCopyData.OpponentId).Length > 0;
                    bool assigned = assignments.TryGetValue(key, out var assignedNodes) && assignedNodes.Count > 0 || nativeBound;
                    var colour = Brush.Parse(assigned ? "#70DEA2" : "#FF9292");
                    // Picking a node puts this team on it (replacing whoever was there); saved with the maps.
                    var nodePicker = new ComboBox {
                        Tag = "opponent-node:" + key, ItemsSource = matchNodes, Width = 420, HorizontalAlignment = HorizontalAlignment.Left,
                        PlaceholderText = matchNodes.Count == 0 ? "No match nodes yet — add Match or Boss nodes in Maps" : "Assign to a match node…",
                        IsEnabled = team.FilePath != null && matchNodes.Count > 0,
                        ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<MatchNode>((n, _) => new TextBlock { Text = n == null ? "" :
                            n.Label + (n.Team.Length == 0 ? " — open" : n.Team.Equals(key, StringComparison.OrdinalIgnoreCase) ? " — this team" : " — " + TeamName(n.Team)) })
                    };
                    nodePicker.SelectionChanged += (_, _) =>
                    {
                        if (nodePicker.SelectedItem is not MatchNode choice) return;
                        EnsureMapEditor().AssignTeam(choice, key);
                        message.Text = $"Assigned {team.DisplayName} to {choice.Label}. Save campaign to keep it.";
                        Dispatcher.UIThread.Post(Refresh);
                    };
                    var nodeList = new StackPanel { Spacing = 2 };
                    if (nativeBound) nodeList.Children.Add(new TextBlock { Text="Vanilla selection · used whenever the game chooses this opponent",Foreground=colour,TextWrapping=TextWrapping.Wrap });
                    if (!assigned) nodeList.Children.Add(new TextBlock { Text = "Not assigned to any map node", Foreground = colour, TextWrapping = TextWrapping.Wrap });
                    else foreach (string label in assignedNodes ?? [])
                    {
                        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = StudioTheme.SpaceXs };
                        line.Children.Add(new TextBlock { Text = label, Foreground = colour, VerticalAlignment = VerticalAlignment.Center });
                        // Legacy assignments.txt entries have no live node to clear, so they get no ×.
                        if (matchNodes.FirstOrDefault(n => n.Label == label && n.Team.Equals(key, StringComparison.OrdinalIgnoreCase)) is MatchNode node)
                        {
                            var unassign = ActionButton("×", () =>
                            {
                                EnsureMapEditor().AssignTeam(node, "");
                                message.Text = $"Removed {team.DisplayName} from {node.Label}. Save campaign to keep it.";
                                Refresh();
                            });
                            unassign.Padding = new Thickness(5, 0); unassign.MinHeight = 0; unassign.FontSize = 11;
                            unassign.VerticalAlignment = VerticalAlignment.Center;
                            ToolTip.SetTip(unassign, $"Unassign {team.DisplayName} from {node.Label}");
                            line.Children.Add(unassign);
                        }
                        nodeList.Children.Add(line);
                    }
                    row.Children.Add(new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Children = {
                        new TextBlock { Text = team.DisplayName, Foreground = colour, FontSize = 18, TextWrapping = TextWrapping.Wrap },
                        nodeList,
                        nodePicker
                    } });
                    var edit = ActionButton("Edit team", () => { if (team.FilePath is string file && CanLeave(true)) open(file); });
                    edit.IsEnabled = team.FilePath != null;
                    Grid.SetColumn(edit, 1); row.Children.Add(edit);
                    var remove = ActionButton("×", () =>
                    {
                        if (team.FilePath is not string file) return;
                        string teamsDirectory = Path.GetFullPath(Path.Combine(directory, "teams"));
                        string teamDirectory = Path.GetFullPath(Path.GetDirectoryName(file)!);
                        if (!string.Equals(Path.GetDirectoryName(teamDirectory), teamsDirectory, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("The opponent must belong to this campaign.");
                        string backup = Path.Combine(directory, "_removed_opponents", Guid.NewGuid().ToString("N"), Path.GetFileName(teamDirectory));
                        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                        Directory.Move(teamDirectory, backup);
                        mapEditor?.SetCampaignDirectory(directory);
                        Refresh();
                        message.Text = $"Removed {team.DisplayName}. A copy is kept in _removed_opponents. Reassign any map nodes that used this team.";
                    });
                    remove.IsEnabled = team.FilePath != null;
                    ToolTip.SetTip(remove, "Remove " + team.DisplayName + " from opponents");
                    Grid.SetColumn(remove, 2); row.Children.Add(remove);
                    list.Children.Add(new Border { Tag = "opponent:" + key, Child = row, Padding = new Thickness(16), Background = Brush.Parse(assigned ? "#19372B" : "#3A242C"), BorderBrush = colour, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8) });
                    continue;
                }
                var editTeam = ActionButton(team.DisplayName, () => {
                    if (team.FilePath is not string teamFile) return;
                    try { SaveWorkingCopy(); open(teamFile); }
                    catch (Exception ex) { message.Text = ex.Message; }
                });
                editTeam.Tag = team.FilePath;
                editTeam.IsEnabled = team.FilePath != null;
                editTeam.HorizontalAlignment = HorizontalAlignment.Stretch;
                editTeam.HorizontalContentAlignment = HorizontalAlignment.Left;
                list.Children.Add(editTeam);
            }
        }
        if (custom) page.Children.Add(new ScrollViewer { Content = list, MaxHeight = 280 });
        page.Children.Add(ActionButton(custom ? "Add player team" : "New opponent team", () =>
        {
            if (!custom && !CanLeave(true)) return;
            try
            {
                if (custom) AddPlayerTeam();
                else NewOpponent();
                Refresh();
            }
            catch (Exception ex) { message.Text = ex.Message; }
        }));
        page.Children.Add(ActionButton(custom ? "Import player team from library" : "Import opponent team", async () => {
            try {
                if (!CanLeave(true)) return;
                string? source = await ContentImport.Pick(owner, root, "team", custom ? ContentImport.PlayerTeamLibrary : null); if (source == null) return;
                string name = new CampaignDocument(File.ReadAllText(source)).Get("Team Name", Path.GetFileName(Path.GetDirectoryName(source)!));
                string file;
                if (custom) {
                    string template = new CampaignDocument(File.ReadAllText(source)).Get("Squad Template");
                    if (SquadTemplateCatalog.Find(template) == null) template = SelectedTemplate;
                    if (string.IsNullOrWhiteSpace(template)) throw new ArgumentException("Needs attention: choose a campaign squad template before importing a custom squad.");
                    SquadCopy.Create(Path.GetDirectoryName(source)!, directory, name, template);
                    file = Path.Combine(directory, "player_teams", name, "team.txt");
                } else file = CreatorContent.Create(root, "team", name, Path.GetDirectoryName(source)!, directory, null);
                Refresh(); open(file);
            } catch (Exception ex) { message.Text = ex.Message; }
        }));
        refreshRosters.Add(Refresh);
        page.AttachedToVisualTree += (_, _) => Refresh();
        Refresh(); if (!custom) page.Children.Add(list); return page;
    }
    private Control FreeAgentsPage()
    {
        var page = Page("Free agents", "Choose base-game players and add custom players to this campaign's pool. An empty pool uses the game's default free agents.");
        page.Children.Add(GeneralManagerNote());
        freeAgentTalentWeights = new FreeAgentTalentWeightsEditor(root, settings.Get(FreeAgentTalentWeights.Key), settings);
        fields[FreeAgentTalentWeights.Key] = () => freeAgentTalentWeights.Value;
        fields[FreeAgentTalentPool.ModeKey] = () => freeAgentTalentWeights.PoolMode;
        fields[FreeAgentTalentPool.SelectedKey] = () => freeAgentTalentWeights.SelectedValue;
        page.Children.Add(freeAgentTalentWeights);
        var pool = new StackPanel { Spacing = StudioTheme.SpaceMd, Children = {
            new TextBlock { Text = "Free-agent pool", FontSize = 20 }
        } };
        page.Children.Add(InsetPoolPanel.Frame(pool, "FreeAgentPoolPanel"));
        if (VanillaCopyData.IsPool(Path.Combine(directory,"player_teams","free_agents")))
        {
            nativeFreeAgents=new NativePlayerPoolEditor(owner,root,()=>Path.Combine(directory,"player_teams","free_agents"),
                file=>{SaveWorkingCopy();open(file);},()=>message.Text="Unsaved free-agent choices",insetList:true);
            pool.Children.Add(nativeFreeAgents);return page;
        }
        selectedBaseAgents = FreeAgentContent.Selected(directory);
        selectedCustomAgents = FreeAgentContent.SelectedCustom(directory);
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = StudioTheme.SpaceXl };
        var baseSide = Page("Base game", "Check the players to include, then save the campaign.");
        var customSide = Page("Custom", "Check the players to include, then save the campaign. Unchecked players stay here to edit. Import or Make New adds a checked player.");
        var baseList = new StackPanel { Name = "BaseFreeAgents", Spacing = 6 };
        foreach (string name in FreeAgentContent.BaseNames)
        {
            bool available = FreeAgentContent.Source(root, name) != null || selectedBaseAgents.Contains(name);
            var check = new CheckBox { Content = name + (available ? "" : " (record unavailable)"), Tag = name,
                IsChecked = selectedBaseAgents.Contains(name), IsEnabled = available };
            check.IsCheckedChanged += (_, _) => {
                if (check.IsChecked == true) selectedBaseAgents.Add(name); else selectedBaseAgents.Remove(name);
                freeAgentsDirty = true; message.Text = "Unsaved free agent selection";
            };
            baseList.Children.Add(check);
        }
        baseSide.Children.Add(InsetPoolPanel.List(baseList, 440, "BaseFreeAgentPoolScroll"));
        columns.Children.Add(baseSide); Grid.SetColumn(customSide, 1); columns.Children.Add(customSide); pool.Children.Add(columns);
        var list = new StackPanel { Name = "CustomFreeAgents", Spacing = StudioTheme.SpaceSm };
        void Refresh()
        {
            list.Children.Clear();
            var players = CampaignNavigation.Load(directory).FirstOrDefault(n => n.DisplayName == "Free Agents")?.Children ?? [];
            // Keep earlier bench selections accessible as authored players instead of hiding them.
            players = players.Where(p => p.FilePath != null && !FreeAgentContent.BaseNames.Contains(
                new CampaignDocument(File.ReadAllText(p.FilePath)).Get(FreeAgentContent.Marker), StringComparer.OrdinalIgnoreCase)).ToList();
            if (players.Count == 0) list.Children.Add(new TextBlock { Text = "No custom free agents yet.", TextWrapping = TextWrapping.Wrap });
            foreach (var player in players)
                if (player.FilePath is string file)
                {
                    string key = FreeAgentContent.SelectionKey(directory, file);
                    var check = new CheckBox { Name = "CustomFreeAgentSelection", Content = player.DisplayName + " · " + player.Context,
                        Tag = key, IsChecked = selectedCustomAgents.Contains(key) };
                    check.IsCheckedChanged += (_, _) => {
                        if (check.IsChecked == true) selectedCustomAgents.Add(key); else selectedCustomAgents.Remove(key);
                        freeAgentsDirty = true; message.Text = "Unsaved free agent selection";
                    };
                    var edit = ActionButton("Edit player", () => { if (CanLeave(true)) open(file); });
                    var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
                    row.Children.Add(check); Grid.SetColumn(edit, 1); row.Children.Add(edit); list.Children.Add(row);
                }
        }
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(ActionButton("Import", async () =>
        {
            try
            {
                if (!CanLeave(true)) return;
                string? source = await ContentImport.Pick(owner, root, "skater"); if (source == null) return;
                var doc = new CampaignDocument(File.ReadAllText(source));
                if (doc.Get("Name").Length == 0 && doc.Get("Import Player").Length == 0)
                    throw new ArgumentException("Choose a player file containing a Name or Import Player field.");
                string file = FreeAgentContent.Create(root, directory, doc.Get("Name", doc.Get("Import Player")), source);
                selectedCustomAgents.Add(FreeAgentContent.SelectionKey(directory, file));
                var imported = new CampaignDocument(File.ReadAllText(file)); imported.Set(FreeAgentContent.Marker, ""); imported.Save(file);
                Refresh(); open(file);
            }
            catch (Exception ex) { message.Text = ex.Message; }
        }));
        actions.Children.Add(ActionButton("Make New", async () =>
        {
            try
            {
                if (!CanLeave(true)) return;
                var dialog = new Window { Title = "Add free agent", Width = 460, Height = 260, WindowStartupLocation = WindowStartupLocation.CenterOwner };
                var name = new TextBox { Watermark = "Player name" };
                var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
                string? createdFile = null;
                var create = ActionButton("Create free agent", () =>
                {
                    try
                    {
                        createdFile = FreeAgentContent.Create(root, directory, name.Text ?? "");
                        selectedCustomAgents.Add(FreeAgentContent.SelectionKey(directory, createdFile));
                        dialog.Close();
                    }
                    catch (Exception ex) { error.Text = ex.Message; }
                });
                dialog.Content = new StackPanel { Margin = new Thickness(24), Spacing = StudioTheme.SpaceMd, Children = {
                    new TextBlock { Text = "Add free agent", FontSize = 24 }, name, error,
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = StudioTheme.SpaceMd, Children = { create, ActionButton("Cancel", () => dialog.Close()) } }
                } };
                await dialog.ShowDialog(owner);
                Refresh();
                if (createdFile != null) open(createdFile);
            }
            catch (Exception ex) { message.Text = ex.Message; }
        }));
        customSide.Children.Add(actions);
        customSide.Children.Add(InsetPoolPanel.List(list, 440, "CustomFreeAgentPoolScroll"));
        refreshRosters.Add(Refresh);
        page.AttachedToVisualTree += (_, _) => Refresh();
        Refresh();
        return page;
    }
    private void AddPlayerTeam()
    {
        SaveWorkingCopy();
        string name = "New player team";
        for (int number = 2; Directory.Exists(Path.Combine(directory, "player_teams", name)) || File.Exists(Path.Combine(directory, "player_teams", name)); number++)
            name = "New player team " + number;
        string team = SquadCopy.Create("", directory, name, SelectedTemplate, root);
        open(Path.Combine(team, "team.txt"));
    }
    private Control RewardsPage()
    {
        var page = Page("Reward pools", "Choose which rewards may appear. Uncheck Base game pool to customize a tab, including rewards not normally offered.");
        page.Spacing = StudioTheme.SpaceSm;
        var sections = new TabControl { Name = "RewardSections" };
        var sectionTabs = new List<TabItem>();
        foreach (string kind in RewardPoolDefinition.Kinds)
        {
            var entries = RewardCatalog.Load(root, kind);
            var definition = RewardCatalog.ReadPool(root, rewards, kind);
            var nativeDefaults=rewards.Fields.ContainsKey("Native Defaults "+kind)
                ? rewards.Get("Native Defaults "+kind).Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase) : null;
            rewardPools[kind] = definition;
            var selected = definition.Selected;
            var available = entries.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var reminders = RewardCatalog.Descriptions(root, kind);
            foreach (string id in selected) entries.TryAdd(id, "Unavailable reward · " + id);
            bool hasExpansion = RewardPoolDefinition.CatalogExpansionKey(kind).Length > 0;
            var panel = new Grid { RowDefinitions = new RowDefinitions(hasExpansion ? "Auto,Auto,Auto,Auto,*" : "Auto,Auto,Auto,*"), RowSpacing = StudioTheme.SpaceXs };
            string label = kind switch { "Talents" => "Regular talents", "Abilities" => "Skater abilities", RewardPoolDefinition.ChaosTalents => "Chaos talents", _ => kind };
            var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = StudioTheme.SpaceMd };
            heading.Children.Add(new TextBlock { Text = label, FontSize = 20, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            var basePool = new CheckBox { Name = "BaseGamePool", Content = "Base game pool", IsChecked = definition.UseBaseGame };
            Grid.SetColumn(basePool, 1); heading.Children.Add(basePool); panel.Children.Add(heading);
            var guidance = new Button { Name = "RewardPoolGuidance", Content = "Pool help", Padding = new Thickness(10, 5), VerticalAlignment = VerticalAlignment.Center };
            guidance.Click += async (_, _) =>
            {
                var dialog = new Window { Name = "RewardPoolGuidanceDialog", Title = label + " pool", Width = 620, Height = 340,
                    MinWidth = 460, MinHeight = 290, WindowStartupLocation = WindowStartupLocation.CenterOwner };
                var help = new TextBlock { Name = "RewardOfferSize", Text = PoolChoiceGuidance.RewardOffer(kind) + "\n\n" + PoolChoiceGuidance.RewardEligibility, TextWrapping = TextWrapping.Wrap };
                var close = ActionButton("Done", () => dialog.Close()); close.HorizontalAlignment = HorizontalAlignment.Right;
                var body = new Grid { Margin = new Thickness(24), RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = StudioTheme.SpaceMd };
                body.Children.Add(new TextBlock { Text = label + " pool", FontSize = 24, FontWeight = FontWeight.SemiBold });
                var scroll = new ScrollViewer { Content = help }; Grid.SetRow(scroll, 1); body.Children.Add(scroll);
                Grid.SetRow(close, 2); body.Children.Add(close); dialog.Content = body;
                await dialog.ShowDialog(owner);
            };
            Grid.SetColumn(guidance, 2); heading.Children.Add(guidance);
            var count = new TextBlock { Name = "RewardPoolCount", TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
            Grid.SetRow(count, 1); panel.Children.Add(count);
            void UpdateCount()
            {
                var budget = RewardBudgets().Single(b => b.Kind == kind);
                count.Text = definition.UseBaseGame ? "Base game pool · native eligibility and upgrade rules" : budget.Summary;
                count.Foreground = budget.Errors.Count > 0 ? Brush.Parse("#F5BE65") : StudioTheme.Text;
            }
            refreshRewardCounts.Add(UpdateCount);
            var search = new TextBox { Watermark = "Search " + kind.ToLowerInvariant() };
            var items = new StackPanel { Spacing = StudioTheme.SpaceXs };
            CheckBox? includeOther = null;
            void Refresh()
            {
                items.Children.Clear();
                foreach (var entry in entries.Where(e => RewardCatalog.BelongsToPool(kind, e.Key) || definition.IncludeOtherCategory || selected.Contains(e.Key))
                    .OrderBy(e => !RewardCatalog.OfferedInBasePool(kind, e.Key)).ThenBy(e => e.Value).Where(e => e.Value.Contains(search.Text ?? "", StringComparison.OrdinalIgnoreCase)))
                {
                    bool offered = nativeDefaults?.Contains(entry.Key) ?? RewardCatalog.OfferedInBasePool(kind, entry.Key);
                    string reminder = reminders.TryGetValue(entry.Key, out var text) ? text : "No reminder text provided by the game.";
                    if (!RewardCatalog.BelongsToPool(kind, entry.Key) && selected.Contains(entry.Key) && !definition.IncludeOtherCategory)
                        reminder = "Saved custom selection from the other category · kept visible while catalog expansion is off. " + reminder;
                    var check = new CheckBox {
                        Content = RewardRow.Create(entry.Value, offered ? reminder : "Not normally offered · enable to add it to this campaign's pool. " + reminder),
                        Tag = entry.Key, IsChecked = definition.UseBaseGame ? offered : selected.Contains(entry.Key), IsEnabled = available.Contains(entry.Key) && basePool.IsChecked != true, FontSize = 13,
                        HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch
                    };
                    check.IsCheckedChanged += (_, _) =>
                    {
                        if (basePool.IsChecked == true) return;
                        if (check.IsChecked == true) selected.Add(entry.Key); else selected.Remove(entry.Key);
                        foreach (var refreshCount in refreshRewardCounts) refreshCount();
                        rewardDirty = true; message.Text = "Unsaved reward pool changes";
                        if (check.IsChecked != true && !definition.IncludeOtherCategory && !RewardCatalog.BelongsToPool(kind, entry.Key)) Refresh();
                    };
                    items.Children.Add(check);
                }
                if (items.Children.Count == 0) items.Children.Add(new TextBlock { Text = entries.Count == 0 ? "No exported entries are available for this section." : "No matching rewards.", TextWrapping = TextWrapping.Wrap });
                UpdateCount();
            }
            if (hasExpansion)
            {
                includeOther = new CheckBox { Name = "IncludeOtherRewardCategory", Content = kind == "Talents" ? "Include Chaos talents" : "Include regular talents",
                    IsChecked = definition.IncludeOtherCategory, IsEnabled = !definition.UseBaseGame };
                ToolTip.SetTip(includeOther, PoolChoiceGuidance.CatalogExpansion);
                includeOther.IsCheckedChanged += (_, _) =>
                {
                    definition.IncludeOtherCategory = includeOther.IsChecked == true;
                    rewardDirty = true; message.Text = "Unsaved reward catalog preference";
                    Refresh();
                };
                Grid.SetRow(includeOther, 2); panel.Children.Add(includeOther);
            }
            basePool.IsCheckedChanged += (_, _) =>
            {
                definition.UseBaseGame = basePool.IsChecked == true;
                if (includeOther != null) includeOther.IsEnabled = !definition.UseBaseGame;
                rewardDirty = true;
                message.Text = "Unsaved reward pool changes";
                Refresh();
                foreach (var refreshCount in refreshRewardCounts) refreshCount();
            };
            search.TextChanged += (_, _) => Refresh(); Refresh();
            int searchRow = hasExpansion ? 3 : 2;
            Grid.SetRow(search, searchRow); panel.Children.Add(search);
            var list = RewardRow.ListBox(items); Grid.SetRow(list, searchRow + 1); panel.Children.Add(list);
            sectionTabs.Add(new TabItem { Header = label, Content = panel, Padding = new Thickness(10, 8), HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch });
        }
        sections.ItemsSource = sectionTabs; sections.SelectedIndex = 0;
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = StudioTheme.SpaceMd };
        layout.Children.Add(page); Grid.SetRow(sections, 1); layout.Children.Add(sections); return layout;
    }
    private void SaveWorkingCopy()
    {
        freeAgentTalentWeights?.Validate();
        var rewardErrors = RewardBudgets().SelectMany(b => b.Errors).ToArray();
        if (rewardErrors.Length > 0) throw new InvalidOperationException(string.Join("\n", rewardErrors));
        SaveStartingPools();
        nativeFreeAgents?.Save();
        if (mapEditor != null && (currentSection == MapSection || mapEditor.HasUnsavedChanges))
        {
            if (!mapEditor.TrySaveMap(out string error)) throw new InvalidOperationException(error);
        }
        if (freeAgentsDirty) { FreeAgentContent.Save(root, directory, selectedBaseAgents, selectedCustomAgents); freeAgentsDirty = false; }
        var updated = new CampaignDocument(File.ReadAllText(Path.Combine(directory, "campaign.txt")));
        foreach (var field in fields) if (field.Value() != settings.Get(field.Key)) updated.Set(field.Key, field.Value());
        if (!CampaignActSequence.TryParse(updated.Get("Act Sequence", CampaignActSequence.Default), out _))
            throw new ArgumentException(CampaignActSequence.ValidationMessage);
        updated.Save(Path.Combine(directory, "campaign.txt"));
        settings = updated;
        if (rewardDirty)
        {
            var changed = new CampaignDocument(rewards.ToString());
            foreach (var pair in rewardPools) pair.Value.Write(changed, pair.Key);
            changed.Save(Path.Combine(directory, "reward_pools.txt")); rewards = changed; rewardDirty = false;
        }
    }
    private void Save()
    {
        if (!isNew) { SaveWorkingCopy(); message.Text = currentSection == MapSection ? "Campaign and maps saved" : "Campaign saved"; return; }
        if (string.IsNullOrWhiteSpace(campaignName.Text))
        {
            SaveWorkingCopy(); message.Text = "Working draft saved. Add a name above when you are ready."; return;
        }
        string name = CreatorContent.ValidateName(campaignName.Text);
        string drafts = Path.GetFullPath(Path.Combine(root, ".studio-drafts")) + Path.DirectorySeparatorChar;
        string target = Path.GetFullPath(Path.Combine(root, "campaigns", name));
        string campaignsRoot = Path.GetFullPath(Path.Combine(root, "campaigns")) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(directory).StartsWith(drafts, StringComparison.OrdinalIgnoreCase) || !target.StartsWith(campaignsRoot, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Invalid campaign working folder.");
        if (Directory.Exists(target) || File.Exists(target)) throw new ArgumentException("That name already exists. Choose a different name.");
        SaveWorkingCopy(); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        Directory.Move(directory, target);
        directory = target; mapEditor?.SetCampaignDirectory(directory); isNew = false; campaignName.Text = name; campaignName.IsReadOnly = true;
        foreach (var refresh in refreshRosters) refresh();
        message.Text = "Campaign saved"; created?.Invoke(directory);
    }
}



