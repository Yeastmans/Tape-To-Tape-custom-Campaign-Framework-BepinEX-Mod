using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CampaignCore;
using Data;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using Tape2Tape.Customization.UI;
using UnityEngine;

namespace EndlessMode;

/// <summary>
/// Watches the preview document and redraws the open Play Now editor.
/// LiveEquipmentPreviewRuntime uses the same document for every native renderer.
/// </summary>
public sealed class LivePlayerPreview : MonoBehaviour
{
    private static readonly MethodInfo ApplyGraphics = AccessTools.Method(
        typeof(SkaterCustomizationMenu), "ApplyForwardGraphics", new[] { typeof(ForwardData) });
    internal static readonly Dictionary<string, Action<ForwardData, string>> Skins = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Face"] = (p, v) => p.headSkin = v,
        ["Body"] = (p, v) => p.bodySkin = v,
        ["Body Away"] = (p, v) => p.bodyAwaySkin = v,
        ["Helmet"] = (p, v) => p.helmetSkin = v,
        ["Helmet Away"] = (p, v) => p.helmetAwaySkin = v,
        ["Bicep"] = (p, v) => p.bicepSkin = v,
        ["Bicep Away"] = (p, v) => p.bicepAwaySkin = v,
        ["Gloves"] = (p, v) => p.gloveSkin = v,
        ["Gloves Away"] = (p, v) => p.gloveAwaySkin = v,
        ["Pants"] = (p, v) => p.pantsSkin = v,
        ["Pants Away"] = (p, v) => p.pantsAwaySkin = v,
        ["Skates"] = (p, v) => p.skateSkin = v,
        ["Skates Away"] = (p, v) => p.skateAwaySkin = v,
        ["Stick"] = (p, v) => p.stickSkin = v,
        ["Glasses"] = (p, v) => p.glassesSkin = v,
        ["Number Skin"] = (p, v) => p.numberSkin = v,
        ["Logo Skin"] = (p, v) => p.logoSkin = v,
    };
    private static readonly HashSet<string> Colors = new(StringComparer.OrdinalIgnoreCase);
    private static bool installed;
    internal static CampaignDocument ActiveDocument { get; private set; }

    private readonly string file = Path.Combine(Plugin.ModContentRoot, "live-preview.txt");
    private SkaterCustomizationMenu currentMenu;
    private ForwardData currentSource;
    private ForwardData currentClone;
    private string lastText;
    private string activeText;
    private float nextPoll;

    static LivePlayerPreview()
    {
        foreach (string piece in new[] { "Helmet", "Gloves", "Pants", "Socks", "Number", "Stick" })
            foreach (string suffix in new[] { " Color", " Secondary Color", " Tertiary Color", " Away Color", " Away Secondary Color", " Away Tertiary Color" })
                Colors.Add(piece + suffix);
        foreach (string key in new[] { "Jersey Color", "Jersey Secondary Color", "Jersey Accent Color", "Jersey Away Color", "Jersey Away Secondary Color", "Jersey Away Accent Color", "Skates Color", "Blade Color", "Laces Color", "Skates Away Color", "Blade Away Color", "Laces Away Color", "Bicep Color", "Bicep Away Color" })
            Colors.Add(key);
    }

    public LivePlayerPreview(IntPtr pointer) : base(pointer) { }

    internal static void Install()
    {
        if (installed) return;
        if (ApplyGraphics == null)
            Plugin.Log.LogWarning("[LivePreview] Native ApplyForwardGraphics method was not found; Play Now editor redraw disabled.");
        ClassInjector.RegisterTypeInIl2Cpp<LivePlayerPreview>();
        LiveEquipmentPreviewRuntime.Install(new Harmony("com.mods.customcampaign.liveequipmentpreview"));
        var host = new GameObject("CustomCampaignLivePlayerPreview");
        UnityEngine.Object.DontDestroyOnLoad(host);
        host.hideFlags = HideFlags.HideAndDontSave;
        host.AddComponent<LivePlayerPreview>();
        installed = true;
        Plugin.Log.LogInfo("[LivePreview] Watching " + Path.Combine(Plugin.ModContentRoot, "live-preview.txt"));
    }

    private void Update()
    {
        if (Time.unscaledTime < nextPoll) return;
        nextPoll = Time.unscaledTime + 0.4f;
        try { Poll(); }
        catch (Exception ex) { Plugin.Log.LogError("[LivePreview] " + ex); }
    }

    private void Poll()
    {
        string text = File.Exists(file) ? File.ReadAllText(file) : "";
        if (text != activeText)
        {
            var next = string.IsNullOrWhiteSpace(text) ? null : new CampaignDocument(text);
            if (next != null) Validate(next);
            activeText = text;
            ActiveDocument = next;
            LiveEquipmentPreviewRuntime.Refresh();
            Plugin.Log.LogInfo(next == null ? "[LivePreview] Cleared global preview." :
                $"[LivePreview] Updated global preview ({next.Fields.Count} field(s)).");
        }
        if (ApplyGraphics == null) return;
        var menu = Resources.FindObjectsOfTypeAll<SkaterCustomizationMenu>()
            .FirstOrDefault(m => m != null && m.gameObject.activeInHierarchy && m.CurrentSkater?.SkaterData?.TryCast<ForwardData>() != null);
        var source = menu?.CurrentSkater?.SkaterData?.TryCast<ForwardData>();
        if (menu == null || source == null)
        {
            Release();
            return;
        }
        if (currentMenu != menu || currentSource != source)
        {
            Release();
            currentMenu = menu;
            currentSource = source;
            lastText = null;
        }
        if (text == lastText) return;
        lastText = text;
        if (string.IsNullOrWhiteSpace(text)) { RenderOriginal(); return; }

        var doc = ActiveDocument;
        string wanted = doc.Get("Player ID");
        if (wanted.Length > 0 && !wanted.Equals(source.id, StringComparison.OrdinalIgnoreCase))
        {
            RenderOriginal();
            return;
        }
        var fields = doc.Fields;
        var clone = Clone(source, doc);
        Render(menu, clone);
        ReplaceClone(clone);
        Plugin.Log.LogInfo($"[LivePreview] Rendered {fields.Count - (wanted.Length > 0 ? 1 : 0)} field(s) on '{source.firstName} {source.lastName}' ({source.id}).");
    }

    internal static bool Matches(SkaterData player, CampaignDocument doc)
        => player != null && doc != null && (doc.Get("Player ID").Length == 0 ||
            doc.Get("Player ID").Equals(player.id, StringComparison.OrdinalIgnoreCase));

    internal static ForwardData Clone(ForwardData source, CampaignDocument doc)
    {
        var clone = UnityEngine.Object.Instantiate(source);
        CampaignAppearanceRuntime.Copy(source, clone);
        clone.colorSchemes = TeamColorsData.CopyFrom(source.colorSchemes ?? TeamColorsData.Default);
        foreach (var pair in doc.Fields)
            if (Skins.TryGetValue(pair.Key, out var set)) set(clone, pair.Value);
        PlayNowTranslation.Paint(clone.colorSchemes, doc, false);
        PatchPlayerTeamInit.HandleNoHelmetSentinel(clone);
        return clone;
    }

    private static void Validate(CampaignDocument doc)
    {
        foreach (var pair in doc.Fields)
        {
            if (pair.Key.Equals("Player ID", StringComparison.OrdinalIgnoreCase) || Skins.ContainsKey(pair.Key)
                || LiveEquipmentPreviewRuntime.GoalieSkins.ContainsKey(pair.Key)
                || pair.Key is "Goalie Pants Skin" or "Goalie Pants Away" or "Socks" or "Socks Away") continue;
            if (Colors.Contains(pair.Key))
            {
                if (!FieldSchema.TryRgb(pair.Value, out _, out _, out _))
                    throw new InvalidDataException("Invalid RGB value for " + pair.Key + ": " + pair.Value);
                continue;
            }
            throw new InvalidDataException("Unsupported live preview field: " + pair.Key);
        }
    }

    private static void Render(SkaterCustomizationMenu menu, ForwardData data)
        => ApplyGraphics.Invoke(menu, new object[] { data });

    private void RenderOriginal()
    {
        if (currentMenu != null && currentSource != null && currentClone != null)
        {
            Render(currentMenu, currentSource);
            ReplaceClone(null);
            Plugin.Log.LogInfo("[LivePreview] Restored the player editor's original render.");
        }
    }

    private void ReplaceClone(ForwardData next)
    {
        if (currentClone != null) UnityEngine.Object.Destroy(currentClone, 5f);
        currentClone = next;
    }

    private void Release()
    {
        ReplaceClone(null);
        currentMenu = null;
        currentSource = null;
        lastText = null;
    }
}
