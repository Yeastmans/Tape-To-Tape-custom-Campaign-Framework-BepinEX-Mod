using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CampaignCore;
using Data;
using HarmonyLib;
using Spine;
using UnityEngine;

namespace EndlessMode;

internal static class CustomHeadRuntime
{
    private static readonly Dictionary<string, CustomHeadDefinition> Heads = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Texture2D> Textures = new();
    private static readonly Dictionary<string, Sprite> Portraits = new();
    private static readonly List<UnityEngine.Object> Owned = new();
    private static readonly HashSet<string> Reported = new();
    private static readonly Dictionary<string, (int left, int top, int right, int bottom)> VisibleBounds = new();
    internal static void Install(Harmony harmony)
    {
        foreach (var head in CustomHeadCatalog.Load(Plugin.ModContentRoot, message => Plugin.Log.LogWarning("[CustomHeads] " + message))) Heads[head.Skin] = head;
        harmony.Patch(AccessTools.Method(typeof(Skaters.SkinHelper), "GetSkins"), prefix: new HarmonyMethod(typeof(CustomHeadRuntime), nameof(BeforeSkins)));
        // Kit/preview prefixes can change helmetSkin. Register the final selected
        // mask after those changes and before native skin lookup.
        harmony.Patch(AccessTools.Method(typeof(Skaters.SkinHelper), "GetSkinsForGoalie"), prefix: new HarmonyMethod(typeof(CustomHeadRuntime), nameof(BeforeGoalieSkins)) { priority = Priority.Last });
        // UI head selection and portraits use a separate Unity Sprite repository.
        var ui = AccessTools.TypeByName("Tape2Tape.Customization.UI.SkaterUIHeadData");
        if (ui != null)
        {
            harmony.Patch(AccessTools.Method(ui, "GetHead"), prefix: new HarmonyMethod(typeof(CustomHeadRuntime), nameof(Portrait)));
            harmony.Patch(AccessTools.Method(ui, "GetAllHeads"), postfix: new HarmonyMethod(typeof(CustomHeadRuntime), nameof(HeadOptions)));
        }
        Plugin.Log.LogInfo($"[CustomHeads] {Heads.Count} custom head project(s) available");
    }
    private static void HeadOptions(Il2CppSystem.Collections.Generic.List<string> results, bool isGoalie)
    {
        if (results == null) return;
        foreach (var head in Heads.Values) if (head.IsGoalie == isGoalie && !results.Contains(head.Skin)) results.Add(head.Skin);
    }
    private static bool Portrait(string headSkin, ref Sprite __result)
    {
        string key = HeadAppearance.Base(headSkin);
        if (!Heads.TryGetValue(key, out var head)) return true;
        try
        {
            if (!Portraits.TryGetValue(key, out var sprite))
            {
                var image = head.Images.Single(i => i.Group == "Portrait");
                var texture = Texture(head.Asset(image.File));
                sprite = Sprite.Create(texture, new Rect(0, 0, 512, 512), new Vector2(.5f, .5f), 100);
                Keep(sprite); Portraits[key] = sprite;
            }
            __result = sprite; return false;
        }
        catch (Exception ex) { Warn(key, ex); return true; }
    }
    private static void BeforeSkins(SkeletonData skeletonData, ForwardData forwardData)
    {
        if (skeletonData == null || forwardData == null) return;
        string key = HeadAppearance.Base(forwardData.headSkin);
        if (!Heads.TryGetValue(key, out var head) || head.IsGoalie) return;
        try
        {
            if (skeletonData.FindSkin(key) == null)
            {
                // Build complete skins first, then publish them to this skeleton only.
                var bare = Build(skeletonData, head, false);
                var fitted = head.SupportsHelmet ? Build(skeletonData, head, true) : null;
                skeletonData.Skins.Add(bare);
                if (fitted != null) skeletonData.Skins.Add(fitted);
                Plugin.Log.LogInfo($"[CustomHeads] Registered '{key}' from '{head.TemplateSkin}'");
            }
            HeadAppearanceRuntime.Configure(forwardData);
        }
        catch (Exception ex) { Warn(key, ex); }
    }
    // Referee composition borrows only head attachments from this standalone
    // skin. Do not register it into shared SkeletonData or change ForwardData.
    internal static Skin RefereeHead(SkeletonData skeletonData, string key)
    {
        var native = skeletonData.FindSkin(key);
        if (native != null) return native;
        if (!Heads.TryGetValue(key, out var head) || head.IsGoalie) return null;
        try { return Build(skeletonData, head, false); }
        catch (Exception ex) { Warn(key, ex); return null; }
    }

    private static void BeforeGoalieSkins(SkeletonData skeletonData, GoaltenderData data)
    {
        if (skeletonData == null || data == null || !Heads.TryGetValue(data.helmetSkin ?? "", out var mask) || !mask.IsGoalie) return;
        try
        {
            if (skeletonData.FindSkin(mask.Skin) != null) return;
            var skin = Build(skeletonData, mask, false);
            skeletonData.Skins.Add(skin);
            Plugin.Log.LogInfo($"[CustomHeads] Registered goalie mask '{mask.Skin}' from '{mask.TemplateSkin}'");
        }
        catch (Exception ex) { Warn(mask.Skin, ex); }
    }
    internal static Skin Build(SkeletonData data, CustomHeadDefinition head, bool helmet)
    {
        string donorName = head.Version >= 2 ? (helmet ? head.TemplateHelmetSkin : head.TemplateSkin)
            : "Faces/Toronto/Mathieu" + (helmet ? "_Helmet" : "");
        var donor = data.FindSkin(donorName) ?? throw new InvalidOperationException("Native head template is unavailable: " + donorName);
        var skin = new Skin(head.Skin + (helmet ? "_Helmet" : ""));
        // AddSkin executes natively and preserves the donor's supporting hand entries.
        // Never marshal the unsafe boxed SkinEntry collection into managed code.
        skin.AddSkin(donor);
        string view = head.IsGoalie ? (data.FindAnimation("North/Standing_Idle_N") != null ? "North" : "South") : "";
        var bindings = head.Version >= 2 ? head.Bindings.Where(b => b.Helmet == helmet && (!head.IsGoalie || b.View == view))
            : new[] { "S", "N", "W", "E", "NE", "NW", "SW", "SE", "N2" }.Select(token => new CustomHeadBinding {
                Slot = "Helmet", Placeholder = token == "N2" ? "N-Helmet2" : token + "-Helmet",
                File = head.Direction(token == "N2" ? "N" : token, helmet).File });
        foreach (var binding in bindings)
        {
            int slot = data.FindSlot(binding.Slot)?.Index ?? throw new InvalidOperationException("Missing native head slot: " + binding.Slot);
            string placeholder = binding.Placeholder;
            var original = donor.GetAttachment(slot, placeholder);
            if (original == null && head.Version == 1 && placeholder == "N-Helmet2") continue;
            var sourceRegion = SequenceOf(original).Regions[0].TryCast<AtlasRegion>()
                ?? throw new InvalidOperationException("Missing native atlas region.");
            var image = head.Images.Single(i => i.File == binding.File);
            string direction = head.Version >= 2 ? image.Direction : placeholder == "N-Helmet2" ? "N" : placeholder.Split('-')[0];
            var texture = Texture(head.Asset(image.File));
            // T2T billboards skater vertices in its skeleton shader. A generic
            // sprite shader instead projects the head in world space, which puts
            // it inside the torso under the match camera even though flat preview
            // renders look correct. Keep the native projection and depth handling;
            // the PNG artwork and attachment geometry need no vertical offset.
            var shader = Shader.Find("Universal Render Pipeline/Spine/T2T Skeleton")
                ?? throw new InvalidOperationException("Native skater shader unavailable.");
            var material = new Material(shader) { name = head.Name + " " + direction, mainTexture = texture };
            // User PNGs are finished straight-alpha art. A fresh native material
            // avoids inheriting equipment color masks or UI stencil overrides.
            material.SetFloat("_StraightAlphaInput", 1);
            material.EnableKeyword("_STRAIGHT_ALPHA_INPUT");
            Keep(material);
            var page = new AtlasPage { name = head.Name + "-" + direction, width = 512, height = 512, rendererObject = material, pma = false };
            var bounds = Bounds(texture, head.Version >= 2);
            var sampling = bounds.right > bounds.left ? HeadArtworkPlacement.Align(image, direction, bounds.left, bounds.top, bounds.right, bounds.bottom)
                : new HeadArtworkPlacement.Sampling(image.Left, image.Top, image.ContentWidth, image.ContentHeight, 1, 0, 0);
            float top = sampling.Top / 512f, bottom = (sampling.Top + sampling.Height) / 512f;
            bool flipV = sourceRegion.v > sourceRegion.v2;
            var region = new AtlasRegion { page = page, name = head.Skin + "/" + direction,
                width = image.NativeWidth, height = image.NativeHeight, originalWidth = image.NativeWidth, originalHeight = image.NativeHeight,
                u = sampling.Left / 512f, u2 = (sampling.Left + sampling.Width) / 512f,
                v = flipV ? 1 - top : top, v2 = flipV ? 1 - bottom : bottom,
                offsetX = 0, offsetY = 0, degrees = 0, rotate = false, loadTexturesOnDemand = false };
            var sequence = new Sequence(1, false); sequence.Regions[0] = region;
            var originalRegion = original.TryCast<RegionAttachment>();
            if (originalRegion != null)
            {
                var attachment = new RegionAttachment(original.Name, sequence) {
                    X = originalRegion.X, Y = originalRegion.Y, Rotation = originalRegion.Rotation,
                    ScaleX = originalRegion.ScaleX, ScaleY = originalRegion.ScaleY, Width = originalRegion.Width, Height = originalRegion.Height, Path = region.name
                };
                attachment.SetColor(1, 1, 1, 1); attachment.UpdateSequence(); skin.SetAttachment(slot, placeholder, attachment);
            }
            else
            {
                var mesh = original.TryCast<MeshAttachment>() ?? throw new InvalidOperationException("Unsupported head attachment.");
                var attachment = new MeshAttachment(original.Name, sequence) {
                    Bones = mesh.Bones, Vertices = mesh.Vertices, WorldVerticesLength = mesh.WorldVerticesLength,
                    RegionUVs = mesh.RegionUVs, Triangles = mesh.Triangles, HullLength = mesh.HullLength,
                    Edges = mesh.Edges, Width = mesh.Width, Height = mesh.Height, Path = region.name
                };
                attachment.SetColor(1, 1, 1, 1); attachment.UpdateSequence(); skin.SetAttachment(slot, placeholder, attachment);
            }
        }
        return skin;
    }
    private static (int left, int top, int right, int bottom) Bounds(Texture2D texture, bool allowBlank = false)
    {
        if (VisibleBounds.TryGetValue(texture.name, out var bounds)) return bounds;
        int left = 512, top = 512, right = 0, bottom = 0;
        var pixels = texture.GetPixels32();
        for (int y = 0; y < 512; y++) for (int x = 0; x < 512; x++)
        {
            if (pixels[y * 512 + x].a <= 32) continue;
            int row = 511 - y; // Unity pixel rows start at the bottom.
            left = Math.Min(left, x); right = Math.Max(right, x + 1);
            top = Math.Min(top, row); bottom = Math.Max(bottom, row + 1);
        }
        bounds = (left, top, right, bottom);
        if ((right <= left || bottom <= top) && !allowBlank) throw new InvalidDataException("Head PNG has no visible artwork: " + texture.name);
        VisibleBounds[texture.name] = bounds;
        return bounds;
    }
    private static Texture2D Texture(string file)
    {
        if (Textures.TryGetValue(file, out var texture)) return texture;
        CustomHeadDefinition.ValidatePng(file);
        texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = file, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(file), false)) { UnityEngine.Object.Destroy(texture); throw new InvalidDataException("Cannot decode " + file); }
        Keep(texture); Textures[file] = texture; return texture;
    }
    private static void Keep(UnityEngine.Object obj) { obj.hideFlags = HideFlags.HideAndDontSave; UnityEngine.Object.DontDestroyOnLoad(obj); Owned.Add(obj); }
    private static void Warn(string key, Exception ex) { if (Reported.Add(key)) Plugin.Log.LogError("[CustomHeads] " + key + ": " + ex); }
    internal static Sequence SequenceOf(Attachment attachment) => attachment?.TryCast<RegionAttachment>()?.Sequence
        ?? attachment?.TryCast<MeshAttachment>()?.Sequence ?? throw new InvalidOperationException("Missing head sequence.");
}
