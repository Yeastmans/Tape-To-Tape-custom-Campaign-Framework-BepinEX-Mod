using CampaignCore;

namespace CampaignCreator;

/// <summary>Effective authoring graphs for a single squad, including unsaved editor drafts.</summary>
internal static class SquadMapCatalog
{
    internal sealed record Resolved(int Stage, MapLayout Layout, bool Native, bool SquadOverride);
    internal static List<Resolved> Resolve(string root, string campaign, CampaignDocument team, string? squad,
        IReadOnlyList<MapLayout>? drafts, string? draftSquad, IReadOnlyList<int>? originals, IReadOnlyList<bool>? inherited,
        string sequence, string fallbackTemplate)
    {
        bool sharedDraft = drafts != null && draftSquad == null;
        bool ownDraft = drafts != null && squad != null && draftSquad != null
            && Path.GetFullPath(squad).Equals(Path.GetFullPath(draftSquad), StringComparison.OrdinalIgnoreCase);
        int[] acts = CampaignActSequence.Read(new CampaignDocument("Act Sequence = " + sequence));
        int count = sharedDraft || ownDraft ? drafts!.Count : acts.Length;
        string template = team.Get("Squad Template", fallbackTemplate);
        List<MapLayout>? native = null;
        var result = new List<Resolved>();
        for (int i = 0; i < count; i++)
        {
            int original = sharedDraft ? originals?[i] ?? i + 1 : i + 1;
            string? own = squad != null && original > 0 && SquadMapOverrides.Own(team) ? SquadMapOverrides.OverridePath(squad, original) : null;
            string file = own != null && File.Exists(own) ? own : Path.Combine(campaign, "map_layouts", $"map-{original}.txt");
            bool isOverride = own != null && file == own;
            if (ownDraft || sharedDraft && !isOverride)
            {
                if (inherited == null || !inherited[i]) { result.Add(new(i + 1, drafts![i], false, ownDraft)); continue; }
                file = ""; // An unedited native preview must keep the selected squad's template variants.
            }
            bool isNative = !File.Exists(file);
            var layout = isNative
                ? MapCatalog.TemplateMap(native ??= MapCatalog.LoadBaseMaps(Path.Combine(root, "_game_maps.txt")), template, acts[Math.Min(i, acts.Length - 1)])
                : MapLayout.Parse(File.ReadAllText(file));
            result.Add(new(i + 1, layout, isNative, isOverride));
        }
        return result;
    }
}
