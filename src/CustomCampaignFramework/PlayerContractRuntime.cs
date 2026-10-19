using System;
using System.Collections.Generic;
using System.IO;
using CampaignCore;
using Data;
using HarmonyLib;
using Rogue.FreeAgents;
using Rogue.NPC.GeneralManagerNPC;
using State;
using STS.Map;
using Tape2Tape;
using Conditions = Il2CppSystem.Collections.Generic.List<Rogue.FreeAgents.LeavingCondition>;

namespace EndlessMode;

// Authoring belongs to templates. Signed contracts belong exclusively to native
// RunData.freeAgents, including their played-match counter and save serialization.
internal static class PlayerContractRuntime
{
    private sealed class Binding
    {
        internal ForwardData Player;
        internal int Matches;
    }
    private static readonly Dictionary<IntPtr, Binding> bindings = new();
    private static readonly Dictionary<IntPtr, Dictionary<string, Binding>> startingCandidates = new();
    private static string campaign = "";
    private static CampaignState freshState;
    private static RunData freshRun;
    private static bool Context()
    {
        if (Plugin.IsDefaultMode || string.IsNullOrWhiteSpace(Plugin.ModFolder)) return false;
        string folder = Path.GetFullPath(Plugin.ModFolder);
        if (!string.Equals(campaign, folder, StringComparison.OrdinalIgnoreCase))
        {
            bindings.Clear(); startingCandidates.Clear(); CancelStarting(); campaign = folder;
        }
        return true;
    }
    internal static void Bind(ForwardData player, PlayerConfig config)
    {
        if (player == null || config == null || !Context()) return;
        int? duration = PlayerContractOptions.Duration(config.Fields);
        if (duration is int matches) bindings[player.Pointer] = new Binding { Player = player, Matches = matches };
        else bindings.Remove(player.Pointer);
        if (PlayerContractOptions.Enabled(config.Fields) && duration == null)
            Plugin.Log.LogWarning("[PlayerContracts] Invalid duration for " + config.Name + "; normal contract rules retained.");
    }
    private static Binding Bound(ForwardData player) => player != null && bindings.TryGetValue(player.Pointer, out var value)
        && value.Player != null ? value : null;

    // Native starting-team Instantiate preserves skater IDs. Match only within
    // the selected squad's exact source roster, never another team's same ID/name.
    internal static void StartingTeam(RunSquadScriptableObject squad, TeamData team)
    {
        if (!Context() || squad?.startingTeam?.forwards == null || team?.forwards == null) return;
        foreach (var source in squad.startingTeam.forwards)
        {
            var binding = Bound(source);
            if (binding == null) continue;
            foreach (var player in team.forwards)
                if (player != null && player.id == source.id)
                    bindings[player.Pointer] = new Binding { Player = player, Matches = binding.Matches };
        }
    }
    internal static void StartingCandidate(RunSquadScriptableObject squad, ForwardData player)
    {
        if (!Context() || squad == null || player == null || string.IsNullOrEmpty(player.id)) return;
        if (!startingCandidates.TryGetValue(squad.Pointer, out var candidates))
            startingCandidates[squad.Pointer] = candidates = new Dictionary<string, Binding>(StringComparer.Ordinal);
        var binding = Bound(player);
        if (binding == null) candidates.Remove(player.id); else candidates[player.id] = binding;
    }
    internal static void BeginStarting(CampaignState state)
    {
        CancelStarting();
        if (!Context() || state?.runData?.teamData == null) return;
        freshState = state; freshRun = state.runData;
    }
    internal static void CancelStarting() { freshState = null; freshRun = null; }
    internal static void CompleteStarting(CampaignState state)
    {
        if (!Context() || state == null || state.Pointer != freshState?.Pointer
            || state.runData == null || state.runData.Pointer != freshRun?.Pointer) return;
        // Consume the fresh-run boundary once, even if native code calls the
        // effects helper again. Continue never arms this boundary.
        CancelStarting();
        var run = state.runData;
        var squad = state.GetCurrentSquad();
        Dictionary<string, Binding> candidates = null;
        if (squad != null) startingCandidates.TryGetValue(squad.Pointer, out candidates);
        foreach (var player in run.teamData.GetForwards())
        {
            var binding = Bound(player);
            if (binding == null && player != null && candidates != null) candidates.TryGetValue(player.id ?? "", out binding);
            if (binding != null) Sign(run, player, NewConditions(binding.Matches));
        }
    }
    private static Conditions NewConditions(int matches)
    {
        var conditions = new Conditions();
        conditions.Add(new MaxGamesCondition { maxMatches = matches, matches = 0 });
        return conditions;
    }
    private static void Sign(RunData run, ForwardData player, Conditions conditions)
    {
        if (player == null || string.IsNullOrEmpty(player.id)) return;
        run.freeAgents ??= new Il2CppSystem.Collections.Generic.List<FreeAgent>();
        // Replace the native GM/random conditions only for this opted-in copy.
        // Off never reaches this path, and existing signed players are not read
        // from authoring settings during load, team initialization or match end.
        for (int i = run.freeAgents.Count - 1; i >= 0; i--)
            if (run.freeAgents[i]?.id == player.id) run.freeAgents.RemoveAt(i);
        run.freeAgents.Add(new FreeAgent { id = player.id, leavingConditions = conditions });
        Plugin.Log.LogInfo("[PlayerContracts] Signed " + player.id + " with a custom played-match contract.");
    }
    private static bool Owns(TeamData team, ForwardData player, bool exact)
    {
        foreach (var owned in team.GetForwards())
            if (owned != null && (exact ? owned.Pointer == player.Pointer : owned.id == player.id)) return true;
        return false;
    }
    internal static void ConfigureOffer(GeneralManagerEvent node, ForwardData template, TeamData team, int index)
    {
        if (!Context()) return;
        var binding = Bound(template);
        var state = CampaignUtil.CampaignState;
        var run = state?.runData;
        if (binding == null || run?.teamData == null || team?.Pointer != run.teamData.Pointer
            || node.eventChoices == null || index < 0 || index >= node.eventChoices.Count) return;
        var card = node.eventChoices[index];
        var player = card?.Skater?.TryCast<ForwardData>();
        var native = card?.onSelection;
        if (player == null || player.Pointer == template.Pointer || native == null) return;
        var conditions = NewConditions(binding.Matches);
        string ownerCampaign = campaign;
        bool completed = false, selecting = false;
        bool Current() => Context() && string.Equals(campaign, ownerCampaign, StringComparison.OrdinalIgnoreCase)
            && CampaignUtil.CampaignState?.Pointer == state.Pointer
            && state.runData?.Pointer == run.Pointer && run.teamData?.Pointer == team.Pointer;
        card.FreeAgentLeavingConditions = conditions.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<LeavingCondition>>();
        card.onSelection = (Action)(() =>
        {
            if (completed || selecting || !Current() || Owns(team, player, false)) return;
            selecting = true;
            try
            {
                // Native placement, talents, injuries and other recruitment
                // effects run first. Two-line placement wraps this callback.
                native.Invoke();
                if (!Current() || !Owns(team, player, true)) return;
                Sign(run, player, conditions);
                completed = true;
            }
            finally { selecting = false; }
        });
        var cards = new List<EventChoice>();
        for (int i = 0; i < node.eventChoices.Count; i++) cards.Add(i == index ? card : node.eventChoices[i]);
        // EventChoice is a native value type; use the existing audited unboxed
        // list writer rather than generic interop List<T>.Add/set_Item.
        TwoLineContracts.ReplaceChoices(node, cards);
    }
    internal static void Safe(Action action)
    {
        try { action(); }
        catch (Exception ex) { Plugin.Log.LogError("[PlayerContracts] " + ex); }
    }
}

[HarmonyPatch(typeof(CampaignState), "ResetRunData")]
internal static class PlayerContractNewRun
{
    [HarmonyPostfix] private static void Postfix(CampaignState __instance) =>
        PlayerContractRuntime.Safe(() => PlayerContractRuntime.BeginStarting(__instance));
}
