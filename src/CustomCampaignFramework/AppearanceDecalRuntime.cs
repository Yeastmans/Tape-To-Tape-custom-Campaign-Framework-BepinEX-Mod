using System;
using CampaignCore;
using Data;
using HarmonyLib;
using Spine;
using Tape2Tape.Customization;

namespace EndlessMode;

internal static class AppearanceDecalRuntime
{
    private static readonly string[] PantsLeft = { "Pants_left", "Pants_left_NE", "Pants_left_NW", "S_pants_left", "SW_Pants_Left", "SE_Pants_left" };
    private static readonly string[] PantsRight = { "Pants_right", "Pants_right_NE", "Pants_right_NW", "S_Pants_right", "SW_Pants_right", "SE_Pants_right" };
    internal static bool SupportsLogo(SkaterData player)
    {
        var f = player?.TryCast<ForwardData>();
        if (f != null) return !string.IsNullOrEmpty(f.bodySkin) && AppearanceContract.SupportsJerseyLogo("skater", f.bodySkin, f.bodyAwaySkin);
        var g = player?.TryCast<GoaltenderData>();
        return g != null && !string.IsNullOrEmpty(g.skin) && AppearanceContract.SupportsJerseyLogo("goalie", g.skin, g.awaySkin);
    }
    // Referees are game-owned, not campaign players. Emptying their logo was the
    // last thing the mod did before the first-faceoff crash after the 2026-09-25
    // game update, so the mod no longer touches them at all.
    internal static bool IsReferee(ForwardData f) => f != null &&
        ((f.headSkin?.StartsWith("Faces/Referees/", StringComparison.Ordinal) ?? false)
         || (f.bodySkin?.StartsWith("Body/Referee", StringComparison.Ordinal) ?? false));

    internal static bool ClearIncompatibleLogo(SkaterData player, Il2CppSystem.Collections.Generic.List<Skin> skins = null)
    {
        if (player == null || SupportsLogo(player)) return false;
        var f = player.TryCast<ForwardData>(); var g = player.TryCast<GoaltenderData>();
        if (IsReferee(f)) return false;
        if (f != null) f.logoSkin = "";
        if (g != null) g.logoSkin = "";
        if (skins != null)
            for (int i = skins.Count - 1; i >= 0; i--)
                if (skins[i]?.Name?.StartsWith("Team_Logo/", StringComparison.Ordinal) == true) skins.RemoveAt(i);
        return true;
    }
    internal static void Forward(SkeletonData skeleton, ForwardData player, Il2CppSystem.Collections.Generic.List<Skin> skins, bool home)
    {
        if (IsReferee(player)) return;
        bool premade = ClearIncompatibleLogo(player, skins);
        var doc = CampaignAppearanceRuntime.Read(player) ?? PlayNowTranslation.ReadPlayer(player);
        // Native SkinHelper replaces logoSkin for customizable uniforms and uses
        // CustomNumberController instead of the old serialized numberSkin field.
        // Apply explicit artwork after that native composition, before rendering.
        var preview = LivePlayerPreview.ActiveDocument;
        if (!LivePlayerPreview.Matches(player, preview)) preview = null;
        if (doc == null && preview == null) return;
        string logo = preview?.Get("Logo Skin") is { Length: > 0 } previewLogo ? previewLogo : doc?.Get("Logo Skin") ?? "";
        string number = preview?.Get("Number Skin") is { Length: > 0 } previewNumber ? previewNumber : doc?.Get("Number Skin") ?? "";
        if (!premade && Replace(skeleton, skins, logo, s => s.StartsWith("Team_Logo/", StringComparison.Ordinal))) player.logoSkin = logo;
        Replace(skeleton, skins, number, s => s.StartsWith("Numbers/", StringComparison.Ordinal) || s == CustomNumberController.CustomNumberSkinName);
        try { ApplySocks(skeleton, player, skins, home, preview, doc); }
        catch (Exception ex) { Plugin.Log.LogWarning("[Appearance] Sock style could not be composed: " + ex); }
    }
    // The game draws socks as the Leg slots of the jersey (Body) skin. A separate sock
    // style swaps in only those attachments, the same way goalie pants are composed.
    private static void ApplySocks(SkeletonData skeleton, ForwardData player, Il2CppSystem.Collections.Generic.List<Skin> skins, bool home, CampaignCore.CampaignDocument preview, CampaignCore.CampaignDocument doc)
    {
        string key = home ? "Socks" : "Socks Away";
        string requested = preview?.Get(key) is { Length: > 0 } previewSocks ? previewSocks : doc?.Get(key) ?? "";
        if (requested.Length == 0) return;
        string jerseyName = home || string.IsNullOrEmpty(player.bodyAwaySkin) ? player.bodySkin : player.bodyAwaySkin;
        var socks = skeleton.FindSkin(requested);
        if (socks == null || string.IsNullOrEmpty(jerseyName)) return;
        for (int i = 0; i < skins.Count; i++)
        {
            var jersey = skins[i];
            if (jersey == null || !jersey.Name.Equals(jerseyName, StringComparison.OrdinalIgnoreCase)) continue;
            var combined = ComposeSocks(skeleton, jersey, socks, out int replaced);
            if (replaced > 0) skins[i] = combined;
            return;
        }
    }
    internal static Skin ComposeSocks(SkeletonData skeletonData, Skin jersey, Skin socks, out int replaced)
    {
        replaced = 0;
        if (skeletonData == null || jersey == null || socks == null || jersey.Name.Equals(socks.Name, StringComparison.OrdinalIgnoreCase)) return jersey;
        var combined = new Skin(jersey.Name + "+Socks/" + socks.Name);
        combined.AddSkin(jersey);
        foreach (string slotName in new[] { "Leg left", "Leg right" })
        {
            var slot = skeletonData.FindSlot(slotName);
            if (slot == null) continue;
            foreach (string direction in new[] { "S", "N", "E", "W", "NE", "NW", "SE", "SW" })
            {
                string name = direction + "-" + slotName;
                if (jersey.GetAttachment(slot.Index, name) == null) continue;
                var replacement = socks.GetAttachment(slot.Index, name);
                if (replacement == null) { replaced = 0; return jersey; } // Keep every pose on one sock style.
                combined.SetAttachment(slot.Index, name, replacement);
                replaced++;
            }
        }
        return replaced > 0 ? combined : jersey;
    }
    private static bool Replace(SkeletonData data, Il2CppSystem.Collections.Generic.List<Skin> skins, string requested, Func<string, bool> matches)
    {
        if (string.IsNullOrEmpty(requested)) return false;
        var replacement = data.FindSkin(requested);
        if (replacement == null) return false; // Generated number names continue through the native controller.
        for (int i = skins.Count - 1; i >= 0; i--) if (skins[i] != null && matches(skins[i].Name)) skins.RemoveAt(i);
        skins.Add(replacement); return true;
    }
    public static void GoaliePostfix(SkeletonData skeletonData, GoaltenderData data, bool useHomeUniform, Il2CppSystem.Collections.Generic.List<Skin> __result)
    {
        if (data == null || skeletonData == null || __result == null) return;
        ClearIncompatibleLogo(data, __result);
        try { ApplyGoaliePants(skeletonData, data, useHomeUniform, __result); }
        catch (Exception ex) { Plugin.Log.LogWarning("[Appearance] Goalie pants style could not be composed: " + ex); }
    }
    private static void ApplyGoaliePants(SkeletonData skeletonData, GoaltenderData data, bool home, Il2CppSystem.Collections.Generic.List<Skin> skins)
    {
        var preview = LivePlayerPreview.ActiveDocument;
        var doc = CampaignAppearanceRuntime.Read(data) ?? PlayNowTranslation.ReadPlayer(data);
        string key = home ? "Goalie Pants Skin" : "Goalie Pants Away";
        string requested = LivePlayerPreview.Matches(data, preview) ? preview.Get(key) : "";
        if (requested.Length == 0) requested = doc?.Get(key) ?? "";
        if (requested.Length == 0 && doc == null) return;
        if (string.IsNullOrWhiteSpace(requested) || requested.Equals("standard", StringComparison.OrdinalIgnoreCase) || requested.Equals("team colors", StringComparison.OrdinalIgnoreCase))
            requested = AppearanceContract.DefaultSkin("goalie", "Goalie Pants Skin");
        var pants = skeletonData.FindSkin(requested);
        string jerseyName = home ? data.skin : data.awaySkin;
        if (pants == null || string.IsNullOrEmpty(jerseyName)) return;
        for (int i = 0; i < skins.Count; i++)
        {
            var jersey = skins[i];
            if (jersey == null || !jersey.Name.Equals(jerseyName, StringComparison.OrdinalIgnoreCase)) continue;
            var combined = ComposeGoaliePants(skeletonData, jersey, pants, out int replaced);
            if (replaced > 0) skins[i] = combined;
            return;
      }
    }
    internal static Skin ComposeGoaliePants(SkeletonData skeletonData, Skin jersey, Skin pants, out int replaced)
    {
        replaced = 0;
        if (skeletonData == null || jersey == null || pants == null || jersey.Name.Equals(pants.Name, StringComparison.OrdinalIgnoreCase)) return jersey;
        var combined = new Skin(jersey.Name + "+Pants/" + pants.Name);
        combined.AddSkin(jersey);
        foreach (var (slotName, names) in new[] { ("Pants_left", PantsLeft), ("Pants_right", PantsRight) })
        {
            var slot = skeletonData.FindSlot(slotName);
            if (slot == null) continue;
            foreach (string name in names)
            {
                if (jersey.GetAttachment(slot.Index, name) == null) continue;
                var replacement = pants.GetAttachment(slot.Index, name);
                if (replacement == null) { replaced = 0; return jersey; } // Keep every pose on one pants style.
                combined.SetAttachment(slot.Index, name, replacement);
                replaced++;
            }
        }
        return replaced > 0 ? combined : jersey;
    }
    internal static void Install(Harmony harmony) => harmony.Patch(
        AccessTools.Method(typeof(Skaters.SkinHelper), "GetSkinsForGoalie"),
        prefix: new HarmonyMethod(typeof(AppearanceDecalRuntime), nameof(GoaliePrefix)),
        postfix: new HarmonyMethod(typeof(AppearanceDecalRuntime), nameof(GoaliePostfix)));

    private static void GoaliePrefix(GoaltenderData data, bool useHomeUniform)
        => CampaignAppearanceRuntime.ForKit(data, useHomeUniform);
}
