using System;
using System.Collections.Generic;
using Data;
using STS.Map;
using STS.NodeRewards;
using UnityEngine;

namespace EndlessMode;

// Studio's per-node stadium (map_layouts "Stadium <node>") on the world map.
//
// A stadium is an animation on the node's Spine skeleton, and the game takes it as
// an argument (disassembled, build 25595829):
// - EliteMapNode.SetElite(team, eliteSkin, scale) plays eliteSkin on skeletonSprite
//   and passes it to SetStadium, which places the door/explosion anchors for it.
// - BossMapNode.SetBoss has no callers: MapObject.SetBoss inlines it and reads
//   ActBoss.stadiumAnimation straight off the ActBoss it picks.
// So the elite argument is swapped, and for the boss every candidate ActBoss points
// at the authored stadium for that one call.
internal static class NodeStadiums
{
    private static readonly HashSet<string> warned = new(StringComparer.OrdinalIgnoreCase);

    // The authored stadium for this node, or null to keep the game's choice.
    private static string For(MatchMapNode node, string native)
    {
        string wanted = CustomMapLayout.StadiumFor(node);
        if (string.IsNullOrEmpty(wanted) || wanted == native) return null;
        if (!HasAnimation(node, wanted))
        {
            if (warned.Add(wanted))
                Plugin.Log.LogWarning($"[Stadium] '{wanted}' is not an animation on the map node skeleton — keeping the game's stadium.");
            return null;
        }
        Plugin.Log.LogInfo($"[Stadium] Layer {node.Node.LayerIndex} node {node.Node.NodeIndex}: '{native ?? "game pick"}' -> '{wanted}'");
        return wanted;
    }

    // AnimationState.AddAnimation throws on an unknown name, so check first.
    private static bool HasAnimation(MapNode node, string name)
    {
        try
        {
            var skel = node.skeletonSprite;
            if (skel == null) return false;
            var data = skel.Skeleton != null ? skel.Skeleton.Data : null;
            if (data == null && skel.skeletonDataAsset != null) data = skel.skeletonDataAsset.GetSkeletonData(true);
            return data != null && data.FindAnimation(name) != null;
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[Stadium] animation check: {ex.Message}"); return false; }
    }

    public static void ElitePrefix(EliteMapNode __instance, ref string __1)
    {
        try
        {
            string stadium = For(__instance, __1);
            if (stadium != null) __1 = stadium;
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[Stadium] elite: {ex.Message}"); }
    }

    public static void BossPrefix(Il2CppSystem.Collections.Generic.List<ActBoss> __0, BossMapNode __1,
        out List<KeyValuePair<ActBoss, string>> __state)
    {
        __state = null;
        try
        {
            if (__0 == null || __1 == null) return;
            string stadium = For(__1, null);
            if (stadium == null) return;
            __state = new List<KeyValuePair<ActBoss, string>>();
            for (int i = 0; i < __0.Count; i++)
            {
                var boss = __0[i];
                if (boss == null) continue;
                __state.Add(new KeyValuePair<ActBoss, string>(boss, boss.stadiumAnimation));
                boss.stadiumAnimation = stadium;
            }
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[Stadium] boss: {ex.Message}"); }
    }

    // ActBoss entries are the shared campaign config: always put them back.
    public static Exception BossFinalizer(Exception __exception, List<KeyValuePair<ActBoss, string>> __state)
    {
        if (__state != null)
            foreach (var entry in __state)
                try { entry.Key.stadiumAnimation = entry.Value; } catch { }
        return __exception;
    }
}

// The campaign team's logo above its map node.
//
// SetElite/SetBoss end with NodeRewardUI.Clear(), which deactivates the icon's
// GameObject and hides the bubble, so writing the logo into that Image (the July
// fix) no longer draws anything. SetRewardIconRaw is the UI's own "show this sprite":
// world-size scale, fit to the sprite's opaque area (skipped for unreadable
// textures), activate, bubble.
//
// Same rule as the Studio's node preview: only a team with its own logo (Logo From,
// else Import Team) shows one. Otherwise the node stays as the game draws elites —
// stadium only — rather than showing the native team's logo it was cloned from.
internal static class NodeLogo
{
    internal static bool HasOwnLogo(TeamConfig cfg)
    {
        string source = !string.IsNullOrWhiteSpace(cfg.LogoFrom) ? cfg.LogoFrom : cfg.ImportTeam;
        return !string.IsNullOrWhiteSpace(source) && !source.Trim().Equals("RANDOM", StringComparison.OrdinalIgnoreCase);
    }

    // True when the node has the game's reward UI, i.e. its logo is handled here
    // (shown, or deliberately absent for native teams and teams without a logo).
    internal static bool Show(MatchMapNode node, TeamData team)
    {
        if (node == null || team == null) return false;
        var ui = node.nodeRewardUI;
        if (ui == null) ui = node.GetComponentInChildren<NodeRewardUI>(true);
        if (ui == null) return false;
        var cfg = CampaignOpponents.ConfigFor(team);
        if (cfg == null) return true;

        Sprite logo = HasOwnLogo(cfg) ? team.logo : null;
        var icon = ui.icon;
        bool showing = icon != null && icon.gameObject.activeSelf && logo != null && icon.overrideSprite == logo;
        if (logo == null) return true;
        // A beaten node has the game's post-match flag and explosion; take the logo off.
        if (node.CurrentState == NodeStates.Visited)
        {
            if (showing) ui.Clear();
            return true;
        }
        if (!showing)
        {
            ui.SetRewardIconRaw(logo);
            Plugin.Log.LogInfo($"[NodeArt] '{team.teamName}': logo '{logo.name}' shown above layer {node.Node?.LayerIndex} node {node.Node?.NodeIndex}.");
        }
        return true;
    }
}
