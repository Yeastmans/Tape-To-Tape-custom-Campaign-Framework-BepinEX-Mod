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
    private static readonly Dictionary<IntPtr, (CustomRewardDefinition[] Loadouts, long Hot, long Cold)> bonuses = new();
    internal static void Initialize(Team team)
    {
        if (!CustomRewardRuntime.InMatch || team?._teamData == null || pairs.ContainsKey(team.Pointer)) return;
        var rules = CustomRewardRuntime.DefinitionsForEffects(CustomRewardEffects.HotAttribute, CustomRewardEffects.ColdAttribute)
            .Where(d => CustomRewardRuntime.Has(team._teamData, d.Id)).ToArray();
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
        if (pairs.Count == 0 || !CustomRewardDefinition.IsOverallStat(stat) || player?.Team is not { } team
            || !pairs.TryGetValue(team.Pointer, out var pair) || player.Pointer != pair.Hot && player.Pointer != pair.Cold) return 0;
        var equipped = CustomRewardRuntime.InPlay;
        if (!bonuses.TryGetValue(team.Pointer, out var bonus) || !ReferenceEquals(bonus.Loadouts, equipped)) {
            long hot = 0, cold = 0;
            foreach (var rule in equipped) {
                if (rule.NeedsRedesign || !CustomRewardEffects.IsTemperature(rule.Effect) || !CustomRewardRuntime.Has(team._teamData, rule.Id)) continue;
                if (rule.Effect == CustomRewardEffects.HotAttribute) hot += rule.Amount;
                else cold -= rule.Amount;
            }
            bonuses[team.Pointer] = bonus = (equipped, hot, cold);
        }
        return player.Pointer == pair.Hot ? bonus.Hot : bonus.Cold;
    }
    internal static void Clear() { pairs.Clear(); bonuses.Clear(); }
}
