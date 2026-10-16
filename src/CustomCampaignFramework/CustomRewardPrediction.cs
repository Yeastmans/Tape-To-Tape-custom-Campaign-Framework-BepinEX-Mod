using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Skaters;

namespace EndlessMode;

[HarmonyPatch]
internal static class CustomRewardPrediction
{
    private static IEnumerable<MethodBase> TargetMethods() => typeof(Forward).GetMethods()
        .Where(m => m.Name == "CanPokecheckCarrierForPrediction" && m.GetParameters().Length == 3);
    [HarmonyPrefix] private static void Prefix(out bool __state)
    {
        __state = CustomRewardCapabilities.PredictionQuery;
        CustomRewardCapabilities.PredictionQuery = true;
    }
    [HarmonyFinalizer] private static void Finalizer(bool __state) => CustomRewardCapabilities.PredictionQuery = __state;
}
