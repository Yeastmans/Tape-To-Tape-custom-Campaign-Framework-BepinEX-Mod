using System;
using System.Collections.Generic;
using CampaignCore;
using DAGG.Core;
using DAGG.Generation;
using DAGG.Generation.Template;
using HarmonyLib;

namespace EndlessMode;

internal static class CustomRandomEvents
{
    // Pointer identity limits the corrected roll to pools on this authored graph.
    // Thread-local scope also prevents unrelated or nested map conversions using it.
    [ThreadStatic] private static Dictionary<IntPtr, IReadOnlyDictionary<string, decimal>> pools;

    internal static Il2CppSystem.Collections.Generic.List<NodeTypeWeight> WeightsFor(MapLayoutNode authored, NodeData source)
    {
        var weights = new Il2CppSystem.Collections.Generic.List<NodeTypeWeight>();
        if (authored.Type == "NULL" && authored.RandomWeights != null)
        {
            // Replace the inherited pool. Missing and zero-weight entries are excluded.
            foreach (var entry in authored.RandomWeights)
                if (entry.Value > 0)
                    weights.Add(new NodeTypeWeight { type = (NodeType)Enum.Parse(typeof(NodeType), entry.Key), weight = (float)entry.Value });
        }
        else if (source?.nodeTypeWeights != null)
        {
            // Unedited maps retain their native pool; never mutate shared template assets.
            foreach (var entry in source.nodeTypeWeights)
                if (entry != null) weights.Add(new NodeTypeWeight { type = entry.type, weight = entry.weight });
        }
        return weights;
    }

    internal static Dictionary<IntPtr, IReadOnlyDictionary<string, decimal>> Enter(Graph graph, MapLayout layout)
    {
        var previous = pools;
        pools = new();
        if (layout != null && graph != null)
            foreach (var authored in layout.Nodes)
            {
                if (authored.Type != "NULL" || authored.RandomWeights == null) continue;
                var node = graph.GetNode(authored.Layer, layout.NativeIndex(authored));
                if (node?.type != NodeType.NULL || node.CustomNodeTypeChances == null) continue;
                pools[node.CustomNodeTypeChances.Pointer] = authored.RandomWeights;
            }
        return previous;
    }

    internal static void Exit(Dictionary<IntPtr, IReadOnlyDictionary<string, decimal>> previous) => pools = previous;

    internal static bool TryPick(Il2CppSystem.Collections.Generic.Dictionary<NodeType, float> weights,
        Il2CppSystem.Random random, out NodeType result)
    {
        result = NodeType.NULL;
        if (weights == null || pools == null || !pools.TryGetValue(weights.Pointer, out var authored)) return false;
        double sample = (random ?? new Il2CppSystem.Random()).NextDouble();
        // Authored positive weights override neighbor, layer, Nurse and Solo filters.
        string selected = RandomNodePool.Pick(authored, sample);
        if (selected != null) result = (NodeType)Enum.Parse(typeof(NodeType), selected);
        return true;
    }

    internal static List<(Node Node, NodeType Type)> CaptureCustomChoices(Graph graph, MapLayout layout, bool includePendingRandom = false)
    {
        var choices = new List<(Node, NodeType)>();
        if (graph != null && layout != null)
            foreach (var authored in layout.Nodes)
            {
                var node = graph.GetNode(authored.Layer, layout.NativeIndex(authored));
                if (node == null) continue;
                // A fixed type saved in the custom layout is authoritative, even
                // when the node retains source indices from an imported map.
                // An unconfigured NULL still inherits native replacement rules.
                bool fixedType = authored.Type != "NULL" && authored.Type == node.type.ToString();
                bool customRoll = authored.Type == "NULL" && authored.RandomWeights != null
                    && (includePendingRandom && node.type == NodeType.NULL
                        || authored.RandomWeights.TryGetValue(node.type.ToString(), out var weight) && weight > 0);
                if (fixedType || customRoll) choices.Add((node, node.type));
            }
        return choices;
    }
}

// Native GM placement runs before the random picker and can consume any NULL
// node. Hide only explicit pools from that candidate list, then restore them
// for the weighted roll. Untouched native nodes remain eligible for placement.
[HarmonyPatch(typeof(TemplateToGraphConverter), "HandleGeneralManagerNode")]
internal static class PreserveAuthoredGeneralManagerNodes
{
    [HarmonyPrefix]
    internal static void Prefix(Graph __0, out List<(Node Node, NodeType Type)> __state)
    {
        __state = CustomRandomEvents.CaptureCustomChoices(__0, CustomMapLayout.GeneratingLayout, includePendingRandom: true);
        foreach (var choice in __state)
            if (choice.Type == NodeType.NULL) choice.Node.type = NodeType.Coach;
    }

    [HarmonyFinalizer]
    internal static Exception Finalizer(Exception __exception, List<(Node Node, NodeType Type)> __state) =>
        PreserveCustomRandomEvents.Finalizer(__exception, __state);
}

[HarmonyPatch(typeof(TemplateToGraphConverter), "HandleOldSchoolNodeReplacements")]
internal static class PreserveAuthoredOldSchoolNodes
{
    [HarmonyPrefix]
    internal static void Prefix(Graph __0, out List<(Node Node, NodeType Type)> __state) =>
        __state = CustomRandomEvents.CaptureCustomChoices(__0, CustomMapLayout.GeneratingLayout);

    [HarmonyFinalizer]
    internal static Exception Finalizer(Exception __exception, List<(Node Node, NodeType Type)> __state) =>
        PreserveCustomRandomEvents.Finalizer(__exception, __state);
}

[HarmonyPatch(typeof(TemplateToGraphConverter), "FillRemainingNodeTypesRandomly")]
internal static class CustomRandomEventScope
{
    [HarmonyPrefix]
    internal static void Prefix(Graph __0, out Dictionary<IntPtr, IReadOnlyDictionary<string, decimal>> __state) =>
        __state = CustomRandomEvents.Enter(__0, CustomMapLayout.GeneratingLayout);

    [HarmonyFinalizer]
    internal static Exception Finalizer(Exception __exception, Dictionary<IntPtr, IReadOnlyDictionary<string, decimal>> __state)
    {
        CustomRandomEvents.Exit(__state);
        return __exception;
    }
}

[HarmonyPatch(typeof(DAGG.Utilities), nameof(DAGG.Utilities.GetRandomNodeType))]
internal static class CustomRandomEventRoll
{
    [HarmonyPrefix]
    internal static bool Prefix(Il2CppSystem.Collections.Generic.Dictionary<NodeType, float> __0,
        Il2CppSystem.Collections.Generic.List<NodeType> __1, Il2CppSystem.Random __2, ref NodeType __result)
    {
        // __1 is deliberately ignored only when this is a registered custom pool.
        if (!CustomRandomEvents.TryPick(__0, __2, out var selected)) return true;
        __result = selected;
        return false;
    }
}

// Some squad templates replace Chaos/Blademaster after the random roll. Keep
// fixed authored types and exact custom choices; inherited nodes still convert.
[HarmonyPatch(typeof(TemplateToGraphConverter), "ReplaceSpecialNodesWithTraining")]
internal static class PreserveCustomRandomEvents
{
    [HarmonyPrefix]
    internal static void Prefix(Graph __0, out List<(Node Node, NodeType Type)> __state) =>
        __state = CustomRandomEvents.CaptureCustomChoices(__0, CustomMapLayout.GeneratingLayout);

    [HarmonyFinalizer]
    internal static Exception Finalizer(Exception __exception, List<(Node Node, NodeType Type)> __state)
    {
        if (__state != null) foreach (var choice in __state) choice.Node.type = choice.Type;
        return __exception;
    }
}
