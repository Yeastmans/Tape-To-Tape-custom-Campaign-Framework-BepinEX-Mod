using System;
using System.Collections.Generic;
using System.Linq;

namespace CampaignCore;

// Custom item selection is independent of the native category allow-lists.
public static class CustomRewardPoolSelection
{
    public static string ForNative(string kind) => kind == "Relics" ? CustomRewardDefinition.CustomRelics
        : kind == "Talents" ? CustomRewardDefinition.CustomTalents
        : kind == RewardPoolDefinition.ChaosTalents ? CustomRewardDefinition.CustomChaosTalents
        : kind == "Goalie Talents" ? CustomRewardDefinition.CustomGoalieTalents : "";

    public static RewardPoolDefinition Read(CampaignDocument document, string customKind, IEnumerable<CustomRewardDefinition> rewards)
    {
        var pool = RewardPoolDefinition.Read(document, customKind, Array.Empty<string>());
        var ids = rewards.Where(r => r.Kind == customKind && !r.NeedsRedesign).Select(r => r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (document.Get("Pool Mode " + customKind).Length > 0) { pool.Selected.IntersectWith(ids); return pool; }
        // Migrate the previous preview's enabled entries without inventing new IDs.
        foreach (string kind in RewardPoolDefinition.Kinds.Where(k => ForNative(k) == customKind || customKind == CustomRewardDefinition.CustomRelics && k == RewardPoolDefinition.ChaosRelics)) {
            var old = RewardPoolDefinition.Read(document, kind, Array.Empty<string>());
            if (!old.UseBaseGame) pool.Selected.UnionWith(old.Selected.Where(ids.Contains));
        }
        pool.UseBaseGame = pool.Selected.Count == 0;
        return pool;
    }

    public static RewardPoolDefinition Effective(string kind, RewardPoolDefinition native, RewardPoolDefinition custom,
        IEnumerable<string> defaults, IEnumerable<string> customIds)
    {
        var result = new RewardPoolDefinition { UseBaseGame = native.UseBaseGame, IncludeOtherCategory = native.IncludeOtherCategory };
        result.Selected.UnionWith(native.Selected);
        if (custom == null || ForNative(kind).Length == 0) return result;
        result.Selected.ExceptWith(customIds);
        if (native.UseBaseGame) return result;
        if (custom.UseBaseGame || custom.Selected.Count == 0) return result;
        result.UseBaseGame = false;
        result.Selected.UnionWith(custom.Selected);
        return result;
    }
}
