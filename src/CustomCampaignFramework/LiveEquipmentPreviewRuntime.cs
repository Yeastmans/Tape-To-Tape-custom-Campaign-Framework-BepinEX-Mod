using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CampaignCore;
using Data;
using HarmonyLib;
using Spine;
using Spine.Unity;
using UnityEngine;

namespace EndlessMode;

// The preview is applied to short-lived render inputs. Native player assets,
// campaign state and Play Now customization data are never edited.
internal static class LiveEquipmentPreviewRuntime
{
    internal static readonly Dictionary<string, Action<GoaltenderData, string>> GoalieSkins = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Skin"] = (p, v) => p.skin = v,
        ["Skin Away"] = (p, v) => p.awaySkin = v,
        ["Helmet Skin"] = (p, v) => p.helmetSkin = v,
        ["Glove Skin"] = (p, v) => p.gloveSkin = v,
        ["Glove Away"] = (p, v) => p.awayGloveSkin = v,
        ["Blocker Skin"] = (p, v) => p.blockerSkin = v,
        ["Blocker Away"] = (p, v) => p.awayBlockerSkin = v,
        ["Pads Skin"] = (p, v) => p.padsSkin = v,
        ["Pads Away"] = (p, v) => p.awayPadsSkin = v,
        ["Stick Skin"] = (p, v) => p.stickSkin = v,
        ["Stick Away"] = (p, v) => p.awayStickSkin = v,
        ["Logo Skin"] = (p, v) => p.logoSkin = v,
    };

    private sealed class Recipe
    {
        internal MethodInfo Method;
        internal object[] Arguments;
        internal SkaterData Player;
    }
    private sealed class ComposeState
    {
        internal Recipe Recipe;
        internal UnityEngine.Object Clone;
    }
    private sealed class Binding
    {
        internal SkeletonAnimation World;
        internal SkeletonGraphic Graphic;
        internal SkaterData Player;
        internal TeamColorsData Colors;
        internal Texture Logo;
        internal Recipe Recipe;
        internal bool MatchTransform, SkipAnimation;
    }

    private static readonly Dictionary<IntPtr, Recipe> Latest = new();
    private static readonly Dictionary<IntPtr, Binding> Bindings = new();
    private static readonly MethodInfo ForwardMethod = AccessTools.Method(typeof(Skaters.SkinHelper), "GetSkins");
    private static readonly MethodInfo GoalieMethod = AccessTools.Method(typeof(Skaters.SkinHelper), "GetSkinsForGoalie");
    private static bool refreshing;

    internal static void Install(Harmony harmony)
    {
        harmony.Patch(ForwardMethod, prefix: new HarmonyMethod(typeof(LiveEquipmentPreviewRuntime), nameof(ForwardPrefix)),
            postfix: new HarmonyMethod(typeof(LiveEquipmentPreviewRuntime), nameof(Composed)));
        harmony.Patch(GoalieMethod, prefix: new HarmonyMethod(typeof(LiveEquipmentPreviewRuntime), nameof(GoaliePrefix)),
            postfix: new HarmonyMethod(typeof(LiveEquipmentPreviewRuntime), nameof(Composed)));
        var type = typeof(Skaters.T2TSkeletonExtensions);
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m => (m.Name == "ApplySkin" || m.Name == "ApplySkinAsync") &&
                m.GetParameters().Any(p => p.ParameterType == typeof(SkaterData))))
        {
            string name = method.GetParameters()[0].ParameterType == typeof(SkeletonAnimation)
                ? nameof(CaptureWorld) : nameof(CaptureGraphic);
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(LiveEquipmentPreviewRuntime), name));
        }
        harmony.Patch(AccessTools.Method(typeof(TeamColorsData), "MergeWithCustomSkaterColors"),
            postfix: new HarmonyMethod(typeof(LiveEquipmentPreviewRuntime), nameof(ColorsPostfix)));
    }

    private static void ForwardPrefix(ref ForwardData forwardData, object[] __args, out ComposeState __state)
    {
        var source = forwardData;
        __state = source == null ? null : new ComposeState {
            Recipe = new Recipe { Method = ForwardMethod, Arguments = (object[])__args.Clone(), Player = source } };
        var doc = LivePlayerPreview.ActiveDocument;
        if (source == null || !LivePlayerPreview.Matches(source, doc)) return;
        var clone = LivePlayerPreview.Clone(source, doc);
        __state.Clone = clone;
        forwardData = clone;
    }

    private static void GoaliePrefix(ref GoaltenderData data, object[] __args, out ComposeState __state)
    {
        var source = data;
        __state = source == null ? null : new ComposeState {
            Recipe = new Recipe { Method = GoalieMethod, Arguments = (object[])__args.Clone(), Player = source } };
        var doc = LivePlayerPreview.ActiveDocument;
        if (source == null || !LivePlayerPreview.Matches(source, doc)) return;
        var clone = UnityEngine.Object.Instantiate(source);
        CampaignAppearanceRuntime.Copy(source, clone);
        clone.colorSchemes = TeamColorsData.CopyFrom(source.colorSchemes ?? TeamColorsData.Default);
        foreach (var pair in doc.Fields)
            if (GoalieSkins.TryGetValue(pair.Key, out var set)) set(clone, pair.Value);
        PlayNowTranslation.Paint(clone.colorSchemes, doc, false);
        __state.Clone = clone;
        data = clone;
    }

    private static void Composed(ComposeState __state)
    {
        if (__state == null) return;
        if (!refreshing) Latest[__state.Recipe.Player.Pointer] = __state.Recipe;
        if (__state.Clone != null) UnityEngine.Object.Destroy(__state.Clone, 5f);
    }

    private static void CaptureWorld(SkeletonAnimation skeletonAnimation, SkaterData skaterData,
        TeamColorsData teamColors, Texture logo)
    {
        if (refreshing || skeletonAnimation == null || skaterData == null) return;
        Latest.TryGetValue(skaterData.Pointer, out var recipe);
        Bindings[skeletonAnimation.Pointer] = new Binding { World = skeletonAnimation,
            Player = skaterData, Colors = teamColors, Logo = logo, Recipe = recipe };
    }

    private static void CaptureGraphic(SkeletonGraphic skeletonGraphic, SkaterData skaterData,
        TeamColorsData teamColors, Texture logo, bool matchTransform, bool skipAnimation)
    {
        if (refreshing || skeletonGraphic == null || skaterData == null) return;
        Latest.TryGetValue(skaterData.Pointer, out var recipe);
        Bindings[skeletonGraphic.Pointer] = new Binding { Graphic = skeletonGraphic,
            Player = skaterData, Colors = teamColors, Logo = logo, Recipe = recipe,
            MatchTransform = matchTransform, SkipAnimation = skipAnimation };
    }

    // The native palette merge is also used in campaign and Play Now. Apply the
    // preview last, to a copy, so teammates sharing a palette stay unchanged.
    private static void ColorsPostfix(TeamColorsData colors, SkaterData skater, ref TeamColorsData __result)
    {
        var doc = LivePlayerPreview.ActiveDocument;
        if (__result == null || !LivePlayerPreview.Matches(skater, doc)) return;
        var copy = TeamColorsData.CopyFrom(__result);
        PlayNowTranslation.Paint(copy, doc, PlayNowTranslation.PreviewAway(colors, skater));
        __result = copy;
    }

    internal static void Refresh()
    {
        refreshing = true;
        int refreshed = 0;
        try
        {
            foreach (var pair in Bindings.ToArray())
            {
                var binding = pair.Value;
                try
                {
                    var renderer = (Component)binding.World ?? binding.Graphic;
                    if (renderer == null || !renderer.gameObject.activeInHierarchy || binding.Player == null)
                    {
                        Bindings.Remove(pair.Key);
                        continue;
                    }
                    if (binding.Recipe == null) continue;
                    var skins = binding.Recipe.Method.Invoke(null, binding.Recipe.Arguments)
                        as Il2CppSystem.Collections.Generic.List<Skin>;
                    if (skins == null) continue;
                    var composite = new Skin("LivePreview/" + binding.Player.id);
                    for (int i = 0; i < skins.Count; i++) if (skins[i] != null) composite.AddSkin(skins[i]);
                    if (binding.World != null)
                        Skaters.T2TSkeletonExtensions.ApplySkin(binding.World, composite, binding.Player, binding.Colors, binding.Logo);
                    else
                        Skaters.T2TSkeletonExtensions.ApplySkin(binding.Graphic, composite, binding.Player,
                            binding.Colors, binding.Logo, binding.MatchTransform, binding.SkipAnimation, true);
                    refreshed++;
                }
                catch (Exception ex)
                {
                    Bindings.Remove(pair.Key);
                    Plugin.Log.LogWarning("[LivePreview] Refresh failed on " + binding.Player?.id + ": " + ex);
                }
            }
        }
        finally { refreshing = false; }
        Plugin.Log.LogInfo($"[LivePreview] Refreshed {refreshed} active renderer(s).");
    }
}
