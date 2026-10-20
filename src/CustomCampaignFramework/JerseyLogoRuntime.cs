using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using CampaignCore;
using Data;
using UnityEngine;

namespace EndlessMode;

internal readonly struct JerseyLogoRenderChoice
{
    internal bool Override { get; }
    internal Texture Texture { get; }
    internal JerseyLogoRenderChoice(Texture texture) { Override = texture != null; Texture = texture; }
}

// Documents and image content are frozen before an asynchronous render starts.
// This owns jersey artwork only; the team's identity sprites are never modified.
internal static class JerseyLogoRuntime
{
    private sealed class Binding
    {
        internal SkaterData Player;
        internal CampaignDocument Team, Personal, Own;
        internal string Context;
    }
    private sealed class Image
    {
        internal Texture2D Texture;
        internal long Context;
    }
    private static readonly Dictionary<IntPtr, Binding> bindings = new();
    private static readonly Dictionary<string, Image> images = new(StringComparer.OrdinalIgnoreCase);
    private static string activeContext;
    internal static long Context { get; private set; }
    internal static string ContextPath => Path.GetFullPath(Plugin.ModFolder);

    internal static void EnsureContext()
    {
        string next = ContextPath;
        if (string.Equals(activeContext, next, StringComparison.OrdinalIgnoreCase)) return;
        activeContext = next; Context++;
        bindings.Clear();
        JerseyLogoRequests.RetireContext(Context);
        Collect();
    }
    private static CampaignDocument Freeze(CampaignDocument doc) => new(doc?.ToString() ?? "");
    internal static void Bind(SkaterData player, CampaignDocument team, CampaignDocument personal, CampaignDocument own, string context)
    {
        EnsureContext();
        if (player == null) return;
        bindings[player.Pointer] = new Binding { Player = player, Team = Freeze(team), Personal = Freeze(personal), Own = Freeze(own), Context = Path.GetFullPath(context) };
    }
    internal static bool Bound(SkaterData player)
    {
        EnsureContext();
        return player != null && bindings.ContainsKey(player.Pointer);
    }
    internal static void Copy(SkaterData source, SkaterData target)
    {
        EnsureContext();
        if (source != null && target != null && bindings.TryGetValue(source.Pointer, out var b))
            Bind(target, b.Team, b.Personal, b.Own, b.Context);
    }
    internal static bool CopyUniqueClone(SkaterData clone, IEnumerable<SkaterData> candidates)
    {
        if (clone == null || string.IsNullOrEmpty(clone.id)) return false;
        var matches = candidates.Where(p => p != null && p.id == clone.id).Take(2).ToArray();
        if (matches.Length != 1 || !Bound(matches[0])) return false;
        Copy(matches[0], clone); return true;
    }
    internal static CampaignDocument Snapshot(SkaterData player)
    {
        EnsureContext(); var result = new CampaignDocument("");
        if (player == null || !bindings.TryGetValue(player.Pointer, out var b)) return result;
        result.Set("Jersey Binding Context", b.Context);
        foreach (var item in new[] { ("Team", b.Team), ("Player", b.Personal), ("Own", b.Own) })
            foreach (var field in item.Item2.Fields) result.Set("Jersey Binding " + item.Item1 + " " + field.Key, field.Value);
        return result;
    }
    internal static void Restore(SkaterData player, CampaignDocument snapshot)
    {
        if (snapshot.Get("Jersey Binding Context").Length == 0) return;
        CampaignDocument Read(string kind)
        {
            string prefix = "Jersey Binding " + kind + " "; var doc = new CampaignDocument("");
            foreach (var pair in snapshot.Fields.Where(p => p.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) doc.Set(pair.Key.Substring(prefix.Length), pair.Value);
            return doc;
        }
        Bind(player, Read("Team"), Read("Player"), Read("Own"), snapshot.Get("Jersey Binding Context"));
    }
    internal static bool Compatible(SkaterData player, bool away)
    {
        var f = player?.TryCast<ForwardData>();
        if (f != null)
        {
            if ((f.headSkin?.StartsWith("Faces/Referees/", StringComparison.Ordinal) ?? false) || (f.bodySkin?.StartsWith("Body/Referee", StringComparison.Ordinal) ?? false)) return false;
            string skin = away && !string.IsNullOrEmpty(f.bodyAwaySkin) ? f.bodyAwaySkin : f.bodySkin;
            return !string.IsNullOrEmpty(skin) && AppearanceContract.SupportsJerseyLogo("skater", skin);
        }
        var g = player?.TryCast<GoaltenderData>();
        string goalieSkin = away && !string.IsNullOrEmpty(g?.awaySkin) ? g.awaySkin : g?.skin;
        return g != null && !string.IsNullOrEmpty(goalieSkin) && AppearanceContract.SupportsJerseyLogo("goalie", goalieSkin);
    }
    internal static JerseyLogoRenderChoice Resolve(SkaterData player, bool away)
    {
        EnsureContext();
        if (!Compatible(player, away) || !bindings.TryGetValue(player.Pointer, out var b)) return default;
        try
        {
            var choice = JerseyLogoOptions.Resolve(b.Personal, b.Team, away);
            if (choice.Mode == JerseyLogoMode.Inherit) return default;
            if (choice.Mode == JerseyLogoMode.None) return new(Blank());
            bool personal = AppearanceFields.OverridesTeam(b.Personal) && b.Personal.Get("Logo From").Length > 0;
            var payload = b.Team.Fields.Count == 0 || personal ? b.Personal : b.Team;
            string prefix = choice.MetadataPrefix;
            // Legacy exported personal/identity logo is the shared fallback for both kits.
            if (payload.Get(prefix + " Mode").Length == 0 && payload.Get("Logo From") == choice.Source)
                prefix = "Export Logo";
            byte[] bytes; string identity;
            if (payload.Get(prefix + " Mode") == "embedded")
            {
                bytes = Convert.FromBase64String(payload.Get(prefix + " PNG"));
                string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                if (bytes.Length == 0 || payload.Get(prefix + " SHA256") != hash || payload.Get(prefix + " ID") != "custom_t2t_" + hash + ".png")
                    throw new InvalidDataException("Jersey logo payload checksum mismatch.");
                identity = b.Context + "|embedded|" + hash;
            }
            else
            {
                string path = LogoFiles.Resolve(Plugin.ModContentRoot, Path.Combine(UnityEngine.Application.persistentDataPath, "CustomLogos"), choice.Source, b.Context);
                if (path == null) return new(PatchBossLaunchMatch.FindTeamByName(choice.Source)?.logo?.texture);
                path = Path.GetFullPath(path); bytes = File.ReadAllBytes(path);
                identity = b.Context + "|" + path + "|" + Convert.ToHexString(SHA256.HashData(bytes));
            }
            if (images.TryGetValue(identity, out var cached)) { cached.Context = Context; return new(cached.Texture); }
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "CampaignJerseyLogo", hideFlags = HideFlags.HideAndDontSave };
            if (!ImageConversion.LoadImage(texture, bytes)) { UnityEngine.Object.Destroy(texture); throw new InvalidDataException("Jersey logo image cannot be decoded."); }
            UnityEngine.Object.DontDestroyOnLoad(texture);
            images[identity] = new Image { Texture = texture, Context = Context };
            return new(texture);
        }
        catch (Exception ex) { Plugin.Log.LogWarning("[JerseyLogo] Keeping native logo: " + ex.Message); return default; }
    }
    private static Texture Blank()
    {
        const string key = "transparent";
        if (images.TryGetValue(key, out var cached)) { cached.Context = Context; return cached.Texture; }
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "CampaignJerseyNoLogo", hideFlags = HideFlags.HideAndDontSave };
        texture.SetPixels32(new Color32[4]); texture.Apply(false, false); UnityEngine.Object.DontDestroyOnLoad(texture);
        images[key] = new Image { Texture = texture, Context = Context }; return texture;
    }
    // Context changes invalidate ownership, but displayed and in-flight artwork
    // stays alive until a verified replacement or renderer destruction releases it.
    internal static void Collect()
    {
        var retained = JerseyLogoRequests.RetainedTextures().ToHashSet();
        foreach (var pair in images.Where(p => p.Value.Context != Context && !retained.Contains(p.Value.Texture)).ToArray())
        { UnityEngine.Object.Destroy(pair.Value.Texture); images.Remove(pair.Key); }
    }
}
