using System;
using System.Collections.Generic;
using System.Linq;
using CampaignCore;

namespace EndlessMode;

internal static partial class CustomRewardRuntime
{
    private static Dictionary<string, CustomRewardDefinition> coachRewards = new(StringComparer.OrdinalIgnoreCase);
    private static string coachRewardsScope;

    internal static CustomRewardDefinition[] CoachRewardSnapshots() => !Plugin.IsDefaultMode && coachRewardsScope == DefinitionScope()
        ? coachRewards.Values.Select(d => d.Copy()).ToArray() : Array.Empty<CustomRewardDefinition>();

    internal static void ResetCoachRewards()
    {
        if (coachRewards.Count == 0) { coachRewardsScope = null; return; }
        foreach (string id in coachRewards.Keys) InvalidateCoachAsset(id);
        coachRewards.Clear(); coachRewardsScope = null; loaded = false; inPlayStale = true;
    }

    private static void SetCoachRewardSnapshots(Dictionary<string, CustomRewardDefinition> snapshots)
    {
        if (Plugin.IsDefaultMode || coachRewards.Count == 0 && snapshots.Count == 0) return;
        bool changed = coachRewardsScope != DefinitionScope() || coachRewards.Count != snapshots.Count
            || snapshots.Any(p => !coachRewards.TryGetValue(p.Key, out var old) || old.ToDocument().ToString() != p.Value.ToDocument().ToString());
        if (!changed) return;
        foreach (string id in coachRewards.Keys.Concat(snapshots.Keys).Distinct(StringComparer.OrdinalIgnoreCase)) InvalidateCoachAsset(id);
        coachRewards = snapshots; coachRewardsScope = DefinitionScope(); loaded = false; inPlayStale = true;
    }

    // Preserve acquired dependencies from earlier acts, including acts whose new
    // map has no custom Coach. The matching native map receipt carries the set.
    internal static Dictionary<string, CustomRewardDefinition> PrepareCoachRewards(MapLayout layout)
        => PrepareCoachRewards(layout, CoachRewardSnapshots());

    internal static void RestoreCoachRewards(MapLayout layout, IEnumerable<CustomRewardDefinition> retained)
        => CommitCoachRewards(PrepareCoachRewards(layout, retained));

    private static Dictionary<string, CustomRewardDefinition> PrepareCoachRewards(MapLayout layout, IEnumerable<CustomRewardDefinition> retained)
    {
        var snapshots = new Dictionary<string, CustomRewardDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in retained) {
            d.Validate();
            if (!d.IsRelic || d.NeedsRedesign) throw new ArgumentException("Unsupported saved Coach dependency: " + d.Name);
            CustomRewardExport.AddSnapshot(snapshots, d.Copy());
        }
        if (layout != null) foreach (var node in layout.Nodes.Where(n => n.Type == "Coach" && n.CustomCoach != null))
            CustomCoachPool.AddDependencies(snapshots, node.CustomCoach);
        return snapshots;
    }

    internal static void CommitCoachRewards(Dictionary<string, CustomRewardDefinition> snapshots)
    {
        SetCoachRewardSnapshots(snapshots);
        if (snapshots.Count > 0) EnsureAssets();
    }
}
