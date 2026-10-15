using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CampaignCore;

namespace CampaignCreator;

internal sealed partial class TeamEditorView
{
    private static readonly HashSet<string> preparingTwoLineTeams = new(StringComparer.OrdinalIgnoreCase);
    private Control Roster(string file, Action<string> openPlayer, Action<string, bool> addPlayer, Action<string[]>? bulkImportPlayers)
    {
        var layout = new Grid { Width = 1080, ColumnDefinitions = new ColumnDefinitions("*,260"), ColumnSpacing = StudioTheme.SpaceLg, Margin = new Thickness(0,12,0,0) };
        string directory = Path.Combine(Path.GetDirectoryName(file)!, "players");
        var entries = (Directory.Exists(directory) ? Directory.GetFiles(directory, "*.txt", SearchOption.AllDirectories) : [])
            .Order(StringComparer.OrdinalIgnoreCase).Select(path => (path, player: new CampaignDocument(File.ReadAllText(path)))).ToArray();
        var linePositions = RosterLines.Assign(directory);
        var inactive = Value("Starting Bench Positions").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool TwoLines() => IsCustomSquad(file) && SquadBehaviors.Enabled(Value("Squad Template"), "two_lines", Value);
        IReadOnlyList<string> RosterProblems()
        {
            if (!TwoLines()) return [];
            string[] required = RosterLines.Skaters.Concat(RosterLines.Skaters.Select(p => "Line 2 " + p)).Append("Goalie").ToArray();
            var available = entries.Where(e => !removedRosterPlayers.Contains(e.path) && File.Exists(e.path)).ToArray();
            var errors = new List<string>();
            var missing = required.Where(position => !available.Any(e => linePositions[e.path].Equals(position, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(e.player.Get("Name")))).ToArray();
            if (missing.Length > 0) errors.Add("Two-line roster flow requires ten named skaters and a goalie. Fill these Team roster slots before saving: " + string.Join(", ", missing) + ".");
            var assigned = available.Where(e => required.Contains(linePositions[e.path], StringComparer.OrdinalIgnoreCase)).ToArray();
            var repeatedSlots = assigned.GroupBy(e => linePositions[e.path], StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
            if (repeatedSlots.Length > 0) errors.Add("Give each two-line roster slot exactly one player: " + string.Join(", ", repeatedSlots) + ".");
            var repeatedNames = assigned.Select(e => e.player.Get("Name").Trim()).Where(n => n.Length > 0)
                .GroupBy(n => n, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
            if (repeatedNames.Length > 0) errors.Add("Two-line roster players must have unique names: " + string.Join(", ", repeatedNames) + ".");
            return errors;
        }
        if (IsCustomSquad(file))
        {
            Track("Starting Bench Positions", () => TwoLines() ? "" : string.Join(", ", inactive.Order(StringComparer.OrdinalIgnoreCase)));
            validateStartingRoster = () => {
                var errors = RosterProblems();
                if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
                if (TwoLines()) return;
                int limit = SquadBehaviors.Enabled(Value("Squad Template"), "three_skaters", Value) ? 3
                    : SquadBehaviors.Enabled(Value("Squad Template"), "four_skaters", Value) ? 4 : 5;
                int active = entries.Count(e => !removedRosterPlayers.Contains(e.path) && linePositions[e.path] != "Goalie"
                    && !linePositions[e.path].StartsWith("Line 2 ") && !inactive.Contains(linePositions[e.path]));
                if (active > limit) throw new InvalidOperationException($"This team has {active} authored active starters but allows {limit}. In Team roster, turn off Start active for the players you want on the bench.");
            };
        }
        string Position(string path, CampaignDocument player) => linePositions[path];
        var positions = new[] { "Left Wing", "Center", "Right Wing", "Left Defense", "Goalie", "Right Defense" };
        var players = new StackPanel { Spacing = StudioTheme.SpaceLg };
        var template = TemplateForTeam(file);
        var templateSummary = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = template != null };
        players.Children.Add(templateSummary);
        string teamPath = Path.GetFullPath(file);
        var prepareTwoLines = new CheckBox { Name = "PrepareTwoLineRoster", Content = "Prepare two-line roster", IsChecked = preparingTwoLineTeams.Contains(teamPath) };
        ToolTip.SetTip(prepareTwoLines, "Open all ten skater slots for authoring without changing gameplay. Fill both lines and the goalie, then enable Two-line roster flow in Squad behaviors and save.");
        players.Children.Add(prepareTwoLines);
        var rosterProblems = new TextBlock { Name = "TwoLineRosterProblems", Foreground = Brushes.Orange, TextWrapping = TextWrapping.Wrap };
        players.Children.Add(rosterProblems);
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var lines = new StackPanel { Orientation = Orientation.Horizontal, Spacing = StudioTheme.SpaceXs };
        var count = Label("", 12); count.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(count, 2); header.Children.Add(count);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), RowDefinitions = new RowDefinitions("Auto,Auto"), ColumnSpacing = StudioTheme.SpaceMd, RowSpacing = StudioTheme.SpaceLg };
        Button first = null!, second = null!;
        var lineTwoOverflow = new StackPanel { Spacing = 6, IsVisible = false };
        bool showingLineTwo = false;
        bool AuthoringTwoLines() => TwoLines() || IsCustomSquad(file) && prepareTwoLines.IsChecked == true;
        bool Locked(string position)
        {
            bool setupRequired = SquadBehaviors.Enabled(Value("Squad Template"), "superstar_screen", Value);
            bool unlockedTrio = template?.Id == "trio" && !SquadBehaviors.Enabled(Value("Squad Template"), "three_skaters", Value);
            return template != null && !AuthoringTwoLines() && setupRequired && !unlockedTrio
                && !template.StartingPositions.Contains(position, StringComparer.OrdinalIgnoreCase);
        }
        string[] EmptySkaterSlots()
        {
            var available = RosterLines.Skaters.Where(p => !Locked(p));
            if (!IsCustomSquad(file) || AuthoringTwoLines())
                available = available.Concat(RosterLines.Skaters.Select(p => "Line 2 " + p));
            return available.Where(p => !entries.Any(e => !removedRosterPlayers.Contains(e.path)
                && linePositions[e.path].Equals(p, StringComparison.OrdinalIgnoreCase))).ToArray();
        }
        var bulkImport = ActionButton("Bulk import players", () => bulkImportPlayers?.Invoke(EmptySkaterSlots()));
        bulkImport.Name = "BulkImportRosterPlayers";
        var bulkHint = new TextBlock { Name = "BulkRosterSlots", TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Muted };
        players.Children.Add(new StackPanel { Spacing = StudioTheme.SpaceSm, Children = { bulkImport, bulkHint } });
        Button RemoveButton(string playerFile, string name)
        {
            var button = ActionButton("×", async () => {
                var dialog = new Window { Title = "Remove roster player", Width = 430, Height = 210, WindowStartupLocation = WindowStartupLocation.CenterOwner };
                var cancel = ActionButton("Keep player", () => dialog.Close(false));
                var remove = ActionButton("Remove player", () => dialog.Close(true));
                dialog.Content = new StackPanel { Margin = new Thickness(20), Spacing = StudioTheme.SpaceLg, Children = {
                    new TextBlock { Text = "Remove " + name + " from this team? Save the team to apply the removal.", TextWrapping = TextWrapping.Wrap },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = StudioTheme.SpaceMd, Children = { cancel, remove } } } };
                if (!await dialog.ShowDialog<bool>(owner)) return;
                removedRosterPlayers.Add(playerFile); changed(); ShowLine(showingLineTwo);
            });
            button.Padding = new Thickness(7,2); ToolTip.SetTip(button,"Remove " + name + " from roster");
            return button;
        }
        void ShowLine(bool line2)
        {
            if (IsCustomSquad(file)) template = SquadTemplateCatalog.Find(Value("Squad Template")) ?? TemplateForTeam(file);
            bool twoLines = TwoLines(), authoringTwoLines = AuthoringTwoLines();
            int emptySlots = EmptySkaterSlots().Length;
            bulkImport.IsEnabled = bulkImportPlayers != null && emptySlots > 0;
            bulkHint.Text = $"{emptySlots} empty skater slots across available lines. Original positions are matched where possible; other players fill the remaining openings. Import the goalie separately.";
            ToolTip.SetTip(bulkImport, emptySlots == 0 ? "Remove a player and save the team to make room." : "Fill empty skater slots across all available lines. Filled and squad-setup slots stay as they are.");
            templateSummary.Text = twoLines
                ? "Two-line roster flow · fill ten skaters and the goalie before saving. All ten skaters are active; starting superstar and bench drafts are skipped, and in-match lineup editing is locked."
                : authoringTwoLines
                ? "Preparing two lines only · fill all ten skaters and the goalie, then enable Two-line roster flow in Squad behaviors and save. This checkbox opens editor slots without changing the saved team or gameplay."
                : template == null ? "" : template.Id == "noeffects"
                ? "No Effects · all five skaters and the goalie are editable. " +
                  (SquadBehaviors.Enabled(Value("Squad Template"), "superstar_screen", Value)
                      ? "Leave a skater position open if you want the game's starting superstar choice."
                      : "Fill all five skater positions before play when starting superstars are off.")
                : template.Name + " · editable starters: " + string.Join(", ", template.StartingPositions) + "\nRemaining positions follow the squad's setup steps.";
            prepareTwoLines.IsVisible = IsCustomSquad(file) && !twoLines;
            bool hasSecondLine = entries.Any(e => !removedRosterPlayers.Contains(e.path) && linePositions[e.path].StartsWith("Line 2 ", StringComparison.OrdinalIgnoreCase));
            if (template != null && !authoringTwoLines && !hasSecondLine) line2 = false;
            if (IsCustomSquad(file)) second.IsVisible = authoringTwoLines || hasSecondLine;
            rosterProblems.Text = string.Join("\n", RosterProblems());
            rosterProblems.IsVisible = !string.IsNullOrEmpty(rosterProblems.Text);
            showingLineTwo = line2;
            lineTwoOverflow.IsVisible = line2;
            foreach (var row in lineTwoOverflow.Children) row.IsVisible = row.Tag is not string path || !removedRosterPlayers.Contains(path);
            first.Background = line2 ? StudioTheme.Input : Mint; first.Foreground = line2 ? Muted : StudioTheme.Background;
            second.Background = line2 ? Mint : StudioTheme.Input; second.Foreground = line2 ? StudioTheme.Background : Muted;
            grid.Children.Clear(); int filled = 0;
            for (int index = 0; index < positions.Length; index++)
            {
                string position = positions[index];
                if (line2 && position == "Goalie") continue;
                string key = (line2 ? "Line 2 " : "") + position;
                var entry = entries.FirstOrDefault(e => !removedRosterPlayers.Contains(e.path) && Position(e.path, e.player).Equals(key, StringComparison.OrdinalIgnoreCase));
                bool goalie = position == "Goalie";
                var slot = new StackPanel { Spacing = StudioTheme.SpaceSm, Tag = key };
                if (IsCustomSquad(file) && !goalie && !line2 && entry.path != null)
                {
                    var active = new CheckBox { Content = "Start active", Name = "StartingActive_" + position.Replace(" ", ""), IsChecked = twoLines || !inactive.Contains(position), IsEnabled = !twoLines };
                    active.IsCheckedChanged += (_, _) => { if (active.IsChecked == true) inactive.Remove(position); else inactive.Add(position); changed(); };
                    ToolTip.SetTip(active, twoLines ? "Two-line roster flow requires all ten authored skaters in the active lines." : "Turn off to keep this authored player on the starting bench.");
                    slot.Children.Add(active);
                }
                var badge = RosterSelectionCard.Badge(position);
                var slotHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") }; slotHeader.Children.Add(badge);
                if (entry.path != null) { var remove = RemoveButton(entry.path, entry.player.Get("Name",Path.GetFileNameWithoutExtension(entry.path))); Grid.SetColumn(remove,1); slotHeader.Children.Add(remove); }
                slot.Children.Add(slotHeader);
                var body = new Grid { Height = 210, RowDefinitions = new RowDefinitions("Auto,*,Auto") };
                var card = RosterSelectionCard.Shell(body, entry.path == null);
                bool locked = entry.path == null && (line2 ? IsCustomSquad(file) && !authoringTwoLines : Locked(position));
                if (locked)
                {
                    var notice = new StackPanel { Spacing = StudioTheme.SpaceSm, VerticalAlignment = VerticalAlignment.Center };
                    notice.Children.Add(new TextBlock { Text = "Filled by squad setup", TextWrapping = TextWrapping.Wrap });
                    if (entry.path != null) notice.Children.Add(new TextBlock { Text = "Stored player retained; this slot is supplied by the template.", TextWrapping = TextWrapping.Wrap });
                    Grid.SetRow(notice, 1); body.Children.Add(notice);
                }
                else if (entry.path == null)
                {
                    var actions = new StackPanel { Spacing = StudioTheme.SpaceSm, Width = 150, VerticalAlignment = VerticalAlignment.Center };
                    var create = ActionButton(goalie ? "+ New goalie" : "+ New player", () => addPlayer(key, false)); create.Foreground = Mint; create.BorderBrush = Mint;
                    actions.Children.Add(create); actions.Children.Add(ActionButton(goalie ? "Import goalie" : "Import player", () => addPlayer(key, true)));
                    Grid.SetRow(actions, 1); body.Children.Add(actions);
                }
                else
                {
                    filled++;
                    body.Children.Add(Label(entry.player.Get("Name", entry.player.Get("Import Player", Path.GetFileNameWithoutExtension(entry.path))), 16, Brushes.White));
                    var details = new Grid { ColumnDefinitions = new ColumnDefinitions("68,*"), ColumnSpacing = StudioTheme.SpaceMd, Margin = new Thickness(0,10,0,10), VerticalAlignment = VerticalAlignment.Top };
                    string role = goalie ? "goalie" : "skater", face = entry.player.Get(goalie ? "Helmet Skin" : "Face");
                    if (goalie && !entry.player.Get("Override Team Settings").Equals("yes", StringComparison.OrdinalIgnoreCase))
                    {
                        string inherited = new TeamGoalieEquipment(file).Get("Helmet Skin");
                        if (inherited.Length > 0) face = inherited;
                    }
                    string? preview = data.HeadPreview(role, face);
                    Control portrait = Label("#" + entry.player.Get("Number", "—"), 24, Mint);
                    if (preview != null)
                    {
                        var bitmap = new Bitmap(preview); images.Add(bitmap); portrait = new Image { Source = bitmap, Stretch = Stretch.Uniform };
                    }
                    details.Children.Add(RosterSelectionCard.Portrait(portrait));
                    var attributes = new StackPanel { Spacing = StudioTheme.SpaceSm, Children = { Label(goalie ? "Skill · " + entry.player.Get("Skill", "50") : "Ability · " + RewardCatalog.DisplayNames(data.Root, "Abilities", entry.player.Get("Ability")), 12), Label("Talents · " + RewardCatalog.DisplayNames(data.Root, goalie ? "Goalie Talents" : "Talents", entry.player.Get(goalie ? "Goalie Talents" : "Talents")), 12) } };
                    Grid.SetColumn(attributes, 1); details.Children.Add(attributes); Grid.SetRow(details, 1); body.Children.Add(details);
                    var actions = new StackPanel { Spacing = 4 };
                    actions.Children.Add(ActionButton(goalie ? "Edit goalie" : "Edit player", () => openPlayer(entry.path)));
                    if (!goalie)
                    {
                        string target = line2 ? position : "Line 2 " + position;
                        var other = entries.FirstOrDefault(e => !removedRosterPlayers.Contains(e.path) && linePositions[e.path].Equals(target, StringComparison.OrdinalIgnoreCase));
                        bool swap = other.path != null;
                        var move = ActionButton((swap ? "Swap with Line " : "Move to Line ") + (line2 ? "1" : "2"), () => {
                            linePositions[entry.path] = target; rosterPositionChanges[entry.path] = target;
                            if (other.path != null) { linePositions[other.path] = key; rosterPositionChanges[other.path] = key; }
                            changed(); ShowLine(showingLineTwo);
                        });
                        move.Name = "MoveRosterPlayer"; move.Tag = entry.path;
                        move.IsEnabled = line2 ? swap || !Locked(position) : !IsCustomSquad(file) || authoringTwoLines || swap;
                        ToolTip.SetTip(move, (swap ? "Swap with " + other.player.Get("Name") : "Move into the empty matching position") + ". Save the team to apply.");
                        actions.Children.Add(move);
                    }
                    Grid.SetRow(actions, 2); body.Children.Add(actions);
                }
                slot.Children.Add(card); Grid.SetColumn(slot, index % 3); Grid.SetRow(slot, index / 3); grid.Children.Add(slot);
            }
            count.Text = $"{filled} of {(template == null || authoringTwoLines ? (line2 ? 5 : 6) : template.StartingPositions.Count(p => !line2 || p != "Goalie"))} filled";
        }
        first = ActionButton("LINE 1", () => ShowLine(false)); second = ActionButton("LINE 2", () => ShowLine(true));
        prepareTwoLines.IsCheckedChanged += (_, _) => {
            if (prepareTwoLines.IsChecked == true) preparingTwoLineTeams.Add(teamPath); else preparingTwoLineTeams.Remove(teamPath);
            ShowLine(showingLineTwo);
        };
        refreshTemplateRoster = () => ShowLine(false);
        if (file.Contains(Path.DirectorySeparatorChar + "player_teams" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            string campaignPath = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(file)))!;
            second.IsVisible = template?.Lines == 2 || entries.Any(e => Position(e.path, e.player).StartsWith("Line 2 ", StringComparison.OrdinalIgnoreCase));
        }
        lines.Children.Add(first); lines.Children.Add(second); header.Children.Add(lines); players.Children.Add(header); players.Children.Add(grid);
        // Keep unusual or duplicate authored positions reachable instead of hiding their files.
        var assigned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var extra = entries.Where(e => !positions.Contains(Position(e.path, e.player).Replace("Line 2 ", ""), StringComparer.OrdinalIgnoreCase)
            || Position(e.path, e.player).Equals("Line 2 Goalie", StringComparison.OrdinalIgnoreCase) || !assigned.Add(Position(e.path, e.player))).ToArray();
        if (extra.Length > 0)
        {
            foreach (var entry in extra)
            {
                string name = entry.player.Get("Name", Path.GetFileNameWithoutExtension(entry.path));
                var row = new Grid { Tag = entry.path, ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = StudioTheme.SpaceSm };
                row.Children.Add(ActionButton(name + " · " + Position(entry.path,entry.player), () => openPlayer(entry.path)));
                var remove = RemoveButton(entry.path,name); Grid.SetColumn(remove,1); row.Children.Add(remove); lineTwoOverflow.Children.Add(row);
            }
        }
        players.Children.Add(lineTwoOverflow);
        layout.Children.Add(Card(players, 20)); ShowLine(false);
        string relicKey = doc.Fields.ContainsKey("Relics") && !doc.Fields.ContainsKey("Team Relics") ? "Relics" : "Team Relics";
        var relicNames = RewardCatalog.ReadEntries(data.Root, "Relics");
        var relicList = new StackPanel { Spacing = StudioTheme.SpaceMd };
        var selected = Value(relicKey).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        string savedRelics=Value(relicKey),savedRelicMode=Value("Starting Relics Mode");
        bool nativeCopy=Value(VanillaCopyData.SquadId).Length>0;
        if (IsCustomSquad(file))
        {
            // Materialize legacy inherited effects into the editable list once.
            // From then on removing a relic really removes it in the game.
            if (!Value("Starting Relics Mode").Equals("Custom", StringComparison.OrdinalIgnoreCase))
                foreach (string relic in nativeCopy ? Value("Vanilla Starting Relics").Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries) : SquadTemplateCatalog.StartingRelics(Value("Squad Template")))
                    if (!(relic == "Bench Bonus" && Value("No Bench Bonus").Equals("yes", StringComparison.OrdinalIgnoreCase))
                        && !selected.Any(s => RewardCatalog.ResolveKey(data.Root, "Relics", s) == RewardCatalog.ResolveKey(data.Root, "Relics", relic))) selected.Add(relic);
            string originalRelics=string.Join(",",selected.Order(StringComparer.OrdinalIgnoreCase));
            Track("Starting Relics Mode", () => nativeCopy && originalRelics==string.Join(",",selected.Order(StringComparer.OrdinalIgnoreCase))
                ? savedRelicMode : "Custom");
        }
        string initialRelics=string.Join(",",selected.Order(StringComparer.OrdinalIgnoreCase));
        Track(relicKey, () => nativeCopy && initialRelics==string.Join(",",selected.Order(StringComparer.OrdinalIgnoreCase)) ? savedRelics : string.Join(", ", selected));
        void RefreshRelics()
        {
            relicList.Children.Clear();
            foreach (string relic in selected.ToArray())
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = StudioTheme.SpaceSm };
                row.Children.Add(relic.Equals("Bum Bench Bonus", StringComparison.OrdinalIgnoreCase) ? Label("Bum Bench Bonus", 13, Brushes.White)
                    : relicNames.TryGetValue(RewardCatalog.ResolveKey(data.Root, "Relics", relic), out var relicEntry)
                    ? Label(relicEntry.Name, 13, Brushes.White) : RewardRow.ImportedTitleTile(relic));
                var remove = ActionButton("×", () => { selected.Remove(relic); changed(); RefreshRelics(); }); remove.Name = "RemoveTeamRelic"; remove.Tag = relic; remove.Padding = new Thickness(7,2); Grid.SetColumn(remove, 1); row.Children.Add(remove);
                var card = Card(row, 10); card.CornerRadius = new CornerRadius(8); card.Background = StudioTheme.Input; relicList.Children.Add(card);
            }
            if (selected.Count == 0) relicList.Children.Add(Label("No team relics yet", 13));
        }
        var addRelic = ActionButton("+ Add relic", async () => {
            var dialog = new Window { Title = "Team relics", Width = 900, Height = 820, MinWidth = 640, MinHeight = 520, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var picker = FieldControls.Create(relicKey, string.Join(", ", selected), "skater", data, () => { }, _ => { }, opponentTeam: !IsCustomSquad(file));
            var listScroll = picker.control.GetLogicalDescendants().OfType<ScrollViewer>().First();
            listScroll.Height = 500;
            dialog.SizeChanged += (_, _) => listScroll.Height = Math.Max(140, dialog.Bounds.Height - 320);
            // Done is docked to the bottom so it stays visible even if the picker runs long.
            var done = ActionButton("Done", () => {
                // Native relic variants may share a GUID (Bum Bench Bonus).
                // Keep the authored asset token for any selection that survives.
                selected = picker.read().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(key => selected.FirstOrDefault(old => RewardCatalog.ResolveKey(data.Root, "Relics", old) == key) ?? key).ToList();
                changed(); RefreshRelics(); dialog.Close();
            });
            done.Margin = new Thickness(0, StudioTheme.SpaceLg, 0, 0); DockPanel.SetDock(done, Dock.Bottom);
            var title = Label("Team relics", 22, Brushes.White); title.Margin = new Thickness(0, 0, 0, StudioTheme.SpaceLg); DockPanel.SetDock(title, Dock.Top);
            var panel = new DockPanel { Margin = new Thickness(20), Children = { title, done, picker.control } };
            dialog.Content = panel; await dialog.ShowDialog(owner);
        }); addRelic.Foreground = Mint; addRelic.BorderBrush = Mint;
        RefreshRelics();
        var relicBody = new StackPanel { Spacing = StudioTheme.SpaceLg, Children = { Label("Team relics", 18, Brushes.White), relicList, addRelic } };
        if (IsCustomSquad(file))
        {
            var addTemplate = ActionButton("Add template starting relics", () => {
                foreach (string relic in nativeCopy && Value(VanillaCopyData.SquadId)==SquadTemplatePolicy.RuntimeIdentity(Value("Squad Template"))
                    ? Value("Vanilla Starting Relics").Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries)
                    : SquadTemplateCatalog.StartingRelics(Value("Squad Template")))
                    if (!selected.Any(s => RewardCatalog.ResolveKey(data.Root, "Relics", s) == RewardCatalog.ResolveKey(data.Root, "Relics", relic))) selected.Add(relic);
                changed(); RefreshRelics();
            });
            addTemplate.Name = "AddTemplateStartingRelics"; relicBody.Children.Add(addTemplate);
            relicBody.Children.Add(Label("Adds this preset's base-team relics. Remove any relic above to leave its effect out.", 12));
        }
        var relics = Card(relicBody, 16);
        relics.VerticalAlignment = VerticalAlignment.Top; Grid.SetColumn(relics, 1); layout.Children.Add(relics); return layout;
    }
}
