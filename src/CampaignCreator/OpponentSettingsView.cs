using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using CampaignCore;

namespace CampaignCreator;

public sealed class OpponentSettingsView : UserControl
{
    private Func<string[]> conflicts = () => Array.Empty<string>();
    public string[] Conflicts => conflicts();
    public OpponentSettingsView(CampaignDocument document, Action changed, Action<string, Func<string>> register)
    {
        var shell = new Grid { Margin = new Thickness(18), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*"), RowSpacing = StudioTheme.SpaceMd };
        var heading = new StackPanel { Spacing = 6, Children = {
            new TextBlock { Text = "Opponent settings", FontSize = 24, FontWeight = FontWeight.Bold },
            new TextBlock { Text = "Choose how matches against this team play. These settings follow the team on every assigned map node.", TextWrapping = TextWrapping.Wrap }
        } };
        shell.Children.Add(heading);
        var setup = new StackPanel { Spacing = StudioTheme.SpaceLg };
        var columns = new Grid { Name = "OpponentColumns", ColumnSpacing = StudioTheme.SpaceLg };
        Grid.SetRow(columns, 3); shell.Children.Add(columns);
        var sections = new List<(Control card, int column)>();
        var lanes = Enumerable.Range(0, 3).Select(_ => new StackPanel { Spacing = StudioTheme.SpaceLg }).ToArray();
        var scrolls = lanes.Select(lane => new ScrollViewer { Content = lane, Padding = new Thickness(0,0,8,0), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }).ToArray();
        foreach (var scroll in scrolls) columns.Children.Add(scroll);
        Expander Section(string title, Control content) => new() {
            Header = new TextBlock { Text = title, FontWeight = FontWeight.SemiBold }, IsExpanded = true,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = StudioTheme.Surface, BorderBrush = StudioTheme.Border, BorderThickness = new Thickness(1),
            Content = new Border { Padding = new Thickness(12), Child = content }
        };
        ComboBox Choice(string key, string title, IEnumerable<string> values, string fallback, string help)
        {
            string saved = document.Get(key);
            if (saved.Trim().Equals("Use preset", StringComparison.OrdinalIgnoreCase)) saved = fallback;
            string display = string.IsNullOrWhiteSpace(saved) ? fallback : saved;
            var options = values.ToList();
            if (display.Length > 0 && !options.Contains(display, StringComparer.OrdinalIgnoreCase)) options.Add(display);
            var combo = new ComboBox { Name = key.Replace(" ", ""), Tag = key, ItemsSource = options, SelectedItem = options.FirstOrDefault(v => v.Equals(display, StringComparison.OrdinalIgnoreCase)) ?? fallback, HorizontalAlignment = HorizontalAlignment.Stretch };
            register(key, () => saved);
            combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is string value) { saved = value; changed(); } };
            setup.Children.Add(new StackPanel { Spacing = 6, Children = {
                new TextBlock { Text = title, FontWeight = FontWeight.SemiBold }, combo,
                new TextBlock { Text = help, Foreground = StudioTheme.Muted, TextWrapping = TextWrapping.Wrap }
            } });
            return combo;
        }
        string preset = document.Get("Opponent Preset");
        string serialized = document.Get("Opponent Effects");
        var selected = serialized.Length == 0 ? OpponentEffects.FromPreset(preset) : OpponentEffects.Parse(serialized);
        conflicts = () => OpponentEffects.Conflicts(selected).ToArray();
        register("Opponent Preset", () => preset);
        register("Opponent Effects", () => serialized);
        var notice = new TextBlock { Name = "EffectNotice", TextWrapping = TextWrapping.Wrap, Foreground = Brush.Parse("#FFD18C") };
        Grid.SetRow(notice, 1); shell.Children.Add(notice);
        void Summary()
        {
            notice.Text = serialized.Length == 0 && preset.Length > 0 && preset != "Standard"
                ? "Saved team type: " + preset + ". Editing an effect switches this team to individual rules. Formations, puck and surface are selected separately."
                : "Each checkbox enables the named base-game rule and keeps its native requirements. It does not supply players, abilities, relics or a different map node. Effects stack unless marked as alternatives; see each rule for setup and timing.";
            if (selected.Contains("golfer-rotation") || selected.Contains("golfer-tiers"))
                notice.Text += " Copy base campaign preserves the native Golfers' thirteen-player roster, including reserves outside the editable lineup. You can edit five starters, or ten skaters with two lines enabled. A newly authored ten-slot roster does not supply the native reserve players or transformation targets.";
        }
        Summary();
        var offense = Choice("Opponent Offense", "Offensive formation", OpponentEffects.Offense, "Standard", "Defaults to Standard. One formation at a time. Golfer offensive AI replaces this formation when selected.");
        var defense = Choice("Opponent Defense", "Defensive formation", OpponentEffects.Defense, "Standard", "Defaults to Standard. One formation at a time. Golfer defense roster preparation is separate and keeps this formation. Native short-handed and roster-size rules still apply.");
        void UpdateFormationAvailability(bool edited)
        {
            offense.IsEnabled = !selected.Contains("golfer-offense");
            if (edited && !offense.IsEnabled) offense.SelectedItem = "Standard";
            ToolTip.SetTip(offense, offense.IsEnabled ? "Choose offensive formation" : "Uncheck Golfer offensive AI to choose a normal formation.");
        }
        UpdateFormationAvailability(false);
        Choice("Match Puck", "Puck / ball", OpponentOptions.Pucks, "Hockey puck", "Defaults to Hockey puck. Choose one puck or ball for matches against this opponent.");
        var surface = Choice("Match Surface", "Playing surface", OpponentOptions.Surfaces, "Standard ice", "Defaults to Standard ice. Choose one surface. Surface relics may also affect the rink.");
        string fairwaySetting = document.Get("Golf Fairway");
        var fairway = new CheckBox
        {
            Name = "GolfFairway", Content = "Golf Fairway",
            IsChecked = OpponentOptions.GolfFairwayEnabled(surface.SelectedItem?.ToString() ?? "", fairwaySetting),
            IsEnabled = surface.SelectedItem?.ToString() == "Golf ice"
        };
        register("Golf Fairway", () => fairwaySetting);
        fairway.IsCheckedChanged += (_, _) => { fairwaySetting = fairway.IsChecked == true ? "yes" : "no"; changed(); };
        setup.Children.Add(fairway);
        var golfNote = new TextBlock
        {
            Name = "GolfSurfaceNote",
            Text = "Golf Fairway turns on when you select Golf ice. Uncheck it for a fixed layout without terrain physics. It affects either a hockey puck or a ball, not skater movement speed. The course changes between periods: layouts 1, 2 and 3, with layout 3 retained in overtime. Selecting another surface turns Golf Fairway off.",
            Foreground = StudioTheme.Muted,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = surface.SelectedItem is string initialSurface && initialSurface == "Golf ice"
        };
        surface.SelectionChanged += (_, _) =>
        {
            bool golf = surface.SelectedItem?.ToString() == "Golf ice";
            fairwaySetting = golf ? "yes" : "no";
            fairway.IsEnabled = golf;
            fairway.IsChecked = golf;
            golfNote.IsVisible = golf;
        };
        setup.Children.Add(golfNote);
        string sponsorsSetting = document.Get("Tycoons Sponsors");
        var sponsors = new CheckBox
        {
            Name = "TycoonsSponsors", Content = "Tycoons sponsors",
            IsChecked = OpponentOptions.TycoonsSponsorsEnabled(surface.SelectedItem?.ToString() ?? "", sponsorsSetting),
            IsEnabled = surface.SelectedItem?.ToString() == "Tycoons ice"
        };
        register("Tycoons Sponsors", () => sponsorsSetting);
        sponsors.IsCheckedChanged += (_, _) => { sponsorsSetting = sponsors.IsChecked == true ? "yes" : "no"; changed(); };
        setup.Children.Add(sponsors);
        var sponsorsNote = new TextBlock
        {
            Name = "TycoonsSponsorsNote",
            Text = "Shows the Tycoons sponsor panel in the match menu. It turns on when you select Tycoons ice and can be unchecked. Selecting another surface turns it off. Tycoons ice has a fixed layout. Brawls and two-line behavior are separate gameplay options.",
            Foreground = StudioTheme.Muted, TextWrapping = TextWrapping.Wrap,
            IsVisible = surface.SelectedItem?.ToString() == "Tycoons ice"
        };
        surface.SelectionChanged += (_, _) =>
        {
            bool tycoons = surface.SelectedItem?.ToString() == "Tycoons ice";
            sponsorsSetting = tycoons ? "yes" : "no";
            sponsors.IsEnabled = tycoons;
            sponsors.IsChecked = tycoons;
            sponsorsNote.IsVisible = tycoons;
        };
        setup.Children.Add(sponsorsNote);
        sections.Add((Section("Match setup", setup), 0));
        string twoLines = document.Get("Opponent Two Lines");
        var lineBehavior = new CheckBox { Name = "TycoonsTwoLines", Content = "Tycoons two-line behavior", IsChecked = twoLines.Equals("yes", StringComparison.OrdinalIgnoreCase) };
        register("Opponent Two Lines", () => twoLines);
        lineBehavior.IsCheckedChanged += (_, _) => { twoLines = lineBehavior.IsChecked == true ? "yes" : "no"; changed(); };
        sections.Add((Section("Roster behavior", new StackPanel { Spacing = 6, Children = { lineBehavior,
            new TextBlock { Text = "Use the native opponent line-changing system. Fill all five positions on Line 2 in Team roster. The game controls when lines change; this does not replace the defensive formation or add a player-squad bench.", TextWrapping = TextWrapping.Wrap, Foreground = StudioTheme.Muted } } }), 0));
        string cursesSetting = document.Get("Canada Curses");
        var curses = new CheckBox { Name = "CanadaCurses", Content = "Canada curses",
            IsChecked = OpponentOptions.CanadaCursesEnabled(cursesSetting) };
        register("Canada Curses", () => cursesSetting);
        curses.IsCheckedChanged += (_, _) => { cursesSetting = curses.IsChecked == true ? "yes" : "no"; changed(); };
        var curseSection = Section("Canada curses", new StackPanel { Spacing = 6, Children = { curses,
            new TextBlock { Text = "Use Team Canada's native curse-selection sequence against this opponent. The player chooses from eligible handicaps such as reduced stats, frozen relics or fragile players. Native timing, repeat-pick limits and squad exemptions still apply. Independent of the arena and other opponent effects.", TextWrapping = TextWrapping.Wrap, Foreground = StudioTheme.Muted } } });
        sections.Add((curseSection, 1));
        var relicRows = new StackPanel { Spacing = StudioTheme.SpaceMd };
        foreach (var interaction in RelicOpponentInteractions.All)
        {
            string mode = RelicOpponentInteractions.Resolve(document.Get(interaction.SettingKey),
                OpponentEffects.Parse(serialized), interaction.EffectKey);
            var choice = new ComboBox { Name = interaction.SettingKey.Replace(" ", ""), Tag = interaction.SettingKey,
                ItemsSource = RelicOpponentInteractions.Modes, SelectedItem = mode, HorizontalAlignment = HorizontalAlignment.Stretch };
            register(interaction.SettingKey, () => mode);
            choice.SelectionChanged += (_, _) => { if (choice.SelectedItem is string value) { mode = value; changed(); } };
            relicRows.Children.Add(new StackPanel { Spacing = 6, Children = {
                new TextBlock { Text = interaction.Label, FontWeight = FontWeight.SemiBold }, choice,
                new TextBlock { Text = interaction.Help, TextWrapping = TextWrapping.Wrap, Foreground = StudioTheme.Muted }
            } });
        }
        var relicSection = Section("Relic interactions", relicRows);
        sections.Add((relicSection, 1));
        var search = new TextBox { Name = "EffectSearch", Watermark = "Find an effect, team or rule…" };
        Grid.SetRow(search, 2); shell.Children.Add(search);
        var boxes = new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase);
        void UpdateConflicts()
        {
            foreach (var (key, box) in boxes)
            {
                var candidate = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase) { key };
                var errors = OpponentEffects.Conflicts(candidate).ToArray();
                box.IsEnabled = selected.Contains(key) || errors.Length == 0;
                ToolTip.SetTip(box, box.IsEnabled ? null : string.Join("\n", errors) + " Uncheck the current alternative first.");
            }
        }
        var rows = new List<(Control row, OpponentEffect effect)>();
        var effectSections = new List<(Expander card, List<Control> rows)>();
        string Group(OpponentEffect effect) => effect.Group == "Special rules"
            ? effect.Consumers.Any(c => c.StartsWith("AI.", StringComparison.Ordinal)) ? "AI behavior"
                : effect.Key.Contains("goalie", StringComparison.Ordinal) ? "Goalkeepers" : "Special match rules"
            : effect.Group;
        bool updating = false;
        foreach (var group in OpponentEffects.All.Where(e => !RelicOpponentInteractions.IsInteraction(e.Key)).GroupBy(Group))
        {
            var list = new StackPanel { Spacing = StudioTheme.SpaceMd };
            var groupRows = new List<Control>();
            foreach (var effect in group)
            {
                var box = new CheckBox { Name = "Effect_" + effect.Key.Replace('-', '_'), Tag = effect.Key,
                    Content = new TextBlock { Text = effect.Label, TextWrapping = TextWrapping.Wrap },
                    HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    IsChecked = selected.Contains(effect.Key) };
                boxes[effect.Key] = box;
                var row = new StackPanel { Spacing = 3, Children = { box, new TextBlock { Text = effect.Help, Margin = new Thickness(28,0,0,0), TextWrapping = TextWrapping.Wrap, Foreground = StudioTheme.Muted } } };
                rows.Add((row, effect)); list.Children.Add(row); groupRows.Add(row);
                box.IsCheckedChanged += (_, _) => {
                    if (updating) return;
                    var next = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);
                    if (box.IsChecked == true) next.Add(effect.Key); else next.Remove(effect.Key);
                    var conflicts = OpponentEffects.Conflicts(next).ToArray();
                    if (conflicts.Length > 0 && box.IsChecked == true)
                    {
                        updating = true; box.IsChecked = false; updating = false;
                        notice.Text = string.Join("\n", conflicts) + " Uncheck the current alternative first.";
                        return;
                    }
                    selected = next; serialized = OpponentEffects.Serialize(selected);
                    UpdateFormationAvailability(true);
                    UpdateConflicts();
                    changed(); Summary();
                };
            }
            var card = Section(group.Key + " · " + group.Count(), list);
            sections.Add((card, group.Key == "AI behavior" ? 0 : group.Key == "Appearance and sound" ? 2 : 1));
            effectSections.Add((card, groupRows));
        }
        UpdateConflicts();
        search.TextChanged += (_, _) => {
            string term = search.Text?.Trim() ?? "";
            curseSection.IsVisible = term.Length == 0 || "Canada curses handicaps stats relics fragile players".Contains(term, StringComparison.OrdinalIgnoreCase);
            if (term.Length > 0 && curseSection.IsVisible) curseSection.IsExpanded = true;
            relicSection.IsVisible = term.Length == 0 || RelicOpponentInteractions.All.Any(i => (i.Label + " " + i.Help).Contains(term, StringComparison.OrdinalIgnoreCase));
            if (term.Length > 0 && relicSection.IsVisible) relicSection.IsExpanded = true;
            foreach (var (row, effect) in rows) row.IsVisible = term.Length == 0 || (effect.Label + " " + effect.Help).Contains(term, StringComparison.OrdinalIgnoreCase);
            foreach (var (card, groupRows) in effectSections) { card.IsVisible = groupRows.Any(r => r.IsVisible); if (term.Length > 0 && card.IsVisible) card.IsExpanded = true; }
        };
        var unknown = selected.Where(k => !boxes.ContainsKey(k) && !RelicOpponentInteractions.IsInteraction(k)).ToArray();
        if (unknown.Length > 0) setup.Children.Add(new TextBlock { Text = "Unrecognized stored effects (preserved): " + string.Join(", ", unknown), TextWrapping = TextWrapping.Wrap });
        var existingConflicts = OpponentEffects.Conflicts(selected).ToArray();
        if (existingConflicts.Length > 0) notice.Text = string.Join("\n", existingConflicts) + " Conflicting rules will be disabled at runtime until corrected.";
        setup.Children.Add(new TextBlock { Text = "Campaign opponents only.", TextWrapping = TextWrapping.Wrap, Foreground = StudioTheme.Muted });
        int displayedColumns = 0;
        void LayoutColumns(double width)
        {
            int count = width >= 1150 ? 3 : width >= 760 ? 2 : 1;
            if (count == displayedColumns) return;
            displayedColumns = count;
            foreach (var lane in lanes) lane.Children.Clear();
            columns.ColumnDefinitions = new ColumnDefinitions(string.Join(",", Enumerable.Repeat("*", count)));
            for (int i = 0; i < scrolls.Length; i++) { scrolls[i].IsVisible = i < count; Grid.SetColumn(scrolls[i], Math.Min(i, count - 1)); }
            // Setup + AI on the left, match rules in the middle, presentation on
            // the right. Smaller windows merge presentation into the second lane.
            foreach (var section in sections.OrderBy(s => s.column)) lanes[Math.Min(section.column, count - 1)].Children.Add(section.card);
        }
        SizeChanged += (_, _) => LayoutColumns(Bounds.Width);
        LayoutColumns(Bounds.Width);
        Content = shell;
    }
}
