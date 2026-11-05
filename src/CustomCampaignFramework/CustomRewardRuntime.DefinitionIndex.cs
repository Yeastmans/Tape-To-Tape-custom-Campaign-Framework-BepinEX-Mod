using System;
using System.Collections.Generic;
using CampaignCore;

namespace EndlessMode;

internal static partial class CustomRewardRuntime
{
    private sealed class DefinitionIndex
    {
        internal readonly Dictionary<string, CustomRewardDefinition[]> Effects, Triggers;
        internal readonly Dictionary<(string, string), CustomRewardDefinition[]> Pairs = new();
        internal readonly Dictionary<CustomRewardDefinition, int> Order = new();

        internal DefinitionIndex(IEnumerable<CustomRewardDefinition> definitions)
        {
            var effects = new Dictionary<string, List<CustomRewardDefinition>>(StringComparer.Ordinal);
            var triggers = new Dictionary<string, List<CustomRewardDefinition>>(StringComparer.Ordinal);
            foreach (var rule in definitions) {
                if (rule.NeedsRedesign) continue;
                Order[rule] = Order.Count;
                Add(effects, rule.Effect, rule);
                Add(triggers, rule.Trigger, rule);
            }
            Effects = Freeze(effects);
            Triggers = Freeze(triggers);
        }

        private static void Add(Dictionary<string, List<CustomRewardDefinition>> groups, string key, CustomRewardDefinition rule)
        {
            if (!groups.TryGetValue(key, out var rules)) groups[key] = rules = new();
            rules.Add(rule);
        }
        private static Dictionary<string, CustomRewardDefinition[]> Freeze(Dictionary<string, List<CustomRewardDefinition>> groups)
        {
            var result = new Dictionary<string, CustomRewardDefinition[]>(groups.Count, StringComparer.Ordinal);
            foreach (var group in groups) result.Add(group.Key, group.Value.ToArray());
            return result;
        }
    }

    private static DefinitionIndex catalogIndex;
    private static long catalogIndexGeneration;
    private static DefinitionIndex PublishedDefinitionIndex()
    {
        if (!loaded) EnsureAssets();
        if (catalogIndex == null || catalogIndexGeneration != definitionGeneration) {
            catalogIndex = new DefinitionIndex(Definitions.Values);
            catalogIndexGeneration = definitionGeneration;
        }
        return catalogIndex;
    }
    internal static CustomRewardDefinition[] DefinitionsForEffect(string effect)
        => PublishedDefinitionIndex().Effects.TryGetValue(effect, out var rules) ? rules : Array.Empty<CustomRewardDefinition>();
    internal static CustomRewardDefinition[] DefinitionsForTrigger(string trigger)
        => PublishedDefinitionIndex().Triggers.TryGetValue(trigger, out var rules) ? rules : Array.Empty<CustomRewardDefinition>();
    internal static CustomRewardDefinition[] DefinitionsForEffects(string first, string second)
    {
        var index = PublishedDefinitionIndex();
        bool hasFirst = index.Effects.TryGetValue(first, out var a);
        bool hasSecond = index.Effects.TryGetValue(second, out var b);
        if (!hasFirst) return hasSecond ? b : Array.Empty<CustomRewardDefinition>();
        if (!hasSecond || first == second) return a;
        if (!index.Pairs.TryGetValue((first, second), out var result)) {
            // Boss notifications, random rolls, and equal-priority choices must
            // see their original catalog order when two effect families apply.
            result = new CustomRewardDefinition[a.Length + b.Length];
            int left = 0, right = 0;
            for (int i = 0; i < result.Length; i++) {
                if (right == b.Length || left < a.Length && index.Order[a[left]] < index.Order[b[right]]) result[i] = a[left++];
                else result[i] = b[right++];
            }
            index.Pairs[(first, second)] = result;
        }
        return result;
    }
}
