using System.Linq;
using CampaignCore;
using Goaltender.GoaltenderStates;
using HarmonyLib;
using Rogue;
using Skaters;
using UnityEngine;

namespace EndlessMode;

internal static class CustomRewardGoalieCopy
{
    internal static bool Apply(CustomRewardDefinition rule, Skater actor)
    {
        var goalie = actor?.TryCast<global::Goaltender.Goaltender>();
        var opponent = goalie?.Team?.otherTeam?.goaltender?._skaterData;
        if (goalie == null || opponent == null) return false;
        if (rule.Effect == CustomRewardEffects.CopyGoalieAbility) {
            var ability = opponent.ability;
            if (ability == null || goalie._skaterData.ability?.id == ability.id) return false;
            goalie.AddAbility(Object.Instantiate(ability), true);
            return true;
        }
        if (opponent.powerups == null) return false;
        // Snapshot before adding: native grant callbacks may modify a loadout.
        var candidates = opponent.powerups.ToArray();
        bool changed = false;
        foreach (var talent in candidates) {
            if (talent == null) continue;
            bool custom = CustomRewardRuntime.Definitions.TryGetValue(talent.id, out var definition);
            if (custom) {
                if (!CustomRewardAssignments.FitsPlayer(definition, true) || definition.NeedsRedesign
                    || CustomRewardEffects.IsGoalieCopy(definition.Effect)
                    || CustomRewardRuntime.Has(goalie._skaterData, talent.id)) continue;
            } else if (!MusicalNetsController.IsCopyable(talent) || goalie.HasTalentType(talent.GetIl2CppType())) continue;
            // Custom neutral carriers share a native type; compare their IDs.
            changed |= goalie.TryAddTalent(Object.Instantiate(talent), custom);
        }
        if (changed) goalie.RefreshSkaterUI();
        return changed;
    }
}

// Reproduce only the inspected AlwaysCatchPuck branch. We do not insert an
// Infinimitt into the goalie's powers or change the save/collision calculation.
[HarmonyPatch(typeof(SavingState), "HandlePossession")]
internal static class CustomRewardGoalieCatch
{
    [HarmonyPrefix] private static bool Prefix(SavingState __instance, Collision __0)
    {
        var goalie = __instance?.goaltender;
        if (goalie == null || !goalie.canPickupPuck || __0?.collider == null
            || !CustomRewardRuntime.ActiveConditions(goalie).Any(d => d.Effect == CustomRewardEffects.CatchSaves)) return true;
        if (goalie.HaveTalentCached<AlwaysCatchPuck>()) return true;
        var type = __instance.CurrentSaveType;
        // GobbleStanding already catches via its normal animation callback.
        if (type == SaveType.GobbleStanding) return true;
        if (type == SaveType.Gobble) {
            goalie.SetToButterfly(); goalie.RetrievePuck();
            __instance._moutardeGobbleCoroutine = goalie.StartCoroutine(__instance.MoutardeGobble());
            return false;
        }
        if (type is SaveType.Desperate or SaveType.PadStacker or SaveType.AboveTheNet) return true;
        var contact = __0.collider.gameObject;
        if (!contact.CompareTag("glove_collider") && !contact.CompareTag("blocker_collider") && !contact.CompareTag("body_collider")) return true;
        goalie.RetrievePuck();
        __instance._moutardeGloveBlockerCoroutine = goalie.StartCoroutine(__instance.MoutardeGloveBlocker());
        return false;
    }
}

[HarmonyPatch(typeof(global::Goaltender.Goaltender), "GetMaxSpeedFetch")]
internal static class CustomRewardGoalieFetch
{
    [HarmonyPostfix] private static void Postfix(global::Goaltender.Goaltender __instance, ref float __result)
    {
        // Matching native build: max(standing speed, 6) plus Falcon's +6.
        // The native result already contains any owned Falcon bonus.
        __result += CustomRewardRuntime.ActiveConditions(__instance)
            .Where(d => d.Effect == CustomRewardEffects.GoalieFetchSpeed).Sum(d => 6f * d.Amount / 100f);
    }
}

[HarmonyPatch(typeof(FollowingPuckStanding), "FixedUpdate")]
internal static class CustomRewardGoalieThrow
{
    [HarmonyPrefix] private static bool Prefix(FollowingPuckStanding __instance)
    {
        var goalie = __instance?.goaltender;
        if (goalie == null || goalie.HaveTalentCached<GoalieThrowStick>()
            || !CustomRewardRuntime.ActiveConditions(goalie).Any(d => d.Effect == CustomRewardEffects.GoalieThrow)) return true;
        // Native FixedUpdate performs this check first and returns after a
        // successful throw. Calling its decision method retains timing, chance,
        // range, equipment, facing and the animation state transition.
        return !__instance.ThrowStick();
    }
}
