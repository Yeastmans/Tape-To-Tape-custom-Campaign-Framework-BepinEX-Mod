using System;
using System.Collections.Generic;
using Data;
using HarmonyLib;
using Spine;
using Spine.Unity;
using UnityEngine;
using Skaters;

namespace EndlessMode;

// Several native UI continuations repaint their newly built materials with
// the raw team palette. Retain the native merged palette only when the exact
// preview, skin, raw palette and material still belong to that render.
internal static class PreviewEquipmentColors
{
    internal sealed class Entry
    {
        internal SkeletonGraphic Owner;
        internal Skin Skin, AppliedSkin;
        internal SkaterData Player;
        internal TeamColorsData Raw, Merged;
    }

    internal const int MaxOwners = 64;
    private static readonly Dictionary<IntPtr, Entry> Entries = new();
    private static int warnings;

    // These are borrowed native objects. This class never creates, destroys,
    // registers or changes a palette, texture, material or player field.
    private static bool Bound(SkaterData player)
        => player != null && (CampaignAppearanceRuntime.Read(player) != null
            || PlayNowTranslation.ReadPlayer(player)?.Get("Export Version") is "2" or "3");

    internal static Entry Track(SkeletonGraphic owner, Skin skin, SkaterData player, TeamColorsData raw)
    {
        try
        {
            Sweep();
            if (owner == null) return null;
            // Reusing a preview for a native/unbound player clears old ownership.
            Entries.Remove(owner.Pointer);
            if (skin == null || raw == null || !Bound(player)
                || !(player.HasCustomStickColor || player.HasCustomSkatesColor)
                || Entries.Count >= MaxOwners) return null;
            var entry = new Entry { Owner = owner, Skin = skin, Player = player, Raw = raw };
            Entries.Add(owner.Pointer, entry);
            return entry;
        }
        catch (Exception ex) { Report(ex); return null; }
    }

    internal static void Cancel(Entry entry)
    {
        if (entry?.Owner == null) return;
        if (Entries.TryGetValue(entry.Owner.Pointer, out var current) && ReferenceEquals(current, entry))
            Entries.Remove(entry.Owner.Pointer);
    }

    internal static void Capture(SkeletonGraphic owner, Skin skin, TeamColorsData merged)
    {
        try
        {
            if (owner == null || skin == null
                || !Entries.TryGetValue(owner.Pointer, out var entry)
                || entry.Skin.Pointer != skin.Pointer) return;
            entry.Merged = null;
            entry.AppliedSkin = null;
            if (merged == null || entry.Raw.Pointer == merged.Pointer) return;
            // Only accept the native equipment merge. A different body/number
            // palette, stale player colors or a third-party merge is left alone.
            var skeleton = owner.Skeleton;
            var applied = skeleton?.Skin;
            if (applied == null) return;
            if (applied.Pointer != skin.Pointer)
            {
                // Native SetSkinAsync keeps the previous Skin object when the
                // requested content hash equals _lastSkinHash. Reopened menus
                // can rebuild an equivalent preload with a different pointer.
                // Require that same native hash and equivalent applied content;
                // do not treat arbitrary different Skin objects as this render.
                int requestedHash = Skin.ComputeHash(skin);
                if (skeleton._lastSkinHash != requestedHash
                    || Skin.ComputeHash(applied) != requestedHash) return;
            }
            if (MatchesEquipmentMerge(entry, merged))
            {
                entry.AppliedSkin = applied;
                entry.Merged = merged;
            }
        }
        catch (Exception ex) { Report(ex); }
    }

    internal static void Restore(ref TeamColorsData colors, Material material)
    {
        try
        {
            Sweep();
            if (colors == null || material == null) return;
            Entry selected = null;
            foreach (var entry in Entries.Values)
            {
                if (!OwnsMaterial(entry.Owner, material)) continue;
                // A material shared by previews has no unique owner.
                if (selected != null) return;
                selected = entry;
            }
            if (selected == null || selected.Raw.Pointer != colors.Pointer || selected.Merged == null
                || selected.AppliedSkin == null
                || selected.Owner.Skeleton?.Skin?.Pointer != selected.AppliedSkin.Pointer
                || !Bound(selected.Player) || !MatchesEquipmentMerge(selected, selected.Merged)) return;
            // Native team previews prepare an invisible buffer before revealing
            // it. Membership/skin identity, not screen visibility, owns a render.
            var texture = selected.Merged._texture2D;
            if (texture == null || material.GetTexture(T2TSkeletonExtensions.Scheme) != texture) return;
            // The last native call already references this merged texture.
            // Preserve another mod's replacement texture/palette by requiring
            // that identity before redirecting this argument only.
            colors = selected.Merged;
        }
        catch (Exception ex) { Report(ex); }
    }

    private static bool OwnsMaterial(SkeletonGraphic owner, Material material)
    {
        if (owner == null || owner.CustomMaterialOverride == null) return false;
        foreach (var value in owner.CustomMaterialOverride.Values)
            if (value != null && value.Pointer == material.Pointer) return true;
        return false;
    }

    internal static bool MatchesEquipmentMerge(Entry entry, TeamColorsData merged)
    {
        var raw = entry.Raw; var player = entry.Player;
        if (raw == null || merged == null || player == null) return false;
        if (!Same(raw.jerseyScheme, merged.jerseyScheme) || !Same(raw.numberScheme, merged.numberScheme)
            || !Same(raw.pantsScheme, merged.pantsScheme) || !Same(raw.glovesScheme, merged.glovesScheme)
            || !Same(raw.helmetScheme, merged.helmetScheme) || !Same(raw.socksScheme, merged.socksScheme)) return false;
        var stick = player.HasCustomStickColor ? player.StickColor : raw.stickScheme;
        var skates = player.HasCustomSkatesColor ? player.SkatesColor : raw.skatesScheme;
        return Same(stick, merged.stickScheme) && Same(skates, merged.skatesScheme)
            && (!Same(raw.stickScheme, merged.stickScheme) || !Same(raw.skatesScheme, merged.skatesScheme));
    }

    private static bool Same(ColorScheme a, ColorScheme b)
        => a != null && b != null && Same(a.primaryColor, b.primaryColor)
            && Same(a.secondaryColor, b.secondaryColor) && Same(a.tertiaryColor, b.tertiaryColor);
    private static bool Same(Color a, Color b)
        => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;

    internal static void Sweep()
    {
        var dead = new List<IntPtr>();
        foreach (var pair in Entries)
            if (pair.Value.Owner == null || pair.Value.Player == null) dead.Add(pair.Key);
        foreach (var key in dead) Entries.Remove(key);
    }

    private static void Report(Exception ex)
    {
        if (warnings++ < 4) Plugin.Log.LogWarning("[PreviewEquipment] Kept native palette: " + ex.Message);
    }
}

[HarmonyPatch(typeof(T2TSkeletonExtensions), "ApplySkinAsync", new Type[] {
    typeof(SkeletonGraphic), typeof(Skin), typeof(SkaterData), typeof(TeamColorsData),
    typeof(Texture), typeof(bool), typeof(bool), typeof(bool) })]
internal static class PreviewEquipmentTrackPatch
{
    [HarmonyPrefix, HarmonyPriority(Priority.Last)]
    internal static void Prefix(SkeletonGraphic __0, Skin __1, SkaterData __2, TeamColorsData __3,
        out PreviewEquipmentColors.Entry __state)
        => __state = PreviewEquipmentColors.Track(__0, __1, __2, __3);

    [HarmonyFinalizer]
    internal static Exception Finalizer(Exception __exception, PreviewEquipmentColors.Entry __state)
    {
        if (__exception != null) PreviewEquipmentColors.Cancel(__state);
        return __exception;
    }
}

[HarmonyPatch(typeof(T2TSkeletonExtensions), "OverrideSlotWithCustomizationMaterial", new Type[] {
    typeof(SkeletonGraphic), typeof(Skin), typeof(TeamColorsData), typeof(string),
    typeof(Il2CppSystem.Collections.Generic.Dictionary<Texture, Material>) })]
internal static class PreviewEquipmentCapturePatch
{
    [HarmonyPrefix, HarmonyPriority(Priority.Last)]
    internal static void Prefix(SkeletonGraphic __0, Skin __1, TeamColorsData __2)
        => PreviewEquipmentColors.Capture(__0, __1, __2);
}

[HarmonyPatch(typeof(T2TSkeletonExtensions), "ApplyColorSchemesToMaterial", new Type[] {
    typeof(TeamColorsData), typeof(Material) })]
internal static class PreviewEquipmentRestorePatch
{
    [HarmonyPrefix, HarmonyPriority(Priority.Last)]
    internal static void Prefix(ref TeamColorsData __0, Material __1)
        => PreviewEquipmentColors.Restore(ref __0, __1);
}
