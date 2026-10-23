using System;
using System.Collections.Generic;
using System.Linq;
using CampaignCore;
using Data;
using UnityEngine;

namespace EndlessMode;

// Campaign and exported players share the native post-merge palette writer.
// Retain authored home/away values until render time instead of painting shared
// team assets once and hoping every preview uses the same kit.
internal static class CampaignAppearanceRuntime
{
    // RunDataModelV2 stores home palettes but omits away palettes and size offsets.
    // Restore only appearance after native loading; earned stats, talents,
    // roster identities and the saved personal colorSchemes remain authoritative.
    internal static void RestoreSavedTeam(TeamData team)
    {
        if (Plugin.IsDefaultMode || !CustomTeamRules.IsCustom(team)) return;
        var config = Plugin.PlayerTeamConfigs.Values.Concat(Plugin.ConfigTeams)
            .FirstOrDefault(c => c != null && !string.IsNullOrEmpty(c.IdentityKey)
                && CustomTeamIdentity.Create(Plugin.ActiveCampaign, c.IdentityKey) == team.id);
        if (config == null) return;
        RestoreAuthoredEquipment(team, config, preserveSavedColors: true);
        SavedPlayerNumberStyles.Restore(team, config);
        // The saved goalie model also omits numberFormat. Restore appearance only
        // for the authored goalie; a replacement or unnamed donor stays unchanged.
        var goalie = team.goalie;
        if (goalie != null && !GoalieRecruitment.IsRecruit(goalie) && !string.IsNullOrWhiteSpace(config.Goalie?.Name)
            && string.Equals(config.Goalie.Name.Trim(), ((goalie.firstName ?? "") + " " + (goalie.lastName ?? "")).Trim(), StringComparison.OrdinalIgnoreCase)
            && config.Uniform.NumberFormat >= 0 && config.Uniform.NumberFormat < FieldSchema.NumberStyles.Length)
            goalie.numberFormat = (NumberFormat)(config.Goalie.NumberFormat >= 0 ? config.Goalie.NumberFormat : config.Uniform.NumberFormat);
        var palette = TeamPalette(config);
        team.homeColors = TeamColorsData.CopyFrom(team.homeColors ?? TeamColorsData.Default);
        team.awayColors = TeamColorsData.CopyFrom(team.awayColors ?? team.homeColors);
        team.homeGoalieColors = TeamColorsData.CopyFrom(team.homeGoalieColors ?? team.homeColors);
        team.awayGoalieColors = TeamColorsData.CopyFrom(team.awayGoalieColors ?? team.homeGoalieColors);
        PlayNowTranslation.Paint(team.homeColors, palette, false);
        PlayNowTranslation.Paint(team.awayColors, palette, true);
        PlayNowTranslation.Paint(team.homeGoalieColors, palette, false);
        PlayNowTranslation.Paint(team.awayGoalieColors, palette, true);
        if (team.forwards != null)
            foreach (var forward in team.forwards)
            {
                if (forward == null) continue;
                forward.skateSkin = forward.skateAwaySkin = AppearanceContract.StandardSkates;
                // Menu previews use these fields instead of the match palette.
                if (forward.colorSchemes?.stickScheme is { } stick && !stick.IsEmpty())
                    forward.StickColor = ColorScheme.CopyFrom(stick);
                if (forward.colorSchemes?.skatesScheme is { } skates && !skates.IsEmpty())
                    forward.SkatesColor = ColorScheme.CopyFrom(skates);
            }
    }
    // Native new-run SetupCampaignSkins resets every skater to the custom team
    // uniform. Reapply only the uniquely matched author's equipment after that
    // reset (and when loading affected saves), never starting stats or abilities.
    internal static void RestoreAuthoredEquipment(TeamData team, TeamConfig config, bool preserveSavedColors = false)
    {
        if (team?.forwards == null || config == null || VanillaCampaignRuntime.InheritedSquad(config)) return;
        if (SquadBehaviors.Enabled(config.SquadTemplate, "referee_look", key =>
            config.SquadBehaviors.TryGetValue(key, out var value) ? value : "")) return;
        var authored = new[] { config.LW, config.RW, config.C, config.LD, config.RD,
            config.L2_LW, config.L2_RW, config.L2_C, config.L2_LD, config.L2_RD };
        var names = new string[team.forwards.Count];
        for (int i = 0; i < names.Length; i++)
        {
            var player = team.forwards[i];
            if (player != null) names[i] = ((player.firstName ?? "") + " " + (player.lastName ?? "")).Trim();
        }
        var targets = AuthoredRosterMatcher.Resolve(authored.Select(p => p?.Name).ToArray(), names, liveRun: true);
        int restored = 0;
        for (int slot = 0; slot < targets.Length; slot++)
        {
            int index = targets[slot];
            if (index < 0 || authored[slot] == null) continue;
            var player = team.forwards[index];
            var savedColors = player.colorSchemes;
            var savedStick = player.StickColor;
            var savedSkates = player.SkatesColor;
            PatchPlayerTeamInit.ApplyUniformToForward(player, config.Uniform);
            // The native save retains skaterSize (including earned changes), but
            // omits sizeOffsetPercentage. This appearance-only call restores the
            // authored multiplier without changing the saved size enum or stats.
            PatchBossLaunchMatch.ApplyPlayerLookOnly(player, authored[slot]);
            if (preserveSavedColors)
            {
                player.colorSchemes = savedColors;
                player.StickColor = savedStick;
                player.SkatesColor = savedSkates;
            }
            restored++;
        }
        Plugin.Log.LogInfo($"[Appearance] Restored authored equipment and size offsets for {restored} skaters on '{team.teamName}'; saved stats, abilities and size enum retained.");
    }
    private sealed class Binding
    {
        internal CampaignDocument Team = new(""), Player = new(""), Effective = new(""), Own = new("");
        internal bool TeamPainted;
        internal bool Away;
        internal bool Export;
        internal bool OwnColors;
    }

    // What the match renderer actually does (Forward.InitGraphics / Goaltender.InitializeSkin
    // → ApplySkinAsync → TeamColorsData.Merge(teamKit, player.colorSchemes)): per piece, the
    // player's colorSchemes wins where set (any channel alpha > 0), otherwise the team kit for
    // this game's side. MergeWithCustomSkaterColors/StickColor/SkatesColor are menu-only.
    // The mod therefore builds colorSchemes as: the native team kit, the campaign team file's
    // colors on top, and the player's own stick/skates (one value, both kits) last.
    internal static void OwnColors(SkaterData player, Func<string, string> own, CampaignDocument fallback)
    {
        if (player == null) return;
        var defaults = TeamColorsData.Default;
        // Kept for the game's menus, which do use these fields.
        player.StickColor = Scheme(PlayerColorModel.StickKeys, own, fallback, defaults?.stickScheme);
        player.SkatesColor = Scheme(PlayerColorModel.SkatesKeys, own, fallback, defaults?.skatesScheme);
    }
    // Base game: the stick is the player's alone (no team stick colors in the team customizer);
    // a player who never chose stick colors has the customizer's default ones. Skates are on
    // both: the team kit, with an optional player override.
    private static void Overlay(TeamColorsData palette, CampaignDocument own, CampaignDocument fallback, bool away = false)
    {
        if (palette == null) return;
        PlayNowTranslation.Paint(palette, PlayerKitColors.Palette(own, away, fallback), away);
        Func<string, string> read = k => own.Get(k);
        palette.stickScheme = PlayerColorModel.HasStick(read)
            ? Scheme(PlayerColorModel.StickKeys, read, null, DefaultStickColors())
            : ColorScheme.CopyFrom(DefaultStickColors());
        if (PlayerColorModel.HasSkates(read)) palette.skatesScheme = Scheme(PlayerColorModel.SkatesKeys, read, fallback, palette.skatesScheme);
    }
    private static ColorScheme defaultStick;
    private static ColorScheme DefaultStickColors()
    {
        if (defaultStick != null) return defaultStick;
        try
        {
            foreach (var data in Resources.FindObjectsOfTypeAll<Tape2Tape.Customization.UI.SkaterCustomizationColorData>())
                if (data?.DefaultStickColors != null) { defaultStick = ColorScheme.CopyFrom(data.DefaultStickColors); break; }
        }
        catch (Exception ex) { Plugin.Log.LogWarning("[Appearance] Default stick colors unavailable: " + ex.Message); }
        if (defaultStick != null)
            Plugin.Log.LogInfo($"[Appearance] Default stick colors: {defaultStick.primaryColor} / {defaultStick.secondaryColor} / {defaultStick.tertiaryColor}");
        return defaultStick ??= ColorScheme.CopyFrom(TeamColorsData.Default.stickScheme);
    }
    // A palette whose every piece is unset: the renderer then shows the team kit throughout.
    internal static TeamColorsData Unset()
    {
        var palette = TeamColorsData.CopyFrom(TeamColorsData.Default);
        foreach (var scheme in new[] { palette.jerseyScheme, palette.numberScheme, palette.pantsScheme, palette.glovesScheme,
            palette.stickScheme, palette.helmetScheme, palette.socksScheme, palette.skatesScheme })
            if (scheme != null) scheme.primaryColor = scheme.secondaryColor = scheme.tertiaryColor = new Color(0, 0, 0, 0);
        return palette;
    }
    // Play Now players: the team's kit, explicit piece overrides and personal stick/skates.
    internal static TeamColorsData PlayNowPalette(Func<string, string> own, bool away = false)
    {
        var palette = Unset(); var doc = new CampaignDocument("");
        foreach (string key in PlayerColorModel.PlayerKeys.Concat(PlayerKitColors.Keys)) if (own(key).Length > 0) doc.Set(key, own(key));
        Overlay(palette, doc, null, away);
        return palette;
    }
    private static ColorScheme Scheme(string[] keys, Func<string, string> own, CampaignDocument fallback, ColorScheme standard)
    {
        var scheme = new ColorScheme();
        if (!keys.Any(k => own(k).Length > 0))
        {
            scheme.primaryColor = scheme.secondaryColor = scheme.tertiaryColor = new Color(0, 0, 0, 0);
            return scheme;
        }
        // A partly authored set keeps the team kit's (or the default) value for the rest,
        // because the game swaps the whole scheme.
        Color Channel(int i)
        {
            string value = own(keys[i]);
            if (value.Length == 0) value = fallback?.Get(keys[i]) ?? "";
            if (FieldSchema.TryRgb(value, out byte r, out byte g, out byte b)) return new Color(r / 255f, g / 255f, b / 255f, 1);
            var c = standard == null ? Color.white : i == 0 ? standard.primaryColor : i == 1 ? standard.secondaryColor : standard.tertiaryColor;
            c.a = 1; return c;
        }
        scheme.primaryColor = Channel(0); scheme.secondaryColor = Channel(1); scheme.tertiaryColor = Channel(2);
        return scheme;
    }
    private static readonly Dictionary<IntPtr, Binding> Bindings = new();
    private static long logoContext = -1;
    private static void CheckContext()
    {
        JerseyLogoRuntime.EnsureContext();
        if (logoContext == JerseyLogoRuntime.Context) return;
        logoContext = JerseyLogoRuntime.Context; Bindings.Clear();
    }
    private static Binding Get(SkaterData player)
    {
        CheckContext();
        if (!Bindings.TryGetValue(player.Pointer, out var value)) Bindings[player.Pointer] = value = new Binding();
        return value;
    }
    private static void Merge(Binding binding)
    {
        binding.Effective = new CampaignDocument(binding.Team.ToString());
        foreach (var pair in binding.Player.Fields) binding.Effective.Set(pair.Key, pair.Value);
    }
    internal static CampaignDocument Read(SkaterData player)
    {
        CheckContext();
        return player != null && Bindings.TryGetValue(player.Pointer, out var value) && !value.Export ? value.Effective : null;
    }
    internal static CampaignDocument RecruitSnapshot(SkaterData player)
    {
        CheckContext();
        var snapshot = new CampaignDocument("");
        if (player == null || !Bindings.TryGetValue(player.Pointer, out var value)) return snapshot;
        foreach (var pair in value.Team.Fields) snapshot.Set("Appearance Team " + pair.Key, pair.Value);
        foreach (var pair in value.Player.Fields) snapshot.Set("Appearance Player " + pair.Key, pair.Value);
        foreach (var pair in value.Own.Fields) snapshot.Set("Appearance Own " + pair.Key, pair.Value);
        snapshot.Set("Appearance Team Painted", value.TeamPainted ? "yes" : "no");
        foreach (var pair in JerseyLogoRuntime.Snapshot(player).Fields) snapshot.Set(pair.Key, pair.Value);
        return snapshot;
    }
    internal static void RestoreRecruit(SkaterData player, CampaignDocument snapshot)
    {
        CheckContext();
        var value = new Binding();
        foreach (var pair in snapshot.Fields)
        {
            if (pair.Key.StartsWith("Appearance Team ", StringComparison.OrdinalIgnoreCase) && !pair.Key.Equals("Appearance Team Painted", StringComparison.OrdinalIgnoreCase)) value.Team.Set(pair.Key.Substring(16), pair.Value);
            else if (pair.Key.StartsWith("Appearance Player ", StringComparison.OrdinalIgnoreCase)) value.Player.Set(pair.Key.Substring(18), pair.Value);
            else if (pair.Key.StartsWith("Appearance Own ", StringComparison.OrdinalIgnoreCase)) value.Own.Set(pair.Key.Substring(15), pair.Value);
        }
        value.TeamPainted = snapshot.Get("Appearance Team Painted") == "yes";
        value.OwnColors = PlayerColorModel.HasStick(k => value.Own.Get(k)) || PlayerColorModel.HasSkates(k => value.Own.Get(k));
        Merge(value); Bindings[player.Pointer] = value;
        SyncLogo(player, value); JerseyLogoRuntime.Restore(player, snapshot);
        // Native colorSchemes is saved. Rebuild omitted render metadata without
        // repainting or reapplying any authored stats, names or earned rewards.
        OwnColors(player, k => value.Own.Get(k), value.Effective);
    }
    internal static bool Away(SkaterData player)
        => player != null && Bindings.TryGetValue(player.Pointer, out var value) && value.Away;
    internal static CampaignDocument OwnPalette(SkaterData player, bool away)
        => player != null && Bindings.TryGetValue(player.Pointer, out var value) && !value.Export
            ? PlayerKitColors.Palette(value.Own, away, value.Effective) : null;
    internal static void ForKit(SkaterData player, bool home)
    {
        CheckContext();
        if (player == null) return;
        if (Bindings.TryGetValue(player.Pointer, out var binding) && !binding.Export)
        { binding.Away = !home; Paint(player, binding); return; }
        var exported = PlayNowTranslation.ReadPlayer(player);
        if (exported == null) return;
        binding = Get(player); binding.Export = true; binding.Away = !home;
        var palette = PlayNowPalette(k => exported.Get(k), !home);
        var forward = player.TryCast<ForwardData>(); var goalie = player.TryCast<GoaltenderData>();
        if (forward != null) forward.colorSchemes = palette;
        else if (goalie != null) goalie.colorSchemes = palette;
    }
    internal static void Copy(SkaterData source, SkaterData target)
    {
        CheckContext();
        JerseyLogoRuntime.Copy(source, target);
        if (Bindings.TryGetValue(source.Pointer, out var value))
            Bindings[target.Pointer] = new Binding { Team = value.Team, Player = value.Player, Effective = value.Effective, Own = value.Own,
                TeamPainted = value.TeamPainted, Away = value.Away, Export = value.Export, OwnColors = value.OwnColors };
    }
    private static void Paint(SkaterData player, Binding binding)
    {
        if (AppearanceDecalRuntime.ClearIncompatibleLogo(player)) AppearanceContract.ClearJerseyLogo(binding.Effective);
        var forward = player.TryCast<ForwardData>(); var goalie = player.TryCast<GoaltenderData>();
        var palette = KitPalette(binding, binding.Away);
        if (forward != null) forward.colorSchemes = palette;
        else if (goalie != null) goalie.colorSchemes = palette;
    }
    // A detached preview must resolve the requested kit without changing the
    // player's current kit or its saved colorSchemes. Empty restored metadata
    // has no authored authority; retain that native player's saved palette.
    internal static TeamColorsData PreviewPalette(SkaterData player, bool away)
    {
        CheckContext();
        if (player == null || !Bindings.TryGetValue(player.Pointer, out var binding) || binding.Export
            || binding.Team.Fields.Count + binding.Player.Fields.Count + binding.Own.Fields.Count == 0) return null;
        return KitPalette(binding, away);
    }
    private static TeamColorsData KitPalette(Binding binding, bool away)
    {
        // Never start from the donor's own palette: a piece the player doesn't own must show
        // the team. With the team's palettes painted natively (Team), everything but the
        // player's explicit overrides stays unset and the game supplies the selected kit.
        var palette = Unset();
        if (!binding.TeamPainted) PlayNowTranslation.Paint(palette, binding.Effective, away);
        Overlay(palette, binding.Own, binding.Effective, away);
        return palette;
    }
    internal static void Player(SkaterData player, PlayerConfig config, bool away)
    {
        if (player == null || config == null) return;
        var value = Get(player); value.Away = away;
        value.Export = config.Fields.Get("Export Version").Length > 0;
        // Keep opted-in kit colors separate from team defaults. Stick/skates also use
        // the game's personal color fields, independently of the style toggle.
        value.Player = new CampaignDocument("");
        foreach (var pair in config.Fields.Fields)
            if (pair.Key.StartsWith("Export ", StringComparison.OrdinalIgnoreCase) || pair.Key.StartsWith("Jersey Logo", StringComparison.OrdinalIgnoreCase) || pair.Key == "Override Team Settings") value.Player.Set(pair.Key, pair.Value);
        Func<string, string> own = k => config.Fields.Get(k);
        value.OwnColors = PlayerColorModel.HasStick(own) || PlayerColorModel.HasSkates(own);
        value.Own = new CampaignDocument("");
        foreach (string key in PlayerColorModel.PlayerKeys.Concat(PlayerKitColors.Keys)) if (own(key).Length > 0) value.Own.Set(key, own(key));
        foreach (string key in PlayerKitColors.Keys)
            if (PlayerKitColors.Owns(own, key) && own(key).Length > 0) value.Player.Set(key, own(key));
        if (player.TryCast<GoaltenderData>() != null)
            foreach (string key in new[] { "Goalie Pants Skin", "Goalie Pants Away" })
                if (config.Fields.Get(key).Length > 0) value.Player.Set(key, config.Fields.Get(key));
        if (AppearanceFields.OverridesTeam(config.Fields) && config.Fields.Get("Logo From").Length > 0)
            value.Player.Set("Logo From", config.Fields.Get("Logo From"));
        if (AppearanceFields.OverridesTeam(config.Fields))
        {
            foreach (string key in new[] { "Logo Skin", "Number Skin" })
                if (config.Fields.Get(key).Length > 0) value.Player.Set(key, config.Fields.Get(key));
            // Same rule as the editor: a blank home sock style inherits the team's, while the
            // away style is the player's own (blank follows the player's away jersey).
            if (config.Fields.Get("Socks").Length > 0) value.Player.Set("Socks", config.Fields.Get("Socks"));
            value.Player.Set("Socks Away", config.Fields.Get("Socks Away"));
        }
        Merge(value); SyncLogo(player, value); OwnColors(player, own, value.Effective); Paint(player, value);
    }
    internal static void Team(SkaterData player, TeamConfig config, TeamData team, bool away)
    {
        if (player == null || config == null) return;
        var value = Get(player); value.Away = away; value.Team = TeamPalette(config); Merge(value); SyncLogo(player, value);
        // Team-look players (no authored stick/skates) must not keep a donor's own colors.
        if (!value.OwnColors) OwnColors(player, _ => "", value.Effective);
        if (team != null)
        {
            PlayNowTranslation.Register(team.homeColors, false); PlayNowTranslation.Register(team.awayColors, true);
            PlayNowTranslation.Register(team.homeGoalieColors, false); PlayNowTranslation.Register(team.awayGoalieColors, true);
            var forward = player.TryCast<ForwardData>(); var goalie = player.TryCast<GoaltenderData>();
            // The campaign team file's kit goes into the team's own palettes, where the renderer
            // reads it per side (GetColors(isHome) / Team.GetGoalieColors()).
            PlayNowTranslation.Paint(team.homeColors, value.Team, false); PlayNowTranslation.Paint(team.awayColors, value.Team, true);
            PlayNowTranslation.Paint(team.homeGoalieColors, value.Team, false); PlayNowTranslation.Paint(team.awayGoalieColors, value.Team, true);
            value.TeamPainted = team.homeColors != null && team.awayColors != null;
        }
        Paint(player, value);
    }
    internal static CampaignDocument TeamPalette(TeamConfig config)
    {
        var doc = new CampaignDocument("");
        void Set(string key, int[] value) { if (value != null) doc.Set(key, string.Join(",", value)); }
        void Piece(string name, int[] p, int[] s, int[] t, int[] ap, int[] ass, int[] at)
        {
            Set(name + " Color", p); Set(name + " Secondary Color", s); Set(name + " Tertiary Color", t);
            Set(name + " Away Color", ap); Set(name + " Away Secondary Color", ass); Set(name + " Away Tertiary Color", at);
        }
        // Defaults and the older RGB-in-equipment syntax normalize here too.
        Set("Jersey Color", config.JerseyPrimary); Set("Jersey Secondary Color", config.JerseySecondary); Set("Jersey Accent Color", config.JerseyAccent);
        Set("Jersey Away Color", config.AwayPrimary); Set("Jersey Away Secondary Color", config.AwaySecondary); Set("Jersey Away Accent Color", config.AwayAccent);
        Piece("Gloves", config.TeamGlovesColor, config.TeamGlovesSecondary, config.TeamGlovesTertiary, config.TeamGlovesColorAway, config.TeamGlovesSecondaryAway, config.TeamGlovesTertiaryAway);
        Piece("Helmet", config.TeamHelmetColor, config.TeamHelmetSecondary, config.TeamHelmetTertiary, config.TeamHelmetColorAway, config.TeamHelmetSecondaryAway, config.TeamHelmetTertiaryAway);
        Piece("Pants", config.TeamPantsColor, config.TeamPantsSecondary, config.TeamPantsTertiary, config.TeamPantsColorAway, config.TeamPantsSecondaryAway, config.TeamPantsTertiaryAway);
        Piece("Socks", config.TeamSocksColor, config.TeamSocksSecondary, config.TeamSocksTertiary, config.TeamSocksColorAway, config.TeamSocksSecondaryAway, config.TeamSocksTertiaryAway);
        Piece("Number", config.TeamNumberColor, config.TeamNumberSecondary, config.NumberColorHome, config.TeamNumberColorAway, config.TeamNumberSecondaryAway, config.NumberColorAway);
        Piece("Stick", config.TeamStickColor, null, null, config.TeamStickColorAway, null, null);
        Set("Skates Color", config.TeamSkatesColor); Set("Blade Color", config.TeamBladeColor); Set("Laces Color", config.TeamLacesColor);
        Set("Skates Away Color", config.TeamSkatesColorAway); Set("Blade Away Color", config.TeamBladeColorAway); Set("Laces Away Color", config.TeamLacesColorAway);
        Set("Bicep Color", config.TeamBicepColor); Set("Bicep Away Color", config.TeamBicepColorAway);
        foreach (var pair in config.Colors.Fields) doc.Set(pair.Key, pair.Value);
        // Sock styles are skins, not colors; AppearanceDecalRuntime composes them at render time.
        foreach (string key in new[] { "Socks", "Socks Away" })
            if (config.Fields.Get(key).Length > 0) doc.Set(key, config.Fields.Get(key));
        foreach (var pair in config.Fields.Fields)
            if (pair.Key.StartsWith("Export ", StringComparison.OrdinalIgnoreCase) || pair.Key.StartsWith("Jersey Logo", StringComparison.OrdinalIgnoreCase) || pair.Key == "Logo From") doc.Set(pair.Key, pair.Value);
        return doc;
    }
    private static void SyncLogo(SkaterData player, Binding binding)
        => JerseyLogoRuntime.Bind(player, binding.Team, binding.Player, binding.Own, Plugin.ModFolder);
    internal static Texture Logo(SkaterData player)
    {
        if (!AppearanceDecalRuntime.SupportsLogo(player)) return null;
        string name = Read(player)?.Get("Logo From");
        if (string.IsNullOrEmpty(name)) return null;
        var custom = PatchBossLaunchMatch.LoadCustomLogoSprite(name);
        return custom != null ? custom.texture : PatchBossLaunchMatch.FindTeamByName(name)?.logo?.texture;
    }
}
