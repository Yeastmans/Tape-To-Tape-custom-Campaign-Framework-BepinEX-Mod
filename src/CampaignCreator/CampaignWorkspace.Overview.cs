using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CampaignCore;

namespace CampaignCreator;

/// <summary>Campaign hub: one card per section showing what it holds, plus anything that needs fixing.</summary>
public sealed partial class CampaignWorkspace
{
    private static readonly IBrush GoodFill = Brush.Parse("#19372B"), Good = Brush.Parse("#70DEA2"),
        BadFill = Brush.Parse("#3A242C"), Bad = Brush.Parse("#FF9292"),
        WarnFill = Brush.Parse("#382B19"), Warn = Brush.Parse("#FFD18C");

    private string ActSequence => settings.Get("Act Sequence", "1,2,3");
    /// <summary>Live nodes from an open map editor, otherwise the saved layouts (never creates an editor, which can mark maps dirty).</summary>
    private List<MatchNode> CurrentMatchNodes()
    {
        if (mapEditor != null) return mapEditor.MatchNodes();
        try { return OpponentAssignments.MatchNodes(OpponentAssignments.LoadLayouts(root, directory, SelectedTemplate, ActSequence)); }
        catch { return []; }
    }
    private void NewOpponent()
    {
        if (!CanLeave(true)) return;
        string name = "New opponent team";
        for (int number = 2; Directory.Exists(Path.Combine(directory, "teams", name)); number++)
            name = "New opponent team " + number;
        open(CreatorContent.Create(root, "team", name, "", directory, null));
    }
    private void OpenMap(int index)
    {
        var editor = EnsureMapEditor();
        ShowSection(MapSection);
        editor.SelectMap(index);
    }

    private Control OverviewPage()
    {
        var page = new StackPanel { Spacing = StudioTheme.SpaceLg };
        page.AttachedToVisualTree += (_, _) => BuildOverview(page);
        refreshRosters.Add(() => BuildOverview(page));
        BuildOverview(page);
        return page;
    }

    private void BuildOverview(StackPanel page)
    {
        page.Children.Clear();
        var nav = CampaignNavigation.Load(directory);
        var opponents = nav.Where(n => n.Context.StartsWith("Opponent ") && n.FilePath != null).ToList();
        var squads = nav.FirstOrDefault(n => n.DisplayName == "Custom Squads")?.Children.Where(n => n.FilePath != null).ToList() ?? [];
        static string Key(CampaignNode team) => Path.GetFileName(Path.GetDirectoryName(team.FilePath!)!);
        static bool NativeBound(CampaignNode team) => new CampaignDocument(File.ReadAllText(team.FilePath!)).Get(VanillaCopyData.OpponentId).Length>0;
        var teamKeys = opponents.Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var assignments = mapEditor?.AssignedOpponents ?? OpponentAssignments.Load(directory);
        var nodes = CurrentMatchNodes();
        var acts = ActSequence.Split(',').Select(s => int.TryParse(s.Trim(), out int n) ? Math.Clamp(n, 1, 3) : 1).ToArray();
        int mapCount = mapEditor?.MapCount ?? acts.Length;

        string template = SelectedTemplate;
        page.Children.Add(new TextBlock {
            Text = $"Squad template {(template.Length > 0 ? template : "not chosen")} · Act sequence {string.Join(", ", acts)} · {mapCount} {(mapCount == 1 ? "map" : "maps")}",
            Foreground = StudioTheme.Muted, TextWrapping = TextWrapping.Wrap
        });

        // Only things that are actually wrong; empty match nodes are fine (the game picks the opponent).
        var attention = new List<(string Text, string Link, Action Fix)>();
        foreach (var team in opponents)
            if (!NativeBound(team) && (!assignments.TryGetValue(Key(team), out var placed) || placed.Count == 0))
                attention.Add(($"{team.DisplayName} isn't on any map node", "Assign", () => ShowSection(OpponentsSection)));
        foreach (var node in nodes.Where(n => n.Team.Length > 0 && !teamKeys.Contains(n.Team)))
            attention.Add(($"{node.Label} uses a team that no longer exists ({node.Team})", $"Open map {node.Map}", () => OpenMap(node.Map - 1)));
        if (attention.Count > 0)
        {
            var items = new StackPanel { Spacing = StudioTheme.SpaceXs, Children = { new TextBlock { Text = "Needs attention", Foreground = Warn, FontWeight = FontWeight.SemiBold } } };
            foreach (var item in attention)
            {
                var link = ActionButton(item.Link, item.Fix);
                link.Background = Brushes.Transparent; link.BorderThickness = new Thickness(0); link.Padding = new Thickness(4, 0); link.Foreground = Warn;
                link.Content = new TextBlock { Text = item.Link, TextDecorations = TextDecorations.Underline };
                items.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = {
                    new TextBlock { Text = "•  " + item.Text + "  —", Foreground = Warn, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap }, link
                } });
            }
            page.Children.Add(new Border { Name = "CampaignAttention", Background = WarnFill, CornerRadius = StudioTheme.Radius, Padding = new Thickness(StudioTheme.SpaceMd), Child = items });
        }

        // Opponents
        var opponentList = new StackPanel { Name = "CampaignListOpponents", Spacing = StudioTheme.SpaceSm };
        foreach (var team in opponents)
        {
            bool nativeBound=NativeBound(team);
            bool placed = assignments.TryGetValue(Key(team), out var where) && where.Count > 0 || nativeBound;
            var colour = placed ? Good : Bad;
            opponentList.Children.Add(ItemButton(team.DisplayName, nativeBound ? "Vanilla selection" : placed ? where!.Count == 1 ? where[0] : $"{where.Count} map nodes" : "No map node",
                colour, placed ? GoodFill : BadFill, colour, () => { if (CanLeave(true)) open(team.FilePath!); }));
        }
        if (opponents.Count == 0) opponentList.Children.Add(Muted("No opponents yet. Create one to face on your map's match nodes."));
        int assignedCount = opponents.Count(t => NativeBound(t) || assignments.TryGetValue(Key(t), out var p) && p.Count > 0);
        var opponentCard = OverviewCard(OpponentsSection, "Opponents", $"{Plural(opponents.Count, "team")} · {assignedCount} assigned",
            Scroll(opponentList), "Open opponents", MintButton("+ New opponent", NewOpponent));

        // Custom teams
        var squadList = new StackPanel { Name = "CampaignListCustomTeams", Spacing = StudioTheme.SpaceSm };
        foreach (var squad in squads)
            squadList.Children.Add(ItemButton(squad.DisplayName, Plural(squad.Children.Count, "player"), StudioTheme.Text, StudioTheme.Input, null,
                () => { try { SaveWorkingCopy(); open(squad.FilePath!); } catch (Exception ex) { message.Text = ex.Message; } }));
        if (squads.Count == 0) squadList.Children.Add(Muted("No custom teams yet. Players will pick from the base-game squads."));
        var squadCard = OverviewCard(CustomSquadsSection, "Custom teams", Plural(squads.Count, "squad"),
            Scroll(squadList), "Open superstar/bench options", MintButton("+ Add player team", AddPlayerTeam));

        // Maps
        var mapList = new StackPanel { Name = "CampaignListMaps", Spacing = StudioTheme.SpaceSm };
        for (int map = 1; map <= mapCount; map++)
        {
            int index = map - 1;
            var onMap = nodes.Where(n => n.Map == map).ToList();
            int filled = onMap.Count(n => n.Team.Length > 0), act = onMap.FirstOrDefault()?.Act ?? (index < acts.Length ? acts[index] : 1);
            var bar = new Grid { Height = 5, Background = StudioTheme.Hover, ColumnDefinitions = new ColumnDefinitions($"{Math.Max(filled, 0)}*,{Math.Max(onMap.Count - filled, onMap.Count == 0 ? 1 : 0)}*") };
            bar.Children.Add(new Border { Background = Good, CornerRadius = new CornerRadius(3) });
            var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            head.Children.Add(new TextBlock { Text = $"Map {map} · Act {act}" });
            var count = new TextBlock { Text = $"{filled} / {onMap.Count} teams set", Foreground = StudioTheme.Muted, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(count, 1); head.Children.Add(count);
            var row = new Button { Background = StudioTheme.Input, BorderThickness = new Thickness(0), Padding = new Thickness(10, 7), HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = new StackPanel { Spacing = 6, Children = { head, bar } } };
            row.Click += (_, _) => { try { OpenMap(index); } catch (Exception ex) { message.Text = ex.Message; } };
            mapList.Children.Add(row);
        }
        mapList.Children.Add(Muted("Match nodes without a team use the game's own opponents."));
        var mapCard = OverviewCard(MapSection, "Maps", $"{nodes.Count(n => n.Team.Length > 0)} / {nodes.Count} match nodes set", mapList, "Open map editor");

        // Free agents
        int customAgents = (nav.FirstOrDefault(n => n.DisplayName == "Free Agents")?.Children ?? []).Count(p => p.FilePath != null &&
            !FreeAgentContent.BaseNames.Contains(new CampaignDocument(File.ReadAllText(p.FilePath)).Get(FreeAgentContent.Marker), StringComparer.OrdinalIgnoreCase));
        var agentCard = OverviewCard(FreeAgentsSection, "Free agents",
            selectedBaseAgents.Count == 0 && customAgents == 0 ? "Game default pool" : $"{selectedBaseAgents.Count} base game · {customAgents} custom",
            Muted("Players you can recruit during the campaign."), "Choose free agents");

        // Reward pools
        var tiles = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*"), ColumnSpacing = StudioTheme.SpaceSm };
        int customTotal = 0, column = 0;
        foreach (var (kind, label) in new[] { ("Relics", "RELICS"), ("Talents", "REGULAR TALENTS"), ("Goalie Talents", "GOALIE TALENTS"), ("Abilities", "ABILITIES"), (RewardPoolDefinition.ChaosTalents, "CHAOS TALENTS") })
        {
            var pool = rewardPools.TryGetValue(kind, out var edited) ? edited : RewardCatalog.ReadPool(root, rewards, kind);
            if (!pool.UseBaseGame) customTotal++;
            var tile = new Border { Background = StudioTheme.Input, CornerRadius = StudioTheme.Radius, Padding = new Thickness(6, 7), Child = new StackPanel { Spacing = 2, Children = {
                new TextBlock { Text = label, FontSize = 11, Foreground = StudioTheme.Muted, HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center },
                new TextBlock { Text = pool.UseBaseGame ? "Base game" : pool.Selected.Count + " selected", Foreground = StudioTheme.Text, HorizontalAlignment = HorizontalAlignment.Center }
            } } };
            Grid.SetColumn(tile, column++); tiles.Children.Add(tile);
        }
        var rewardCard = OverviewCard(RewardPoolsSection, "Reward pools", customTotal == 0 ? "Base game pools" : $"{customTotal} custom pools", tiles, "Edit reward pools");

        var cards = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("Auto,Auto"), ColumnSpacing = StudioTheme.SpaceMd, RowSpacing = StudioTheme.SpaceMd };
        cards.Children.Add(opponentCard);
        Grid.SetColumn(squadCard, 1); cards.Children.Add(squadCard);
        Grid.SetRow(mapCard, 1); cards.Children.Add(mapCard);
        var side = new StackPanel { Spacing = StudioTheme.SpaceMd, Children = { agentCard, rewardCard, MatchRulesCard() } };
        Grid.SetRow(side, 1); Grid.SetColumn(side, 1); cards.Children.Add(side);
        page.Children.Add(cards);
    }

    private Border OverviewCard(int section, string title, string summary, Control body, string openLabel, params Button[] actions)
    {
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = StudioTheme.SpaceMd };
        head.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeight.SemiBold, Foreground = StudioTheme.Accent });
        var note = new TextBlock { Text = summary, Foreground = StudioTheme.Muted, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(note, 1); head.Children.Add(note);
        // Same colour as the card reads as text, so give it a raised fill, accent edge and an arrow.
        var openButton = ActionButton(openLabel, () => ShowSection(section));
        openButton.Name = "CampaignOverviewSection" + section;
        openButton.Content = openLabel + "  →";
        openButton.Background = StudioTheme.Hover; openButton.BorderBrush = StudioTheme.Accent;
        openButton.FontWeight = FontWeight.SemiBold; openButton.FontSize = 15; openButton.Padding = new Thickness(16, 9);
        var footer = new DockPanel { Margin = new Thickness(0, StudioTheme.SpaceMd, 0, 0) };
        DockPanel.SetDock(openButton, Dock.Right); footer.Children.Add(openButton);
        var actionRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = StudioTheme.SpaceSm };
        foreach (var action in actions) actionRow.Children.Add(action);
        footer.Children.Add(actionRow);
        head.Margin = new Thickness(0, 0, 0, StudioTheme.SpaceMd);
        DockPanel.SetDock(head, Dock.Top); DockPanel.SetDock(footer, Dock.Bottom);
        return new Border {
            Background = StudioTheme.Surface, BorderBrush = StudioTheme.Border, BorderThickness = new Thickness(1), CornerRadius = StudioTheme.Radius,
            Padding = new Thickness(StudioTheme.SpaceLg), Child = new DockPanel { Children = { head, footer, body } }
        };
    }
    private Button ItemButton(string name, string detail, IBrush foreground, IBrush background, IBrush? border, Action click)
    {
        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = StudioTheme.SpaceMd };
        content.Children.Add(new TextBlock { Text = name, Foreground = foreground, TextTrimming = TextTrimming.CharacterEllipsis });
        var right = new TextBlock { Text = detail, Foreground = border == null ? StudioTheme.Muted : foreground, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(right, 1); content.Children.Add(right);
        var button = ActionButton(name, click);
        button.Content = content; button.Background = background; button.Padding = new Thickness(10, 7);
        button.BorderBrush = border; button.BorderThickness = new Thickness(border == null ? 0 : 1);
        button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        return button;
    }
    private Button MintButton(string label, Action action)
    {
        var button = ActionButton(label, action);
        button.Foreground = StudioTheme.Accent; button.BorderBrush = StudioTheme.Accent; button.Background = StudioTheme.Input;
        button.FontWeight = FontWeight.SemiBold; button.FontSize = 15; button.Padding = new Thickness(16, 9);
        return button;
    }
    private static ScrollViewer Scroll(Control content) => new() { Content = content, MaxHeight = 220 };
    private static TextBlock Muted(string text) => new() { Text = text, Foreground = StudioTheme.Muted, TextWrapping = TextWrapping.Wrap };
    private static string Plural(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";
}
