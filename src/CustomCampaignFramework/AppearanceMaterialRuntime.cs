using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Data;
using HarmonyLib;
using Spine;
using Spine.Unity;
using UnityEngine;
using Flags = Skaters.T2TSkeletonExtensions.CustomizationFlags;

namespace EndlessMode;

// Native ForwardData flags mix the home and away fields and classify every
// "Custom" helmet as a color mask (including painted Helmet_Reference art).
// Decide from the attachments actually composed for this render, after talents,
// equipment, handedness, injuries and the selected kit have been resolved.
internal static class AppearanceMaterialRuntime
{
    private sealed class Probe
    {
        internal string Role, Slot, Attachment;
        internal int Bit;
        internal readonly Dictionary<string, bool> Paths = new(StringComparer.Ordinal);
    }
    private static readonly List<Probe> Probes = Load();
    // Pieces whose color-mask art the manifest actually knows, per role. Skater skates (32)
    // and sticks (128) have no mask paths in it, so their attachments can only count as
    // "painted"; overriding there stripped the game's own correct flag and drew the raw
    // mask texture (red/yellow skates). Those pieces keep the base game's flag.
    private static readonly Dictionary<string, int> MaskBits = LoadMaskBits();
    private static Dictionary<string, int> LoadMaskBits()
    {
        var bits = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var probe in Probes)
            if (probe.Paths.ContainsValue(true))
                bits[probe.Role] = (bits.TryGetValue(probe.Role, out int known) ? known : 0) | Bit(probe);
        return bits;
    }
    internal static bool Classifies(bool goalie, int bit) => MaskBits.TryGetValue(goalie ? "goalie" : "skater", out int bits) && (bits & bit) != 0;
    // Skater socks (Leg slots) share the native Torso flag (2), but a separate sock
    // style can put painted socks on a mask jersey or the reverse. Classify the legs
    // on their own pseudo-bit; it never reaches the native flags.
    private const int LegsBit = 512;
    private static readonly string[] LegSlots = { "Leg left", "Leg right" };
    private static readonly string[] JerseySlots = { "Body", "Forearm left", "Forearm right" };
    internal enum TorsoSplit { None, LegsPainted, LegsMask }
    // GoaltenderData has a separate Pants flag (8). The native goalie slot
    // helpers still group Pants_left/right with Body (2), so correct that
    // grouping when classifying attachments and assigning materials.
    private static int Bit(Probe probe) => probe.Role == "goalie" && probe.Slot is "Pants_left" or "Pants_right" ? 8
        : probe.Role == "skater" && probe.Slot is "Leg left" or "Leg right" ? LegsBit : probe.Bit;
    [ThreadStatic] private static RenderScope Current;
    internal sealed class RenderScope
    {
        internal RenderScope Previous;
        internal SkeletonData Data;
        internal Skin Skin;
        internal IntPtr Player;
    }
    internal static RenderScope Begin(SkeletonData data, Skin skin, SkaterData player)
    {
        var scope = new RenderScope { Previous = Current, Data = data, Skin = skin, Player = player?.Pointer ?? IntPtr.Zero };
        Current = scope;
        return scope;
    }
    internal static void End(RenderScope scope) { if (scope != null) Current = scope.Previous; }

    // Native wrappers read GetCustomizationFlags synchronously, before their
    // first await. Correct the complete mask there so the native caller chooses
    // the right per-piece materials. Never detour the world UniTask/ct overload.
    private static void WorldPrefix(SkeletonAnimation skeletonAnimation, Skin skin, SkaterData skaterData, out RenderScope __state)
        => __state = Begin(skeletonAnimation?.Skeleton?.Data ?? skeletonAnimation?.skeletonDataAsset?.GetSkeletonData(true), skin, skaterData);
    private static void SelectionPrefix(SkeletonGraphic skeletonGraphic, Skin skin, SkaterData skaterData, out RenderScope __state)
        => __state = Begin(skeletonGraphic?.Skeleton?.Data ?? skeletonGraphic?.skeletonDataAsset?.GetSkeletonData(true), skin, skaterData);
    private static void RenderFinalizer(RenderScope __state) => End(__state);
    private static void ForwardFlags(ForwardData __instance, ref Flags __result)
    {
        if (Current?.Player == __instance.Pointer) __result = Resolve(Current.Data, Current.Skin, false, __result);
    }
    private static void GoalieFlags(GoaltenderData __instance, ref Flags __result)
    {
        if (Current?.Player == __instance.Pointer) __result = Resolve(Current.Data, Current.Skin, true, __result);
    }

    private static List<Probe> Load()
    {
        var probes = new Dictionary<string, Probe>(StringComparer.Ordinal);
        using var stream = typeof(AppearanceMaterialRuntime).Assembly.GetManifestResourceStream("NativeAppearanceSlots.tsv");
        using var reader = new StreamReader(stream ?? throw new InvalidDataException("Missing native appearance manifest"));
        string line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.StartsWith("#") || line.Length == 0) continue;
            var c = line.Split('\t');
            string key = c[0] + "|" + c[1] + "|" + c[3];
            if (!probes.TryGetValue(key, out var probe))
                probes.Add(key, probe = new Probe { Role = c[0], Slot = c[1], Bit = int.Parse(c[2]), Attachment = c[3] });
            probe.Paths[c[4]] = c[5] == "mask";
        }
        return probes.Values.ToList();
    }

    internal static Flags Resolve(SkeletonData data, Skin skin, bool goalie, Flags original)
    {
        if (data == null || skin == null) return original;
        var maskCounts = new Dictionary<int, int>(); var paintCounts = new Dictionary<int, int>();
        string role = goalie ? "goalie" : "skater";
        foreach (var probe in Probes)
        {
            if (probe.Role != role) continue;
            var slot = data.FindSlot(probe.Slot);
            if (slot == null) continue;
            // Named lookup avoids boxing SkinEntry, which crashes IL2CPP.
            var attachment = skin.GetAttachment(slot.Index, probe.Attachment);
            if (attachment == null) continue;
            string path = attachment.TryCast<RegionAttachment>()?.Path ?? attachment.TryCast<MeshAttachment>()?.Path;
            if (path == null || !probe.Paths.TryGetValue(path, out bool mask)) continue;
            int bit = Bit(probe);
            var counts = mask ? maskCounts : paintCounts;
            counts[bit] = counts.TryGetValue(bit, out int count) ? count + 1 : 1;
        }
        // Native skins retain unused alternate placeholders from the default
        // skin (notably gloves). The selected directional set takes precedence
        // over that minority. Ties and unknown custom assets keep native flags.
        int value = (int)original;
        for (int bit = 1; bit <= 256; bit <<= 1)
        {
            if (!Classifies(goalie, bit)) continue;
            maskCounts.TryGetValue(bit, out int m); paintCounts.TryGetValue(bit, out int p);
            if (m > p) value |= bit;
            else if (p > m) value &= ~bit;
        }
        // Mask socks need the Torso material even on a painted jersey; TorsoSplit then
        // keeps it off the jersey slots. Without jersey pieces the legs decide alone.
        maskCounts.TryGetValue(LegsBit, out int legMask); paintCounts.TryGetValue(LegsBit, out int legPaint);
        if (legMask > legPaint) value |= 2;
        else if (legPaint > legMask && !maskCounts.ContainsKey(2) && !paintCounts.ContainsKey(2)) value &= ~2;
        // The native manifest probes Helmet_Custom, while authored finished
        // artwork occupies Helmet. Inspect the composed mask directly so names
        // containing "Custom" cannot select the palette-mask slot/material.
        if (goalie && HasCustomMask(data, skin)) value &= ~1;
        var resolved = (Flags)value;
        return resolved;
    }

    private static bool HasCustomMask(SkeletonData data, Skin skin)
    {
        var slot = data.FindSlot("Helmet");
        if (slot == null) return false;
        foreach (string name in new[] { "S_Helmet", "SE_Helmet", "SW_Helmet", "Helmet", "Helmet_NE", "Helmet_NW" })
        {
            var attachment = skin.GetAttachment(slot.Index, name);
            string path = attachment?.TryCast<RegionAttachment>()?.Path ?? attachment?.TryCast<MeshAttachment>()?.Path;
            if (path != null && path.StartsWith("Helmet/CustomMasks/", StringComparison.Ordinal)) return true;
        }
        return false;
    }

    // Which side of the skater Torso group differs when the socks come from another jersey skin.
    internal static TorsoSplit Split(SkeletonData data, Skin skin)
    {
        if (data == null || skin == null || data.FindSlot("Leg left") == null) return TorsoSplit.None;
        int jerseyMask = 0, jerseyPaint = 0, legMask = 0, legPaint = 0;
        foreach (var probe in Probes)
        {
            int bit = Bit(probe);
            if (probe.Role != "skater" || bit is not (2 or LegsBit)) continue;
            var slot = data.FindSlot(probe.Slot);
            if (slot == null) continue;
            var attachment = skin.GetAttachment(slot.Index, probe.Attachment);
            if (attachment == null) continue;
            string path = attachment.TryCast<RegionAttachment>()?.Path ?? attachment.TryCast<MeshAttachment>()?.Path;
            if (path == null || !probe.Paths.TryGetValue(path, out bool mask)) continue;
            if (bit == LegsBit) { if (mask) legMask++; else legPaint++; }
            else if (mask) jerseyMask++; else jerseyPaint++;
        }
        if (jerseyMask > jerseyPaint && legPaint > legMask) return TorsoSplit.LegsPainted;
        if (jerseyPaint > jerseyMask && legMask > legPaint) return TorsoSplit.LegsMask;
        return TorsoSplit.None;
    }
    // The slots that must not use the Torso color-mask material under this split.
    private static string[] PaintedSide(TorsoSplit split) => split == TorsoSplit.LegsPainted ? LegSlots : split == TorsoSplit.LegsMask ? JerseySlots : Array.Empty<string>();

    // Native code builds the Torso material from the first attachment named "Body".
    // For mask socks on a painted jersey, build it from the socks instead.
    private static void WorldOverridePrefix(SkeletonRenderer renderer, Skin skin, bool isGoalie, ref string slotName)
    {
        if (!isGoalie && slotName == "Body" && Split(renderer?.Skeleton?.Data, skin) == TorsoSplit.LegsMask) slotName = "Leg";
    }
    private static void GraphicOverridePrefix(SkeletonGraphic skeletonGraphic, Skin skin, ref string slotName)
    {
        if (slotName == "Body" && Split(skeletonGraphic?.Skeleton?.Data, skin) == TorsoSplit.LegsMask) slotName = "Leg";
    }

    public static void SlotsPrefix(Skeleton skeleton, bool isGoalie, ref Flags flags)
        => flags = Resolve(skeleton?.Data, skeleton?.Skin, isGoalie, flags);

    private static void SlotsPostfix(Skeleton skeleton, bool isGoalie, Flags flags)
    {
        if (skeleton == null) return;
        if (!isGoalie)
        {
            // Customizable selects the color-mask shader path per slot.
            foreach (string name in PaintedSide(Split(skeleton.Data, skeleton.Skin)))
            {
                var slot = skeleton.FindSlot(name);
                if (slot != null) slot.Customizable = false;
            }
            return;
        }
        bool customPants = ((int)flags & 8) != 0;
        foreach (string name in new[] { "Pants_left", "Pants_right" })
        {
            var slot = skeleton.FindSlot(name);
            if (slot != null) slot.Customizable = customPants;
        }
    }

    public static void MaterialsPrefix(SkeletonRenderer skeletonAnim, bool isGoalie, ref Flags flags)
    {
        // Here flags names the slots receiving THIS material, not all custom
        // pieces on the player. Expanding it spreads (e.g.) a helmet texture to
        // the jersey/gloves. Only remove incompatible recipients.
        var resolved = Resolve(skeletonAnim?.Skeleton?.Data, skeletonAnim?.Skeleton?.Skin, isGoalie, flags);
        flags = (Flags)((int)flags & (int)resolved);
    }
    private static void MaterialsPostfix(SkeletonRenderer skeletonAnim, bool isGoalie, Material mat, Flags flags)
    {
        if (skeletonAnim?.skeleton == null) return;
        // Native SetupSkeletonSlotsMaterials routes Stick only for goalies,
        // although the skater path also creates a Stick material and sets its
        // shader flag. Assign that material to the skater's actual stick slot.
        if (!isGoalie && mat != null && ((int)flags & 128) != 0)
        {
            var stick = skeletonAnim.skeleton.FindSlot("Stick");
            if (stick != null) skeletonAnim.CustomSlotMaterials[stick] = mat;
        }
        if (!isGoalie && mat != null && ((int)flags & 2) != 0)
        {
            // Native Torso routing covers jersey and socks together. Leave the side
            // whose pieces are painted on its own atlas material.
            foreach (string name in PaintedSide(Split(skeletonAnim.skeleton.Data, skeletonAnim.skeleton.Skin)))
            {
                var slot = skeletonAnim.skeleton.FindSlot(name);
                if (slot == null) continue;
                if (skeletonAnim.CustomSlotMaterials.TryGetValue(slot, out var current) && current?.Pointer == mat.Pointer)
                    skeletonAnim.CustomSlotMaterials.Remove(slot);
            }
        }
        if (isGoalie && mat != null)
        {
            foreach (string name in new[] { "Pants_left", "Pants_right" })
            {
                var slot = skeletonAnim.skeleton.FindSlot(name);
                if (slot == null) continue;
                if (((int)flags & 2) != 0)
                {
                    // Native Body routing also paints goalie pants. Leave the
                    // pants atlas alone until its own material is processed.
                    if (skeletonAnim.CustomSlotMaterials.TryGetValue(slot, out var current) && current?.Pointer == mat.Pointer)
                        skeletonAnim.CustomSlotMaterials.Remove(slot);
                }
                if (((int)flags & 8) != 0) skeletonAnim.CustomSlotMaterials[slot] = mat;
            }
        }
    }

    internal static void Install(Harmony harmony)
    {
        foreach (var method in typeof(Skaters.T2TSkeletonExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m => (m.Name == "ApplySkin" || m.Name == "ApplySkinAsync") && m.GetParameters().Any(p => p.ParameterType == typeof(SkaterData))))
        {
            string prefix = method.GetParameters()[0].ParameterType == typeof(SkeletonGraphic) ? nameof(SelectionPrefix) : nameof(WorldPrefix);
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(AppearanceMaterialRuntime), prefix),
                finalizer: new HarmonyMethod(typeof(AppearanceMaterialRuntime), nameof(RenderFinalizer)));
        }
        harmony.Patch(AccessTools.Method(typeof(ForwardData), "GetCustomizationFlags"), postfix: new HarmonyMethod(typeof(AppearanceMaterialRuntime), nameof(ForwardFlags)));
        harmony.Patch(AccessTools.Method(typeof(GoaltenderData), "GetCustomizationFlags"), postfix: new HarmonyMethod(typeof(AppearanceMaterialRuntime), nameof(GoalieFlags)));
        // Both native async renderers set Skeleton.Skin before these void calls.
        // Hook the actual slot/material application, avoiding the world async
        // entry point's by-value CancellationToken and UniTask return bridge.
        harmony.Patch(AccessTools.Method(typeof(Skaters.T2TSkeletonExtensions), "SetupSkeletonSlots"),
            prefix: new HarmonyMethod(typeof(AppearanceMaterialRuntime), nameof(SlotsPrefix)),
            postfix: new HarmonyMethod(typeof(AppearanceMaterialRuntime), nameof(SlotsPostfix)));
        harmony.Patch(AccessTools.Method(typeof(Skaters.T2TSkeletonExtensions), "SetupSkeletonSlotsMaterials"),
            prefix: new HarmonyMethod(typeof(AppearanceMaterialRuntime), nameof(MaterialsPrefix)),
            postfix: new HarmonyMethod(typeof(AppearanceMaterialRuntime), nameof(MaterialsPostfix)));
        foreach (var method in typeof(Skaters.T2TSkeletonExtensions).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m => m.Name == "OverrideSlotWithCustomizationMaterial"))
        {
            string prefix = method.GetParameters()[0].ParameterType == typeof(SkeletonGraphic) ? nameof(GraphicOverridePrefix) : nameof(WorldOverridePrefix);
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(AppearanceMaterialRuntime), prefix));
        }
    }
}
