using System;
using System.Collections.Generic;
using CampaignCore;
using HarmonyLib;
using Rogue;
using Rogue.Powerups.Repository;
using Rogue.Relics.Repository;

namespace EndlessMode;

internal static class EligibleRewardFilter
{
    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(RelicRepository), "GetWeightedRandomRelics")
            ?? throw new MissingMethodException("GetWeightedRandomRelics"),
            prefix: new HarmonyMethod(typeof(EligibleRewardFilter), nameof(Relics)));
        harmony.Patch(AccessTools.Method(typeof(TalentRepository), "GetWeightedRandomTalents")
            ?? throw new MissingMethodException("GetWeightedRandomTalents"),
            prefix: new HarmonyMethod(typeof(EligibleRewardFilter), nameof(Talents)));
        Plugin.Log.LogInfo("[RewardPool] Relic and skater/goalie talent exclusions linked to native eligible weighted pools");
    }
    private static void Relics(ref Il2CppSystem.Collections.Generic.IEnumerable<Relic> relicList, int numberOfRelics)
    {
        if (Plugin.IsDefaultMode || relicList == null || Plugin.ExcludedRewardRelicIds.Count == 0) return;
        try
        {
            var input = new Il2CppSystem.Collections.Generic.List<Relic>(relicList);
            var copy = new List<Relic>();
            for (int i = 0; i < input.Count; i++) if (input[i] != null) copy.Add(input[i]);
            var filtered = RewardPoolSelection.Filter(copy, numberOfRelics,
                r => Plugin.ExcludedRewardRelicIds.Contains(r.id ?? "") || Plugin.ExcludedRewardRelicIds.Contains(r.name ?? ""), out int restored);
            var result = new Il2CppSystem.Collections.Generic.List<Relic>();
            foreach (var item in filtered) result.Add(item);
            relicList = result.Cast<Il2CppSystem.Collections.Generic.IEnumerable<Relic>>();
            Report("relic", input.Count, result.Count, restored);
        }
        catch (Exception ex) { Plugin.Log.LogError("[RewardPool] Relic filter failed: " + ex); }
    }
    private static void Talents(ref Il2CppSystem.Collections.Generic.IEnumerable<Talent> powerList, int numberOfPowers)
    {
        if (Plugin.IsDefaultMode || powerList == null || Plugin.ExcludedRewardTalentIds.Count == 0) return;
        try
        {
            var input = new Il2CppSystem.Collections.Generic.List<Talent>(powerList);
            var copy = new List<Talent>();
            for (int i = 0; i < input.Count; i++) if (input[i] != null) copy.Add(input[i]);
            var filtered = RewardPoolSelection.Filter(copy, numberOfPowers,
                t => Plugin.ExcludedRewardTalentIds.Contains(t.id ?? "") || Plugin.ExcludedRewardTalentIds.Contains(t.name ?? ""), out int restored);
            var result = new Il2CppSystem.Collections.Generic.List<Talent>();
            foreach (var item in filtered) result.Add(item);
            powerList = result.Cast<Il2CppSystem.Collections.Generic.IEnumerable<Talent>>();
            Report("talent", input.Count, result.Count, restored);
        }
        catch (Exception ex) { Plugin.Log.LogError("[RewardPool] Talent filter failed: " + ex); }
    }
    private static void Report(string kind, int before, int after, int restored)
    {
        Plugin.Log.LogInfo($"[RewardPool] Eligible {kind} pool {before} -> {after}");
        if (restored > 0) Plugin.Log.LogWarning($"[RewardPool] Restored {restored} excluded {kind} choices because this category has too few allowed rewards. Re-enable more rewards in Reward Pools.");
    }
}
