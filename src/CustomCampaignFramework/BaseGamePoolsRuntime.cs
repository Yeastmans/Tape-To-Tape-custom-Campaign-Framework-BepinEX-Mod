using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CampaignCore;
using Data;
using HarmonyLib;
using Rogue.NPC.GeneralManagerNPC;
using State;
using Tape2Tape.Hockey.UI;
using Unlocks;
using UnityEngine;
using Pool = Il2CppSystem.Collections.Generic.List<Data.ForwardData>;
using Scope = EndlessMode.BaseGamePoolSelectionRuntime.Scope;

namespace EndlessMode;

// Explicitly installed only in Base Game mode. No HarmonyPatch attributes: the
// general custom-campaign PatchAll must never install these adapters.
internal static partial class BaseGamePoolsRuntime
{
    private sealed class CachedPool { internal string Revision; internal Pool Players; }
    private static readonly Dictionary<string, CachedPool> cache = new();
    private static readonly HashSet<string> authoredIds = new(StringComparer.Ordinal);
    [ThreadStatic] private static int consumerDepth;
    private static bool installed;
    private static string lastProfileError;

    internal static void Install()
    {
        if (installed || !Plugin.IsDefaultMode) return;
        var harmony = new Harmony("com.mods.customcampaign.basegamepools");
        try
        {
            var module = Process.GetCurrentProcess().Modules.Cast<ProcessModule>().First(m => m.ModuleName.Equals("GameAssembly.dll", StringComparison.OrdinalIgnoreCase));
            NativeCompatibility.Load(module.FileName);
            Patch(harmony, typeof(BenchwarmerMenu), "LoadCurrentTeam", nameof(BenchPrefix), nameof(Restore));
            Patch(harmony, typeof(CampaignState), "GetRandomUnlockedFreeAgent", nameof(RandomPrefix), nameof(Restore));
            Patch(harmony, typeof(CampaignState), "PreGenerateFreeAgents", nameof(PreGeneratePrefix), nameof(Restore));
            Patch(harmony, typeof(GeneralManagerEvent), "NextChoices", nameof(ManagerPrefix), nameof(Restore));
            Patch(harmony, typeof(OldSquadMenu), "GenerateSuperStarSkaters", nameof(StarsPrefix), nameof(Restore));
            harmony.Patch(AccessTools.Method(typeof(UnlockSystem), "IsUnlocked", new[] { typeof(string) }), prefix: new HarmonyMethod(typeof(BaseGamePoolsRuntime), nameof(UnlockPrefix)));
            harmony.Patch(AccessTools.Method(typeof(DeserializerV2), "DeserializeFromModel"),
                postfix: new HarmonyMethod(typeof(BaseGamePoolsRuntime), nameof(RestoreLoadedBenchwarmers)));
            InstallEmptyStars(harmony);
            installed = true;
            Plugin.Log.LogInfo("[BaseGamePools] Independent bench, recruitment and superstar adapters installed.");
        }
        catch (Exception ex)
        {
            harmony.UnpatchSelf();
            Plugin.Log.LogError("[BaseGamePools] Native compatibility or hook installation failed; pools remain native: " + ex);
        }
    }

    private static void Patch(Harmony harmony, Type type, string method, string prefix, string finalizer)
        => harmony.Patch(AccessTools.Method(type, method) ?? throw new MissingMethodException(type.FullName, method),
            prefix: new HarmonyMethod(typeof(BaseGamePoolsRuntime), prefix), finalizer: new HarmonyMethod(typeof(BaseGamePoolsRuntime), finalizer));

    internal static BaseGamePoolSelection Selection()
    {
        if (!Plugin.IsDefaultMode) return null;
        try
        {
            var selected = BaseGamePlayerPools.Load(Plugin.ModContentRoot);
            return BaseGamePoolSelectionRuntime.Applies(Plugin.IsDefaultMode, selected.Enabled) ? selected : null;
        }
        catch (Exception ex) { ReportUnavailable("settings", ex); return null; }
    }

    private static void ReportUnavailable(string kind, Exception error)
    {
        string message = "[BaseGamePools] Cannot load " + kind + ": " + error.Message
            + " Native pools remain active. Repair or disable Base Game Pools in Campaign Studio.";
        if (message == lastProfileError) return;
        lastProfileError = message;
        Plugin.Log.LogError(message);
    }

    private static Pool TryCandidates(CampaignState state, string kind, BaseGamePoolSelection selection)
    {
        // Prepare completely before opening any consumer scope. Invalid files,
        // missing assets and IO failures must not break the native menu flow.
        try { var result = Candidates(state, kind, selection); lastProfileError = null; return result; }
        catch (Exception ex) { ReportUnavailable(kind, ex); return null; }
    }

    internal static Pool Candidates(CampaignState state, string kind, BaseGamePoolSelection selected)
    {
        string[] files = BaseGamePlayerPools.SelectedFiles(Plugin.ModContentRoot, kind, selected);
        string revision = string.Join("|", files.Select(f => f + ":" + File.GetLastWriteTimeUtc(f).Ticks + ":" + new FileInfo(f).Length));
        if (cache.TryGetValue(kind, out var entry) && entry.Revision == revision)
        {
            // Scene/resource cleanup can destroy Unity objects while their
            // managed wrappers remain cached. Rebuild the same authored pool
            // instead of treating those dead templates as ineligible players.
            bool live = true;
            foreach (var player in entry.Players)
                if (player == null) { live = false; break; }
            if (live) return entry.Players;
        }
        var pool = new Pool();
        var newAuthoredIds = new List<string>();
        var sources = Resources.FindObjectsOfTypeAll<ForwardData>();
        foreach (string file in files)
        {
            var config = new PlayerConfig(); Plugin.LoadPlayerFile(file, config);
            bool native = config.Fields.Get(VanillaCopyData.PlayerId).Length > 0;
            var player = VanillaCampaignRuntime.CloneCandidate(config);
            if (player == null)
            {
                var prototype = sources.FirstOrDefault(p => p != null && !p.name.EndsWith("(Clone)", StringComparison.Ordinal)
                    && !p.name.StartsWith(BaseGamePlayerPools.CustomIdPrefix, StringComparison.Ordinal));
                if (prototype == null) throw new InvalidOperationException("No native forward prototype is loaded for Base Game pools.");
                player = UnityEngine.Object.Instantiate(prototype);
                player.id = BaseGamePlayerPools.CustomPlayerId(kind, Path.GetFileName(file));
            }
            player.name = BaseGamePlayerPools.CustomIdPrefix + kind + "_" + Path.GetFileNameWithoutExtension(file);
            if (!native)
            {
                // Imported files define their own powers. The arbitrary native
                // prototype supplies a valid skater object, never implicit
                // talents or an ability. ApplyFreshPlayerConfig then applies
                // any explicit authored powers through the existing resolver.
                player.powerups = new Il2CppSystem.Collections.Generic.List<Rogue.Talent>();
                player.ResetTalentCache();
                player.ability = null;
                // 26372689 adds a native-only exception used by one special
                // Prince asset. All 69 standard candidates default to false;
                // independently imported players must not inherit that flag.
                player.preventEliminatorRemoval = false;
            }
            CampaignSquadSetup.ApplyFreshPlayerConfig(player, config);
            // Native bench-only relics use this category, not roster position.
            // Native-bound imports retain their stock character classification.
            if (!native && kind == "draft_pool") player.skaterCategory = SkaterCategory.Benchwarmer;
            if (!native) newAuthoredIds.Add(player.id);
            pool.Add(player);
        }
        cache[kind] = new CachedPool { Revision = revision, Players = pool };
        foreach (string id in newAuthoredIds) authoredIds.Add(id);
        return pool;
    }

    internal static void RestoreLoadedBenchwarmers(Data.RunData __0)
    {
        if (!Plugin.IsDefaultMode || __0?.teamData == null) return;
        int repaired = 0;
        foreach (var player in __0.teamData.GetForwards(false))
        {
            // R34 saves retain both our stable custom ID and source-pool name.
            // Acquired bench players keep their role even if new offers are off.
            if (player == null || player.skaterCategory == SkaterCategory.Benchwarmer
                || player.id?.StartsWith(BaseGamePlayerPools.CustomIdPrefix, StringComparison.Ordinal) != true
                || player.name?.StartsWith(BaseGamePlayerPools.CustomIdPrefix + "draft_pool_", StringComparison.Ordinal) != true) continue;
            player.skaterCategory = SkaterCategory.Benchwarmer;
            repaired++;
        }
        if (repaired > 0) Plugin.Log.LogInfo($"[BaseGamePools] Restored the native benchwarmer category for {repaired} saved custom benchwarmer(s).");
    }

    private static Scope Begin()
    {
        var scope = new Scope();
        scope.Replace(() => consumerDepth, value => consumerDepth = value, consumerDepth + 1);
        return scope;
    }

    internal static void BenchPrefix(BenchwarmerMenu __instance, out Scope __state)
    {
        __state = null;
        var state = __instance?.m_CampaignState;
        var squad = state?.GetCurrentSquad();
        // All Angus owns its native Angus-variant candidates. The shared bench
        // profile must not replace that squad-specific selection.
        // Keep native no-bench teams, screens and capacity untouched.
        if (squad == null || squad.id == "allangus" || squad.m_MaxBenchwarmersOnBench <= 0) return;
        var selection = Selection(); if (selection == null) return;
        var pool = TryCandidates(state, "draft_pool", selection); if (pool == null) return;
        __state = Begin();
        __state.Replace(() => squad.m_CustomSwapPool, value => squad.m_CustomSwapPool = value, pool);
    }

    internal static void RandomPrefix(CampaignState __instance, out Scope __state)
    {
        __state = null;
        var selection = Selection(); if (selection == null || __instance == null) return;
        var pool = TryCandidates(__instance, "free_agents", selection); if (pool == null) return;
        __state = Begin();
        __state.Replace(() => __instance.freeAgents, value => __instance.freeAgents = value, pool);
    }

    internal static void PreGeneratePrefix(ref Pool __1, out Scope __state)
    {
        __state = null;
        var selection = Selection(); if (selection == null) return;
        var pool = TryCandidates(Tape2Tape.CampaignUtil.CampaignState, "free_agents", selection); if (pool == null) return;
        __state = Begin(); __1 = pool;
    }

    internal static List<ForwardData> Eligible(Pool source, CampaignState state, TeamData team, UnlockSystem unlocks,
        Func<string, bool> previouslyOffered = null)
    {
        var squad = state.GetCurrentSquad();
        var owned = team.GetForwards(false);
        var candidates = new List<ForwardData>();
        foreach (var candidate in source) candidates.Add(candidate);
        return BaseGamePoolSelectionRuntime.Eligible(candidates, player =>
        {
            if (player == null || player.firstName == "Gawain" && state.runData.CurrentAct != 1
                || previouslyOffered?.Invoke(player.FullName) == true
                || unlocks != null && !unlocks.IsUnlocked(player.id)
                || Tape2Tape.CampaignHelper.IsForwardExcludedBySquad(player, squad)) return false;
            foreach (var existing in owned)
                if (existing != null && (existing.id == player.id || existing.FullName == player.FullName)) return false;
            return true;
        });
    }

    internal static bool ManagerPrefix(GeneralManagerEvent __instance, int __0, ref bool __result, out Scope __state)
    {
        __state = null;
        var selection = Selection(); if (selection == null) return true;
        var state = Tape2Tape.CampaignUtil.CampaignState;
        var team = state?.runData?.teamData;
        var squad = state?.GetCurrentSquad();
        if (team == null || squad == null) return true;
        var pool = TryCandidates(state, "free_agents", selection); if (pool == null) return true;
        __state = Begin();
        __state.Replace(() => state.freeAgents, value => state.freeAgents = value, pool);
        __state.Replace(() => squad.m_CustomSwapPool, value => squad.m_CustomSwapPool = value, null);
        // Native capacity and completed-visit paths remain authoritative.
        if (__0 >= __instance.NumTimesToOfferChoices
            || team.NumPlayingForwards >= Tape2Tape.CampaignUtil.GetMaxActiveForwards(true) && !team.HasRoomOnBench()) return true;
        var eligible = Eligible(pool, state, team, __instance._unlockSystem ?? state._unlockSystem,
            name => __instance._previouslyOfferedNames.Contains(name));
        if (eligible.Count >= 2) return true;
        __instance.eventChoices.Clear(); __instance.descTermParams.Clear();
        __result = eligible.Count > 0;
        if (eligible.Count == 0) GeneralManagerEvent.RerollSkaterNodeRewards();
        else
        {
            __instance._previouslyOfferedNames.Add(eligible[0].FullName);
            __instance.CreatePlayerChoice(eligible[0], team);
        }
        return false;
    }

    internal static bool StarsPrefix(OldSquadMenu __instance, ref Pool __result, out Scope __state)
    {
        __state = null;
        var selection = Selection(); if (selection == null) return true;
        // The audited native generator reads the global CampaignState source,
        // while using the menu's team for roster exclusion and offer creation.
        var state = Tape2Tape.CampaignUtil.CampaignState;
        if (state?.GetCurrentSquad() == null || __instance.m_Team == null) return true;
        emptyStarMenus.Remove(__instance.Pointer);
        var pool = TryCandidates(state, "superstars", selection); if (pool == null) return true;
        __state = Begin();
        __state.Replace(() => state.freeAgents, value => state.freeAgents = value, pool);
        var eligible = Eligible(pool, state, __instance.m_Team, __instance.m_UnlockSystem);
        if (eligible.Count >= 3) return true;
        __result = new Pool();
        foreach (var player in BaseGamePoolSelectionRuntime.Sample(eligible, 3, max => UnityEngine.Random.Range(0, max)))
            __result.Add(__instance.CreatePlayerChoice(player, __instance.m_Team));
        if (__result.Count == 0) RememberEmptyStars(__instance, __result);
        return false;
    }

    internal static bool UnlockPrefix(string __0, ref bool __result)
    {
        if (!Plugin.IsDefaultMode || consumerDepth == 0 || __0 == null || !authoredIds.Contains(__0)) return true;
        __result = true; return false;
    }

    internal static Exception Restore(Scope __state, Exception __exception)
    {
        try { __state?.Dispose(); }
        catch (Exception ex) { Plugin.Log.LogError("[BaseGamePools] Source restoration failed: " + ex); return __exception ?? ex; }
        return __exception;
    }
}
