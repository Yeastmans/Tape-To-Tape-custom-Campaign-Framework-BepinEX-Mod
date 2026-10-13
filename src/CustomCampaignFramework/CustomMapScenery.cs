using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using CampaignCore;
using HarmonyLib;
using STS.Map;
using UnityEngine;
using Tape2Tape.Hockey.UI;

namespace EndlessMode;

// Called after regular nodes exist, before the native boss snap and link creation.
// Horizontal spacing is projected onto a whole scenery length without compression. Authored
// coordinates remain unchanged, and the boss uses the shifted native ending anchor.
[HarmonyPatch(typeof(MapObject), "SnapBossToAnchorPoint")]
internal static class CustomMapScenery
{
    private sealed class Built
    {
        internal MapObject Map;
        internal GameObject Root;
        internal readonly List<UnityEngine.Object> Assets = new();
        internal readonly List<Action> Rollback = new();
        internal Transform ExternalIntroMarker;
        internal Vector3 IntroBefore, IntroAfter;
        internal Camera Camera;
        internal float CameraBefore, CameraAfter;
        internal WorldMapMenusHandler Menus;
        internal float MenuOrthoBefore;
        internal WorldMapActIntroStart Intro;
        internal float ZoomBefore, ZoomAfter;
        internal readonly List<(SpriteRenderer Renderer, Vector3 Before, Vector3 After)> Curtains = new();
    }
    private static readonly List<Built> builds = new();

    internal static void Release(MapObject map)
    {
        foreach (var built in builds.Where(b => b.Map == null || b.Map.Pointer == map.Pointer).ToArray())
            ReleaseBuilt(built);
    }

    private static void ReleaseBuilt(Built built)
    {
        ReleasePresentation(built);
        foreach (var asset in built.Assets) if (asset != null) UnityEngine.Object.Destroy(asset);
        builds.Remove(built);
    }

    private static void ReleasePresentation(Built built)
    {
        // A scene-level marker can outlive the map. Restore only our own last
        // position, never a newer map's position or another animation's change.
        if (built.ExternalIntroMarker != null && built.ExternalIntroMarker.position == built.IntroAfter)
            built.ExternalIntroMarker.position = built.IntroBefore;
        if (built.Intro != null && built.Intro._zoom == built.ZoomAfter)
            built.Intro._zoom = built.ZoomBefore;
        // Only the exact map camera/property we changed is owned. Match cameras,
        // replacement maps and unrelated zoom animations are never overwritten.
        if (built.Camera != null && built.Camera.orthographicSize == built.CameraAfter)
            built.Camera.orthographicSize = built.CameraBefore;
        if (built.Menus != null && built.Menus.m_CamInitialOrtho == built.CameraAfter)
            built.Menus.m_CamInitialOrtho = built.MenuOrthoBefore;
        foreach (var curtain in built.Curtains)
            if (curtain.Renderer != null && curtain.Renderer.transform.position == curtain.After)
                curtain.Renderer.transform.position = curtain.Before;
        built.Curtains.Clear();
        built.ExternalIntroMarker = null; built.Intro = null; built.Camera = null; built.Menus = null;
    }

    internal static bool TryPreviewFrame(MapNode node, Camera camera, out float halfHeight)
    {
        halfHeight = 0;
        if (Plugin.IsDefaultMode || node == null || camera == null || !camera.orthographic) return false;
        var map = node.GetComponentInParent<MapObject>();
        if (map == null) return false;
        var built = builds.FirstOrDefault(b => b.Map != null && b.Map.Pointer == map.Pointer
            && b.Camera != null && b.Camera.Pointer == camera.Pointer);
        if (built == null || built.CameraAfter <= MapVisualFit.CameraHalfHeight
            || CustomMapLayout.LayoutFor(map.mapManager?.CurrentMap) == null) return false;
        halfHeight = built.CameraAfter;
        return true;
    }

    [HarmonyPrefix]
    internal static void Prefix(MapObject __instance)
    {
        Built pending = null;
        try
        {
            foreach (var old in builds.Where(b => b.Map == null).ToArray())
                ReleaseBuilt(old);
            if (builds.Any(b => b.Map.Pointer == __instance.Pointer)) return;
            // Unity may defer destruction of the previous map until this frame
            // ends. Release its scene-level presentation before another map
            // captures the same camera/intro marker, then disown those fields.
            foreach (var old in builds) ReleasePresentation(old);
            var layout = CustomMapLayout.LayoutFor(__instance.mapManager?.CurrentMap);
            var anchor = __instance.m_BossAnchorPoint;
            if (layout == null || anchor == null) return;
            SpriteRenderer background = null;
            foreach (var renderer in __instance.GetComponentsInChildren<SpriteRenderer>(true))
            {
                string name = renderer.sprite?.name ?? "";
                if (name == "Rogue2_Maptest" || name == "Level2_background" || name == "Stage3Background")
                { background = renderer; break; }
            }
            if (background == null) { Plugin.Log.LogWarning("[MapEditor] No supported background found for extension"); return; }
            var sprite = background.sprite;
            if (background.flipX || background.flipY || sprite.packed)
                throw new InvalidOperationException("Unexpected packed or flipped map background");
            int appearance = sprite.name == "Rogue2_Maptest" ? 1 : sprite.name == "Level2_background" ? 2 : 3;
            string transitionFile = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location), "MapSeams", $"act-{appearance}-transition.png");
            bool generatedTransition = File.Exists(transitionFile);
            string endingTransitionFile = Path.Combine(Path.GetDirectoryName(transitionFile), $"act-{appearance}-a-ending.png");
            bool halfTiles = generatedTransition && File.Exists(endingTransitionFile);
            var tiling = new MapSceneryTiling(sprite.rect.width, generatedTransition);
            var geometry = GeometryFor(background);
            var curtains = NativeEdgeCurtains(__instance, background);
            float worldStep = background.transform.TransformVector(new Vector3(geometry.LocalX(halfTiles ? sprite.rect.width / 4 : tiling.Step), 0, 0)).x;
            float rightmost = float.NegativeInfinity, leftmost = float.PositiveInfinity, authoredBoss = float.NaN;
            var regularNodes = new List<MapNode>();
            var mapped = new List<(MapNode View, MapLayoutNode Data)>();
            foreach (var view in __instance.MapNodes)
            {
                if (view?.Node == null) continue;
                var data = layout.InLayer(view.Node.LayerIndex).ElementAtOrDefault(view.Node.NodeIndex);
                if (data == null) throw new InvalidOperationException("Incomplete authored node mapping");
                mapped.Add((view, data));
            }
            float routeShift = RouteStartShift(layout, mapped);
            // Finish the existing logo setup before measuring visible badges.
            // Native RefreshNodeStates repeats this idempotent setup later.
            foreach (var (view, _) in mapped)
            {
                var match = view.TryCast<MatchMapNode>();
                if (match != null) PatchMapOpponents.EnsureNodeArt(match);
            }
            Canvas.ForceUpdateCanvases();
            var measured = new Dictionary<IntPtr, MapVisualFit.Envelope>();
            foreach (var (view, _) in mapped)
                if (CustomMapVisualBounds.TryMeasure(view, true, out var extent)) measured[view.Pointer] = extent;
            var columns = mapped.GroupBy(n => n.Data.Layer).OrderBy(g => g.Key).ToArray();
            var rowOffsets = mapped.ToDictionary(n => n.View.Pointer, _ => 0f);
            var staggerOffsets = mapped.ToDictionary(n => n.View.Pointer, _ => 0f);
            if (measured.Count != mapped.Count)
                Plugin.Log.LogWarning($"[MapFraming] Only {measured.Count}/{mapped.Count} node artwork bounds were ready before link creation; native vertical placement was retained.");
            float verticalShift = 0;
            float cameraHalfHeight = MapVisualFit.CameraHalfHeight;
            var camera = Camera.main;
            bool nativeViewport = camera != null && camera.orthographic
                && Math.Abs(camera.orthographicSize - MapVisualFit.CameraHalfHeight) <= .05f;
            if (!nativeViewport)
                Plugin.Log.LogWarning("[MapFraming] Expected native orthographic half-height 4.5; vertical fitting was skipped for an unavailable or different camera.");
            if (measured.Count == mapped.Count && nativeViewport)
            {
                float cameraY = camera.transform.position.y;
                var scenery = background.bounds;
                float maximumHalfHeight = Math.Min(cameraY - scenery.min.y, scenery.max.y - cameraY) - .01f;
                if (maximumHalfHeight < MapVisualFit.CameraHalfHeight)
                    throw new InvalidOperationException("The native map camera is outside its background artwork.");
                var fittedColumns = columns.Select(column =>
                {
                    var rows = column.OrderBy(n => n.View.transform.position.y).ToArray();
                    var bounds = rows.Select(n => measured[n.View.Pointer].Offset(0,
                        n.Data.Type == "Boss" ? anchor.position.y - n.View.transform.position.y : 0)).ToArray();
                    var separation = MapVisualFit.RowOffsets(bounds);
                    var envelope = bounds.Select((b, i) => b.Offset(0, separation[i])).Aggregate((a, b) => a.Union(b));
                    return (Rows: rows, Bounds: bounds, Frame: MapVisualFit.FitFrame(envelope, cameraY));
                }).ToArray();
                cameraHalfHeight = Math.Min(maximumHalfHeight, fittedColumns.Max(c => c.Frame.HalfHeight));
                foreach (var column in fittedColumns)
                {
                    var placements = MapVisualFit.FitColumn(column.Bounds, cameraY, cameraHalfHeight);
                    for (int i = 0; i < column.Rows.Length; i++)
                    {
                        var n = column.Rows[i];
                        staggerOffsets[n.View.Pointer] = placements[i].X;
                        if (n.Data.Type == "Boss") verticalShift = placements[i].Y;
                        else rowOffsets[n.View.Pointer] = placements[i].Y;
                    }
                }
            }
            var columnBounds = columns.Select(g => g.Select(n => measured.TryGetValue(n.View.Pointer, out var extent)
                ? extent.Offset(staggerOffsets[n.View.Pointer], rowOffsets[n.View.Pointer])
                : new MapVisualFit.Envelope(n.View.transform.position.x, n.View.transform.position.x,
                    n.View.transform.position.y, n.View.transform.position.y)).Aggregate((a, b) => a.Union(b))).ToArray();
            var offsets = MapVisualFit.ColumnOffsets(columnBounds);
            var layerOffsets = columns.Select((g, i) => (g.Key, Offset: offsets[i])).ToDictionary(x => x.Key, x => x.Offset);
            foreach (var (view, data) in mapped)
            {
                float x = view.transform.position.x + routeShift + layerOffsets[data.Layer] + staggerOffsets[view.Pointer];
                if (data.Type == "Boss") authoredBoss = x;
                else
                {
                    regularNodes.Add(view);
                    rightmost = Math.Max(rightmost, x);
                    leftmost = Math.Min(leftmost, x);
                }
            }
            if (!float.IsFinite(rightmost)) return;
            // Preserve a forward final gap even for imported templates with a misplaced boss coordinate.
            authoredBoss = Math.Max(float.IsFinite(authoredBoss) ? authoredBoss : rightmost, rightmost + 1);
            var spacing = new MapRouteSpacing(leftmost, authoredBoss, anchor.position.x, worldStep, halfTiles ? -1 : 0);
            int repeats = spacing.Repeats;
            if (repeats > (halfTiles ? 256 : 128)) throw new InvalidOperationException("Map extension exceeds 128 scenery repeats");
            float shift = repeats * worldStep;
            var shader = Shader.Find("Sprites/Default");
            if (shader == null) throw new InvalidOperationException("Sprite blending shader unavailable");
            pending = new Built { Map = __instance, Root = new GameObject("Campaign stitched scenery") };
            pending.Root.SetActive(false);
            pending.Root.transform.SetParent(background.transform, false);
            var intro = UnityEngine.Object.FindObjectOfType<WorldMapActIntroStart>();
            var scrollers = __instance.GetComponentsInChildren<ScrollNonUI>(true).ToList();
            if (__instance.m_Scroller != null && !scrollers.Any(s => s.Pointer == __instance.m_Scroller.Pointer)) scrollers.Add(__instance.m_Scroller);
            if (nativeViewport)
            {
                pending.Camera = camera;
                pending.CameraBefore = camera.orthographicSize;
                pending.CameraAfter = cameraHalfHeight;
                float before = pending.CameraBefore;
                pending.Rollback.Add(() => camera.orthographicSize = before);
                camera.orthographicSize = cameraHalfHeight;
                var menus = UnityEngine.Object.FindObjectOfType<WorldMapMenusHandler>();
                if (menus != null && menus.m_Camera != null && menus.m_Camera.Pointer == camera.Pointer)
                {
                    // Start can have captured the default camera before this
                    // asynchronous map load. Update only this camera's return
                    // size, so closing a node/menu restores the fitted frame.
                    pending.Menus = menus;
                    float original = menus.m_CamInitialOrtho;
                    pending.MenuOrthoBefore = original > 0 ? original : before;
                    pending.Rollback.Add(() => menus.m_CamInitialOrtho = original);
                    menus.m_CamInitialOrtho = cameraHalfHeight;
                }
                // Keep the native horizontal intro and timing, but do not zoom
                // into art that only just fits. Native Play snapshots this size
                // after loading; menu camera restoration snapshots it as well.
                if (intro != null)
                {
                    pending.Intro = intro; pending.ZoomBefore = intro._zoom; pending.ZoomAfter = 1;
                    float zoom = intro._zoom;
                    pending.Rollback.Add(() => intro._zoom = zoom);
                    intro._zoom = 1;
                }
            }
            if (halfTiles && repeats != 0)
            {
                BuildHalfTiles(pending, background, shader, new MapHalfTiles(sprite.rect.width, repeats + 2), transitionFile, endingTransitionFile);
            }
            else if (!halfTiles && repeats > 0)
            {
                var material = new Material(shader) { mainTexture = sprite.texture };
                pending.Assets.Add(material);
                AddPiece(pending, background, material, 0, tiling.Right, 0, 0, 0);
                for (int i = 0; i < repeats; i++)
                    AddPiece(pending, background, material, tiling.Left, tiling.Right, tiling.RepeatStart(i), tiling.Overlap, i + 1);
                AddPiece(pending, background, material, tiling.Right - tiling.Overlap, tiling.Width,
                    tiling.EndingStart(repeats), tiling.Overlap, repeats + 1);
                if (generatedTransition)
                {
                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    pending.Assets.Add(texture);
                    if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(transitionFile), true))
                        throw new InvalidOperationException("Cannot decode generated map transition");
                    texture.wrapMode = TextureWrapMode.Clamp; texture.filterMode = FilterMode.Bilinear;
                    var transitionMaterial = new Material(shader) { mainTexture = texture };
                    pending.Assets.Add(transitionMaterial);
                    for (int i = 0; i < repeats; i++)
                        AddPiece(pending, background, transitionMaterial, 0, tiling.Width * .25f,
                            tiling.TransitionStart(i), 0, repeats + 2 + i, true);
                }
            }
            else
            {
                // A short route uses the original finite artwork. Camera bounds
                // retain the native edge masks; no border pixels are stretched.
                var material = new Material(shader) { mainTexture = sprite.texture };
                pending.Assets.Add(material);
                AddPiece(pending, background, material, 0, sprite.rect.width, 0, 0, 0);
            }

            foreach (var node in regularNodes)
            {
                var transform = node.transform; var before = transform.position;
                pending.Rollback.Add(() => transform.position = before);
                var data = mapped.First(n => n.View.Pointer == node.Pointer).Data;
                var adjusted = before;
                adjusted.x = spacing.Position(before.x + routeShift + layerOffsets[data.Layer] + staggerOffsets[node.Pointer]);
                adjusted.y += rowOffsets[node.Pointer];
                transform.position = adjusted;
            }
            float cut = background.transform.TransformPoint(new Vector3(geometry.LocalX(tiling.Right - sprite.pivot.x), 0, 0)).x;
            var endingObjects = new List<Transform> { anchor };
            // The new native intro reads this marker instead of the scroller's
            // extended maximum. Preserve its boss-relative framing on short and
            // long maps, including when it is already below the ending anchor.
            if (intro != null)
            {
                endingObjects.Add(intro.transform);
                if (!intro.transform.IsChildOf(__instance.transform))
                {
                    pending.ExternalIntroMarker = intro.transform;
                    pending.IntroBefore = intro.transform.position;
                    pending.IntroAfter = pending.IntroBefore + new Vector3(shift, 0, 0);
                }
            }
            foreach (var renderer in __instance.GetComponentsInChildren<SpriteRenderer>(true))
            {
                var t = renderer.transform;
                if (renderer == background || renderer.GetComponentInParent<MapNode>() != null || t.IsChildOf(pending.Root.transform)
                    || curtains.Any(c => c.Renderer.Pointer == renderer.Pointer)
                    || (__instance.nodesParent != null && t.IsChildOf(__instance.nodesParent.transform))
                    || (__instance.linesParent != null && t.IsChildOf(__instance.linesParent.transform))) continue;
                if (t.position.x >= cut) endingObjects.Add(t);
            }
            // Move each ending decoration once, including children of the boss anchor.
            var uniqueEndingObjects = endingObjects.GroupBy(t => t.Pointer).Select(g => g.First()).ToArray();
            var roots = uniqueEndingObjects.Where(t => !uniqueEndingObjects.Any(other => other.Pointer != t.Pointer && t.IsChildOf(other))).ToArray();
            foreach (var t in roots)
            {
                var before = t.position; pending.Rollback.Add(() => t.position = before);
                t.position = before + new Vector3(shift, 0, 0);
            }
            // Move the boss with the route vertically, while leaving the native
            // scenery, camera Y and external intro marker's Y unchanged.
            var anchorBeforeVertical = anchor.position;
            pending.Rollback.Add(() => anchor.position = anchorBeforeVertical);
            anchor.position = anchorBeforeVertical + new Vector3(0, verticalShift, 0);
            foreach (var scroller in scrollers)
            {
                if (scroller.xConstraints != null)
                {
                    var before = scroller.xConstraints; pending.Rollback.Add(() => scroller.xConstraints = before);
                    var range = MapVisualFit.ScrollRange(before.min, before.max + shift,
                        curtains.Single(c => c.Left).Renderer.bounds.max.x,
                        curtains.Single(c => !c.Left).Renderer.bounds.min.x + shift,
                        cameraHalfHeight * (camera == null ? 16f / 9 : camera.aspect));
                    scroller.xConstraints = new FloatMinMax { min = range.Min, max = range.Max };
                }
            }
            var mapRange = __instance.m_Scroller?.xConstraints;
            if (intro != null && mapRange != null)
            {
                var before = intro.transform.position;
                pending.Rollback.Add(() => intro.transform.position = before);
                intro.transform.position = new Vector3(Math.Clamp(before.x, mapRange.min, mapRange.max), before.y, before.z);
                if (pending.ExternalIntroMarker != null) pending.IntroAfter = intro.transform.position;
            }
            foreach (var target in scrollers.Select(s => s.gameObject).Append(background.gameObject).GroupBy(g => g.Pointer).Select(g => g.First()))
                ExtendDragSurface(pending, target, shift);
            foreach (var curtain in curtains)
            {
                var t = curtain.Renderer.transform;
                var before = t.position;
                // Preserve the entrance mask and move the ending mask with
                // the ending scenery, exactly once, by the same distance.
                float dx = curtain.Left ? 0 : shift;
                var after = before + new Vector3(dx, 0, 0);
                pending.Rollback.Add(() => { if (t != null) t.position = before; });
                pending.Curtains.Add((curtain.Renderer, before, after));
                t.position = after;
            }
            string orders = SinkBelowBackground(pending.Root, background.sortingOrder);
            pending.Rollback.Add(() => background.enabled = true);
            background.enabled = false;
            pending.Root.SetActive(true);
            pending.Rollback.Clear();
            builds.Add(pending); pending = null;
            Plugin.Log.LogInfo($"[MapEditor] Stitched {(halfTiles ? (repeats + 2) + " alternating half tiles" : repeats + " extra whole sections")} using {(generatedTransition ? "generated transitions" : "fallback overlaps")}; ending and boss shifted {shift:0.0}; route starts at the native entry {leftmost:0.00} (moved {routeShift:0.00}); layer spacing scaled {spacing.Scale:0.000}x without compression; artwork widened route by {offsets.LastOrDefault():0.00}, rows moved at most {rowOffsets.Values.Max(v => Math.Abs(v)):0.00}, vertical translation {verticalShift:0.00}, custom camera half-height {cameraHalfHeight:0.00}; {orders}");
        }
        catch (Exception ex)
        {
            if (pending != null)
            {
                foreach (var undo in pending.Rollback.AsEnumerable().Reverse())
                    try { undo(); } catch { /* Keep releasing owned resources if a scene was destroyed. */ }
                UnityEngine.Object.Destroy(pending.Root);
                foreach (var asset in pending.Assets) UnityEngine.Object.Destroy(asset);
            }
            Plugin.Log.LogError("[MapEditor] Could not extend scenery: " + ex.Message);
        }
    }

    private static Material LoadTransition(Built built, Shader shader, string file)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        built.Assets.Add(texture);
        if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(file), true)) throw new InvalidOperationException("Cannot decode " + file);
        texture.wrapMode = TextureWrapMode.Clamp; texture.filterMode = FilterMode.Bilinear;
        var material = new Material(shader) { mainTexture = texture }; built.Assets.Add(material);
        return material;
    }

    // GraphMapAdapter centres every route on (min X + max X) / 2 of its nodes. Native
    // templates all span the whole background, so their entry sits at the map start;
    // a shorter or longer authored route would start mid-map or off it. Move the route
    // so its entry lands where the source template's entry does.
    private static float RouteStartShift(MapLayout layout, List<(MapNode View, MapLayoutNode Data)> mapped)
    {
        if (layout.Nodes.Count == 0 || !CustomMapLayout.NativeXRange(layout, out float nativeMin, out float nativeMax)) return 0;
        double authoredMin = layout.Nodes.Min(n => n.X), authoredMax = layout.Nodes.Max(n => n.X);
        var low = mapped.OrderBy(n => n.Data.X).First(); var high = mapped.OrderBy(n => n.Data.X).Last();
        double gridSpan = high.Data.X - low.Data.X;
        if (gridSpan < .001) return 0;
        float worldPerGrid = (high.View.transform.position.x - low.View.transform.position.x) / (float)gridSpan;
        return worldPerGrid * ((float)(authoredMax - authoredMin) - (nativeMax - nativeMin)) / 2;
    }

    // The background shares the Default sorting layer with the native path lines
    // (LineRenderer, order 0), stadiums (order 5) and node logos/points (10-100).
    // Strips stacked above the background's order painted over all of them, so keep
    // their relative stacking but end it one below the background. No world-map
    // renderer on that layer uses a negative order (checked against data.unity3d).
    private static string SinkBelowBackground(GameObject root, int backgroundOrder)
    {
        var pieces = root.GetComponentsInChildren<MeshRenderer>(true);
        if (pieces.Length == 0) return "no scenery strips";
        int top = pieces.Max(r => r.sortingOrder), shift = top - backgroundOrder + 1;
        foreach (var piece in pieces) piece.sortingOrder -= shift;
        return $"strip orders {pieces.Min(r => r.sortingOrder)}..{top - shift} below background {backgroundOrder}";
    }

    private static MapSpriteGeometry GeometryFor(SpriteRenderer renderer)
    {
        var sprite = renderer.sprite;
        bool sized = renderer.drawMode != SpriteDrawMode.Simple;
        return new MapSpriteGeometry(sprite.rect.width, sprite.rect.height, sprite.pixelsPerUnit,
            sized, renderer.size.x, renderer.size.y);
    }

    private static List<(SpriteRenderer Renderer, bool Left)> NativeEdgeCurtains(MapObject map, SpriteRenderer background)
    {
        var result = new List<(SpriteRenderer Renderer, bool Left)>();
        var source = background.bounds;
        foreach (var renderer in map.GetComponentsInChildren<SpriteRenderer>(true))
        {
            var t = renderer.transform;
            var color = renderer.color;
            var bounds = renderer.bounds;
            int side = MapVisualFit.NativeEdgeCurtainSide(t.parent != null && t.parent.Pointer == map.transform.Pointer,
                renderer.sprite?.name, renderer.sortingLayerName, renderer.sortingOrder,
                renderer.drawMode == SpriteDrawMode.Simple, color.r == 0 && color.g == 0 && color.b == 0 && color.a == 1,
                new MapVisualFit.Envelope(bounds.min.x, bounds.max.x, bounds.min.y, bounds.max.y),
                new MapVisualFit.Envelope(source.min.x, source.max.x, source.min.y, source.max.y));
            if (side != 0) result.Add((renderer, side < 0));
        }
        if (result.Count != 2 || result.Count(c => c.Left) != 1)
            throw new InvalidOperationException("Expected the two native opaque map edge curtains; framing was not changed.");
        return result;
    }

    private static void BuildHalfTiles(Built built, SpriteRenderer background, Shader shader, MapHalfTiles tiles, string loopFile, string endFile)
    {
        var material = new Material(shader) { mainTexture = background.sprite.texture }; built.Assets.Add(material);
        AddPiece(built, background, material, 0, tiles.TileWidth, 0, 0, 0);
        for (int i = 0; i < tiles.Parts; i++)
            AddPiece(built, background, material, tiles.SourceStart(i), tiles.SourceStart(i) + tiles.TileWidth, tiles.Destination(i), 0, i + 1);
        AddPiece(built, background, material, tiles.Width * .75f, tiles.Width, tiles.EndingStart, 0, tiles.Parts + 1);
        Material loop = null;
        for (int i = 2; i < tiles.Parts; i += 2)
        {
            loop ??= LoadTransition(built, shader, loopFile);
            AddPiece(built, background, loop, 0, tiles.TileWidth, tiles.SeamStart(tiles.Destination(i)), 0, tiles.Parts + 2 + i, true);
        }
        if (tiles.EndsWithA)
        {
            var ending = LoadTransition(built, shader, endFile);
            AddPiece(built, background, ending, 0, tiles.TileWidth, tiles.SeamStart(tiles.EndingStart), 0, tiles.Parts * 2 + 3, true);
        }
    }

    private static void ExtendDragSurface(Built built, GameObject target, float shift)
    {
        float localShift = target.transform.InverseTransformVector(new Vector3(shift, 0, 0)).x;
        float sizeDelta = shift / Math.Max(.001f, Math.Abs(target.transform.lossyScale.x));
        var box2 = target.GetComponent<BoxCollider2D>();
        if (box2 != null)
        {
            var size = box2.size; var offset = box2.offset;
            built.Rollback.Add(() => { box2.size = size; box2.offset = offset; });
            box2.size = new Vector2(Math.Max(.1f, size.x + sizeDelta), size.y);
            box2.offset = offset + new Vector2(localShift / 2, 0);
        }
        var box3 = target.GetComponent<BoxCollider>();
        if (box3 != null)
        {
            var size = box3.size; var center = box3.center;
            built.Rollback.Add(() => { box3.size = size; box3.center = center; });
            box3.size = new Vector3(Math.Max(.1f, size.x + sizeDelta), size.y, size.z);
            box3.center = center + new Vector3(localShift / 2, 0, 0);
        }
    }

    private static void AddPiece(Built built, SpriteRenderer source, Material material,
        float from, float to, float destination, float fade, int order, bool transition = false)
    {
        var sprite = source.sprite;
        float width = to - from, height = sprite.rect.height;
        var geometry = GeometryFor(source);
        // Generated art repairs the complete join. Only its outermost 2% is
        // feathered to absorb tiny raster/color differences against the source.
        var columns = (transition ? new[] { 0f, width * .02f, width * .98f, width }
            : fade > 0 ? new[] { 0f, fade * .65f, fade, width } : new[] { 0f, width }).ToList();
        var rows = new List<float> { 0, height };
        var vertices = new Vector3[columns.Count * rows.Count];
        var uv = new Vector2[vertices.Length];
        var colors = new Color[vertices.Length];
        var triangles = new int[(columns.Count - 1) * (rows.Count - 1) * 6];
        for (int i = 0; i < columns.Count; i++)
        {
            float x = columns[i];
            for (int row = 0; row < rows.Count; row++)
            {
                int v = i * rows.Count + row;
                float y = rows[row];
                vertices[v] = new Vector3(geometry.LocalX(destination + x - sprite.pivot.x), geometry.LocalY(y - sprite.pivot.y), 0);
                // Sample only the finite native rectangle. The half-texel
                // inset avoids atlas bleeding; no mesh extends its edge texel.
                uv[v] = transition ? new Vector2(Math.Clamp(x / width, .5f / material.mainTexture.width, 1 - .5f / material.mainTexture.width),
                        Math.Clamp(y / height, .5f / material.mainTexture.height, 1 - .5f / material.mainTexture.height))
                    : new Vector2((sprite.rect.x + from + Math.Clamp(x, .5f, width - .5f)) / sprite.texture.width,
                        (sprite.rect.y + Math.Clamp(y, .5f, height - .5f)) / sprite.texture.height);
                var color = source.color;
                if ((fade > 0 && x <= fade * .65f) || (transition && (x <= 0 || x >= width))) color.a = 0;
                colors[v] = color;
                if (i == columns.Count - 1 || row == rows.Count - 1) continue;
                int t = (i * (rows.Count - 1) + row) * 6;
                triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + rows.Count;
                triangles[t + 3] = v + rows.Count; triangles[t + 4] = v + 1; triangles[t + 5] = v + rows.Count + 1;
            }
        }
        var mesh = new Mesh { vertices = vertices, uv = uv, colors = colors, triangles = triangles };
        mesh.RecalculateBounds(); built.Assets.Add(mesh);
        var piece = new GameObject("Map scenery strip " + order);
        piece.layer = source.gameObject.layer;
        piece.transform.SetParent(built.Root.transform, false);
        piece.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = piece.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.sortingLayerID = source.sortingLayerID;
        renderer.sortingOrder = source.sortingOrder + order;
    }
}
