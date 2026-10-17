using HarmonyLib;
using Skaters;
using Tape2Tape;
using UnityEngine;

namespace EndlessMode;

[HarmonyPatch(typeof(TeamStaminaSystem), nameof(TeamStaminaSystem.Initialize))]
internal static class SquadStaminaRuntime
{
    [HarmonyPostfix]
    internal static void Postfix(TeamStaminaSystem __instance, bool __1)
    {
        // Team.Initialize calls the native HUD factory after this method. Apply
        // the authored policy here so both fatigue and the meter work even when
        // a game update disables the address-based squad comparison bridge.
        if (!__1 || !SquadBehaviorRuntime.TryEnabled(
                CampaignUtil.CampaignState?.GetCurrentSquad()?.id, "line_stamina", out bool enabled)) return;
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
