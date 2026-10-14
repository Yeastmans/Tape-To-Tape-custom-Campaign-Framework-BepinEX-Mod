using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using CampaignCore;

namespace CampaignCreator;

internal sealed class EquipmentStylePicker : StackPanel
{
    private readonly Func<string> read;
    private readonly Action<string> write;
    private readonly string? custom;
    private readonly List<string> premades;
    private readonly ComboBox mode, styles;
    private readonly ComboBoxItem customMode, premadeMode, emptyMode;
    private readonly ComboBoxItem? noHelmetMode;
    private readonly ContentControl detail;
    private readonly Button colours;
    private string? lastPremade;
    private bool updating;
    private Action? refreshSleeves;
    private readonly bool inherited;
    private readonly bool forceNoHelmet;
    private readonly bool teamPaletteOnly;
    private readonly string? emptyHint;
    private readonly Func<bool>? customColors;
    private readonly Action<bool>? selectCustomColors;
    private readonly ComboBoxItem teamMode = new() { Content = "Team" };
    private readonly ComboBoxItem inheritedMode = new() { Content = "Team colors" };

    internal static bool IsCustom(string value)
    {
        string name = value.Split('/').Last().Replace('_', ' ').Trim().ToLowerInvariant();
        if (name is "helmet customization face" || IsIso(value)) return false;
        return name.Length == 0 || name.StartsWith("standard") || name is "custom" or "default" or "team colors" or "team colours" or "colored"
            || name.Contains("customization") || name is "helmet colors" or "custom number" || FieldSchema.TryRgb(value, out _, out _, out _);
    }

    private static bool IsHidden(string value) => !EditorEquipmentPolicy.IsSelectable(value);

    private static bool IsIso(string value) => value.EndsWith("_iso", StringComparison.OrdinalIgnoreCase) || value.EndsWith("_lso", StringComparison.OrdinalIgnoreCase);

    internal static string? MatchingSleeves(string body, IEnumerable<string> sleeves)
    {
        if (IsCustom(body)) return sleeves.FirstOrDefault(IsCustom);
        string Normalize(string value) => new string(value.Split('/').Last().Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        string name=Normalize(body);
        return sleeves.FirstOrDefault(s=>Normalize(s)==name);
    }

    // emptyLabel names a blank value that is a real choice (socks: "Same as jersey") instead of "Nothing selected".
    internal EquipmentStylePicker(IEnumerable<string> catalog, Func<string> read, Action<string> write, Button colours, bool canSelect = true, string customLabel = "Custom color", bool allowNoHelmet = false, string? disabledReason = null, bool inherited = false, bool teamPaletteOnly = false, string? emptyLabel = null, string? emptyHint = null, Func<bool>? customColors = null, Action<bool>? selectCustomColors = null)
    {
        this.read = read; this.write = write; this.colours = colours;
        this.inherited = inherited;
        forceNoHelmet = !canSelect && allowNoHelmet;
        this.teamPaletteOnly = teamPaletteOnly;
        this.emptyHint = emptyHint;
        this.customColors = customColors; this.selectCustomColors = selectCustomColors;
        Spacing = 3;
        var available = catalog.Where(s => !IsHidden(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        custom = available.FirstOrDefault(IsCustom);
        premades = available.Where(s => !IsCustom(s)).OrderBy(FieldControls.Friendly).ToList();
        customMode = new ComboBoxItem { Content = customLabel, IsEnabled = custom != null || IsCustom(read()) };
        premadeMode = new ComboBoxItem { Content = "Premade" };
        // A real choice is plain text like the other modes: the ComboBox shows Control content
        // through a VisualBrush snapshot that doesn't inherit the theme foreground (black text).
        emptyMode = new ComboBoxItem { Content = emptyLabel != null ? emptyLabel
            : new TextBlock { Text = "Nothing selected", Foreground = FieldControls.EmptySelectionBrush } };
        noHelmetMode = allowNoHelmet ? new ComboBoxItem { Content = "No helmet" } : null;
        var modes = teamPaletteOnly ? new List<ComboBoxItem> { customMode, premadeMode } : new List<ComboBoxItem> { emptyMode, customMode, premadeMode };
        if (customColors != null) { modes = new List<ComboBoxItem> { teamMode, customMode, premadeMode }; if (emptyLabel != null) modes.Insert(0, emptyMode); }
        if (inherited) modes.Add(inheritedMode);
        if (noHelmetMode != null) modes.Add(noHelmetMode);
        mode = new StableComboBox { ItemsSource = modes, HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = canSelect };
        styles = new StableComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "Choose base-game style",
            ItemTemplate = new FuncDataTemplate<string>((style, _) => StyleLabel(style)) };
        detail = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch };
        Children.Add(mode); Children.Add(detail);
        if (!canSelect) ToolTip.SetTip(mode, disabledReason ?? "Add a goalie to the roster to choose its equipment.");
        Refresh();
        colours.Click += (_, _) => {
            if (canSelect && custom != null && read() != custom) write(custom);
        };
        mode.SelectionChanged += (_, _) => {
            if (updating || inherited) return;
            if (customColors != null) selectCustomColors?.Invoke(mode.SelectedItem == customMode);
            if (mode.SelectedItem == teamMode) { if (custom != null) write(custom); }
            else if (mode.SelectedItem == emptyMode) write("");
            else if (noHelmetMode != null && mode.SelectedItem == noHelmetMode) write("none");
            else if (mode.SelectedItem == customMode)
            {
                if (custom != null) write(custom);
            }
            else
            {
                string? choice = lastPremade ?? premades.FirstOrDefault();
                if (choice != null) write(choice);
            }
            Refresh();
        };
        styles.SelectionChanged += (_, _) => {
            if (updating || styles.SelectedItem is not string choice) return;
            lastPremade = choice; write(choice);
        };
    }

    internal void AddSleeves(IEnumerable<string> catalog, Func<string> readSleeves, Action<string> writeSleeves)
    {
        var choices = catalog.Where(s => !IsHidden(s) && !IsCustom(s))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(FieldControls.Friendly).ToArray();
        var selector = new StableComboBox { Tag = "Bicep", ItemsSource = choices,
            HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "Choose sleeves",
            ItemTemplate = new FuncDataTemplate<string>((style, _) => StyleLabel(style)) };
        var panel = new StackPanel { Spacing = 3, Children = {
            new TextBlock { Text = "Biceps / sleeves", FontSize = 11 }, selector } };
        Children.Add(panel);
        bool refreshing = false;
        refreshSleeves = () => {
            refreshing = true;
            panel.IsVisible = !inherited && mode.SelectedItem == premadeMode;
            selector.SelectedItem = choices.FirstOrDefault(s => s.Equals(readSleeves(), StringComparison.OrdinalIgnoreCase));
            refreshing = false;
        };
        selector.SelectionChanged += (_, _) => {
            if (!refreshing && selector.SelectedItem is string choice) writeSleeves(choice);
        };
        refreshSleeves();
    }

    // Avalonia also builds the selection template for null while showing PlaceholderText.
    // Friendly("") means "None", which would paint a second label over that placeholder.
    private static TextBlock StyleLabel(string? style) => new() {
        Text = string.IsNullOrEmpty(style) ? "" : FieldControls.Friendly(style),
        TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis
    };

    internal void Refresh()
    {
        updating = true;
        if (forceNoHelmet)
        {
            mode.SelectedItem = noHelmetMode;
            detail.Content = new TextBlock { Text = "No helmet" };
            refreshSleeves?.Invoke();
            updating = false;
            return;
        }
        if (inherited)
        {
            mode.SelectedItem = inheritedMode;
            detail.Content = null;
            refreshSleeves?.Invoke();
            updating = false;
            return;
        }
        string current = read();
        bool noHelmet = noHelmetMode != null && current.Equals("none", StringComparison.OrdinalIgnoreCase);
        bool isCustom = IsCustom(current);
        if (!noHelmet && premades.Contains(current, StringComparer.OrdinalIgnoreCase)) lastPremade = current;
        var choices = premades.ToArray();
        bool supported = noHelmet || (isCustom && !IsHidden(current)) || choices.Contains(current, StringComparer.OrdinalIgnoreCase);
        if (!supported) current = "";
        premadeMode.IsEnabled = choices.Length > 0;
        if (choices.Length == 0) ToolTip.SetTip(premadeMode, "No premade styles are listed for this part in the game catalog.");
        styles.ItemsSource = choices;
        styles.SelectedItem = choices.FirstOrDefault(s => s.Equals(current, StringComparison.OrdinalIgnoreCase));
        mode.SelectedItem = noHelmet ? noHelmetMode : customColors != null && isCustom && !(current.Length == 0 && emptyHint != null)
            ? customColors() ? customMode : teamMode
            : current.Length == 0 ? teamPaletteOnly ? customMode : emptyMode : isCustom ? customMode : premadeMode;
        detail.Content = noHelmet ? new TextBlock { Text = "No helmet" }
            : customColors != null && isCustom && !(current.Length == 0 && emptyHint != null) ? customColors() ? colours : new TextBlock { Text = "Uses the team's colors", FontSize = 11 }
            : teamPaletteOnly && (current.Length == 0 || isCustom) ? new TextBlock { Text = "Uses the team's colors", FontSize = 11 }
            : current.Length == 0 ? emptyHint != null ? new TextBlock { Text = emptyHint, FontSize = 11, TextWrapping = Avalonia.Media.TextWrapping.Wrap } : new TextBlock { Text = "Choose a style" }
            : isCustom ? colours : styles;
        refreshSleeves?.Invoke();
        updating = false;
    }
}
