using CampaignCore;
using HarmonyLib;
using Rogue.Relics.Components;
using Rogue.Relics.Controllers;

namespace EndlessMode;

[HarmonyPatch(typeof(HotPotatoController), "AddEffect")]
internal static class CustomRewardPossessionTimer
{
    [HarmonyPostfix] private static void Postfix(HotPotatoController __instance)
    {
        if (__instance.Relic == null || !CustomRewardRuntime.Definitions.TryGetValue(__instance.Relic.id, out var rule)
            || rule.Effect != CustomRewardEffects.PossessionExplosion) return;
        var timer = __instance._hotPotatoGameObject?.GetComponent<HotPotatoMonoBehaviour>();
        if (timer != null) timer.timeForExplosion = rule.Seconds;
    }
}
