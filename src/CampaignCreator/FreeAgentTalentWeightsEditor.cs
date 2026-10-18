using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CampaignCore;

namespace CampaignCreator;

/// <summary>Edits recruitment talents and weights without changing reward pools or templates.</summary>
public sealed class FreeAgentTalentWeightsEditor : UserControl
{
    private readonly Dictionary<string, string> names = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> selected = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> baseTalents;
    private readonly TextBlock poolStatus = new() { Name = "FreeAgentTalentPoolStatus", TextWrapping = TextWrapping.Wrap };
    private string poolError = "";
    private readonly StackPanel rows = new() { Spacing = 6 };
    private readonly TextBox search = new() { Name = "FreeAgentTalentWeightSearch", Watermark = "Search talent name or ID" };
    private readonly TextBox raw = new() { Name = "FreeAgentTalentWeightRaw", Watermark = "talent-guid:multiplier", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock error = new() { Name = "FreeAgentTalentWeightError", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.IndianRed };
    private readonly Expander repair;
    private string value;
    private bool updating;
    public string Value => value;
    public string PoolMode { get; private set; }
    public string SelectedValue { get; private set; }

    public FreeAgentTalentWeightsEditor(string root, string original, CampaignDocument? document = null)
    {
        value = original;
        document ??= new CampaignDocument("");
        PoolMode = document.Get(FreeAgentTalentPool.ModeKey);
        SelectedValue = document.Get(FreeAgentTalentPool.SelectedKey);
        baseTalents = RewardCatalog.FreeAgentBaseTalents(root);
        try
        {
            var pool = FreeAgentTalentPool.Read(document);
            selected.UnionWith(pool.UseBaseGame ? baseTalents : pool.Selected);
        }
        catch (InvalidOperationException ex) { poolError = ex.Message; }
        foreach (var item in RewardCatalog.ReadEntries(root, "Talents").Values)
        {
            string id = string.IsNullOrEmpty(item.Id) ? item.Key : item.Id;
            if ((Guid.TryParseExact(id, "D", out _) || Guid.TryParseExact(id, "N", out _))) names[id] = item.Name + " · Level " + item.Level;
        }
        repair = new Expander { Header = "Stored weights and imported IDs", Content = new StackPanel { Spacing = 8, Children = {
            new TextBlock { Text = "Stored weights are this campaign's saved talent multipliers. Each entry uses talent-ID:multiplier, with commas between entries. The named controls above edit these same values. Reset to 1 removes that talent's override; a blank field uses native weights for every talent.", TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = "Talents are stored by their internal IDs. An ID from an imported campaign may be missing from the current catalog; it appears above as Unknown talent (retained). It is kept when you save so importing or opening the campaign does not discard its weight. Use the named controls unless you need to repair or copy these stored entries.", TextWrapping = TextWrapping.Wrap, Foreground = StudioTheme.Muted },
            raw } } };
        ToolTip.SetTip(repair, "View or edit the saved multipliers, including imported talent IDs the current catalog cannot name.");
        var restore = new Button { Name = "FreeAgentTalentPoolRestore", Content = "Restore base talent pool" };
        restore.Click += (_, _) => {
            selected.Clear(); selected.UnionWith(baseTalents); PoolMode = "Base Game"; SelectedValue = ""; poolError = ""; LoadValue();
        };
        Content = InsetPoolPanel.Frame(new StackPanel { Spacing = 8, Children = {
            new TextBlock { Text = "Free-agent talent pool", FontSize = 20 },
            new TextBlock { Text = "Checked talents can be rolled as random extras for newly generated recruits and General Manager contracts. Saved random extras are checked against this pool when a new GM offer is created. The base-game choices start checked; check more talents or uncheck any choice. Regular and Chaos reward pools are independent. Unchecking every talent gives no random extras, while fixed player talents and already signed players stay intact.", TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = FreeAgentTalentWeights.Help, TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = "Each checkbox controls one exact level. Unchecked levels cannot appear from random extras or upgrades, including random talent lists in imported recruit templates. Fixed talents remain unchanged. A checked native base talent keeps its act-based upgrade chance only when the resulting level is also checked. Edited pools can roll checked higher levels directly. Native unlock, squad and conditional-weight restrictions still apply. Restoring the base pool keeps your multipliers.", TextWrapping = TextWrapping.Wrap },
            restore, poolStatus, search, error, InsetPoolPanel.List(rows, 300, "FreeAgentTalentWeightsScroll"), repair } }, "FreeAgentTalentWeightsPanel");
        raw.TextChanged += (_, _) => { if (!updating && (raw.Text ?? "") != value) { value = raw.Text ?? ""; LoadValue(); } };
        search.TextChanged += (_, _) => Refresh();
        LoadValue();
    }
    public void Validate()
    {
        var document = new CampaignDocument("");
        document.Set(FreeAgentTalentPool.ModeKey, PoolMode); document.Set(FreeAgentTalentPool.SelectedKey, SelectedValue);
        FreeAgentTalentPool.Read(document);
        if (!FreeAgentTalentWeights.TryParse(value, out _, out string problem))
            throw new InvalidOperationException("Free-agent talent weights: " + problem);
    }
    private void LoadValue()
    {
        updating = true; raw.Text = value; updating = false;
        entries.Clear();
        bool valid = FreeAgentTalentWeights.TryParse(value, out var policy, out string problem);
        if (valid) foreach (var p in policy.Values) entries[p.Key] = p.Value.ToString("R", CultureInfo.InvariantCulture);
        error.Text = poolError.Length > 0 ? poolError : problem;
        repair.IsExpanded = !valid;
        rows.IsEnabled = valid && poolError.Length == 0;
        Refresh();
    }
    private void Refresh()
    {
        rows.Children.Clear();
        poolStatus.Text = selected.Count + " talents checked" + (PoolMode.Equals("Custom", StringComparison.OrdinalIgnoreCase) ? " · Custom recruitment pool" : " · Base game recruitment pool");
        string filter = search.Text?.Trim() ?? "";
        foreach (string id in names.Keys.Concat(entries.Keys).Concat(selected).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => names.GetValueOrDefault(k, k), StringComparer.OrdinalIgnoreCase))
        {
            string name = names.GetValueOrDefault(id, "Unknown talent (retained)");
            if (!name.Contains(filter, StringComparison.OrdinalIgnoreCase) && !id.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            var input = new TextBox { Name = "FreeAgentTalentWeight", Tag = id, Width = 100,
                Text = entries.GetValueOrDefault(id, "1"), VerticalContentAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(input, id + " · native weight × multiplier");
            var reset = new Button { Content = "Reset to 1", Tag = id };
            var check = new CheckBox { Name = "FreeAgentTalentSelection", Tag = id, IsChecked = selected.Contains(id),
                Content = new TextBlock { Text = name, TextWrapping = TextWrapping.Wrap }, HorizontalAlignment = HorizontalAlignment.Stretch };
            ToolTip.SetTip(check, id + (baseTalents.Contains(id) ? " · Base-game talent level" : " · Additional talent level"));
            check.IsCheckedChanged += (_, _) => {
                if (check.IsChecked == true) selected.Add(id); else selected.Remove(id);
                PoolMode = "Custom";
                SelectedValue = string.Join(", ", selected.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
                poolStatus.Text = selected.Count + " talents checked · Custom recruitment pool";
            };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8 };
            row.Children.Add(check);
            Grid.SetColumn(input, 1); row.Children.Add(input); Grid.SetColumn(reset, 2); row.Children.Add(reset); rows.Children.Add(row);
            input.TextChanged += (_, _) => {
                // Avalonia can deliver TextChanged after construction. Merely opening
                // a row must not author hundreds of default overrides or rebuild it.
                if ((input.Text ?? "") == entries.GetValueOrDefault(id, "1")) return;
                entries[id] = input.Text ?? "";
                value = string.Join(", ", entries.Select(p => p.Key + ":" + p.Value));
                updating = true; raw.Text = value; updating = false;
                FreeAgentTalentWeights.TryParse(value, out _, out string problem); error.Text = problem;
            };
            reset.Click += (_, _) => { entries.Remove(id); value = string.Join(", ", entries.Select(p => p.Key + ":" + p.Value)); LoadValue(); };
        }
    }
}
