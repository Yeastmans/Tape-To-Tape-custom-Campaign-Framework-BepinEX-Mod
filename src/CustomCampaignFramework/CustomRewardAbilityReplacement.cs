using System;
using System.Collections.Generic;
using System.Linq;
using CampaignCore;
using Rogue;
using Rogue.Powerups.Repository;
using Skaters;
using Tape2Tape;

namespace EndlessMode;

internal static class CustomRewardAbilityReplacement
{
    private sealed class Grant { internal string Reward, Owner; internal Ability Ability; }
    private sealed class State {
        internal Forward Recipient; internal Ability Original, Applied;
        internal readonly List<Grant> Grants = new();
    }
    private static readonly Dictionary<IntPtr, State> states = new();
    private static Ability EffectiveAbility(Forward player)
        => player.statusEffectsManager?.GetStatusEffect<FrogStatusEffect>()?.TryCast<FrogStatusEffectController>() is { } frog
            ? frog.AbilityOnUnfrog : player._skaterData.ability;
    internal static bool Apply(CustomRewardDefinition rule, Skater source, Forward recipient)
    {
        var repository = CampaignUtil.Instance?._abilityRepository;
        if (recipient == null || source?._skaterData == null || repository == null || recipient.statusEffectsManager?.IsFrogged() == true) return false;
        Refresh();
        var selected = repository.GetRandomAbility(TalentCategory.UsedInCampaignPool, null, true, false, true, 1);
        if (selected == null) return false;
        if (!states.TryGetValue(recipient.Pointer, out var state)) {
            state = new State { Recipient = recipient, Original = EffectiveAbility(recipient) };
            states.Add(recipient.Pointer, state);
        }
        var grant = new Grant { Reward = rule.Id, Owner = rule.IsRelic ? "team:" + source.Team._teamData.id : "player:" + source._skaterData.id, Ability = selected };
        recipient.AddAbility(selected, true);
        state.Grants.RemoveAll(g => g.Reward == grant.Reward && g.Owner == grant.Owner);
        state.Grants.Add(grant); state.Applied = selected;
        return true;
    }
    internal static void Refresh()
    {
        foreach (var pair in states.ToArray()) {
            var state = pair.Value;
            if (state.Recipient == null) { states.Remove(pair.Key); continue; }
            // A later native ability grant owns the slot now. Never overwrite it.
            if (EffectiveAbility(state.Recipient)?.Pointer != state.Applied?.Pointer) { states.Remove(pair.Key); continue; }
            if (state.Grants.RemoveAll(g => !CustomRewardRuntime.StillOwned(g.Reward, g.Owner)) == 0) continue;
            var next = state.Grants.LastOrDefault()?.Ability ?? state.Original;
            if (next?.Pointer != state.Applied?.Pointer) state.Recipient.AddAbility(next, true);
            state.Applied = next;
            if (state.Grants.Count == 0) states.Remove(pair.Key);
        }
    }
    internal static void Clear()
    {
        foreach (var state in states.Values.ToArray()) if (state.Recipient != null && EffectiveAbility(state.Recipient)?.Pointer == state.Applied?.Pointer)
            CustomRewardRuntime.Safe(() => state.Recipient.AddAbility(state.Original, true));
        states.Clear();
    }
}
