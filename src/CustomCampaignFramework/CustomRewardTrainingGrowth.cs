using System;
using Data;
using HarmonyLib;
using Rogue.NPC.ConditioningNPC;
using Tape2Tape.Hockey.UI;

namespace EndlessMode;

// The native training award point queries TrainingWhistle in these two choice
// handlers. Observe that query without changing its answer or granting a relic.
// Preview queries are explicitly excluded, including nested preview creation.
internal static class CustomRewardTrainingGrowth
{
    internal sealed class Scope { internal bool Used; }
    [ThreadStatic] internal static Scope Current;
    [ThreadStatic] internal static bool Preview;
    internal static void Query(TeamData team)
    {
        if (Current == null || Current.Used || Preview || team == null) return;
        Current.Used = true;
        CustomRewardRuntime.Safe(() => CustomRewardRuntime.TrainingChosen(team));
    }
}

[HarmonyPatch(typeof(EventChoiceMenu._Choose_d__172), "MoveNext")]
internal static class CustomRewardTrainingChoiceScope
{
    [HarmonyPrefix] private static void Prefix(out CustomRewardTrainingGrowth.Scope __state)
    { __state = CustomRewardTrainingGrowth.Current; CustomRewardTrainingGrowth.Current = new(); }
    [HarmonyFinalizer] private static void Finalizer(CustomRewardTrainingGrowth.Scope __state) => CustomRewardTrainingGrowth.Current = __state;
}
[HarmonyPatch(typeof(EventChoicePanel.__c__DisplayClass16_1), "_InitializeChoicesButtons_b__1")]
internal static class CustomRewardLegacyTrainingChoiceScope
{
    [HarmonyPrefix] private static void Prefix(out CustomRewardTrainingGrowth.Scope __state)
    { __state = CustomRewardTrainingGrowth.Current; CustomRewardTrainingGrowth.Current = new(); }
    [HarmonyFinalizer] private static void Finalizer(CustomRewardTrainingGrowth.Scope __state) => CustomRewardTrainingGrowth.Current = __state;
}
[HarmonyPatch(typeof(TrainingEvent), "GetCustomStatBonusesData")]
internal static class CustomRewardTrainingPreviewScope
{
    [HarmonyPrefix] private static void Prefix(out bool __state)
    { __state = CustomRewardTrainingGrowth.Preview; CustomRewardTrainingGrowth.Preview = true; }
    [HarmonyFinalizer] private static void Finalizer(bool __state) => CustomRewardTrainingGrowth.Preview = __state;
}
