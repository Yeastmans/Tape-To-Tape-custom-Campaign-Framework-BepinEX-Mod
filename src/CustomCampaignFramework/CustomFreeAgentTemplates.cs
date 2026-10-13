using System;
using System.Collections.Generic;
using System.IO;
using CampaignCore;
using Data;
using HarmonyLib;
using State;

namespace EndlessMode;

// Native free-agent sources read CampaignState.freeAgents, the template list:
// GeneralManagerEvent.NextChoices (GM recruits; filtered by UnlockSystem.IsUnlocked(id),
// squad exclusions and names already on the team or offered), CampaignState.GetRandomUnlockedFreeAgent
// (TransformFacelessBenchwarmers turns faceless benchwarmers into free agents at run start) and
// PreGenerateFreeAgents. NextChoices prefers a nonempty squad CustomSwapPool, so its scoped
// prefix below separates authored starting-bench choices from in-run recruitment.
// The older mapping (PatchPreGenerateFreeAgents -> ApplyFreeAgentPool) never took
// effect because PreGenerateFreeAgents produced 0 entries, so campaign free agents were never seen.
// With a campaign free-agent pool the list is swapped for one clone per authored free agent (the bench's
// Instantiate + ApplyPlayerConfig path), each with its own CCF_FA_ id that PatchUnlockSystem treats as
// unlocked. The native list is put back whenever no campaign pool applies.
internal static class CustomFreeAgentTemplates
{
    internal const string IdPrefix = "CCF_FA_";
    private static CampaignState owner;
    private static Il2CppSystem.Collections.Generic.List<ForwardData> original, custom;
    private static string builtFor = "";
    internal static bool Active => custom != null && owner != null;
    internal static void Invalidate() => builtFor = "";
    internal static void Ensure(CampaignState state)
    {
        try
        {
            if (state == null) return;
            if (Plugin.IsDefaultMode || Plugin.FreeAgentPoolList.Count == 0) { Restore(); return; }
            string key = Plugin.ModFolder + "|" + Plugin.FreeAgentPoolList.Count;
            bool installed = owner != null && owner.Pointer == state.Pointer && custom != null
                && state.freeAgents != null && state.freeAgents.Pointer == custom.Pointer;
            if (installed && builtFor == key) return;
            if (!installed) { Restore(); owner = state; original = state.freeAgents; }
            if (original == null || original.Count == 0) { owner = null; original = null; return; }
            var list = new Il2CppSystem.Collections.Generic.List<ForwardData>();
            var names = new List<string>();
            int template = 0;
            for (int i = 0; i < Plugin.FreeAgentPoolList.Count; i++)
            {
                ForwardData source = null;
                for (int tries = 0; tries < original.Count && source == null; tries++) source = original[template++ % original.Count];
                if (source == null) break;
                var cfg = Plugin.FreeAgentPoolList[i];
                var clone = VanillaCampaignRuntime.CloneCandidate(cfg) ?? UnityEngine.Object.Instantiate(source);
                clone.name = IdPrefix + i;
                if(cfg.Fields.Get(VanillaCopyData.PlayerId).Length==0)clone.id = IdPrefix + i + "_" + (cfg.Name ?? "").Replace(' ', '_');
                CampaignSquadSetup.ApplyFreshPlayerConfig(clone, cfg);
                list.Add(clone); names.Add(cfg.Name);
            }
            custom = list; state.freeAgents = custom; builtFor = key;
            Plugin.Log.LogInfo($"[FreeAgents] Campaign free agents {string.Join(", ", names)} replace the {original.Count} native free-agent templates");
        }
        catch (Exception ex) { Plugin.Log.LogError("[FreeAgents] Could not install campaign free agents: " + ex); Restore(); }
    }
    internal static void Restore()
    {
        if (owner != null && original != null && custom != null && owner.freeAgents != null && owner.freeAgents.Pointer == custom.Pointer)
        {
            owner.freeAgents = original;
            Plugin.Log.LogInfo("[FreeAgents] Native free-agent templates restored");
        }
        owner = null; original = null; custom = null; builtFor = "";
    }
    internal static void EnsureCurrent()
    {
        CampaignState state = null;
        try { state = Tape2Tape.CampaignUtil.CampaignState; } catch (Exception) { }
        Ensure(state);
    }
}

[HarmonyPatch(typeof(CampaignState), "TransformFacelessBenchwarmers")]
internal static class FacelessFreeAgentTemplates
{
    [HarmonyPrefix] private static void Prefix(CampaignState __instance) => CustomFreeAgentTemplates.Ensure(__instance);
}

[HarmonyPatch(typeof(CampaignState), "GetRandomUnlockedFreeAgent")]
internal static class RandomFreeAgentTemplates
{
    [HarmonyPrefix] private static void Prefix(CampaignState __instance) => CustomFreeAgentTemplates.Ensure(__instance);
}

[HarmonyPatch(typeof(CampaignState), "LoadRunData")]
internal static class LoadedRunFreeAgentTemplates
{
    // A resumed run may look signed campaign free agents up by id before any other hook fires.
    [HarmonyPrefix] private static void Prefix(CampaignState __instance) => CustomFreeAgentTemplates.Ensure(__instance);
}

[HarmonyPatch(typeof(Rogue.NPC.GeneralManagerNPC.GeneralManagerEvent), "NextChoices")]
internal static class GeneralManagerFreeAgentTemplates
{
    internal sealed class SwapPoolState
    {
        internal RunSquadScriptableObject Squad;
        internal Il2CppSystem.Collections.Generic.List<ForwardData> Pool;
    }

    [HarmonyPrefix]
    private static void Prefix(out SwapPoolState __state)
    {
        __state = null;
        CustomFreeAgentTemplates.EnsureCurrent();
        if (Plugin.IsDefaultMode) return;
        try
        {
            var squad = Tape2Tape.CampaignUtil.CampaignState?.GetCurrentSquad();
            if (squad == null || !(squad.id ?? "").StartsWith("Custom_", StringComparison.Ordinal)) return;
            if(!CustomFreeAgentTemplates.Active && VanillaCampaignRuntime.InheritedPool("draft_pool"))return;
            // Native NextChoices uses CustomSwapPool before freeAgents. A saved
            // starting bench must never become the campaign's recruitment pool.
            if (!CustomFreeAgentTemplates.Active
                && SelectedPlayerPool.Read(Path.Combine(Plugin.ModFolder, "player_teams", "draft_pool")) == null) return;
            __state = new SwapPoolState { Squad = squad, Pool = squad.m_CustomSwapPool };
            squad.m_CustomSwapPool = null;
        }
        catch (Exception ex) { Plugin.Log.LogError("[FreeAgents] Could not isolate recruitment pool: " + ex); }
    }

    [HarmonyFinalizer]
    private static Exception Finalizer(SwapPoolState __state, Exception __exception)
    {
        if (__state?.Squad != null) __state.Squad.m_CustomSwapPool = __state.Pool;
        return __exception;
    }
}

[HarmonyPatch(typeof(CampaignState), "PreGenerateFreeAgents")]
internal static class PreGeneratedFreeAgentTemplates
{
    // (squad, freeAgents, output, unlockSystem): the caller passes CampaignState.freeAgents.
    [HarmonyPrefix]
    private static void Prefix(ref Il2CppSystem.Collections.Generic.List<ForwardData> __1)
    {
        CustomFreeAgentTemplates.EnsureCurrent();
        CampaignState state = null;
        try { state = Tape2Tape.CampaignUtil.CampaignState; } catch (Exception) { }
        if (CustomFreeAgentTemplates.Active && state?.freeAgents != null && __1 != null && __1.Pointer != state.freeAgents.Pointer) __1 = state.freeAgents;
    }
}
