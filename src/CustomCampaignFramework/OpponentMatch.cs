using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CampaignCore;
using Data;
using HarmonyLib;
using STS.Map;
using UnityEngine;

namespace EndlessMode;

internal static class OpponentMatch
{
    private static readonly Dictionary<string, TeamConfig> Settings = new();
    private static readonly Dictionary<string, HashSet<string>> Effects = new();
    internal static string Puck = "", Surface = "";
    internal static bool IsBrawlPrince(TeamData team, ForwardData player)
    {
        if (team == null || player == null || !Settings.TryGetValue(team.id ?? "", out var cfg)
            || Plugin.PlayerTeamConfigs.Values.Any(own => ReferenceEquals(own, cfg))) return false;
        return PlayerBrawlOptions.CountsAsPrince(OpponentPlayerConfig.Find(team, player, cfg)?.Fields);
    }
    internal static string Preset(TeamData team) => team != null && Settings.TryGetValue(team.id ?? "", out var cfg)
        ? OpponentOptions.Normalize(cfg.OpponentPreset, OpponentOptions.Presets.Keys, "Standard") : "Standard";
    internal static void Apply(TeamData team, TeamConfig cfg)
    {
        if (!CustomTeamRules.IsCustom(team) && !VanillaCampaignRuntime.Owns(team)) return;
        Settings[team.id] = cfg;
        GolfTerrain.Apply(team, cfg);
        if (!string.IsNullOrWhiteSpace(cfg.OpponentEffects))
        {
            var selected = OpponentEffects.Parse(cfg.OpponentEffects);
            var conflicts = OpponentEffects.Conflicts(selected).ToArray();
            // Invalid hand-edited configurations must not silently choose a winner.
            if (conflicts.Length > 0)
            {
                Plugin.Log.LogWarning("[Opponent] Conflicting effects disabled: " + string.Join("; ", conflicts));
                foreach (var group in OpponentEffects.All.Where(e => selected.Contains(e.Key) && e.Exclusive.Length > 0).GroupBy(e => e.Exclusive).ToArray())
                    if (group.Count() > 1) foreach (var effect in group) selected.Remove(effect.Key);
            }
            Effects[team.id] = selected;
            foreach (string unknown in selected.Where(k => !OpponentEffects.All.Any(e => e.Key.Equals(k, StringComparison.OrdinalIgnoreCase))))
                Plugin.Log.LogWarning("[Opponent] Unknown effect retained but not applied: " + unknown);
        }
        else Effects.Remove(team.id);
        string offense = OpponentEffects.ResolveOffense(cfg.OpponentOffense);
        string defense = OpponentEffects.ResolveDefense(cfg.OpponentDefense);
        bool changeOffense=VanillaCampaignRuntime.OverrideSetting(team,"Opponent Offense","Opponent Effects");
        bool changeDefense=VanillaCampaignRuntime.OverrideSetting(team,"Opponent Defense","Opponent Effects");
        if(changeOffense)team.offensiveAI = null;
        if(changeDefense)team.defensiveAI = null;
        // Each formation is explicit and independent of the old team preset.
        foreach (var donor in Resources.FindObjectsOfTypeAll<TeamData>())
        {
            if (donor == null || CustomTeamRules.IsCustom(donor) || VanillaCampaignRuntime.Owns(donor)) continue;
            if (changeOffense && offense != "Standard" && donor.offensiveAI != null && donor.offensiveAI.name == offense)
                team.offensiveAI = donor.offensiveAI;
            if (changeDefense && defense != "Standard" && donor.defensiveAI != null && donor.defensiveAI.name == defense)
                team.defensiveAI = donor.defensiveAI;
        }
        if (Effects.TryGetValue(team.id, out var active))
        {
            if (changeOffense && active.Contains("golfer-offense")) team.offensiveAI = null;
        }
    }
    internal static bool Classifies(TeamData team, string classifier)
    {
        // Settings is populated by the opponent application path only. Player
        // squad configuration must never acquire automatic referee immunity.
        if (team != null && Settings.TryGetValue(team.id ?? "", out var cfg)
            && !Plugin.PlayerTeamConfigs.Values.Any(player => ReferenceEquals(player, cfg)))
            foreach (var interaction in RelicOpponentInteractions.All)
                if (interaction.Consumer == OpponentEffectScope.Current && interaction.Classifier == classifier)
                {
                    string mode = RelicOpponentInteractions.Resolve(cfg.RefereeHandsInteraction,
                        OpponentEffects.Parse(cfg.OpponentEffects), interaction.EffectKey);
                    // These consumers belong to the equipped relic. Auto uses
                    // that native lifecycle rather than granting or caching a
                    // relic, and Off wins over presets and legacy effect lists.
                    return RelicOpponentInteractions.Allows(interaction.EffectKey, mode,
                        OpponentEffectScope.Current, classifier);
                }
        if (team != null && Effects.TryGetValue(team.id ?? "", out var selected))
            return OpponentEffects.Allows(selected, OpponentEffectScope.Current, classifier);
        return OpponentOptions.Presets[Preset(team)] == classifier;
    }
    internal static void Begin(MatchMapNode node)
    {
        End();
        if (Plugin.IsDefaultMode || node == null) return;
        var team = node.opponent;
        TeamConfig cfg = null;
        if (team != null) Settings.TryGetValue(team.id ?? "", out cfg);
        if (cfg != null) GolfTerrain.Apply(team, cfg);
        bool soccerNode = SoccerMatch.IsSoccerNode(node);
        Puck = cfg != null && VanillaCampaignRuntime.OverrideSetting(team,"Match Puck") ? OpponentOptions.Puck(cfg.MatchPuck) : soccerNode ? "Soccer ball" : "";
        Surface = cfg != null && VanillaCampaignRuntime.OverrideSetting(team,"Match Surface") ? OpponentOptions.Surface(cfg.MatchSurface) : soccerNode ? "Soccer field" : "";
        TycoonsSponsorsMenu.Begin(cfg != null && VanillaCampaignRuntime.OverrideSetting(team,"Match Surface","Tycoons Sponsors") ? team.id : "", cfg?.MatchSurface, cfg?.TycoonsSponsors);
        CanadaCursesMatch.Begin(cfg != null && VanillaCampaignRuntime.OverrideSetting(team,"Canada Curses") ? team.id : "", cfg?.CanadaCurses);
        SoccerMatch.BeginMatch(Surface == "Soccer field", "Opponent settings");
        Plugin.Log.LogInfo($"[Opponent] '{team?.teamName}': preset={Preset(team)}, puck={Puck}, surface={Surface}");
    }
    internal static void End() { Puck = Surface = ""; Plugin.SoccerThisMatch = false; TycoonsSponsorsMenu.End(); CanadaCursesMatch.End(); MatchPeriods.End(); }
    internal static void Clear() { End(); Settings.Clear(); Effects.Clear(); OpponentLineBehavior.Clear(); }
}

[HarmonyPatch]
internal static class CustomOpponentPreset
{
    private static IEnumerable<MethodBase> TargetMethods() => OpponentOptions.Presets.Values
        .Where(m => m.Length > 0 && m != nameof(TeamData.IsGolferTeam))
        .Select(m => (MethodBase)AccessTools.Method(typeof(TeamData), m));
    [HarmonyPrefix] private static bool Prefix(TeamData __instance, MethodBase __originalMethod, ref bool __result)
    {
        if (Plugin.IsDefaultMode || !VanillaCampaignRuntime.OverrideClassifier(__instance,__originalMethod.Name)) return true;
        __result = OpponentMatch.Classifies(__instance, __originalMethod.Name);
        return false;
    }
}
