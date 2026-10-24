using System;
using System.IO;
using System.Linq;
using CampaignCore;

namespace EndlessMode;

internal static class SquadMapRuntime
{
    internal static string CurrentId
    {
        get {
            var state = Tape2Tape.CampaignUtil.CampaignState;
            // The run can differ from the last squad selected in a menu.
            string id = state?.runData?.squadId;
            return !string.IsNullOrEmpty(id) ? id : state?.GetCurrentSquad()?.id ?? "";
        }
    }

    internal static MapLayout Load(int stage, out string source, out string squadId)
    {
        source = Path.Combine(Plugin.ModFolder, "map_layouts", $"map-{stage}.txt");
        squadId = "";
        if (Plugin.IsDefaultMode) return null;
        squadId = CurrentId;
        var team = SquadPoolRuntime.Team(squadId);
        string squad = team == null ? null : Path.Combine(Plugin.ModFolder, team.IdentityKey);
        source = SquadMapOverrides.ResolvePath(Plugin.ModFolder, squad, stage);
        if (!File.Exists(source)) return null;
        var layout = MapLayout.Parse(File.ReadAllText(source));
        var errors = layout.Validate(Plugin.ConfigTeamDirs?.Select(Path.GetFileName) ?? Enumerable.Empty<string>());
        if (errors.Count > 0) throw new ArgumentException(string.Join("; ", errors));
        return layout;
    }

    // Source is diagnostic metadata, never a request to re-read authoring files.
    // Absence of squad metadata identifies a legitimate legacy receipt.
    internal static bool MatchesReceipt(CampaignDocument document, string squadId) => !document.Fields.ContainsKey("Squad")
        || string.Equals(document.Get("Squad"), squadId ?? "", StringComparison.OrdinalIgnoreCase);
}
