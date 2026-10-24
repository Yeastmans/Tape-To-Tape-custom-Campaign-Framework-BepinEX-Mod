using System;
using CampaignCore;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Spine.Unity;
using STS.Map;
using STS.NodeRewards;
using STS.Scripts.Components.MapNodes;
using UnityEngine;
using UnityEngine.UI;

namespace EndlessMode;

// Read only the actual node artwork and its reward bubble, not invisible
// hitboxes, tooltip containers, explosion templates or all descendant rects.
internal static class CustomMapVisualBounds
{
    internal static bool TryMeasure(MapNode node, bool initializePose, out MapVisualFit.Envelope envelope)
    {
        envelope = default;
        MapVisualFit.Envelope measured = default;
        bool found = false;
        void Add(float minX, float maxX, float minY, float maxY)
        {
            if (!float.IsFinite(minX) || !float.IsFinite(maxX) || !float.IsFinite(minY) || !float.IsFinite(maxY)
                || maxX <= minX || maxY <= minY) return;
            var part = new MapVisualFit.Envelope(minX, maxX, minY, maxY);
            measured = found ? measured.Union(part) : part;
            found = true;
        }
        var skeleton = node.skeletonSprite;
        var customCoachArt = CustomCoachPresentation.Renderer(node);
        if (customCoachArt != null) {
            var b = customCoachArt.bounds; Add(b.min.x, b.max.x, b.min.y, b.max.y);
        }
        if (skeleton != null && customCoachArt == null)
        {
            if (initializePose) PrepareNativeEventAnimation(node, skeleton);
            if (initializePose && skeleton.IsValid)
            {
                // SetElite has selected the stadium animation, but Unity may
                // not yet have generated its first mesh at the pre-link hook.
                // Off-screen columns can use a reduced update mode. Generate
                // one zero-time pose/mesh, then restore that native setting.
                var spineRenderer = skeleton.GetComponent<SkeletonRenderer>();
                if (spineRenderer != null)
                {
                    var mode = spineRenderer.UpdateMode;
                    try
                    {
                        spineRenderer.UpdateMode = Spine.Unity.UpdateMode.FullUpdate;
                        skeleton.Update(0);
                        spineRenderer.UpdateMesh(true);
                    }
                    finally { spineRenderer.UpdateMode = mode; }
                }
            }
            foreach (var renderer in skeleton.GetComponentsInChildren<Renderer>(false))
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                var b = renderer.bounds;
                Add(b.min.x, b.max.x, b.min.y, b.max.y);
            }
        }
        // A badge alone cannot establish the stadium's full footprint.
        if (!found) return false;
        var ui = node.GetComponent<NodeRewardUI>();
        if (ui == null) { envelope = measured; return true; }
        var corners = new Il2CppStructArray<Vector3>(4);
        void AddCorners(RectTransform rect, RectTransform animatedContainer = null, float? anchoredY = null,
            float? containerSize = null)
        {
            if (rect == null || !rect.gameObject.activeInHierarchy) return;
            rect.GetWorldCorners(corners);
            float minX = float.PositiveInfinity, minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
            for (int i = 0; i < 4; i++)
            {
                var c = corners[i];
                if (animatedContainer != null)
                {
                    var local = animatedContainer.InverseTransformPoint(c);
                    var current = animatedContainer.rect;
                    if (containerSize.HasValue && current.width > .001f && current.height > .001f)
                    {
                        // The native bubble has stretch anchors and zero size
                        // delta. Its opaque icon fills this same container.
                        local.x *= containerSize.Value / current.width;
                        local.y *= containerSize.Value / current.height;
                    }
                    c = animatedContainer.TransformPoint(local);
                    if (anchoredY.HasValue && animatedContainer.parent != null)
                        c += animatedContainer.parent.TransformVector(new Vector3(0,
                            anchoredY.Value - animatedContainer.anchoredPosition.y, 0));
                }
                minX = Math.Min(minX, c.x); maxX = Math.Max(maxX, c.x);
                minY = Math.Min(minY, c.y); maxY = Math.Max(maxY, c.y);
            }
            // Padding covers outline/filtering after reserving the complete
            // selected animation, rather than standing in for that animation.
            Add(minX - MapVisualFit.BadgePadding, maxX + MapVisualFit.BadgePadding,
                minY - MapVisualFit.BadgePadding, maxY + MapVisualFit.BadgePadding);
        }
        var container = ui.icon?.rectTransform.parent.TryCast<RectTransform>();
        void AddBadge(RectTransform rect)
        {
            AddCorners(rect);
            if (container == null || (rect.Pointer != container.Pointer && !rect.IsChildOf(container))) return;
            // Native Highlight overshoots both size and anchored Y. Project
            // these poses without changing UI state, playing the Animator or
            // depending on which node happens to be selected during measurement.
            foreach (float size in new[] { MapVisualFit.BadgeNormalSize, MapVisualFit.BadgeMaxSize })
                foreach (float y in new[] { MapVisualFit.BadgeMinY, MapVisualFit.BadgeMaxY })
                    AddCorners(rect, container, y, size);
        }
        if (ui.bubble != null && ui.bubble.activeInHierarchy)
            foreach (var graphic in ui.bubble.GetComponentsInChildren<Graphic>(false))
                if (graphic != null && graphic.enabled) AddBadge(graphic.rectTransform);
        if (ui.icon != null && ui.icon.enabled && ui.icon.gameObject.activeInHierarchy)
        {
            // ShowIcon expands the sprite's rect to compensate transparent
            // pixels. Its container is the visible opaque-content size.
            AddBadge(container ?? ui.icon.rectTransform);
        }
        if (ui.rewardText != null && ui.rewardText.enabled && ui.rewardText.gameObject.activeInHierarchy)
        {
            var text = ui.rewardText.rectTransform;
            AddCorners(text);
            AddCorners(text, text, MapVisualFit.RewardTextMinY);
            AddCorners(text, text, MapVisualFit.RewardTextMaxY);
        }
        envelope = measured;
        return true;
    }

    private static void PrepareNativeEventAnimation(MapNode node, SkeletonAnimation skeleton)
    {
        // These native Start methods only select their act-indexed animation.
        // Their serialized prefab animation may be blank or a different event
        // (for example Coach starts as Chopper), so measure the actual native
        // choice now. The later Start repeats the same assignment.
        var skins = node.TryCast<CoachMapNode>()?.coachSkin
            ?? node.TryCast<GeneralManagerMapNode>()?.goalieTrainingNodeSkins
            ?? node.TryCast<FanNumber1MapNode>()?.freeAgentNodeSkins
            ?? node.TryCast<BlademasterMapNode>()?.abilityUpgradeNodeSkins
            ?? node.TryCast<NurseMapNode>()?.trainingNodeSkins
            ?? node.TryCast<ChaosMapNode>()?.restNodeSkins
            ?? node.TryCast<ConditioningMapNode>()?.eventNodeSkins;
        if (skins == null) return;
        int act = Tape2Tape.CampaignUtil.CampaignState?.runData?.CurrentAct ?? 0;
        if (act < 1 || act > skins.Count) return;
        string animation = skins[act - 1];
        if (!string.IsNullOrEmpty(animation) && skeleton.AnimationName != animation)
            skeleton.AnimationName = animation;
    }
}
