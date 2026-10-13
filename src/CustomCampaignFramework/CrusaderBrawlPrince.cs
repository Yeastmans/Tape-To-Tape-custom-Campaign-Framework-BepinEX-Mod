using System;
using CampaignCore;
using Data;
using Fighting;
using HarmonyLib;
using Skaters;

namespace EndlessMode;

// Add eligibility only for the victim inspected by this synchronous trigger.
// Never rewrite native identity: other Prince checks must keep native results.
[HarmonyPatch(typeof(BrawlChallengeTriggers), nameof(BrawlChallengeTriggers.NotifyBodycheckLanded))]
internal static class CrusaderBrawlPrince
{
    [ThreadStatic] internal static IntPtr Target;
    internal struct ScopeState { internal IntPtr Previous; }

    [HarmonyPrefix]
    internal static void Enter(Forward __1, out ScopeState __state)
    {
        __state = new ScopeState { Previous = Target };
        Target = IntPtr.Zero;
        if (Plugin.IsDefaultMode || __1 == null) return;
        try
        {
            var player = __1._forwardData;
            if (OpponentMatch.IsBrawlPrince(__1.Team?._teamData, player)) Target = player.Pointer;
        }
        catch (Exception ex) { Plugin.Log.LogWarning("[Opponent] Prince brawl flag unavailable: " + ex.Message); }
    }

    [HarmonyFinalizer]
    internal static Exception Leave(ScopeState __state, Exception __exception)
    {
        Target = __state.Previous;
        return __exception;
    }
}

[HarmonyPatch(typeof(ForwardData), nameof(ForwardData.IsPrince))]
internal static class CrusaderBrawlPrinceIdentity
{
    [HarmonyPostfix]
    internal static void Postfix(ForwardData __instance, ref bool __result)
    {
        if (!__result && !Plugin.IsDefaultMode && __instance != null
            && OpponentEffectScope.Current == PlayerBrawlOptions.Consumer
            && CrusaderBrawlPrince.Target != IntPtr.Zero && __instance.Pointer == CrusaderBrawlPrince.Target)
            __result = true;
    }
}
