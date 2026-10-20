using System;
using System.Collections.Generic;
using System.Linq;
using CampaignCore;
using HarmonyLib;
using Rogue;
using Rogue.Relics.Controllers;
using Tape2Tape;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EndlessMode;

// SoccerWall's reviewed controller contains one cohesive rink-prop behavior.
// Its native removal removes only its own forwards from both team lists.
internal static class CustomRewardSoccerWall
{
    private sealed class Wall { internal Team Team; internal SoccerWallController Controller; internal SoccerWall Payload; }
    private static readonly Dictionary<IntPtr, Wall> walls = new();
    private static bool Owned(Team team) => CustomRewardCampaignRules.Applying(team, "Match setup", CustomRewardEffects.SoccerWall).Any();
    internal static void Setup(Team team)
    {
        if (team?._teamData == null || walls.ContainsKey(team.Pointer) || team.HaveRelic<SoccerWall>() || !Owned(team)) return;
        var original = Resources.FindObjectsOfTypeAll<SoccerWall>().FirstOrDefault(r => r.name != null && !r.name.StartsWith("CCF_Wall_", StringComparison.Ordinal));
        if (original?.forwardPrefab == null || original.wallPlayers == null) return;
        var payload = Object.Instantiate(original); payload.name = "CCF_Wall_" + team._teamData.id;
        var wall = new Wall { Team = team, Payload = payload, Controller = new SoccerWallController() };
        walls.Add(team.Pointer, wall);
        try { wall.Controller.Initialize(team, CampaignUtil.CampaignState, payload); }
        catch { Remove(team); throw; }
    }
    internal static void Refresh()
    {
        foreach (var wall in walls.Values.ToArray())
            if (wall.Team == null || !Owned(wall.Team) || wall.Team.HaveRelic<SoccerWall>()) Remove(wall.Team, wall);
    }
    internal static void Remove(Team team)
    {
        if (team != null && walls.TryGetValue(team.Pointer, out var wall)) Remove(team, wall);
    }
    private static void Remove(Team team, Wall wall)
    {
        // Keep the managed identity even if Unity has already destroyed Team.
        foreach (var key in walls.Where(p => ReferenceEquals(p.Value, wall)).Select(p => p.Key).ToArray()) walls.Remove(key);
        try { if (team != null) wall.Controller.Destroy(); }
        finally {
            if (wall.Controller._soccerLineInstance != null) Object.Destroy(wall.Controller._soccerLineInstance);
            if (wall.Payload != null) Object.Destroy(wall.Payload);
        }
    }
    internal static void Clear()
    {
        foreach (var wall in walls.Values.ToArray()) Remove(wall.Team, wall);
    }
}

[HarmonyPatch(typeof(Team), "InitializeRelicControllers")]
internal static class CustomRewardWallSetup
{
    [HarmonyPostfix] private static void Postfix(Team __instance) => CustomRewardRuntime.Safe(() => {
        CustomRewardRuntime.MatchStarted(); CustomRewardSoccerWall.Setup(__instance);
    });
}
[HarmonyPatch(typeof(Team), "DestroyRelicControllers")]
internal static class CustomRewardWallCleanup
{
    [HarmonyPrefix] private static void Prefix(Team __instance) => CustomRewardRuntime.Safe(() => CustomRewardSoccerWall.Remove(__instance));
}
