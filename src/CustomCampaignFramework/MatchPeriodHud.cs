using System;
using HarmonyLib;
using ScriptableStates;
using State;
using Tape2Tape.Hockey.UI;
using UnityEngine;
using UnityEngine.UI;

namespace EndlessMode;

// The native one-period Rogue shortcut predates authored timed matches. Keep
// this correction in the HUD: numOfPeriod remains the regulation-period count,
// and native currentPeriod > numOfPeriod remains the overtime test.
internal static class MatchPeriodHud
{
    private static bool Active(out int currentPeriod)
    {
        currentPeriod = 0;
        if (GameManager.GameType != GameType.Rogue || !MatchPeriods.UsesSinglePeriodHud(GameManager.InGameData)) return false;
        var game = GameManager.Instance;
        if (game == null) return false;
        currentPeriod = game.currentPeriod;
        return currentPeriod > 0;
    }

    internal static void RefreshScoreboard(ScoreboardTimeRemaining view)
    {
        try
        {
            if (view == null || view.periodLocalize == null || view.otIndicator == null || !Active(out _)) return;
            // This native formatter already uses the correct strict overtime
            // comparison, native localization terms and current OT number.
            view.OnEndOfPeriod(null);
        }
        catch (Exception ex) { Plugin.Log.LogWarning("[Periods] Could not refresh scoreboard period: " + ex.Message); }
    }

    internal static bool GenerateSingleMarker(PeriodIndicatorUI view, int maxPeriod)
    {
        GameObject marker = null;
        try
        {
            if (view == null || maxPeriod != 1 || !Active(out int current) || current != 1) return true;
            if (view.periodIndicatorPrefab == null || view.periodIndicatorParent == null || view._periodIndicators == null
                || view.objectiveCanvasGroup == null || view.periodIndicatorCanvasGroup == null) return true;
            // Mirror the native regulation branch using its own prefab, sprite,
            // parent and list, so its normal update/cleanup and OT transition
            // continue to own the marker. No synthetic period is introduced.
            marker = UnityEngine.Object.Instantiate(view.periodIndicatorPrefab, view.periodIndicatorParent);
            var image = marker?.GetComponent<Image>();
            if (image == null)
            {
                if (marker != null) UnityEngine.Object.Destroy(marker);
                return true;
            }
            image.sprite = view.periodSprite;
            view.ClearPeriodMarkers();
            view._periodIndicators.Add(marker);
            marker = null;
            view.objectiveCanvasGroup.alpha = 0;
            view.periodIndicatorCanvasGroup.alpha = 1;
            return false;
        }
        catch (Exception ex)
        {
            if (marker != null) UnityEngine.Object.Destroy(marker);
            Plugin.Log.LogWarning("[Periods] Could not display single regulation period: " + ex.Message);
            return true;
        }
    }

    internal static void RefreshMercy(PeriodIndicatorUI view)
    {
        try
        {
            if (view?.mercyCanvasGroup == null || !Active(out int current) || current != 1) return;
            if (GameManager.InGameData.endOfGameConditionData.endOfGameConditions.Contains(EndOfGameConditionType.Mercy))
                view.mercyCanvasGroup.alpha = 1;
        }
        catch (Exception ex) { Plugin.Log.LogWarning("[Periods] Could not refresh regulation mercy label: " + ex.Message); }
    }

    internal static void RefreshPauseObjective(GameMenu view)
    {
        try
        {
            if (view == null || !view.m_ShowPauseMenu || view.m_JumbotronObjectiveSection == null
                || !Active(out int current) || current != 1) return;
            // InitData uses count <= 1 alone for its sudden-death objective.
            // Native timed regulation has no pause objective section; retain
            // that presentation while the live HUD owns the mercy reminder.
            view.m_JumbotronObjectiveSection.SetActive(false);
        }
        catch (Exception ex) { Plugin.Log.LogWarning("[Periods] Could not refresh pause objective: " + ex.Message); }
    }
}

[HarmonyPatch(typeof(ScoreboardTimeRemaining), "Awake")]
internal static class SinglePeriodScoreboardAwake
{
    [HarmonyPostfix] internal static void Postfix(ScoreboardTimeRemaining __instance) => MatchPeriodHud.RefreshScoreboard(__instance);
}

[HarmonyPatch(typeof(ScoreboardTimeRemaining), "GeneratePeriodMarkers")]
internal static class SinglePeriodScoreboardMarkers
{
    [HarmonyPostfix] internal static void Postfix(ScoreboardTimeRemaining __instance) => MatchPeriodHud.RefreshScoreboard(__instance);
}

[HarmonyPatch(typeof(PeriodIndicatorUI), "GeneratePeriodMarkers")]
internal static class SinglePeriodMarkers
{
    [HarmonyPrefix] internal static bool Prefix(PeriodIndicatorUI __instance, int __0) => MatchPeriodHud.GenerateSingleMarker(__instance, __0);
}

[HarmonyPatch(typeof(PeriodIndicatorUI), "UpdateMercyCondition")]
internal static class SinglePeriodMercy
{
    [HarmonyPostfix] internal static void Postfix(PeriodIndicatorUI __instance) => MatchPeriodHud.RefreshMercy(__instance);
}

[HarmonyPatch(typeof(GameMenu), "InitData")]
internal static class SinglePeriodPauseObjective
{
    [HarmonyPostfix] internal static void Postfix(GameMenu __instance) => MatchPeriodHud.RefreshPauseObjective(__instance);
}
