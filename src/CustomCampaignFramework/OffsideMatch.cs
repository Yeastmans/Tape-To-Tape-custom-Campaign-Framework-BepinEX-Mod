using System;
using HarmonyLib;
using Tape2Tape;

namespace EndlessMode;

// A native force source also forces Knockout. Campaign offside instead supplies
// temporary read values to the native rule, without touching persistent settings.
// Native Tchel / Linesman force sources retain their own Knockout precedence.
public static class OffsideMatch
{
    private static bool active;
    private static bool refreshed;
    private static float nextTry;
    private static bool reportedFailure;
    private static int penalty = -1;
    internal static bool Active => active && !Plugin.IsDefaultMode;

    internal static void BeginMatch(string why)
    {
        active = !Plugin.IsDefaultMode && Plugin.OffsideEnabled;
        refreshed = false;
        nextTry = 0f;
        reportedFailure = false;
        penalty = active ? ParsePenalty(Plugin.OffsidePenaltyName) : -1;
        if (active) Plugin.Log.LogInfo($"[Offside] {why}: campaign rule enabled; penalty={(penalty < 0 ? "player setting" : ((Settings.OffsidePenalty)penalty).ToString())}");
    }

    internal static void Tick()
    {
        if (!Active || refreshed || UnityEngine.Time.unscaledTime < nextTry) return;
        nextTry = UnityEngine.Time.unscaledTime + 0.5f;
        try
        {
            var controller = OffsideController._instance;
            if (controller == null || !T2TUtils.IsInMatch) return;
            OffsideController.Refresh();
            refreshed = OffsideController.IsActive && controller._subscribedToEvents;
            if (refreshed)
                Plugin.Log.LogInfo($"[Offside] Rule armed for this match (active=True, events=True, penalty={controller.GetOffsidePenalty()})");
            else if (!reportedFailure)
            {
                reportedFailure = true;
                Plugin.Log.LogWarning("[Offside] Native controller is not ready; activation will retry.");
            }
        }
        catch (Exception ex)
        {
            if (!reportedFailure)
            {
                reportedFailure = true;
                Plugin.Log.LogWarning($"[Offside] Could not arm the offside rule; will retry: {ex.Message}");
            }
        }
    }

    internal static void End()
    {
        bool wasActive = active;
        active = false;
        refreshed = false;
        penalty = -1;
        OpponentMatch.End();
        if (wasActive)
        {
            try { OffsideController.Refresh(); } catch { }
            Plugin.Log.LogInfo("[Offside] Campaign rule cleared; native settings restored");
        }
    }

    internal static void ReadOffside(ref bool result)
    {
        if (Active) result = true;
    }

    internal static bool RefreshCampaignRule()
    {
        if (!Active) return true;
        var controller = OffsideController._instance;
        // Native Refresh reads GameplaySettings.Offside only in Play Now. Its
        // campaign branch otherwise requires Tchel or a force source, both of
        // which also select Knockout. Keep its live-scene guards and Setup;
        // enable only the controller, without adding a force source or changing
        // the game type. Setup is native and idempotently subscribes its events.
        if (controller == null || !T2TUtils.IsInMatch) return true;
        OffsideController.IsActive = true;
        controller.Setup();
        return false;
    }

    internal static void ReadPenalty(ref Settings.OffsidePenalty result)
    {
        if (Active && penalty >= 0) result = (Settings.OffsidePenalty)penalty;
    }

    internal static int ParsePenalty(string value) => (value ?? "").Trim().ToLowerInvariant().Replace("-", " ").Replace("_", " ") switch
    {
        "whistle" or "0" => 0,
        "lose puck" or "losepuck" or "loose puck" or "1" => 1,
        "knockout" or "knock out" or "ko" or "2" => 2,
        _ => -1
    };
}

[HarmonyPatch(typeof(OffsideController), nameof(OffsideController.Refresh))]
internal static class CampaignOffsideActivation
{
    [HarmonyPrefix] internal static bool Prefix() => OffsideMatch.RefreshCampaignRule();
}

[HarmonyPatch(typeof(Settings.GameplaySettings), nameof(Settings.GameplaySettings.Offside), MethodType.Getter)]
internal static class CampaignOffsideSetting
{
    [HarmonyPostfix] private static void Postfix(ref bool __result) => OffsideMatch.ReadOffside(ref __result);
}

[HarmonyPatch(typeof(Settings.GameplaySettings), nameof(Settings.GameplaySettings.OffsidePenalty), MethodType.Getter)]
internal static class CampaignOffsidePenaltySetting
{
    [HarmonyPostfix] private static void Postfix(ref Settings.OffsidePenalty __result) => OffsideMatch.ReadPenalty(ref __result);
}

// Leaving a match through its menu does not have to raise a map-node result.
[HarmonyPatch(typeof(GameManager), "InternalOnDestroy")]
internal static class ClearCampaignOffsideOnSceneExit
{
    [HarmonyPostfix] internal static void Postfix() => OffsideMatch.End();
}
