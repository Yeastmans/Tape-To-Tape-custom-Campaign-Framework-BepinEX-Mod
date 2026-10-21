using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CampaignCore;

namespace CampaignCreator;

internal sealed partial class TeamEditorView
{
    private Control BehaviorControls(string file)
    {
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*"), RowSpacing = 12, Margin = new Thickness(16) };
        var search = new TextBox { Name = "SquadBehaviorSearch", Watermark = "Find a behavior…", Width = 420, HorizontalAlignment = HorizontalAlignment.Left };
        var relicSummary = new TextBlock { Name = "SquadNativeRelics", Foreground = Muted, TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        var explanation = new StackPanel { Spacing = 8, Children = {
            Label("Squad behaviors", 22, Brushes.White),
            Label("Inherit uses your template's base-game setting. On and Off override it for this team. Restart the game after editing; start a new run for roster and starting-screen changes.", 13),
            Label("Injuries is in Team settings. Edit starting relics in Team roster → Team relics. Add template starting relics copies that preset's relics into the list. Slapshot's weak other shots come from Slapshot Plushie. Individual talents and abilities belong to the player editor; campaign offside belongs to Game options.", 12), relicSummary, search } };
        layout.Children.Add(explanation);
        var conflicts = new TextBlock { Name = "SquadBehaviorConflicts", Foreground = Brushes.Orange, TextWrapping = TextWrapping.Wrap };
        Grid.SetRow(conflicts, 1); layout.Children.Add(conflicts);
        var rows = new StackPanel { Spacing = 10 };
        var items = new List<(SquadBehavior behavior, Control row, TextBlock state, ComboBox choice, Func<int> requested)>();
        bool refreshing = false;
        var groups = new Dictionary<string, (Expander section, StackPanel rows)>();
        foreach (var behavior in SquadBehaviors.All.Where(b => b.Id != "no_injuries"))
        {
            if (!groups.TryGetValue(behavior.Group, out var group))
            {
                var contents = new StackPanel { Spacing = 10 };
                var section = new Expander { Header = behavior.Group, IsExpanded = true, Content = contents,
                    HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
                group = (section, contents); groups.Add(behavior.Group, group); rows.Children.Add(section);
            }
            string saved = Value(behavior.Key).Trim().ToLowerInvariant();
            if (saved.Length == 0 && SquadTemplatePolicy.Identity(Value("Squad Template")) == "noeffects")
            {
                string legacy = behavior.Id == "bench_screen" ? "No Effects Bench" : behavior.Id == "superstar_screen" ? "No Effects Superstars" : "";
                if (legacy.Length > 0 && Value(legacy).Equals("yes", StringComparison.OrdinalIgnoreCase)) saved = "on";
            }
            var choice = new ComboBox { Name = "Behavior_" + behavior.Id, Tag = behavior.Key, ItemsSource = new[] { "Inherit", "On", "Off" },
                SelectedIndex = saved is "on" or "yes" or "true" || (behavior.Id == GoalieFeatureOptions.ReserveBehaviorId && saved == "1") ? 1 : saved is "off" or "no" or "false" ? 2 : 0,
                Width = 130, VerticalAlignment = VerticalAlignment.Center };
            int requested = choice.SelectedIndex;
            Track(behavior.Key, () => Value(VanillaCopyData.SquadId).Length>0 && requested==0 ? "Inherit"
                : SquadBehaviors.RequiredValue(Value("Squad Template"), behavior.Id, Value) is bool required
                ? required ? "On" : "Off" : requested == 1 ? "On" : requested == 2 ? "Off" : "Inherit");
            var state = Label("", 12);
            string origins = string.Join(", ", behavior.Templates.Select(id => SquadTemplateCatalog.Find(id)?.Name ?? (id == "sinbin" ? "Sin Bin (native code)" : id == "norelics" ? "No Relics (native code)" : id)));
            var text = new StackPanel { Spacing = 3, Children = { Label(behavior.Name, 15, Brushes.White), Label("Base-game source: " + origins, 12), Label(behavior.Description, 12), state } };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 18 };
            row.Children.Add(text); Grid.SetColumn(choice, 1); row.Children.Add(choice);
            var card = Card(row, 12); items.Add((behavior, card, state, choice, () => requested)); group.rows.Children.Add(card);
            choice.SelectionChanged += (_, _) => {
                if (refreshing) return;
                requested = choice.SelectedIndex;
                refreshSquadBehaviors?.Invoke(); refreshTemplateRoster?.Invoke(); changed();
            };
        }
        refreshSquadBehaviors = () => {
            if (refreshing) return;
            refreshing = true;
            try
            {
            string template = Value("Squad Template"), query = search.Text?.Trim() ?? "";
            var relics = Value(VanillaCopyData.SquadId)==SquadTemplatePolicy.RuntimeIdentity(template)
                ? (IReadOnlyList<string>)Value("Vanilla Starting Relics").Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries)
                : SquadTemplateCatalog.StartingRelics(template);
            relicSummary.Text = "Native template relics: " + (relics.Count == 0 ? "None" : string.Join(", ", relics)) + ". Starting relics mode and the editable Team relics list determine what this team receives.";
            foreach (var item in items)
            {
                bool inherited = SquadBehaviors.Enabled(template, item.behavior.Id, _ => "");
                bool enabled = SquadBehaviors.Enabled(template, item.behavior.Id, Value);
                bool? required = SquadBehaviors.RequiredValue(template, item.behavior.Id, Value);
                item.choice.IsEnabled = required == null;
                item.choice.SelectedIndex = required is bool forced ? forced ? 1 : 2 : item.requested();
                item.state.Text = required != null
                    ? $"Required by Two-line roster flow: {(enabled ? "On" : "Off")}. Turn off Two-line roster flow to change this setting."
                    : $"Template default: {(inherited ? "On" : "Off")} · Effective: {(enabled ? "On" : "Off")}";
                ToolTip.SetTip(item.choice, required == null ? null : item.state.Text);
                item.state.Foreground = enabled ? Mint : Muted;
                item.row.IsVisible = query.Length == 0 || (item.behavior.Name + " " + item.behavior.Description + " " + item.behavior.Group + " " + string.Join(" ", item.behavior.Templates.Select(id => SquadTemplateCatalog.Find(id)?.Name ?? id))).Contains(query, StringComparison.OrdinalIgnoreCase);
            }
            foreach (var group in groups.Values) group.section.IsVisible = group.rows.Children.Any(row => row.IsVisible);
            conflicts.Text = string.Join("\n", SquadBehaviors.Conflicts(template, Value));
            conflicts.IsVisible = !string.IsNullOrEmpty(conflicts.Text);
            }
            finally { refreshing = false; }
        };
        validateSquadBehaviors = () => {
            var errors = SquadBehaviors.Conflicts(Value("Squad Template"), Value);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
        };
        search.TextChanged += (_, _) => refreshSquadBehaviors();
        refreshSquadBehaviors();
        var scroll = new ScrollViewer { Content = rows }; Grid.SetRow(scroll, 2); layout.Children.Add(scroll);
        return layout;
    }
}
