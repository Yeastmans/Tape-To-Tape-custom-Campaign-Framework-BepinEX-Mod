using System;
using System.Collections.Generic;
using HarmonyLib;
using Rogue.Powerups.Repository;

namespace EndlessMode;

internal static class AbilityRewardFilter
{
    internal static void Install(Harmony harmony)
    {
        var method = AccessTools.Method(typeof(AbilityRepository), "GetWeightedRandomAbilities");
        if (method == null) throw new MissingMethodException("AbilityRepository.GetWeightedRandomAbilities");
        harmony.Patch(method, prefix: new HarmonyMethod(typeof(AbilityRewardFilter), nameof(Prefix)));
        Plugin.Log.LogInfo("[RewardPool] Ability exclusions linked to native eligible weighted pool");
    }

    // This native method receives the already category/level/mode-filtered pool.
    // Copy its input; never remove assets from the game's repository collections.
    internal static Il2CppSystem.Collections.Generic.List<Rogue.Ability> Filter(
        Il2CppSystem.Collections.Generic.IEnumerable<Rogue.Ability> source, int requested, HashSet<string> excluded)
    {
        var input = new System.Collections.Generic.List<Rogue.Ability>();
        var snapshot = new Il2CppSystem.Collections.Generic.List<Rogue.Ability>(source);
        for (int i = 0; i < snapshot.Count; i++) { var ability = snapshot[i]; if (ability != null) input.Add(ability); }
        int budget = Math.Max(0, input.Count - Math.Max(1, requested));
        int skipped = 0;
        var result = new Il2CppSystem.Collections.Generic.List<Rogue.Ability>();
        foreach (var ability in input)
        {
            if (excluded.Contains(ability.id ?? "") || excluded.Contains(ability.name ?? ""))
            {
                if (budget > 0) { budget--; continue; }
                skipped++;
            }
            result.Add(ability);
        }
        if (skipped > 0) Plugin.Log.LogWarning($"[RewardPool] {skipped} ability exclusions could not be honored: this category needs {requested} choices and has only {input.Count} candidates. Re-enable abilities in Reward Pools.");
        return result;
    }
    private static void Prefix(ref Il2CppSystem.Collections.Generic.IEnumerable<Rogue.Ability> abilityList, int numberOfAbilities)
    {
        if (abilityList == null || Plugin.ExcludedRewardAbilityIds.Count == 0) return;
        try { abilityList = Filter(abilityList, numberOfAbilities, Plugin.ExcludedRewardAbilityIds).Cast<Il2CppSystem.Collections.Generic.IEnumerable<Rogue.Ability>>(); }
        catch (Exception ex) { Plugin.Log.LogError($"[RewardPool] Ability exclusions failed; preserving native pool: {ex}"); }
    }
}
