using Data;
using HarmonyLib;

namespace EndlessMode;

// The campaign Calaveras use a second native ID predicate. OnKnockout accepts
// either ID, so a copied campaign opponent must route both through the same
// authored choice. Keep this alias local to the knockout consumer; it is not a
// new team identity and must not alter unedited native copies or default mode.
[HarmonyPatch(typeof(TeamData), nameof(TeamData.IsCampaignCalaverasTeam))]
internal static class CampaignCalaverasBrawl
{
    [HarmonyPrefix]
    internal static bool Prefix(TeamData __instance, ref bool __result)
    {
        if (Plugin.IsDefaultMode
            || OpponentEffectScope.Current != "Fighting.BrawlChallengeTriggers.OnKnockout"
            || !VanillaCampaignRuntime.OverrideClassifier(__instance, nameof(TeamData.IsCalaverasTeam)))
            return true;
        __result = OpponentMatch.Classifies(__instance, nameof(TeamData.IsCalaverasTeam));
        return false;
    }
}
