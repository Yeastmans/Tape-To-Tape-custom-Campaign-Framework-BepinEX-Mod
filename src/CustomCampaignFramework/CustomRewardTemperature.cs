using System;
using System.Collections.Generic;
using System.Linq;
using CampaignCore;
using Skaters;
using Tape2Tape;

namespace EndlessMode;

internal static class CustomRewardTemperature
{
    private static readonly Dictionary<IntPtr, (IntPtr Hot, IntPtr Cold)> pairs = new();
    internal static void Initialize(Team team)
    {
        if (!CustomRewardRuntime.InMatch || team?._teamData == null || pairs.ContainsKey(team.Pointer)) return;
        var rules = CustomRewardRuntime.Definitions.Values.Where(d => !d.NeedsRedesign && CustomRewardEffects.IsTemperature(d.Effect) && CustomRewardRuntime.Has(team._teamData, d.Id)).ToArray();
        if (rules.Length == 0) return;
        var players = team.PlayingForwards?.ToArray().Where(f => f != null).ToList();
        if (players == null || players.Count == 0) return;
        var hot = players[UnityEngine.Random.Range(0, players.Count)]; players.Remove(hot);
        var cold = players.Count == 0 ? null : players[UnityEngine.Random.Range(0, players.Count)];
        pairs[team.Pointer] = (hot.Pointer, cold?.Pointer ?? IntPtr.Zero);
        foreach (var rule in rules) {
            var recipient = rule.Effect == CustomRewardEffects.HotAttribute ? hot : cold;
            if (recipient != null) CustomRewardRuntime.Notify(rule, recipient);
        }
    }
    internal static long Bonus(Skater player, string stat)
    {
        if (!CustomRewardDefinition.IsOverallStat(stat) || player?.Team?._teamData == null || !pairs.TryGetValue(player.Team.Pointer, out var pair)) return 0;
        long result = 0;
        foreach (var rule in CustomRewardRuntime.Definitions.Values) {
            if (rule.NeedsRedesign || !CustomRewardEffects.IsTemperature(rule.Effect) || !CustomRewardRuntime.Has(player.Team._teamData, rule.Id)) continue;
            if (rule.Effect == CustomRewardEffects.HotAttribute && player.Pointer == pair.Hot) result += rule.Amount;
            if (rule.Effect == CustomRewardEffects.ColdAttribute && player.Pointer == pair.Cold) result -= rule.Amount;
        }
        return result;
    }
    internal static void Clear() => pairs.Clear();
}
