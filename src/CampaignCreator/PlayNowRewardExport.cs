using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using CampaignCore;

namespace CampaignCreator;

// Review one fully prepared snapshot. Applying a choice never reloads authored
// files, so the warning and the written payload describe the same export.
internal static class PlayNowRewardExport
{
    internal static async Task<PlayNowRewardPolicy?> Choose(Window owner, ExportReport report)
    {
        if (report.Unsupported.Count == 0) return PlayNowRewardPolicy.Strict;
        var dialog = new Window { Title = "Campaign rewards in Play Now", Width = 670, Height = 440,
            MinWidth = 480, MinHeight = 320, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var cancel = new Button { Name = "PlayNowRewardsCancel", Content = "Cancel" };
        var skip = new Button { Name = "PlayNowRewardsSkipUnsupported", Content = "Skip unsupported" };
        var anyway = new Button { Name = "PlayNowRewardsExportAnyway", Content = "Export anyway" };
        cancel.Click += (_, _) => dialog.Close();
        skip.Click += (_, _) => dialog.Close((PlayNowRewardPolicy?)PlayNowRewardPolicy.SkipUnsupported);
        anyway.Click += (_, _) => dialog.Close((PlayNowRewardPolicy?)PlayNowRewardPolicy.ExportAnyway);
        dialog.KeyDown += (_, e) => { if (e.Key == Key.Escape) { dialog.Close(); e.Handled = true; } };
        dialog.Opened += (_, _) => cancel.Focus();
        var layout = new Grid { Margin = new Thickness(24), RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 16 };
        layout.Children.Add(new TextBlock { Text = "These custom rewards depend on campaign progress and may not function in Play Now. Skip unsupported leaves them out of this exported copy. Export anyway keeps their original rules. Your authored files stay unchanged.", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var list = new ScrollViewer { Content = new TextBlock { Text = string.Join("\n", report.Unsupported), TextWrapping = Avalonia.Media.TextWrapping.Wrap } };
        Grid.SetRow(list, 1); layout.Children.Add(list);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var button in new[] { cancel, skip, anyway }) { button.Margin = new Thickness(6, 4, 0, 0); actions.Children.Add(button); }
        Grid.SetRow(actions, 2); layout.Children.Add(actions); dialog.Content = layout;
        return await dialog.ShowDialog<PlayNowRewardPolicy?>(owner);
    }

    internal static void ApplyPlayer(Dictionary<string, object> model, CampaignDocument sidecar, PlayNowRewardPolicy policy)
    {
        var native = JsonSerializer.SerializeToNode(model)!.AsObject();
        Apply(native, sidecar, policy, team: false);
        model["talentIds"] = Ids(native, "talentIds");
    }

    internal static ExportTranslation.Package ApplyTeam(ExportTranslation.Package package, PlayNowRewardPolicy policy)
    {
        var saves = new Dictionary<string, string>();
        var companions = new Dictionary<string, string>(package.Overrides);
        foreach (var pair in package.Saves)
        {
            bool team = pair.Key.StartsWith("CustomTeam-", StringComparison.Ordinal);
            var model = JsonNode.Parse(pair.Value)!.AsObject();
            string id = model[team ? "Id" : "id"]!.GetValue<string>();
            string companion = id + (team ? ".team.txt" : ".txt");
            var sidecar = new CampaignDocument(companions[companion]);
            Apply(model, sidecar, policy, team);
            saves[pair.Key] = model.ToJsonString(); companions[companion] = sidecar.ToString();
        }
        return new(package.TeamId, saves, companions);
    }

    private static string[] Ids(JsonObject model, string key) => model[key]!.AsArray().Select(n => n!.GetValue<string>()).ToArray();
    private static void Apply(JsonObject model, CampaignDocument sidecar, PlayNowRewardPolicy policy, bool team)
    {
        if (!Enum.IsDefined(policy)) throw new ArgumentOutOfRangeException(nameof(policy));
        var items = CustomRewardExport.Read(sidecar).ToArray();
        var unsupported = items.Where(d => !CustomRewardExport.IsPlayNowCompatible(d)).Select(d => d.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var omitted = policy == PlayNowRewardPolicy.SkipUnsupported ? unsupported : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CustomRewardExport.EmbedPlayNow(sidecar, items.Where(d => !omitted.Contains(d.Id)), policy);
        string key = team ? "relics" : "talentIds";
        string[] ids = Ids(model, key).Where(id => !omitted.Contains(id)).ToArray();
        model[key] = new JsonArray(ids.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray());
        if (team)
        {
            sidecar.Set("Export Relic IDs", string.Join(",", ids));
            sidecar.Set("Team Relics", string.Join(",", ids)); sidecar.Set("Relics", "");
        }
        else
        {
            sidecar.Set("Talents", string.Join(",", ids)); sidecar.Set("Goalie Talents", "");
            sidecar.Set("Export Talent IDs", string.Join(",", ids));
            sidecar.Set("Export Model", model.ToJsonString());
        }
        sidecar.Set("Team Talents", "");
    }
}
