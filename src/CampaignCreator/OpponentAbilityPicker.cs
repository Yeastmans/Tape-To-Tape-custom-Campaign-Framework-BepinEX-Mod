using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;

namespace CampaignCreator;

internal static class OpponentAbilityPicker
{
    internal static (Control control, Func<string> read) Create(string value, GameData data, Action changed)
    {
        var catalog = RewardCatalog.ReadEntries(data.Root, "Abilities");
        string resolved = RewardCatalog.ResolveKey(data.Root, "Abilities", value);
        var allowed = catalog.Values.Where(OpponentAbilityPolicy.Supports).OrderBy(e => e.Name).Select(e => e.Key).ToList();
        bool unavailable = resolved.Length > 0 && !resolved.Equals("none", StringComparison.OrdinalIgnoreCase)
            && !allowed.Contains(resolved, StringComparer.OrdinalIgnoreCase);
        var options = new[] { "", "none" }.Concat(allowed).ToList();
        string saved = value; // Preserve the exact authored value until explicitly edited.
        string Label(string item) => item == "" ? "Nothing selected" : item == "none" ? "None" : catalog[item].Name;
        var combo = new ComboBox
        {
            Name = "OpponentAbility", ItemsSource = options,
            SelectedItem = unavailable ? "" : options.FirstOrDefault(x => x.Equals(resolved, StringComparison.OrdinalIgnoreCase)),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            ItemTemplate = new FuncDataTemplate<string>((item, _) => RewardRow.Create(Label(item ?? ""),
                item != null && catalog.TryGetValue(item, out var entry) ? RewardCatalog.Reminder(entry.Description) : ""))
        };
        var warning = new TextBlock
        {
            IsVisible = unavailable, TextWrapping = TextWrapping.Wrap, Foreground = Brush.Parse("#FFD18C"),
            Text = $"Saved ability: {RewardCatalog.DisplayNames(data.Root, "Abilities", value)}. Hockey AI support is not established. Kept until you choose a replacement or None."
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is not string selected) return;
            saved = selected; warning.IsVisible = false; changed();
        };
        return (new StackPanel { Tag = "Ability", Spacing = 6, Children =
        {
            new TextBlock { Text = "Abilities supported by the opponent's hockey AI. The native AI chooses when to use an equipped ability based on charges, cooldowns, targets and game state. Opponent effect checkboxes do not equip abilities.", FontSize = 11, TextWrapping = TextWrapping.Wrap },
            warning, combo
        } }, () => saved);
    }
}
