using System;
using System.Collections.Generic;
using System.Linq;

namespace CampaignCore;

public static class RewardRouteBudget
{
    // Maximum consumption on one reachable entry-to-boss route. Parallel
    // branches are alternatives, while successive maps consume the same pool.
    public static int Maximum(MapLayout map, Func<MapLayoutNode, int> cost)
    {
        var nodes = map.Nodes.ToDictionary(n => n.Id);
        var totals = new Dictionary<string, int>();
        var visiting = new HashSet<string>();
        int Visit(MapLayoutNode node)
        {
            if (totals.TryGetValue(node.Id, out int saved)) return saved;
            if (!visiting.Add(node.Id)) throw new InvalidOperationException("Reward reserve needs a map without cycles.");
            int tail = node.Outgoing.Where(nodes.ContainsKey).Select(id => Visit(nodes[id])).DefaultIfEmpty(0).Max();
            visiting.Remove(node.Id);
            return totals[node.Id] = checked(Math.Max(0, cost(node)) + tail);
        }
        if (map.Nodes.Count == 0) return 0;
        int first = map.Nodes.Min(n => n.Layer);
        return map.Nodes.Where(n => n.Layer == first).Select(Visit).DefaultIfEmpty(0).Max();
    }

    public static int Minimum(int selections) => Math.Max(3, checked(selections + 2));

    public static int NodeSelections(MapLayoutNode node, string kind, bool extraMatchReward = false)
    {
        int ForType(string type)
        {
            if (type is "Elite" or "Boss" or "Challenge")
                return (kind == "Relics" ? node.RelicRewards : kind == "Talents" ? node.TalentRewards : 0)
                    + (extraMatchReward && (kind == "Relics" || kind == "Talents") ? 1 : 0);
            // Reserve one acquisition for each possible event result. Some
            // events upgrade/heal instead; budgeting acquisition is conservative.
            return kind switch {
                "Relics" => type == "Coach" ? 1 : 0,
                // Recruitment extras use their independent free-agent pool and
                // do not consume entries from the Regular reward allow-list.
                "Talents" => type is "Nurse" or "FanNumber1" ? 1 : 0,
                RewardPoolDefinition.ChaosTalents => type == "Chaos" ? 1 : 0,
                "Goalie Talents" => type is "FanNumber1" ? 1 : 0,
                "Abilities" => type is "Blademaster" or "FanNumber1" ? 1 : 0,
                _ => 0
            };
        }
        if (node.Type != "NULL") return ForType(node.Type);
        IReadOnlyDictionary<string, decimal> weights = node.RandomWeights != null ? node.RandomWeights : RandomNodePool.Defaults;
        return weights.Where(p => p.Value > 0).Select(p => ForType(p.Key)).DefaultIfEmpty(0).Max();
    }
}
