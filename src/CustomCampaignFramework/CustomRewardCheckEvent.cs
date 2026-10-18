using Events;
using HarmonyLib;

namespace EndlessMode;

// GameStats is the match-wide consumer of the native SkaterCheck event. Harmony
// boxes its native value-type argument; the managed delegate conversion used by
// SingleEvent.AddListener cannot marshal that same non-blittable value type.
[HarmonyPatch(typeof(GameStats), "OnHit")]
internal static class CustomRewardCheckEvent
{
    [HarmonyPostfix]
    private static void Postfix(SkaterCheckEventArgs __0)
    {
        if (!CustomRewardRuntime.InMatch || __0 == null) return;
        CustomRewardRuntime.Safe(() => {
            CustomRewardRuntime.Signal("Check", __0.Skater, other: __0.Hitted);
            CustomRewardRuntime.Signal("Checked", __0.Hitted, other: __0.Skater);
        });
    }
}
