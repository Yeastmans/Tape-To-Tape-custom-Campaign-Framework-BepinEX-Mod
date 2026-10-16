using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CampaignCore;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using Rogue;
using Rogue.Powerups.Repository;
using Rogue.Relics.Repository;

namespace EndlessMode;

// A custom pool is a complete allow-list. Do not supplement it with native
// candidates or roll an upgrade to an unchecked entry. Native reward consumers
// retain their ownership/upgrade application and weighted choice logic.
internal static class CustomRewardPools
{
    private static readonly Dictionary<string, string[]> normal = LoadNormal();
    [ThreadStatic] private static HashSet<string> weightedSelections;
    private static Dictionary<string, string[]> LoadNormal()
    {
        using var stream = typeof(CustomRewardPools).Assembly.GetManifestResourceStream("NormalRewardPools.tsv");
        using var reader = new StreamReader(stream);
        var result = new Dictionary<string, string[]>();
        var rows = reader.ReadToEnd().Split('\n').Select(line => line.TrimEnd('\r').Split('\t')).ToArray();
        foreach (string kind in new[] { "Relics", "Talents", "Chaos Talents", "Goalie Talents", "Abilities", "Goalie Abilities" })
            result[kind] = rows.Where(p => p.Length >= 5 && p[0] == kind && p[4] == "yes").Select(p => p[1]).Distinct().ToArray();
        return result;
    }
    internal static RewardPoolDefinition Read(string kind)
    {
        if (Plugin.IsDefaultMode || RewardPoolPicks.Probing) return null;
        string file = Path.Combine(Plugin.ModFolder, "reward_pools.txt");
        if (!File.Exists(file)) return null;
        try
        {
            var document = new CampaignDocument(File.ReadAllText(file));
            var defaults = normal.TryGetValue(kind, out var ids) ? ids : Array.Empty<string>();
            var pool = RewardPoolDefinition.Read(document, kind, defaults);
            string customKind = CustomRewardPoolSelection.ForNative(kind);
            if (customKind.Length > 0) {
                CustomRewardRuntime.EnsureAssets();
                var custom = CustomRewardPoolSelection.Read(document, customKind, CustomRewardRuntime.Definitions.Values);
                pool = CustomRewardPoolSelection.Effective(kind, pool, custom, defaults, CustomRewardRuntime.Definitions.Keys);
            }
            CustomRewardRuntime.EnsureAssets();
            pool.Selected.RemoveWhere(id => CustomRewardRuntime.Definitions.TryGetValue(id, out var item)
                && (item.NeedsRedesign || item.NativePool != kind));
            return pool.UseBaseGame ? null : pool;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError("[RewardPool] Cannot read selected pool; blocking native fallback: " + ex);
            return new RewardPoolDefinition { UseBaseGame = false };
        }
    }
    internal static IEnumerable<T> Assets<T>(IEnumerable<string> selected, Func<T, string> id) where T : UnityEngine.Object
    {
        CustomRewardRuntime.EnsureAssets();
        var assets = UnityEngine.Resources.FindObjectsOfTypeAll<T>();
        foreach (string key in selected)
        {
            T match = assets.FirstOrDefault(a => a != null && id(a) == key && !a.name.EndsWith("(Clone)"));
            if (match != null) yield return match;
        }
    }
    internal static Il2CppSystem.Collections.Generic.List<T> Select<T>(string kind, int requested, IEnumerable<T> selected,
        Il2CppSystem.Collections.Generic.List<T> callerExcluded, Func<T, string> id, Func<T, T> baseLevel,
        Func<Il2CppSystem.Collections.Generic.List<T>, int, Il2CppSystem.Collections.Generic.List<T>> choose,
        Func<T, T> clone) where T : Il2CppObjectBase
    {
        var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (callerExcluded != null) foreach (var item in callerExcluded) if (item != null)
        {
            skip.Add(id(item));
            var lower = baseLevel(item);
            if (lower != null) skip.Add(id(lower));
        }
        var input = new Il2CppSystem.Collections.Generic.List<T>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in selected)
            if (item != null && !skip.Contains(id(item)) && seen.Add(id(item))) input.Add(item);
        var result = new Il2CppSystem.Collections.Generic.List<T>();
        if (requested <= 0 || input.Count == 0) return result;
        var previous = weightedSelections; weightedSelections = seen;
        try
        {
            var picks = choose(input, Math.Min(requested, input.Count));
            var offered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (picks != null) foreach (var item in picks) if (item != null && seen.Contains(id(item)) && offered.Add(id(item))) result.Add(clone(item));
        }
        finally { weightedSelections = previous; }
        Plugin.Log.LogInfo($"[RewardPool] Custom {kind}: {input.Count} eligible selected entries, {result.Count}/{requested} choices; no base-game fallback");
        return result;
    }
    internal static void EnsureWeight(string id, ref float weight)
    {
        if (FreeAgentTalentWeightRuntime.ReadingNativeWeights) return;
        // Special assets can have a zero normal reward weight. Explicitly
        // selecting one makes it eligible, without editing the shared asset.
        if (weightedSelections?.Contains(id ?? "") == true && (weight <= 0 || float.IsNaN(weight))) weight = 1;
    }
    internal static Il2CppSystem.Collections.Generic.List<Relic> PickRelics(Il2CppSystem.Collections.Generic.List<Relic> pool, int count)
        => (Il2CppSystem.Collections.Generic.List<Relic>)AccessTools.Method(typeof(RelicRepository), "GetWeightedRandomRelics")
            .Invoke(null, new object[] { pool.Cast<Il2CppSystem.Collections.Generic.IEnumerable<Relic>>(), count });
    internal static Il2CppSystem.Collections.Generic.List<Talent> PickTalents(Il2CppSystem.Collections.Generic.List<Talent> pool, int count, Data.ForwardData forward)
        => (Il2CppSystem.Collections.Generic.List<Talent>)AccessTools.Method(typeof(TalentRepository), "GetWeightedRandomTalents")
            .Invoke(null, new object[] { pool.Cast<Il2CppSystem.Collections.Generic.IEnumerable<Talent>>(), count, forward });
    internal static Il2CppSystem.Collections.Generic.List<Ability> PickAbilities(Il2CppSystem.Collections.Generic.List<Ability> pool, int count)
        => (Il2CppSystem.Collections.Generic.List<Ability>)AccessTools.Method(typeof(AbilityRepository), "GetWeightedRandomAbilities")
            .Invoke(null, new object[] { pool.Cast<Il2CppSystem.Collections.Generic.IEnumerable<Ability>>(), count });
    internal static Il2CppSystem.Collections.Generic.List<Ability> SelectAbilities(RewardPoolDefinition pool, bool goalie,
        int requested, Il2CppSystem.Collections.Generic.List<Ability> excluded, bool clone)
        => Select("ability", requested,
            Assets<Ability>(pool.Selected, a => a.id).Where(a => ((a.category & TalentCategory.Goalie) != 0) == goalie), excluded,
            a => a.id, a => a, PickAbilities, a => clone ? UnityEngine.Object.Instantiate(a) : a);
}

[HarmonyPatch(typeof(RelicRepository), nameof(RelicRepository.GetRandomRelics))]
internal static class CustomRelicPool
{
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static bool Prefix(RelicRepository __instance, RelicCategory __0, Il2CppSystem.Collections.Generic.List<Relic> __1,
        bool __2, int __3, ref Il2CppSystem.Collections.Generic.List<Relic> __result)
    {
        CustomRewardRuntime.EnsureAssets();
        string kind = (__0 & RelicCategory.Chaos) != 0 ? "Chaos Relics" : "Relics";
        var pool = CustomRewardPools.Read(kind); if (pool == null) return true;
        // An invalid custom file must never silently fall back to native picks.
        __result = new Il2CppSystem.Collections.Generic.List<Relic>();
        try { __result = CustomRewardPools.Select("relic", __3, pool.Selected.Select(id => PatchBossLaunchMatch.FindRelic(id))
            .Where(r => r != null && ((__0 & RelicCategory.OnlyStats) == 0 || (r.category & RelicCategory.OnlyStats) != 0)), __1,
            r => r.id, r => r.level > 1 ? __instance.GetRelic(r.GetIl2CppType(), 1, false) : r, CustomRewardPools.PickRelics, r => __2 ? UnityEngine.Object.Instantiate(r) : r); }
        catch (Exception ex) { Plugin.Log.LogError("[RewardPool] Custom relic selection failed: " + ex); }
        return false;
    }
}
[HarmonyPatch(typeof(TalentRepository), nameof(TalentRepository.GetRandomTalents))]
internal static class CustomTalentPool
{
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static bool Prefix(TalentRepository __instance, TalentCategory __0, Il2CppSystem.Collections.Generic.List<Talent> __1,
        bool __2, int __3, bool __4, bool __5, Data.ForwardData __6, ref Il2CppSystem.Collections.Generic.List<Talent> __result)
    {
        if (FreeAgentTalentWeightRuntime.TrySelect(__instance, __0, __1, __2, __3, __4, __5, __6, out var weighted))
        { __result = weighted; return false; }
        if (FreeAgentTalentWeightRuntime.UseNativePool(__0)) return true;
        bool goalie = (__0 & (TalentCategory.Goalie | TalentCategory.UsedInCampaignGoaliePool)) != 0;
        // Wizard requests Chaos through this same repository. Its pool has an
        // independent Base Game/Custom choice; ordinary talent selections must
        // never replace the native Chaos picker when that choice is Base Game.
        string kind = (__0 & TalentCategory.Chaos) != 0 ? "Chaos Talents" : goalie ? "Goalie Talents" : "Talents";
        var pool = CustomRewardPools.Read(kind); if (pool == null) return true;
        __result = new Il2CppSystem.Collections.Generic.List<Talent>();
        try { __result = CustomRewardPools.Select(kind, __3,
            CustomRewardPools.Assets<Talent>(pool.Selected, t => t.id)
                .Where(t => ((t.category & TalentCategory.Goalie) != 0) == goalie && ((__0 & TalentCategory.OnlyStats) == 0 || (t.category & TalentCategory.OnlyStats) != 0)), __1,
            t => t.id, t => t.level > 1 ? __instance.GetTalent(t.GetIl2CppType(), 1, false) : t, (list, count) => CustomRewardPools.PickTalents(list, count, __6), t => __2 ? UnityEngine.Object.Instantiate(t) : t); }
        catch (Exception ex) { Plugin.Log.LogError("[RewardPool] Custom talent selection failed: " + ex); }
        return false;
    }
}
[HarmonyPatch(typeof(AbilityRepository), nameof(AbilityRepository.GetRandomAbilities))]
internal static class CustomAbilityPool
{
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static bool Prefix(AbilityRepository __instance, TalentCategory __0, Il2CppSystem.Collections.Generic.List<Ability> __1,
        bool __2, int __3, ref Il2CppSystem.Collections.Generic.List<Ability> __result)
    {
        bool goalie = (__0 & TalentCategory.Goalie) != 0;
        var pool = CustomRewardPools.Read(goalie ? "Goalie Abilities" : "Abilities"); if (pool == null) return true;
        __result = new Il2CppSystem.Collections.Generic.List<Ability>();
        try { __result = CustomRewardPools.SelectAbilities(pool, goalie, __3, __1, __2); }
        catch (Exception ex) { Plugin.Log.LogError("[RewardPool] Custom ability selection failed: " + ex); }
        return false;
    }
}
[HarmonyPatch(typeof(Relic), nameof(Relic.GetRewardWeight))]
internal static class CustomRelicWeight
{
    [HarmonyPostfix] private static void Postfix(Relic __instance, ref float __result) => CustomRewardPools.EnsureWeight(__instance.id, ref __result);
}
[HarmonyPatch(typeof(PowerUp), nameof(PowerUp.GetRewardWeight))]
internal static class CustomPowerWeight
{
    [HarmonyPostfix] private static void Postfix(PowerUp __instance, ref float __result) => CustomRewardPools.EnsureWeight(__instance.id, ref __result);
}

// Coach uses dedicated pickers for these presets rather than GetRandomRelics.
[HarmonyPatch(typeof(Rogue.NPC.CoachNPC.CoachEvent), "GetStatRewardRelics")]
internal static class CustomStatCoachPool
{
    [HarmonyPrefix]
    private static bool Prefix(IRelicRepository __0, Il2CppSystem.Collections.Generic.List<Relic> __1, ref Il2CppSystem.Collections.Generic.List<Relic> __result)
    {
        var pool = CustomRewardPools.Read("Relics"); if (pool == null) return true;
        __result = new Il2CppSystem.Collections.Generic.List<Relic>();
        try { __result = CustomRewardPools.Select("Coach stat relic", 3,
            CustomRewardPools.Assets<Relic>(pool.Selected, r => r.id).Where(r => (r.category & RelicCategory.OnlyStats) != 0), __1,
            r => r.id, r => r.level > 1 ? __0.GetRelic(r.GetIl2CppType(), 1, false) : r, CustomRewardPools.PickRelics, r => UnityEngine.Object.Instantiate(r)); }
        catch (Exception ex) { Plugin.Log.LogError("[RewardPool] Custom Coach stat selection failed: " + ex); }
        return false;
    }
}
[HarmonyPatch(typeof(Rogue.NPC.CoachNPC.CoachEvent), "GetSpeedrunStarterRelics")]
internal static class CustomSpeedrunCoachPool
{
    [HarmonyPrefix]
    private static bool Prefix(ref Il2CppSystem.Collections.Generic.List<Relic> __result)
    {
        var pool = CustomRewardPools.Read("Relics"); if (pool == null) return true;
        __result = new Il2CppSystem.Collections.Generic.List<Relic>();
        var owned = Tape2Tape.CampaignUtil.CampaignState?.runData?.teamData?.relics;
        try { __result = CustomRewardPools.Select("Speedrun Coach relic", 3, CustomRewardPools.Assets<Relic>(pool.Selected, r => r.id), owned,
            r => r.id, r => r, CustomRewardPools.PickRelics, r => UnityEngine.Object.Instantiate(r)); }
        catch (Exception ex) { Plugin.Log.LogError("[RewardPool] Custom Speedrun Coach selection failed: " + ex); }
        return false;
    }
}

[HarmonyPatch(typeof(RogueUtils), nameof(RogueUtils.GetRandomTalentRewards))]
internal static class CustomCombinedTalentOffer
{
    // Stat Reward Relic rolls its extra card separately from the main offer.
    [HarmonyPostfix]
    private static void Postfix(SkaterTalentRewards __result)
    {
        if (__result?.Talents == null || CustomRewardPools.Read("Talents") == null) return;
        var seen = new HashSet<string>();
        var unique = new Il2CppSystem.Collections.Generic.List<Talent>();
        int count = __result.Talents.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<Talent>>().Count;
        for (int i = 0; i < count; i++) { var talent = __result.Talents[i]; if (talent != null && seen.Add(talent.id)) unique.Add(talent); }
        __result.Talents = unique.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<Talent>>();
    }
}
