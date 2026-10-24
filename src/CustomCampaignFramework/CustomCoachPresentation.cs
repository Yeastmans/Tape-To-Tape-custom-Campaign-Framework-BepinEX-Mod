using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CampaignCore;
using HarmonyLib;
using STS.Map;
using Tape2Tape.Hockey.UI;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EndlessMode;

internal static class CustomCoachPresentation
{
    private sealed class Art
    {
        internal MapObject Map;
        internal MapNode Node;
        internal SpriteRenderer Renderer;
        internal Texture2D Texture;
        internal Sprite Sprite;
    }
    private static readonly Dictionary<IntPtr, Art> art = new();
    internal static SpriteRenderer Renderer(MapNode node) => node != null && art.TryGetValue(node.Pointer, out var value) ? value.Renderer : null;
    internal static void Apply(MapNode node)
    {
        var coach = CustomCoachRuntime.For(node); if (coach == null) return;
        Tooltip(node);
        if (coach.ImagePng.Length == 0 || art.ContainsKey(node.Pointer)) return;
        Texture2D texture = null; Sprite sprite = null; GameObject image = null;
        try {
            if (!CustomMapVisualBounds.TryMeasure(node, true, out var bounds))
                throw new InvalidOperationException("Native Coach artwork bounds are not ready.");
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, Convert.FromBase64String(coach.ImagePng))) throw new ArgumentException("Cannot decode Coach PNG.");
            texture.name = "CCF_CoachTexture_" + coach.Id;
            float pixelsPerUnit = Math.Max(texture.width / (bounds.MaxX - bounds.MinX), texture.height / (bounds.MaxY - bounds.MinY));
            sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, 0), pixelsPerUnit);
            image = new GameObject("CCF_CustomCoachArt"); image.transform.SetParent(node.transform, false);
            image.transform.position = new Vector3((bounds.MinX + bounds.MaxX) / 2, bounds.MinY, node.transform.position.z);
            // Match world dimensions even when the prefab root has a scale.
            var scale = node.transform.lossyScale;
            image.transform.localScale = new Vector3(1 / Math.Max(.001f, Math.Abs(scale.x)), 1 / Math.Max(.001f, Math.Abs(scale.y)), 1);
            var renderer = image.AddComponent<SpriteRenderer>(); renderer.sprite = sprite;
            var native = node.skeletonSprite?.GetComponent<Renderer>();
            if (native != null) { renderer.sortingLayerID = native.sortingLayerID; renderer.sortingOrder = native.sortingOrder; }
            art[node.Pointer] = new Art { Node = node, Map = node.GetComponentInParent<MapObject>(), Renderer = renderer, Texture = texture, Sprite = sprite };
            State(node);
        } catch (Exception ex) {
            if (image != null) Object.Destroy(image); if (sprite != null) Object.Destroy(sprite); if (texture != null) Object.Destroy(texture);
            Plugin.Log.LogError("[CustomCoach] Cannot display custom artwork: " + ex.Message);
        }
    }
    internal static void Tooltip(MapNode node)
    {
        var coach = CustomCoachRuntime.For(node);
        if (coach == null || node.m_TooltipTitle == null) return;
        var title = node.m_TooltipTitle; title.richText = false; title.text = coach.Name;
        title.enableWordWrapping = true; title.enableAutoSizing = true; title.fontSizeMin = 12;
    }
    internal static void State(MapNode node)
    {
        if (!art.TryGetValue(node.Pointer, out var value) || value.Renderer == null) return;
        if (node.skeletonSprite != null) foreach (var renderer in node.skeletonSprite.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
        if (node.m_Icon != null) node.m_Icon.enabled = false;
        int state = (int)node.CurrentState;
        value.Renderer.color = state == 0 ? new Color(.48f, .48f, .48f, 1) : state == 1 ? new Color(.7f, .7f, .7f, 1) : Color.white;
    }
    internal static void Release(MapObject map)
    {
        foreach (var entry in art.Where(p => p.Value.Map == null || p.Value.Map.Pointer == map.Pointer).ToArray()) {
            art.Remove(entry.Key); var value = entry.Value;
            if (value.Renderer != null) Object.Destroy(value.Renderer.gameObject);
            if (value.Sprite != null) Object.Destroy(value.Sprite); if (value.Texture != null) Object.Destroy(value.Texture);
        }
    }
    internal static void EmptyMessage(Transform parent, bool empty)
    {
        const string name = "CCF_CoachEmptyMessage";
        var existing = parent.Find(name);
        if (!empty) { if (existing != null) existing.gameObject.SetActive(false); return; }
        if (existing == null) {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = parent.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.font != null)?.font ?? TMP_Settings.defaultFontAsset;
            text.text = "No eligible relics remain in this Coach's pool.\nYou can leave without taking a relic.";
            text.fontSize = 24; text.alignment = TextAlignmentOptions.Center; text.enableWordWrapping = true; text.raycastTarget = false;
            var rect = text.rectTransform; rect.anchorMin = rect.anchorMax = new Vector2(.5f, .18f);
            rect.pivot = new Vector2(.5f, .5f); rect.sizeDelta = new Vector2(760, 90); rect.anchoredPosition = Vector2.zero;
            existing = go.transform;
        }
        existing.gameObject.SetActive(true);
    }
}
[HarmonyPatch(typeof(MapObject), nameof(MapObject.InitializeMap))]
internal static class PresentCustomCoaches
{
    [HarmonyPostfix] private static void Postfix(MapObject __instance) { foreach (var node in __instance.MapNodes) if (node != null) CustomCoachPresentation.Apply(node); }
}
[HarmonyPatch(typeof(MapNode), nameof(MapNode.SetState))]
internal static class CustomCoachState
{
    [HarmonyPostfix] private static void Postfix(MapNode __instance) => CustomCoachPresentation.State(__instance);
}
[HarmonyPatch]
internal static class CustomCoachEmptyMenu
{
    // FinishChoices contains an inlined shortcut refresh in the current build;
    // patch its real caller as well as the standalone refresh helper.
    private static IEnumerable<MethodBase> TargetMethods() {
        yield return AccessTools.Method(typeof(EventChoiceMenu), "RefreshSkipShortcut");
        yield return AccessTools.Method(typeof(EventChoiceMenu), "FinishChoices");
    }
    [HarmonyPostfix] private static void Postfix(EventChoiceMenu __instance)
    {
        bool empty = CustomCoachRuntime.Empty(__instance.SourceNode);
        if (empty) {
            // Tutorial presentation normally hides Skip, even for an empty
            // starter Coach. Keep the native action available for exhaustion.
            __instance.m_SkipContainer?.SetActive(true);
            if (__instance.m_SkipShortcut != null) {
                __instance.m_SkipShortcut.m_CanTrigger = true;
                __instance.m_SkipShortcut.RefreshTriggerableState();
            }
        }
        CustomCoachPresentation.EmptyMessage(__instance.transform, empty);
    }
}

[HarmonyPatch(typeof(EventChoicePanel), "InitializeChoicesButtons")]
internal static class CustomCoachEmptyLegacyMenu
{
    [HarmonyPrefix] private static bool Prefix(EventChoicePanel __instance) => !CustomCoachRuntime.EmptyFor(__instance.currentMapNode);
    [HarmonyPostfix]
    private static void Postfix(EventChoicePanel __instance)
    {
        bool empty = CustomCoachRuntime.EmptyFor(__instance.currentMapNode);
        CustomCoachPresentation.EmptyMessage(__instance.transform, empty);
        var old = __instance.choicesList.Find("CCF_CoachLeave");
        if (!empty) { if (old != null) old.gameObject.SetActive(false); return; }
        if (old == null) {
            var go = new GameObject("CCF_CoachLeave"); go.transform.SetParent(__instance.choicesList, false);
            var image = go.AddComponent<UnityEngine.UI.Image>(); image.color = new Color(.15f, .22f, .3f, 1);
            image.rectTransform.sizeDelta = new Vector2(300, 65);
            var button = go.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = image;
            button.onClick.AddListener((UnityEngine.Events.UnityAction)(Action)(() => __instance.HideMenu()));
            var label = new GameObject("Label"); label.transform.SetParent(go.transform, false);
            var text = label.AddComponent<TextMeshProUGUI>(); text.text = "Leave"; text.fontSize = 26; text.alignment = TextAlignmentOptions.Center; text.raycastTarget = false;
            text.font = __instance.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.font != null)?.font ?? TMP_Settings.defaultFontAsset;
            text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
            old = go.transform;
        }
        old.gameObject.SetActive(true);
        __instance._eventSystem?.SetSelectedGameObject(old.gameObject);
    }
}
