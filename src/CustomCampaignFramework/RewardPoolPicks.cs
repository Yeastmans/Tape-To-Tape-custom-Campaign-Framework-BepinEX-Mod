using System;
using System.Collections.Generic;
using System.IO;
using CampaignCore;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using Rogue;
using Rogue.Powerups.Repository;
using Rogue.Relics.Repository;

namespace EndlessMode;

// Build 25616594 (disassembled 2026-09-29): RelicRepository.GetRandomRelics, TalentRepository.GetRandomTalents
// and AbilityRepository.GetRandomAbilities pick with WeightedRandomSelector directly and never call the
// GetWeighted* helpers EligibleRewardFilter / AbilityRewardFilter hook, so reward_pools.txt had no effect
// (user: relic rewards, e.g. the Coach node, ignored the pool). The single-item GetRandomRelic calls
// GetRandomRelics. After the game picks, each excluded pick is swapped for an allowed reward from the same
// call's eligible pool: a second native call with the same category / pool / level arguments, excluding
// the current picks, so the game's own eligibility (category, in-pool, owned) still decides what is
// possible. The count never changes; an excluded pick stays only when nothing allowed is left ("restored
// only as needed", as the Studio's Reward pools page says). WeightedRandomSelector stops at the pool size,
// so the wide probe count only returns every eligible reward.
internal static class RewardPoolPicks
{
    internal const int ProbeCount = 500;
    [ThreadStatic] private static bool probing;
    [ThreadStatic] private static int selecting;
    internal static bool Probing => probing;
    internal static bool Selecting => selecting > 0;
    internal static T Probe<T>(Func<T> action)
    {
        bool before = probing; probing = true;
        try { return action(); } finally { probing = before; }
    }
    internal static void Begin() { Refresh(); selecting++; }
    internal static Exception End(Exception error) { selecting--; return error; }
    private static readonly HashSet<string> Warned = new(StringComparer.Ordinal);

    // Studio writes atomically. Reload the active campaign's saved choices before a
    // native picker runs, including when Studio saved while the game was open.
    internal static void Refresh()
    {
        if (Plugin.IsDefaultMode || probing) return;
        try
        {
            string path = Path.Combine(Plugin.ModFolder, "reward_pools.txt");
            var doc = new CampaignDocument(File.Exists(path) ? File.ReadAllText(path) : "");
            Read(Plugin.ExcludedRewardRelicIds, doc, "Excluded Relics", "Excluded Relic");
            Read(Plugin.ExcludedRewardTalentIds, doc, "Excluded Talents", "Excluded Talent", "Excluded Goalie Talents");
            Read(Plugin.ExcludedRewardAbilityIds, doc, "Excluded Abilities", "Excluded Goalie Abilities");
        }
        catch (Exception ex) { Plugin.Log.LogWarning("[RewardPool] Saved choices could not be refreshed: " + ex.Message); }
    }
    private static void Read(HashSet<string> target, CampaignDocument doc, params string[] keys)
    {
        target.Clear();
        foreach (string key in keys)
            foreach (string token in doc.Get(key).Split(','))
                if (!string.IsNullOrWhiteSpace(token)) target.Add(token.Trim());
    }

    internal static void Replace<T>(string kind, Il2CppSystem.Collections.Generic.List<T> picks, Il2CppSystem.Collections.Generic.List<T> alreadyExcluded,
        HashSet<string> excluded, Func<T, string> id, Func<T, string> name,
        Func<Il2CppSystem.Collections.Generic.List<T>, Il2CppSystem.Collections.Generic.List<T>> probe, Func<string, T> fetch,
        Func<T, T> nextLevel = null) where T : Il2CppObjectBase
    {
        if (probing || Plugin.IsDefaultMode || picks == null || picks.Count == 0 || excluded.Count == 0) return;
        bool IsExcluded(T item) => excluded.Contains(id(item) ?? "") || excluded.Contains(name(item) ?? "");
        var bad = new List<int>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < picks.Count; i++)
        {
            var pick = picks[i]; if (pick == null) continue;
            taken.Add(id(pick) ?? "");
            if (IsExcluded(pick)) bad.Add(i);
        }
        if (bad.Count == 0) return;
        var skip = new Il2CppSystem.Collections.Generic.List<T>();
        if (alreadyExcluded != null) for (int i = 0; i < alreadyExcluded.Count; i++) if (alreadyExcluded[i] != null) skip.Add(alreadyExcluded[i]);
        // Skip only retained picks. An excluded upgraded relic in this argument
        // also excludes its allowed base version inside the native repository.
        for (int i = 0; i < picks.Count; i++) if (picks[i] != null && !IsExcluded(picks[i])) skip.Add(picks[i]);
        Il2CppSystem.Collections.Generic.List<T> pool;
        probing = true;
        try { pool = probe(skip); }
        finally { probing = false; }
        int replaced = 0, next = 0;
        foreach (int index in bad)
        {
            T replacement = null;
            while (pool != null && next < pool.Count && replacement == null)
            {
                var candidate = pool[next++];
                // A checked upgrade remains usable when its base entry is unchecked.
                // Follow the same native type/level lookup used by TryUpgrade*.
                var variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                while (candidate != null && IsExcluded(candidate) && nextLevel != null && variants.Add(id(candidate) ?? ""))
                    candidate = nextLevel(candidate);
                if (candidate == null || IsExcluded(candidate) || !taken.Add(id(candidate) ?? "")) continue;
                replacement = fetch(id(candidate));
            }
            if (replacement == null) break;
            picks[index] = replacement; replaced++;
        }
        int restored = bad.Count - replaced;
        Plugin.Log.LogInfo($"[RewardPool] {kind} picks: replaced {replaced} excluded of {picks.Count}" + (restored > 0 ? $", kept {restored} (no allowed {kind} left in this category)" : ""));
        if (restored > 0 && Warned.Add(kind))
            Plugin.Log.LogWarning($"[RewardPool] Some {kind} screens draw from a category with too few allowed rewards; excluded ones fill the gap. Allow more in Reward pools if that matters.");
    }
}

// Retired: live selection is handled by CustomRewardPools.
internal static class RelicPoolPicks
{
    [HarmonyPrefix] private static void Prefix() => RewardPoolPicks.Begin();
    [HarmonyFinalizer] private static Exception Finalizer(Exception __exception) => RewardPoolPicks.End(__exception);
    // (relicCategory, excludedRelics, isClone, numberOfRelics, mustBeInPool, matchAnyCategory)
    [HarmonyPostfix]
    private static void Postfix(RelicRepository __instance, RelicCategory __0, Il2CppSystem.Collections.Generic.List<Relic> __1, bool __2, bool __4, bool __5,
        Il2CppSystem.Collections.Generic.List<Relic> __result)
    {
        try
        {
            RewardPoolPicks.Replace("relic", __result, __1, Plugin.ExcludedRewardRelicIds, r => r.id, r => r.name,
                skip => __instance.GetRandomRelics(__0, skip, false, RewardPoolPicks.ProbeCount, __4, __5), id => __instance.GetRelic(id, __2),
                r => __instance.GetRelic(r.GetIl2CppType(), r.level + 1, false));
        }
        catch (Exception ex) { Plugin.Log.LogError("[RewardPool] Relic picks: " + ex.Message); }
    }
}

// Retired: live selection is handled by CustomRewardPools.
internal static class TalentPoolPicks
{
    [HarmonyPrefix] private static void Prefix() => RewardPoolPicks.Begin();
    [HarmonyFinalizer] private static Exception Finalizer(Exception __exception) => RewardPoolPicks.End(__exception);
    // (talentCategory, excludedTalents, isClone, numberOfTalents, mustBeInPool, matchAnyCategory, forwardData)
    [HarmonyPostfix]
    private static void Postfix(TalentRepository __instance, TalentCategory __0, Il2CppSystem.Collections.Generic.List<Talent> __1, bool __2, bool __4, bool __5,
        Data.ForwardData __6, Il2CppSystem.Collections.Generic.List<Talent> __result)
    {
        try
        {
            RewardPoolPicks.Replace("talent", __result, __1, Plugin.ExcludedRewardTalentIds, t => t.id, t => t.name,
                skip => __instance.GetRandomTalents(__0, skip, false, RewardPoolPicks.ProbeCount, __4, __5, __6), id => __instance.GetTalent(id, __2),
                t => __instance.GetTalent(t.GetIl2CppType(), t.level + 1, false));
        }
        catch (Exception ex) { Plugin.Log.LogError("[RewardPool] Talent picks: " + ex.Message); }
    }
}

// Retired: live selection is handled by CustomRewardPools.
internal static class AbilityPoolPicks
{
    [HarmonyPrefix] private static void Prefix() => RewardPoolPicks.Begin();
    [HarmonyFinalizer] private static Exception Finalizer(Exception __exception) => RewardPoolPicks.End(__exception);
    // (TalentCategory, excludedAbilities, isClone, numberOfAbilities, mustBeInPool, matchAnyCategory, level)
    [HarmonyPostfix]
    private static void Postfix(AbilityRepository __instance, TalentCategory __0, Il2CppSystem.Collections.Generic.List<Ability> __1, bool __2, bool __4, bool __5,
        int __6, Il2CppSystem.Collections.Generic.List<Ability> __result)
    {
        try
        {
            RewardPoolPicks.Replace("ability", __result, __1, Plugin.ExcludedRewardAbilityIds, a => a.id, a => a.name,
                skip => __instance.GetRandomAbilities(__0, skip, false, RewardPoolPicks.ProbeCount, __4, __5, __6), id => __instance.GetAbility(id, __2));
        }
        catch (Exception ex) { Plugin.Log.LogError("[RewardPool] Ability picks: " + ex.Message); }
    }
}

// A replacement probe must see eligible base rewards without randomly upgrading
// them to a separately excluded entry. Ordinary rolls retain their upgrade chance,
// but an excluded upgrade falls back to its allowed original before being cloned.
// Retired: custom reward pools preserve exactly the selected levels.
internal static class RelicPoolUpgrade
{
    [HarmonyPrefix]
    private static bool Prefix(Relic __0, ref Relic __result)
    {
        if (!RewardPoolPicks.Probing) return true;
        __result = __0; return false;
    }
    [HarmonyPostfix]
    private static void Postfix(Relic __0, ref Relic __result)
    {
        if (Plugin.IsDefaultMode || !RewardPoolPicks.Selecting || __0 == null || __result == null) return;
        var excluded = Plugin.ExcludedRewardRelicIds;
        if ((excluded.Contains(__result.id ?? "") || excluded.Contains(__result.name ?? ""))
            && !excluded.Contains(__0.id ?? "") && !excluded.Contains(__0.name ?? "")) __result = __0;
    }
}

// Retired: custom reward pools preserve exactly the selected levels.
internal static class TalentPoolUpgrade
{
    [HarmonyPrefix]
    private static bool Prefix(Talent __0, ref Talent __result)
    {
        if (!RewardPoolPicks.Probing) return true;
        __result = __0; return false;
    }
    [HarmonyPostfix]
    private static void Postfix(Talent __0, ref Talent __result)
    {
        if (Plugin.IsDefaultMode || !RewardPoolPicks.Selecting || __0 == null || __result == null) return;
        var excluded = Plugin.ExcludedRewardTalentIds;
        if ((excluded.Contains(__result.id ?? "") || excluded.Contains(__result.name ?? ""))
            && !excluded.Contains(__0.id ?? "") && !excluded.Contains(__0.name ?? "")) __result = __0;
    }
}
