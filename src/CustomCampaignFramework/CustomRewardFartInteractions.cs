using System.Linq;
using CampaignCore;
using HarmonyLib;
using Rogue.Powerups.Components;
using Tape2Tape;

namespace EndlessMode;

[HarmonyPatch(typeof(FartSynergyResolver), nameof(FartSynergyResolver.Resolve))]
internal static class CustomRewardFartInteractions
{
    [HarmonyPostfix] private static void Postfix(Team sourceTeam, ref FartSynergyState __result)
    {
        if (!CustomRewardRuntime.InMatch || sourceTeam?._teamData == null) return;
        // Knockout rules keep their own victim target; force and range are
        // whole-team rules that an opponent can also aim at this team.
        var rules = CustomRewardCampaignRules.Owned(sourceTeam._teamData, "Fart interaction").Where(d => d.Effect == CustomRewardEffects.FartKnockouts)
            .Concat(CustomRewardCampaignRules.Applying(sourceTeam, "Fart interaction", CustomRewardEffects.FartForce))
            .Concat(CustomRewardCampaignRules.Applying(sourceTeam, "Fart interaction", CustomRewardEffects.FartRange)).ToArray();
        if (rules.Length == 0) return;
        float force = __result.ForceMultiplier, range = __result.RangeMultiplier;
        bool knockout = __result.HasFartKnockout;
        bool opponentsOnly = !knockout || __result.FartKnockoutOpponentsOnly;
        foreach (var d in rules) {
            if (d.Effect == CustomRewardEffects.FartForce) force *= d.Amount / 100f;
            if (d.Effect == CustomRewardEffects.FartRange) range *= d.Amount / 100f;
            if (d.Effect == CustomRewardEffects.FartKnockouts) {
                knockout = true; opponentsOnly &= d.Target == CustomRewardEffects.OpposingTeam;
            }
        }
        __result = new FartSynergyState(force, range, knockout, opponentsOnly, __result.FartImmunity);
    }
}
