using System;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using CampaignCore;
using Data;
using HarmonyLib;
using STS.Map;
using UnityEngine;

namespace EndlessMode;

internal static class CustomTeamRules
{
    private static readonly Dictionary<string, string> Descriptions = new();
    internal static void ClearDescriptions() => Descriptions.Clear();
    internal static string Description(TeamData team) => team != null &&
        Descriptions.TryGetValue(team.id ?? "", out var text) ? text : "";
    private static readonly HashSet<string> ExportedLogos = new();
    internal static void ExportLogo(TeamData team, TeamConfig cfg)
    {
        string source = !string.IsNullOrWhiteSpace(cfg.LogoFrom) ? cfg.LogoFrom : cfg.ImportTeam;
        if (team?.logo == null || string.IsNullOrWhiteSpace(source) || source.Equals("RANDOM", StringComparison.OrdinalIgnoreCase)) return;
        string file = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Plugin.ModFolder)), "_team_logos", CustomTeamIdentity.Create("logo", source) + ".png");
        if (ExportedLogos.Contains(file)) return;
        try
        {
            var png = LogRepositories.SpriteToPng(team.logo);
            if (png == null || png.Length == 0) return;
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllBytes(file, png);
            ExportedLogos.Add(file);
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[TeamLogo] Preview export: {ex.Message}"); }
    }
    internal static bool IsCustom(TeamData team) => team != null && CustomTeamIdentity.IsCustom(team.id);

    // Custom team objects by custom id. The run-end summary looks past opponents up by
    // id, and none of the game's own team lists hold a campaign team.
    private static readonly Dictionary<string, TeamData> Live = new();
    internal static TeamData ForRunSummary(string id)
    {
        if (Live.TryGetValue(id, out var live) && live != null) return live;
        TeamConfig cfg = null;
        foreach (var candidate in Plugin.ConfigTeams)
            if (candidate != null && !string.IsNullOrEmpty(candidate.IdentityKey) && CustomTeamIdentity.Create(Plugin.ActiveCampaign, candidate.IdentityKey) == id) cfg = candidate;
        foreach (var candidate in Plugin.PlayerTeamConfigs.Values)
            if (candidate != null && !string.IsNullOrEmpty(candidate.IdentityKey) && CustomTeamIdentity.Create(Plugin.ActiveCampaign, candidate.IdentityKey) == id) cfg = candidate;
        // Summary-only stand-in (for example after a restart, or a team from another
        // campaign): any native team, renamed, with the campaign team's logo when known.
        TeamData donor = null;
        foreach (var team in Resources.FindObjectsOfTypeAll<TeamData>())
            if (team != null && !IsCustom(team)) { donor = team; break; }
        if (donor == null) return null;
        var standIn = UnityEngine.Object.Instantiate(donor);
        standIn.id = id; standIn.name = id;
        standIn.teamName = cfg?.Name is { Length: > 0 } name ? name : "Custom team";
        string logo = cfg == null ? "" : !string.IsNullOrWhiteSpace(cfg.LogoFrom) ? cfg.LogoFrom : cfg.ImportTeam ?? "";
        standIn.logo = logo.Length == 0 ? null : PatchBossLaunchMatch.LoadCustomLogoSprite(logo) ?? PatchBossLaunchMatch.FindTeamByName(logo)?.logo;
        Live[id] = standIn;
        Plugin.Log.LogInfo($"[RunSummary] Stand-in for '{standIn.teamName}' ({id})");
        return standIn;
    }

    internal static void Prepare(TeamData team, TeamConfig cfg)
    {
        if (Plugin.IsDefaultMode || team == null || cfg == null) return;
        if(VanillaCampaignRuntime.IsCopiedOpponent(cfg))
        {
            TeamGoalSound.Register(team.id,cfg.GoalSoundData,cfg.GoalSoundVolume);return;
        }
        string key = cfg.IdentityKey;
        if (string.IsNullOrEmpty(key))
        {
            int index = Plugin.ConfigTeams.IndexOf(cfg);
            if (index >= 0) key = "campaign.txt/team/" + index;
            else foreach (var entry in Plugin.PlayerTeamConfigs)
                if (ReferenceEquals(entry.Value, cfg)) { key = "player_teams/" + entry.Key; break; }
        }
        if (string.IsNullOrEmpty(key))
            throw new InvalidOperationException("Custom team has no configuration identity.");

        string id = CustomTeamIdentity.Create(Plugin.ActiveCampaign, key);
        Descriptions[id] = cfg.Description ?? "";
        TeamGoalSound.Register(id, cfg.GoalSoundData, cfg.GoalSoundVolume);
        // Check arena ownership independently: native save rehydration can
        // preserve the team ID while restoring a repository arena reference.
        if (team.arenaData != null && team.arenaData.name != id + "_Arena")
        {
            // Detach arena metadata. Its visuals remain usable, but it no longer
            // carries the donor's name or crowd override into a custom team.
            team.arenaData = UnityEngine.Object.Instantiate(team.arenaData);
            team.arenaData.name = id + "_Arena";
            team.arenaData.arenaName = cfg.Name;
            team.arenaData.crowdTheme = null;
        }
        if (team.id != id)
        {
            team.id = id;
            team.name = id;
            Plugin.Log.LogInfo($"[CustomIdentity] '{cfg.Name}' => {id}");
        }
        Live[id] = team;
        // Native AIStrategy explicitly falls back to its normal offense/defense
        // when these are absent. Donor formations must not survive a clone.
        team.offensiveAI = null;
        team.defensiveAI = null;
        team.ResetRelicCache();
    }

    internal static TeamData OwnOpponent(MatchMapNode node)
    {
        var team = node.opponent;
        if (team == null || IsCustom(team)) return team;
        var clone = UnityEngine.Object.Instantiate(team);
        PatchChooseMetaUI.DeepCloneForwards(clone);
        node.opponent = clone;
        return clone;
    }
}

// RunEndMenu.RefreshSkaterAndMatchData finds each played opponent with FindTeam: the
// run's campaign teams, then the repository, else null. A campaign team (CCF_Team_ id)
// is in neither, and RunMatchDetailsUI.Refresh dereferences the null opponent, which
// aborts the end-of-game pipeline: a conceded/lost run never shows its summary or
// reaches OnRunFinished (Player.log 2026-09-29 18:12).
[HarmonyPatch(typeof(Tape2Tape.Hockey.UI.RunEndMenu), "FindTeam")]
internal static class CustomTeamRunSummary
{
    [HarmonyPostfix] private static void Postfix(string __0, ref TeamData __result)
    {
        if (__result != null || !CustomTeamIdentity.IsCustom(__0)) return;
        try { __result = CustomTeamRules.ForRunSummary(__0); }
        catch (Exception ex) { Plugin.Log.LogWarning("[RunSummary] Opponent '" + __0 + "': " + ex.Message); }
    }
}

[HarmonyPatch(typeof(TeamData), nameof(TeamData.GetDescriptionText))]
internal static class CustomTeamDescription
{
    [HarmonyPrefix] private static bool Prefix(TeamData __instance, ref string __result)
    {
        if (Plugin.IsDefaultMode || !CustomTeamRules.IsCustom(__instance)) return true;
        __result = CustomTeamRules.Description(__instance);
        return false;
    }
}

// Finality belongs to the launched node, not to the opponent. Capture before
// OnGameEnd advances/resets the campaign's map counter, and retain through the
// end-game pipeline. Every subsequent match launch replaces this snapshot.
internal static class CustomMatchCompletion
{
    internal static bool Active;
    internal static bool FinalBoss;
    private static string opponentId;
    [ThreadStatic] internal static int CompletionDepth;

    internal static void Begin(MatchMapNode node, bool boss)
    {
        Active = !Plugin.IsDefaultMode && CustomTeamRules.IsCustom(node?.opponent);
        opponentId = Active ? node.opponent.id : null;
        bool terminal = node?.Node?.outgoing != null && node.Node.outgoing.Count == 0;
        FinalBoss = Active && CustomTeamIdentity.IsFinalBoss(boss, terminal,
            Plugin.ActsCompleted + 1, Plugin.TotalMaps);
    }

    internal static bool IsFinalOpponent(TeamData team) => Active && FinalBoss &&
        team != null && team.id == opponentId;
}

// These synchronous native routines use "golfer" as their final-match marker.
// Translate that marker ONLY while assembling/computing end-game state; gameplay
// and all other team classifiers continue to see the real custom identity.
[HarmonyPatch]
internal static class CustomCompletionScope
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(State.GameModeControllers.GameControllerBase), "InitializeEndOfGame");
        yield return AccessTools.Method(typeof(State.EndOfGame.EndOfGameFlowController), "UpdateCampaignContext");
        yield return AccessTools.Method(typeof(State.EndOfGame.EndOfGameFlowController), "CheckRunWon");
        yield return AccessTools.Method(typeof(Tape2Tape.CampaignUtil), "IsFinalMatch");
    }
    [HarmonyPrefix] private static void Prefix(out OpponentEffectScope.ScopeState __state)
    {
        __state = new OpponentEffectScope.ScopeState { Entered = true, Previous = OpponentEffectScope.Current };
        OpponentEffectScope.Current = null;
        CustomMatchCompletion.CompletionDepth++;
    }
    [HarmonyFinalizer] private static Exception Finalizer(Exception __exception, OpponentEffectScope.ScopeState __state)
    {
        if (__state.Entered)
        {
            CustomMatchCompletion.CompletionDepth--;
            OpponentEffectScope.Current = __state.Previous;
        }
        return __exception;
    }
}

[HarmonyPatch(typeof(TeamData), nameof(TeamData.IsGolferTeam))]
internal static class CustomFinalOpponentMarker
{
    [HarmonyPostfix] private static void Postfix(TeamData __instance, ref bool __result)
    {
        if (!Plugin.IsDefaultMode && CustomTeamRules.IsCustom(__instance))
            __result = CustomMatchCompletion.CompletionDepth > 0 && OpponentEffectScope.Current == null
                ? CustomMatchCompletion.IsFinalOpponent(__instance) : OpponentMatch.Classifies(__instance, nameof(TeamData.IsGolferTeam));
        else if(!Plugin.IsDefaultMode && VanillaCampaignRuntime.OverrideClassifier(__instance,nameof(TeamData.IsGolferTeam)))
            __result=OpponentMatch.Classifies(__instance,nameof(TeamData.IsGolferTeam));
    }
}

[HarmonyPatch(typeof(ChallengeMapNode), nameof(ChallengeMapNode.LaunchMatch))]
internal static class ClearCustomCompletionForChallenge
{
    [HarmonyPrefix] private static void Prefix(ChallengeMapNode __instance)
    {
        OffsideMatch.BeginMatch("Challenge node");
        CustomMatchCompletion.Begin(__instance, false);
        OpponentMatch.Begin(__instance);
    }
}
