using Data;
using HarmonyLib;
namespace EndlessMode;

// Unique native methods; do not hook shared field accessors or line-lock queries.
[HarmonyPatch(typeof(TeamData), "SetForward")]
internal static class CustomStartingRosterSetForward
{
    [HarmonyPrefix] internal static bool Prefix(TeamData __instance, int __0, ForwardData __1)
        => CustomStartingRoster.AllowSet(__instance, __0, __1);
}
[HarmonyPatch(typeof(TeamData), "SetForwardAtIndex")]
internal static class CustomStartingRosterSetForwardAtIndex
{
    [HarmonyPrefix] internal static bool Prefix(TeamData __instance, int __0, ForwardData __1)
        => CustomStartingRoster.AllowSet(__instance, __0, __1);
}
