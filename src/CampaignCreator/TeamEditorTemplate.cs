using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CampaignCore;

namespace CampaignCreator;

internal sealed partial class TeamEditorView
{
    internal static bool IsCustomSquad(string file) => string.Equals(
        Directory.GetParent(Path.GetDirectoryName(file)!)?.Name, "player_teams", StringComparison.OrdinalIgnoreCase);
    internal static SquadTemplateOption? TemplateForTeam(string file)
    {
        var folder = Directory.GetParent(Path.GetDirectoryName(file)!);
        if (folder?.Parent == null || !string.Equals(folder.Name, "player_teams", StringComparison.OrdinalIgnoreCase)) return null;
        var team = new CampaignDocument(File.ReadAllText(file));
        if (!File.Exists(Path.Combine(folder.Parent.FullName, "campaign.txt"))) return SquadTemplateCatalog.Find(team.Get("Squad Template", "Basic Squad"));
        string preset = team.Get("Squad Template");
        if (string.IsNullOrWhiteSpace(preset)) preset = new CampaignDocument(File.ReadAllText(Path.Combine(folder.Parent.FullName, "campaign.txt"))).Get("Squad Template", "Basic Squad");
        return SquadTemplateCatalog.Find(preset);
    }

    private Control NoEffectsOptions()
    {
        var useBench = new CheckBox { Name = "NoEffectsBench", Content = "Use a starting bench", IsChecked = Value("No Effects Bench").Equals("yes", StringComparison.OrdinalIgnoreCase) };
        var useSuperstars = new CheckBox { Name = "NoEffectsSuperstars", Content = "Offer a starting superstar", IsChecked = Value("No Effects Superstars").Equals("yes", StringComparison.OrdinalIgnoreCase) };
        Track("No Effects Bench", () => useBench.IsChecked == true ? "yes" : "no");
        Track("No Effects Superstars", () => useSuperstars.IsChecked == true ? "yes" : "no");
        var freeOptions = new StackPanel { Spacing = StudioTheme.SpaceXs, Children = { Label("No Effects options", 14), useBench, useSuperstars,
            new TextBlock { Text = "Save these options with the team, then choose the shared bench and superstar players on the campaign's Choose campaign team page. A complete five-skater roster leaves no open slot for a starting superstar.", TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Muted } } };
        useBench.IsCheckedChanged += (_, _) => { changed(); refreshTemplateRoster?.Invoke(); };
        useSuperstars.IsCheckedChanged += (_, _) => { changed(); refreshTemplateRoster?.Invoke(); };
        refreshTemplateOptions = () => freeOptions.IsVisible = SquadTemplatePolicy.Identity(Value("Squad Template")) == "noeffects";
        refreshTemplateOptions();
        return freeOptions;
    }
}

