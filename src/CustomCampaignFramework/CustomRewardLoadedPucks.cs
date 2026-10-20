using System;
using System.Collections.Generic;
using System.Linq;
using CampaignCore;
using Events;
using Rogue;
using Rogue.Eliminator;
using Skaters;
using Tape2Tape;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EndlessMode;

internal static class CustomRewardLoadedPucks
{
    private sealed class Source {
        internal Forward Player;
        internal CustomRewardDefinition Rule;
        internal LoadedPuck Payload;
        internal LoadedPuckController Controller;
    }
    private static readonly Dictionary<string, Source> sources = new();
    internal static void Refresh(IEnumerable<Forward> players)
    {
        var wanted = new HashSet<string>();
        foreach (var player in players) {
            if (player == null || player.IsBenched() || !player.ArePowerUpsEnabled()) continue;
            var powers = player._skaterData?.powerups;
            for (int i = 0; powers != null && i < powers.Count; i++) {
                var power = powers[i];
                if (power == null || power.PayoffSuppressed || !CustomRewardRuntime.Definitions.TryGetValue(power.id, out var rule)
                    || rule.NeedsRedesign || rule.Kind != CustomRewardDefinition.CustomTalents || rule.Effect != CustomRewardEffects.LoadedPuckBag) continue;
                string key = player.Pointer + "|" + rule.Id; wanted.Add(key);
                if (!sources.ContainsKey(key)) {
                    var payload = ScriptableObject.CreateInstance<LoadedPuck>();
                    payload.name = "CCF_LoadedPuck_" + rule.Id;
                    // The native queue allocates one entry per puck, so very large
                    // bags are scaled down to 10,000 pucks, keeping their mix.
                    long bag = (long)rule.NormalPucks + rule.DoublePucks + rule.TriplePucks;
                    int Scaled(int n) => bag <= 10000 || n == 0 ? n : (int)Math.Max(1, n * 10000L / bag);
                    payload.bagNormalCount = Scaled(rule.NormalPucks); payload.bagTwoGoalCount = Scaled(rule.DoublePucks); payload.bagThreeGoalCount = Scaled(rule.TriplePucks);
                    // No Initialize/AddEffect: only the selected component runs,
                    // with no native goal listener or delayed sync subscription.
                    sources.Add(key, new Source { Player = player, Rule = rule, Payload = payload,
                        Controller = new LoadedPuckController { Talent = payload, skater = player, _forward = player } });
                }
            }
        }
        foreach (var key in sources.Keys.Where(k => !wanted.Contains(k)).ToArray()) Remove(key);
        if (Puck.Instance == null) return;
        foreach (var source in sources.Values) source.Controller.SyncQueue();
    }
    internal static void Scored(GoalScoredArg goal)
    {
        if (goal?.goalType != NHL94.GoalType.GOOD || goal.scorer?.Team == null) return;
        var owner = LoadedPuckController.s_QueueOwner;
        var source = sources.Values.FirstOrDefault(s => owner != null && s.Controller.Pointer == owner.Pointer);
        var queue = source?.Controller._queue;
        if (queue == null || queue.Pointer != EliminatorPuckQueue.Active?.Pointer) return;
        int bonus = Math.Max(0, queue.GetGoalValue() - 1);
        var scoringTeam = goal.scorer.Team;
        // Snapshot the earned value. Losing ownership during a denial window
        // must not discard an accepted goal's points or advance a new queue.
        CustomRewardGoalConfirmation.Defer(() => {
            if (scoringTeam != null && bonus > 0) {
                var stats = scoringTeam.teamStats; stats.Goals += bonus; scoringTeam.teamStats = stats;
                if (source.Player != null) CustomRewardRuntime.Notify(source.Rule, source.Player);
            }
            if (EliminatorPuckQueue.Active?.Pointer == queue.Pointer) queue.AdvanceQueue();
        });
    }
    private static void Remove(string key)
    {
        if (!sources.TryGetValue(key, out var source)) return;
        sources.Remove(key);
        try {
            var queue = source.Controller._queue;
            if (queue != null && EliminatorPuckQueue.Active?.Pointer != queue.Pointer) source.Controller._queue = null;
            source.Controller.TeardownQueue();
        } finally { if (source.Payload != null) Object.Destroy(source.Payload); }
    }
    internal static void Clear() { foreach (var key in sources.Keys.ToArray()) Remove(key); }
}
