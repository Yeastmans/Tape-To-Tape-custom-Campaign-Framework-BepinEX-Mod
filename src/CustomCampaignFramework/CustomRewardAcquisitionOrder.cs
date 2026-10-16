using System.Collections.Generic;
using System.Reflection;
using Data;
using HarmonyLib;
using Skaters;
using Tape2Tape.Hockey.UI;

namespace EndlessMode;

[HarmonyPatch]
internal static class CustomRewardAcquisitionOrder
{
    private static IEnumerable<MethodBase> TargetMethods() {
        yield return AccessTools.Method(typeof(RelicRewardMenu), "ApplyCurrentSelection");
        yield return AccessTools.Method(typeof(SkaterPowerupRewardMenu), "ApplyCurrentSelection");
        yield return AccessTools.Method(typeof(Skater), "AddTalent");
    }
    private static TeamData Team(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase instance) =>
        instance.TryCast<RelicRewardMenu>()?.m_Team ?? instance.TryCast<SkaterPowerupRewardMenu>()?.m_Team ?? instance.TryCast<Skater>()?.Team?._teamData;
    [HarmonyPrefix] private static void Prefix(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase __instance) => CustomRewardRuntime.Safe(() => {
        CustomRewardRuntime.EnsureRun(); CustomRewardRuntime.ObserveOwnership(Team(__instance));
    });
    [HarmonyPostfix] private static void Postfix(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase __instance) => CustomRewardRuntime.Safe(() => {
        CustomRewardRuntime.ObserveOwnership(Team(__instance)); CustomRewardRuntime.Save();
    });
}
