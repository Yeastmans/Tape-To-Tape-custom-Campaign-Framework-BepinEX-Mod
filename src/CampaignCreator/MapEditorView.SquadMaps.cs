using Avalonia.Controls;
using CampaignCore;

namespace CampaignCreator;

public sealed partial class MapEditorView
{
    private readonly string? squadDir;
    private string squadRevision = "";
    private bool SquadMode => squadDir != null;
    private readonly TextBlock squadStatus = new() { Name = "SquadMapStatus", Foreground = StudioTheme.Accent };

    private static bool IsSquadCustom(MapDraft draft) => draft.SquadOverride
        && (draft.SavedSquadOverride || draft.Layout!.Serialize() != draft.OpeningLayout);

    private void CaptureSquadBaselines()
    {
        if (!SquadMode) return;
        foreach (var draft in drafts)
        {
            draft.OpeningLayout = draft.Layout!.Serialize();
            draft.NeedsSpacingSave = false;
        }
        dirty = false;
    }

    private void RefreshSquadStatus()
    {
        if (SquadMode && currentStage < drafts.Count)
            squadStatus.Text = IsSquadCustom(drafts[currentStage]) ? "Custom for this squad" : "Following campaign";
    }

    private Dictionary<int, MapLayout> SquadChanges()
    {
        var result = new Dictionary<int, MapLayout>();
        foreach (var (draft, index) in drafts.Select((d, i) => (d, i)))
        {
            if (!IsSquadCustom(draft)) { if (draft.SavedSquadOverride) result[index + 1] = null!; }
            else if (!draft.SavedSquadOverride || draft.Layout!.Serialize() != draft.OpeningLayout) result[index + 1] = draft.Layout!;
        }
        return result;
    }

    private void ResetSquadMap()
    {
        if (!SquadMode || map == null) return;
        string file = System.IO.Path.Combine(campaignDir, "map_layouts", $"map-{currentStage + 1}.txt");
        var inherited = File.Exists(file) ? MapLayout.Parse(File.ReadAllText(file)) : MapCatalog.TemplateMap(catalog, selectedTemplate(), drafts[currentStage].Act);
        MapCatalog.HydrateNativePools(inherited, catalog);
        if (!File.Exists(file)) inherited.VanillaRandom = true;
        if (inherited.LookAct == 0) inherited.LookAct = drafts[currentStage].Act;
        Checkpoint();
        var draft = drafts[currentStage]; draft.Layout = inherited; draft.SquadOverride = false; draft.Inherited = !File.Exists(file);
        NormalizeCampaign(); LoadStage(currentStage);
        message.Text = "Following the campaign again. Save team maps to keep the reset; Undo restores your previous map.";
    }

    private bool TrySaveSquadMaps(out string error)
    {
        error = "";
        try
        {
            if (sequenceError != null) throw new InvalidOperationException(sequenceError);
            // Check before validation or dependency writes, including no-op saves.
            SquadMapOverrides.Save(campaignDir, squadDir!, squadRevision, new Dictionary<int, MapLayout>());
            var changes = SquadChanges();
            if (changes.Count == 0) { message.Text = "Team maps are up to date."; return true; }
            for (int i = 0; i < drafts.Count; i++)
            {
                var errors = ValidateLayout(drafts[i].Layout!, i + 1);
                if (errors.Count > 0) throw new ArgumentException($"Map {i + 1} not saved: " + string.Join("  ", errors.Take(4)));
            }
            string? reserve = RewardValidation?.Invoke(RewardLayouts)
                ?? string.Join("\n", CampaignRewardBudget.Calculate(contentRoot, campaignDir, layouts: RewardLayouts,
                    mapSquadDirectory: squadDir, nativeInheritedMaps: NativeInheritedMaps, validationSquadDirectory: squadDir).SelectMany(b => b.Errors));
            if (!string.IsNullOrWhiteSpace(reserve)) throw new ArgumentException(reserve);
            CustomCoachCatalog.SaveDependencies(contentRoot, campaignDir, RewardLayouts);
            SquadMapOverrides.Save(campaignDir, squadDir!, squadRevision, changes);
            foreach (var draft in drafts) draft.SquadOverride = draft.SavedSquadOverride = IsSquadCustom(draft);
            CaptureSquadBaselines(); squadRevision = SquadMapOverrides.Revision(campaignDir, squadDir!);
            RefreshSquadStatus(); message.Text = "Team maps saved. Changes apply when a new map is generated.";
            return true;
        }
        catch (Exception ex) { error = ex.Message; message.Text = error; return false; }
    }
}
