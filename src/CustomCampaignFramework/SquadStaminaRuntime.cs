using System;
using Data;
using HarmonyLib;
using Skaters;
using Tape2Tape;
using UnityEngine;

namespace EndlessMode;

[HarmonyPatch(typeof(TeamStaminaSystem), nameof(TeamStaminaSystem.Initialize))]
internal static class SquadStaminaRuntime
{
    [HarmonyPostfix]
    internal static void Postfix(TeamStaminaSystem __instance, Team __0, bool __1)
    {
        // Team.Initialize calls the native HUD factory after this method. Apply
        // the authored policy here so both fatigue and the meter work even when
        // a game update disables the address-based squad comparison bridge.
        PlayNowTeamInitialization.Register(__0);
        bool enabled;
        if (PlayNowRosterRuntime.TryGet(__0?._teamData, out var exported)) enabled = exported.StaminaEnabled;
        else if (!__1 || !SquadBehaviorRuntime.TryEnabled(
                CampaignUtil.CampaignState?.GetCurrentSquad()?.id, "line_stamina", out enabled)) return;
        if (!enabled)
        {
            __instance._isLinesTeam = false;
            return;
        }
        if (__instance.IsActive) return;
        __instance._config = Resources.Load<StaminaConfig>("StaminaConfig");
        __instance._isLinesTeam = __instance._config != null;
        if (!__instance.IsActive)
        {
            Plugin.Log.LogWarning("[Stamina] Native StaminaConfig is missing; custom squad stamina is disabled.");
            return;
        }
        __instance.RefillAll();
        __instance.RebuildOnIce();
    }
}


// Team.Initialize uses Unity Instantiate, not TeamData.Clone. Bind the exact
// invocation input before its stamina initializer and retire it on every exit.
[HarmonyPatch(typeof(Team), nameof(Team.Initialize))]
internal static class PlayNowTeamInitialization
{
    [ThreadStatic] private static Frame current;
    internal sealed class Frame : IDisposable
    {
        internal Frame Parent;
        internal Team Team;
        internal TeamData Source;
        internal bool Disposed;
        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            while (current != null && current.Disposed) current = current.Parent;
        }
    }
    [HarmonyPrefix]
    internal static void Prefix(Team __instance, TeamData __0, out Frame __state)
    {
        if (__instance?._teamData != null && (__0 == null || __instance._teamData.Pointer != __0.Pointer))
            PlayNowRosterRuntime.Forget(__instance._teamData);
        __state = new Frame { Parent = current, Team = __instance, Source = __0 };
        current = __state;
    }
    internal static void Register(Team team)
    {
        var frame = current;
        if (team == null || frame == null || frame.Disposed || frame.Team == null || frame.Team.Pointer != team.Pointer) return;
        if (!PlayNowRosterRuntime.IsRegistered(team._teamData)) PlayNowRosterRuntime.Clone(frame.Source, team._teamData);
    }
    [HarmonyFinalizer]
    internal static Exception Finalizer(Exception __exception, Frame __state)
    {
        __state?.Dispose(); return __exception;
    }
}
