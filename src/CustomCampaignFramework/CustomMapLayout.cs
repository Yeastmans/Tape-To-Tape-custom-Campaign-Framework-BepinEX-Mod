using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using CampaignCore;
using DAGG.Generation.Template;
using HarmonyLib;
using STS.Map;
using UnityEngine;

namespace EndlessMode;

/// <summary>Replace only the generator's argument, never the shared base-game assets.</summary>
[HarmonyPatch(typeof(TemplateMapGenerator), nameof(TemplateMapGenerator.GetMap))]
internal static class CustomMapLayout
{
    internal static bool Active;
    private static MapLayout appliedLayout;
    private static STS.Map.Map appliedMap;
    private static int appliedStage;
    private static string appliedFolder;
    private static bool comparingSave;
    private static bool restoring;
    internal static MapLayout GeneratingLayout => Active && appliedMap == null && !Plugin.IsDefaultMode
        && appliedFolder == Plugin.ModFolder ? appliedLayout : null;
    private static string RuntimeFolder => Path.Combine(Plugin.ModFolder, "_map_runtime");
    internal static void ResetForNewRun()
    {
        ClearApplied();
        if (Plugin.IsDefaultMode) return;
        try
        {
            Directory.CreateDirectory(RuntimeFolder);
            File.WriteAllText(Path.Combine(RuntimeFolder, "run-id.txt"), Guid.NewGuid().ToString("N"));
        }
        catch (Exception ex) { Plugin.Log.LogError("[MapEditor] Could not reset map identity: " + ex.Message); }
    }
    internal static void ClearApplied()
    {
        Active = false; appliedLayout = null; appliedMap = null; appliedFolder = null;
        Stadiums.Clear();
    }
    // Authored stadium animation per node, keyed like Plugin.NodeAssignments.
    private static readonly System.Collections.Generic.Dictionary<string, string> Stadiums = new(StringComparer.OrdinalIgnoreCase);
    private static void CommitStadiums(MapLayout layout, int stage)
    {
        Stadiums.Clear();
        foreach (var n in layout.Nodes.Where(n => n.Stadium.Length > 0))
            Stadiums[Plugin.NodeKey(stage, n.Layer, layout.NativeIndex(n))] = n.Stadium;
    }
    internal static string StadiumFor(MapNode node)
    {
        if (Plugin.IsDefaultMode || node == null) return null;
        // Continue can reach SetElite/SetBoss before the prefab's InitializeMap
        // prefix has a manager reference. Resolve the exact saved map now,
        // before native stadium art, door anchors and explosion art are chosen.
        var map = node.GetComponentInParent<MapObject>()?.mapManager?.CurrentMap;
        if (map != null) LayoutFor(map);
        if (!Active || Stadiums.Count == 0) return null;
        var graph = node.Node;
        if (graph == null) return null;
        return Stadiums.TryGetValue(Plugin.NodeKey(appliedStage, graph.LayerIndex, graph.NodeIndex), out var s) ? s : null;
    }
    private static string RunId(bool create)
    {
        string path = Path.Combine(RuntimeFolder, "run-id.txt");
        if (File.Exists(path) && Guid.TryParse(File.ReadAllText(path).Trim(), out var id)) return id.ToString("N");
        if (!create) return "";
        Directory.CreateDirectory(RuntimeFolder);
        string value = Guid.NewGuid().ToString("N"); File.WriteAllText(path, value); return value;
    }
    private static string SaveHash(string value)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(value)));
    }
    private static readonly System.Collections.Generic.Dictionary<string, Il2CppSystem.Collections.Generic.List<MapConfig>> sources = new(StringComparer.OrdinalIgnoreCase);
    internal static void CaptureSources()
    {
        foreach (var squad in Resources.FindObjectsOfTypeAll<State.RunSquadScriptableObject>())
            if (squad != null && !string.IsNullOrEmpty(squad.squadName) && !(squad.id ?? "").StartsWith("Custom_") && squad.maps != null && !sources.ContainsKey(squad.squadName))
                sources[squad.squadName] = squad.maps;
    }
    // Horizontal extent of the layout's source template, i.e. what fills the native background.
    internal static bool NativeXRange(MapLayout layout, out float min, out float max)
    {
        min = float.PositiveInfinity; max = float.NegativeInfinity;
        CaptureSources();
        if (layout == null || layout.BaseMap < 0 || layout.Template < 0) return false;
        string sourceName = MapLayout.ResolveBaseSquad(layout.BaseSquad, sources.Keys);
        if (sourceName == null || !sources.TryGetValue(sourceName, out var maps) || layout.BaseMap >= maps.Count) return false;
        var templates = maps[layout.BaseMap]?.mapTemplates;
        if (templates == null || layout.Template >= templates.Count || templates[layout.Template]?.layers == null) return false;
        foreach (var l in templates[layout.Template].layers)
            if (l?.nodes != null) foreach (var n in l.nodes)
                if (n?.gridPosition != null) { min = Math.Min(min, n.gridPosition.x); max = Math.Max(max, n.gridPosition.x); }
        return max > min;
    }
    internal static void LoadAssignments()
    {
        // Editable files describe future maps, never an already-saved map.
        if (Plugin.IsDefaultMode || !Active || appliedLayout == null || appliedFolder != Plugin.ModFolder) return;
        string prefix = appliedStage + "|";
        foreach (var key in Plugin.NodeAssignments.Keys.Where(k => k.StartsWith(prefix)).ToArray()) Plugin.NodeAssignments.Remove(key);
        foreach (var n in appliedLayout.Nodes.Where(n => n.Team.Length > 0))
            Plugin.NodeAssignments[Plugin.NodeKey(appliedStage, n.Layer, appliedLayout.NativeIndex(n))] = n.Team;
        CommitStadiums(appliedLayout, appliedStage);
    }
    [HarmonyPrefix]
    internal static void Prefix(ref MapConfig config)
    {
        ClearApplied();
        Plugin.LoadNodeAssignments(false);
        if (Plugin.IsDefaultMode || config == null) return;
        string file = Path.Combine(Plugin.ModFolder, "map_layouts", $"map-{Plugin.ActsCompleted + 1}.txt");
        if (!File.Exists(file)) return;
        MapConfig clone = null;
        MapTemplateData template = null;
        try
        {
            var layout = MapLayout.Parse(File.ReadAllText(file));
            var keys = Plugin.ConfigTeamDirs?.Select(Path.GetFileName) ?? Enumerable.Empty<string>();
            var errors = layout.Validate(keys);
            if (errors.Count > 0) throw new ArgumentException(string.Join("; ", errors));
            CaptureSources();
            string sourceName = MapLayout.ResolveBaseSquad(layout.BaseSquad, sources.Keys);
            if (sourceName == null || !sources.TryGetValue(sourceName, out var sourceMaps) || layout.BaseMap >= sourceMaps.Count) throw new ArgumentException("Base squad map is unavailable or ambiguous: " + layout.BaseSquad);
            layout.BaseSquad = sourceName;
            var source = sourceMaps[layout.BaseMap];
            // Vanilla imports and custom maps both use the one editable graph.
            // Unchanged NULL nodes still copy their native weights below.
            if (source?.mapTemplates == null || layout.Template >= source.mapTemplates.Count) throw new ArgumentException("Base map template is unavailable.");
            var original = source.mapTemplates[layout.Template];
            clone = UnityEngine.Object.Instantiate(source);
            clone.act = config.act;
            template = UnityEngine.Object.Instantiate(original);
            template.layers = new Il2CppSystem.Collections.Generic.List<LayerData>();
            foreach (var group in layout.Nodes.GroupBy(n => n.Layer).OrderBy(g => g.Key))
            {
                var layer = new LayerData { layerIndex = group.Key, nodes = new Il2CppSystem.Collections.Generic.List<NodeData>() };
                foreach (var n in group)
                {
                    var type = (DAGG.Core.NodeType)Enum.Parse(typeof(DAGG.Core.NodeType), n.Type);
                    NodeData sourceNode = null;
                    // Find the source node for settings that have not been customized.
                    if (original.layers != null) foreach (var l in original.layers)
                        if (l != null && l.layerIndex == n.SourceLayer && l.nodes != null)
                            foreach (var candidate in l.nodes) if (candidate != null && candidate.nodeIndex == n.SourceNode) sourceNode = candidate;
                    var node = new NodeData { nodeIndex = layout.NativeIndex(n), type = type,
                        gridPosition = new Coordinate { x = (float)n.X, y = (float)n.Y },
                        eliteGroupId = n.EliteGroup, eliteTeamId = n.Team.Length == 0 ? n.EliteTeam : "",
                        talentRewardCount = n.TalentRewards, relicRewardCount = n.RelicRewards, gmSelectionCount = n.GmChoices,
                        outgoing = new Il2CppSystem.Collections.Generic.List<int>(),
                        nodeTypeWeights = CustomRandomEvents.WeightsFor(n, sourceNode) };
                    foreach (string id in n.Outgoing) node.outgoing.Add(layout.NativeIndex(layout.Nodes.First(t => t.Id == id)));
                    layer.nodes.Add(node);
                }
                template.layers.Add(layer);
            }
            clone.mapTemplates = new Il2CppSystem.Collections.Generic.List<MapTemplateData>();
            clone.mapTemplates.Add(template);
            // Commit graph and team assignments together only after construction succeeds.
            Plugin.LoadNodeAssignments(false);
            string prefix = (Plugin.ActsCompleted + 1) + "|";
            foreach (var key in Plugin.NodeAssignments.Keys.Where(k => k.StartsWith(prefix)).ToArray()) Plugin.NodeAssignments.Remove(key);
            foreach (var n in layout.Nodes.Where(n => n.Team.Length > 0))
                Plugin.NodeAssignments[Plugin.NodeKey(Plugin.ActsCompleted + 1, n.Layer, layout.NativeIndex(n))] = n.Team;
            // GM template restoration must not overwrite explicitly authored node types.
            Plugin.GmForcedLayers.Clear(); Plugin.GmOverrideLayer = -1;
            config = clone;
            Active = true;
            appliedLayout = layout; appliedStage = Plugin.ActsCompleted + 1; appliedFolder = Plugin.ModFolder;
            CommitStadiums(layout, appliedStage);
            Plugin.Log.LogInfo($"[MapEditor] Applied map {Plugin.ActsCompleted + 1}: {layout.Nodes.Count} nodes, {layout.Nodes.Count(n => n.Team.Length > 0)} team slots, {layout.Nodes.Count(n => n.Stadium.Length > 0)} stadiums");
            // The previous map has already been consumed by the generator.
            foreach (var old in owned) if (old != null) UnityEngine.Object.Destroy(old);
            owned.Clear();
            owned.Add(clone); owned.Add(template);
        }
        catch (Exception ex)
        {
            ClearApplied();
            Plugin.LoadNodeAssignments(false);
            if (clone != null) UnityEngine.Object.Destroy(clone);
            if (template != null) UnityEngine.Object.Destroy(template);
            Plugin.Log.LogError("[MapEditor] Custom map rejected; using the native map: " + ex.Message);
        }
    }
    [HarmonyPostfix]
    internal static void Generated(STS.Map.Map __result)
    {
        if (!Active) return;
        if (__result == null) { ClearApplied(); Plugin.LoadNodeAssignments(false); return; }
        appliedMap = __result;
    }
    [HarmonyFinalizer]
    internal static Exception GenerationFinished(Exception __exception)
    {
        if (__exception != null)
        {
            ClearApplied(); Plugin.LoadNodeAssignments(false); Plugin.ComputeGmForcedLayers();
            foreach (var old in owned) if (old != null) UnityEngine.Object.Destroy(old);
            owned.Clear();
            Plugin.Log.LogError("[MapEditor] Native map generation failed; authored state rolled back: " + __exception.Message);
        }
        return __exception;
    }
    internal static void SaveReceipt(MapManager manager, string serialized)
    {
        if (comparingSave || !Active || appliedLayout == null || appliedMap == null || Plugin.IsDefaultMode
            || appliedFolder != Plugin.ModFolder || manager.CurrentMap == null
            || manager.CurrentMap.Pointer != appliedMap.Pointer || string.IsNullOrEmpty(serialized)) return;
        try
        {
            string run = RunId(true), hash = SaveHash(serialized);
            string dir = Path.Combine(RuntimeFolder, run); Directory.CreateDirectory(dir);
            var doc = new CampaignDocument("");
            doc.Set("Campaign", Plugin.ActiveCampaign); doc.Set("Run", run); doc.Set("Map Hash", hash);
            doc.Set("Stage", appliedStage.ToString());
            doc.Set("Layout", Convert.ToBase64String(Encoding.UTF8.GetBytes(appliedLayout.Serialize())));
            doc.Save(Path.Combine(dir, hash + ".txt"));
        }
        catch (Exception ex) { Plugin.Log.LogError("[MapEditor] Could not save applied-map receipt: " + ex.Message); }
    }
    internal static void RestoreReceipt(MapManager manager)
    {
        if (restoring || manager == null) return;
        restoring = true;
        try { RestoreReceiptCore(manager); }
        finally { restoring = false; }
    }
    internal static int LookFor(STS.Map.Map map)
    {
        if (Plugin.IsDefaultMode || map == null) return 0;
        if (!Active || appliedMap == null || appliedMap.Pointer != map.Pointer || appliedFolder != Plugin.ModFolder)
        {
            var manager = UnityEngine.Object.FindObjectOfType<MapManager>();
            if (manager == null || manager.CurrentMap == null || manager.CurrentMap.Pointer != map.Pointer) return 0;
            RestoreReceipt(manager);
        }
        return Active && appliedMap != null && appliedMap.Pointer == map.Pointer
            && appliedFolder == Plugin.ModFolder ? appliedLayout?.LookAct ?? 0 : 0;
    }
    internal static MapLayout LayoutFor(STS.Map.Map map)
    {
        LookFor(map); // Resolve a matching resume receipt before using its layout.
        return !Plugin.IsDefaultMode && Active && appliedMap != null && map != null
            && appliedMap.Pointer == map.Pointer && appliedFolder == Plugin.ModFolder ? appliedLayout : null;
    }
    internal static int StageFor(STS.Map.Map map) => LayoutFor(map) != null ? appliedStage : Plugin.ActsCompleted + 1;
    private static void RestoreReceiptCore(MapManager manager)
    {
        // A freshly generated map already has the exact applied snapshot.
        if (Active && appliedMap != null && appliedFolder == Plugin.ModFolder && manager.CurrentMap != null
            && manager.CurrentMap.Pointer == appliedMap.Pointer) return;
        ClearApplied(); Plugin.LoadNodeAssignments(false);
        if (Plugin.IsDefaultMode || manager.CurrentMap == null) return;
        try
        {
            string run = RunId(false); if (run.Length == 0) return;
            string serialized;
            comparingSave = true;
            try { serialized = manager.SaveMap(); }
            finally { comparingSave = false; }
            if (string.IsNullOrEmpty(serialized)) return;
            string hash = SaveHash(serialized), path = Path.Combine(RuntimeFolder, run, hash + ".txt");
            if (!File.Exists(path)) return;
            var doc = new CampaignDocument(File.ReadAllText(path));
            if (doc.Get("Campaign") != Plugin.ActiveCampaign || doc.Get("Run") != run || doc.Get("Map Hash") != hash
                || !int.TryParse(doc.Get("Stage"), out int stage) || stage != Plugin.ActsCompleted + 1) return;
            var layout = MapLayout.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(doc.Get("Layout"))));
            var errors = layout.Validate(Plugin.ConfigTeamDirs?.Select(Path.GetFileName) ?? Enumerable.Empty<string>());
            if (errors.Count > 0) throw new ArgumentException(string.Join("; ", errors));
            appliedLayout = layout; appliedStage = stage; appliedFolder = Plugin.ModFolder; appliedMap = manager.CurrentMap;
            Active = true; LoadAssignments(); Plugin.GmForcedLayers.Clear(); Plugin.GmOverrideLayer = -1;
            Plugin.Log.LogInfo("[MapEditor] Restored authored map from matching native save receipt");
        }
        catch (Exception ex) { ClearApplied(); Plugin.LoadNodeAssignments(false); Plugin.Log.LogError("[MapEditor] Receipt rejected: " + ex.Message); }
    }
    private static readonly System.Collections.Generic.List<UnityEngine.Object> owned = new();
}

// Select scenery through a private copy of the prefab list. The native Map.act,
// generator settings and shared assets remain untouched, including on resume.
[HarmonyPatch(typeof(MapView), "InstantiateMap")]
internal static class CosmeticMapLook
{
    [HarmonyPrefix]
    internal static void Prefix(MapView __instance, STS.Map.Map __0,
        out Il2CppSystem.Collections.Generic.List<GameObject> __state)
    {
        __state = null;
        try
        {
            int look = CustomMapLayout.LookFor(__0);
            var original = __instance.mapPrefabs;
            if (__0 == null || original == null || look < 1 || look > 3
                || look > original.Count || __0.act < 1 || __0.act > original.Count || look == __0.act) return;
            var temporary = new Il2CppSystem.Collections.Generic.List<GameObject>();
            foreach (var prefab in original) temporary.Add(prefab);
            temporary[__0.act - 1] = original[look - 1];
            __state = original;
            __instance.mapPrefabs = temporary;
        }
        catch (Exception ex) { Plugin.Log.LogError("[MapEditor] Cosmetic map look unavailable: " + ex.Message); }
    }
    [HarmonyFinalizer]
    internal static Exception Finished(MapView __instance,
        Il2CppSystem.Collections.Generic.List<GameObject> __state, Exception __exception)
    {
        if (__state != null) __instance.mapPrefabs = __state;
        return __exception;
    }
}

[HarmonyPatch(typeof(MapManager), nameof(MapManager.SaveMap))]
internal static class SaveCustomMapReceipt
{
    [HarmonyPostfix] internal static void Postfix(MapManager __instance, string __result) => CustomMapLayout.SaveReceipt(__instance, __result);
}

// Do not patch MapManager.LoadMap: its RVA is shared with unrelated trivial
// setters, causing receipt loading to recurse during ordinary object writes.
[HarmonyPatch(typeof(MapObject), nameof(MapObject.InitializeMap))]
internal static class RestoreCustomMapReceipt
{
    [HarmonyPrefix]
    internal static void Prefix(MapObject __instance, STS.Map.Map __0)
    {
        // Native InitializeMap obtains and stores this reference inside its
        // body, so a newly instantiated prefab has no field value yet.
        var manager = __instance.mapManager ?? UnityEngine.Object.FindObjectOfType<MapManager>();
        if (manager == null || __0 == null || manager.CurrentMap == null
            || manager.CurrentMap.Pointer != __0.Pointer) return;
        CustomMapLayout.RestoreReceipt(manager);
    }
}
