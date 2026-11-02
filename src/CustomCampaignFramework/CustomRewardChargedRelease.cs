using HarmonyLib;
using Movements.MovementStates;

namespace EndlessMode;

// Installed explicitly, so the one native trampoline is verified/repaired
// immediately and removed again if any guard fails.
internal static class CustomRewardChargedRelease
{
    [HarmonyPrefix] internal static void Prefix(SlapshotState __instance, float __1)
    {
        if (!CustomRewardRuntime.InMatch) return;
        CustomRewardChargedShots.Release(__instance.forward, __1);
    }
}
