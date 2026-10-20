using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using CampaignCore;
using Data;
using HarmonyLib;
using UnityEngine;

namespace EndlessMode;

internal static class ExportAppearanceRuntime
{
    private static readonly Dictionary<string, Sprite> Logos = new(StringComparer.Ordinal);
    private static readonly HashSet<string> Common = new(new[] { "id", "firstName", "lastName", "number", "headSkin", "logoSkin" });
    private static readonly HashSet<string> Forward = new(new[] { "checking", "speed", "bodySkin", "forwardRarity", "isBlack", "isLefty", "numberSkin", "shotAccuracy", "shotPower", "skaterSize", "stickSkin", "bodyAwaySkin", "defaultSkaterType", "skaterCategory" });
    private static readonly HashSet<string> Goalie = new(new[] { "skin", "awaySkin", "helmetSkin", "blockerSkin", "awayBlockerSkin", "stickSkin", "awayStickSkin", "gloveSkin", "awayGloveSkin", "padsSkin", "awayPadsSkin", "skill", "catchingSkill", "gloveSkill", "blockerSkill", "fiveHoleSkill", "standingSpeed", "butterflySpeed", "recoverySkill", "passPower", "shotPower", "pokecheckSkill", "depth", "controlSkill" });

    internal static Sprite Logo(CampaignDocument doc)
    {
        if (doc.Get("Export Logo Mode") != "embedded") return null;
        string id = doc.Get("Export Logo ID");
        if (Logos.TryGetValue(id, out var cached)) return cached;
        byte[] bytes = Convert.FromBase64String(doc.Get("Export Logo PNG"));
        string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (hash != doc.Get("Export Logo SHA256") || id != "custom_t2t_" + hash + ".png") throw new InvalidDataException("Export logo checksum mismatch.");
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!ImageConversion.LoadImage(texture, bytes)) { UnityEngine.Object.Destroy(texture); throw new InvalidDataException("Export logo PNG cannot be decoded."); }
        texture.name = id; texture.hideFlags = HideFlags.HideAndDontSave;
        var sprite = Sprite.Create(texture, new Rect(0,0,texture.width,texture.height), new Vector2(.5f,.5f),100);
        sprite.name = id; sprite.hideFlags = HideFlags.HideAndDontSave;
        UnityEngine.Object.DontDestroyOnLoad(texture); UnityEngine.Object.DontDestroyOnLoad(sprite);
        return Logos[id] = sprite;
    }

    private static readonly Dictionary<string, string> Extras = new() {
        ["Helmet"]="helmetSkin", ["Helmet Away"]="helmetAwaySkin", ["Bicep"]="bicepSkin", ["Bicep Away"]="bicepAwaySkin",
        ["Gloves"]="gloveSkin", ["Gloves Away"]="gloveAwaySkin", ["Pants"]="pantsSkin", ["Pants Away"]="pantsAwaySkin",
        ["Skates"]="skateSkin", ["Skates Away"]="skateAwaySkin", ["Glasses"]="glassesSkin" };

    internal static void Apply(SkaterData player, CampaignDocument doc, bool away = false)
    {
        if (doc.Get("Export Version") != "3") return;
        JerseyLogoRuntime.Bind(player, new CampaignDocument(""), doc, doc, Plugin.ModFolder);
        bool goalie = player.TryCast<GoaltenderData>() != null;
        object target = goalie ? player.TryCast<GoaltenderData>() : player.TryCast<ForwardData>();
        Type type = goalie ? typeof(GoaltenderData) : typeof(ForwardData);
        using var model = JsonDocument.Parse(doc.Get("Export Model"));
        foreach (var member in model.RootElement.EnumerateObject())
        {
            if (!Common.Contains(member.Name) && !(goalie ? Goalie : Forward).Contains(member.Name)) continue;
            var property = type.GetProperty(member.Name);
            if (property == null || !property.CanWrite) throw new MissingMemberException(type.Name, member.Name);
            object value = member.Value.ValueKind switch {
                JsonValueKind.String => member.Value.GetString(), JsonValueKind.True => true, JsonValueKind.False => false,
                JsonValueKind.Number => member.Value.GetInt32(), _ => throw new InvalidDataException("Unsupported player field " + member.Name) };
            if (property.PropertyType.IsEnum) value = Enum.ToObject(property.PropertyType, value);
            property.SetValue(target, value);
        }
        var pc = new PlayerConfig();
        foreach (var pair in doc.Fields) Plugin.ApplyPlayerField(pc, pair.Key.ToLowerInvariant(), pair.Value);
        if (pc.NumberFormat >= 0) player.numberFormat = (NumberFormat)pc.NumberFormat;
        // Base-game model: a Play Now player owns only stick/skate colors; older exports may
        // still hold a whole kit, which the game would never apply per player.
        var own = new CampaignDocument(doc.ToString()); PlayerColorModel.Migrate(own);
        CampaignAppearanceRuntime.OwnColors(player, k => own.Get(k), null);
        if (!goalie)
        {
            var forward = player.TryCast<ForwardData>();
            // Unset pieces show the Play Now team's kit; only own stick/skates are set.
            forward.colorSchemes = CampaignAppearanceRuntime.PlayNowPalette(k => own.Get(k), away);
            foreach (var pair in Extras) typeof(ForwardData).GetProperty(pair.Value).SetValue(forward, doc.Get(pair.Key));
            forward.sizeOffsetPercentage = float.TryParse(doc.Get("Size Offset"), NumberStyles.Float, CultureInfo.InvariantCulture, out float offset) ? offset : 1f;
            PatchPlayerTeamInit.HandleNoHelmetSentinel(forward);
        }
        else
        {
            var g = player.TryCast<GoaltenderData>();
            g.colorSchemes = CampaignAppearanceRuntime.PlayNowPalette(k => own.Get(k), away);
            if (float.TryParse(doc.Get("Pass Read"), NumberStyles.Float, CultureInfo.InvariantCulture, out float passRead) && float.IsFinite(passRead)) g.passReadSkill = passRead;
        }
        if (AppearanceDecalRuntime.ClearIncompatibleLogo(player)) return;
        var logo = Logo(doc);
        if (logo != null)
        {
            if (goalie) player.TryCast<GoaltenderData>().logoSkin = "Team_Logo/Custom_R";
            else { var f = player.TryCast<ForwardData>(); f.logoSkin = f.isLefty ? "Team_Logo/Custom_L" : "Team_Logo/Custom_R"; }
        }
    }

    internal static void ApplyTeamLogo(TeamData team, CampaignDocument doc)
    {
        if (doc.Get("Export Version") != "3") return;
        var logo = Logo(doc);
        bool hasLogo = logo != null;
        // World matches call the flags-based async renderer directly. A null
        // texture skips SetLogo there, exposing the entire Custom_R/L atlas.
        // Carry an explicit empty texture for an exported "no logo" team so
        // both native match rendering and menu reuse clear the previous crest.
        if (!hasLogo && doc.Get("Export Logo Mode") == "none") logo = EmptyTeamLogo();
        team.logo = logo; team.alternateBigLogo = logo?.texture; team.hasBigLogo = hasLogo;
    }

    private static Sprite _emptyTeamLogo;
    private static Sprite EmptyTeamLogo()
    {
        if (_emptyTeamLogo != null) return _emptyTeamLogo;
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) {
            name = "CampaignStudioNoLogo", hideFlags = HideFlags.HideAndDontSave };
        texture.SetPixels32(new Color32[4]); texture.Apply(false, false);
        var sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), new Vector2(.5f, .5f), 100);
        sprite.name = texture.name; sprite.hideFlags = HideFlags.HideAndDontSave;
        UnityEngine.Object.DontDestroyOnLoad(texture); UnityEngine.Object.DontDestroyOnLoad(sprite);
        return _emptyTeamLogo = sprite;
    }

    // The native renderer receives a team's texture even for individually exported
    // players. Override only that argument, retaining each player's embedded crest.
    public static void SkinPrefix(SkaterData skaterData, ref Texture logo)
    {
        if (skaterData == null || JerseyLogoNativeHooks.Enabled) return;
        if (AppearanceDecalRuntime.ClearIncompatibleLogo(skaterData)) { logo = null; return; }
        try
        {
            var campaignLogo = CampaignAppearanceRuntime.Logo(skaterData);
            if (campaignLogo != null) logo = campaignLogo;
            if (CampaignAppearanceRuntime.Read(skaterData) != null) return;
            var doc = PlayNowTranslation.ReadPlayer(skaterData);
            if (doc == null || doc.Get("Export Version") != "3") return;
            if (doc.Get("Export Logo Mode") == "embedded") logo = Logo(doc).texture;
            // No own crest: keep the default the game passed (team crest, or the customization
            // screen's default logo). Never null it: the game then skips SetLogo, and the
            // Custom_R/L patch draws the whole shared logo sheet ("a bunch of logos").
            else if (doc.Get("Logo Skin").Length == 0 && logo == null) logo = PlaceholderLogo();
        }
        catch (Exception ex) { Plugin.Log.LogError("[ExportParity] Logo render failed: " + ex); }
    }

    private static Texture2D _defaultLogo, _generatedLogo;
    // The game's own default crest (SkaterCustomizationMenu._defaultTeamLogo); a plain
    // generated badge only when that menu isn't loaded.
    internal static Texture PlaceholderLogo()
    {
        if (_defaultLogo != null) return _defaultLogo;
        foreach (var menu in Resources.FindObjectsOfTypeAll<Tape2Tape.Customization.UI.SkaterCustomizationMenu>())
            if (menu != null && menu._defaultTeamLogo != null) return _defaultLogo = menu._defaultTeamLogo;
        if (_generatedLogo != null) return _generatedLogo;
        const int size = 128;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "CampaignStudioPlaceholderLogo", hideFlags = HideFlags.HideAndDontSave };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - 63.5f) * (x - 63.5f) + (y - 63.5f) * (y - 63.5f));
                pixels[y * size + x] = d > 60 ? new Color32(0, 0, 0, 0) : d > 50 ? new Color32(40, 40, 40, 255) : new Color32(225, 225, 225, 255);
            }
        texture.SetPixels32(pixels); texture.Apply(false, false);
        UnityEngine.Object.DontDestroyOnLoad(texture);
        Plugin.Log.LogInfo("[ExportParity] Default team logo not loaded; using a generated placeholder logo");
        return _generatedLogo = texture;
    }
    public static bool LogoPrefix(string logoId, ref Sprite __result)
    {
        if (logoId != null && Logos.TryGetValue(logoId, out var logo)) { __result = logo; return false; }
        return true;
    }

    internal static void Receipt(SkaterData player, CampaignDocument doc)
    {
        if (doc.Get("Export Version") != "3") return;
        bool goalie = player.TryCast<GoaltenderData>() != null;
        object target = goalie ? player.TryCast<GoaltenderData>() : player.TryCast<ForwardData>();
        Type type = goalie ? typeof(GoaltenderData) : typeof(ForwardData);
        var mismatches = new List<string>(); var actual = new Dictionary<string, object>();
        // Audit the supported native model, including the same documented migration
        // used on load. Old exports can still contain per-player team-kit colors and
        // injury skate art; neither is a supported authored player override.
        var own = new CampaignDocument(doc.ToString()); PlayerColorModel.Migrate(own);
        var expectedPalette = CampaignAppearanceRuntime.PlayNowPalette(k => own.Get(k));
        using var model = JsonDocument.Parse(doc.Get("Export Model"));
        foreach (var member in model.RootElement.EnumerateObject())
        {
            if (!Common.Contains(member.Name) && !(goalie ? Goalie : Forward).Contains(member.Name)) continue;
            object value = type.GetProperty(member.Name)?.GetValue(target);
            if (value != null && value.GetType().IsEnum) value = Convert.ToInt32(value, CultureInfo.InvariantCulture);
            actual[member.Name] = value;
            bool same = JsonSerializer.Serialize(value) == member.Value.GetRawText();
            if (member.Name == "logoSkin" && !AppearanceDecalRuntime.SupportsLogo(player))
                same = string.IsNullOrEmpty(value as string);
            if (member.Name == "headSkin" && !goalie)
                same = HeadAppearance.Base(value as string) == HeadAppearance.Base(member.Value.GetString());
            if (!same) mismatches.Add(member.Name);
        }
        if (!goalie)
        {
            var forward = player.TryCast<ForwardData>();
            actual["sizeOffsetPercentage"] = forward.sizeOffsetPercentage;
            float expectedOffset = float.TryParse(doc.Get("Size Offset"), NumberStyles.Float,
                CultureInfo.InvariantCulture, out float offset) ? offset : 1f;
            if (Math.Abs(forward.sizeOffsetPercentage - expectedOffset) > .00001f)
                mismatches.Add("sizeOffsetPercentage");
            foreach (var pair in Extras)
            {
                string value = (string)typeof(ForwardData).GetProperty(pair.Value).GetValue(forward) ?? "";
                string expected = doc.Get(pair.Key);
                if (pair.Key.StartsWith("Helmet") && (HeadAppearance.IsNone(expected) || !HeadAppearance.CanWearHelmet(forward.headSkin)))
                    expected = "__NO_HELMET__";
                if (pair.Key.StartsWith("Skates")) expected = AppearanceContract.StandardSkates;
                if (pair.Key == "Glasses" && string.Equals(expected, "none", StringComparison.OrdinalIgnoreCase)) expected = "";
                actual[pair.Value] = value;
                if (value != expected) mismatches.Add(pair.Value);
            }
        }
        var palette = goalie ? player.TryCast<GoaltenderData>().colorSchemes : player.TryCast<ForwardData>().colorSchemes;
        var nativeColors = new Dictionary<string, float[]>();
        foreach (string scheme in AppearanceContract.Schemes)
        {
            string schemeProperty = scheme + "Scheme";
            if (palette == null) { mismatches.Add(schemeProperty); continue; }
            var colors = (ColorScheme)typeof(TeamColorsData).GetProperty(scheme + "Scheme").GetValue(palette);
            var expectedColors = (ColorScheme)typeof(TeamColorsData).GetProperty(schemeProperty).GetValue(expectedPalette);
            if (colors == null) { mismatches.Add(schemeProperty); continue; }
            for (int channel = 0; channel < 3; channel++)
            {
                var color = channel == 0 ? colors.primaryColor : channel == 1 ? colors.secondaryColor : colors.tertiaryColor;
                var expected = channel == 0 ? expectedColors.primaryColor : channel == 1 ? expectedColors.secondaryColor : expectedColors.tertiaryColor;
                string key = AppearanceContract.ColorKey(scheme, channel, false);
                nativeColors[key] = new[] { color.r, color.g, color.b, color.a };
                if (Math.Abs(color.r - expected.r) > .00001f || Math.Abs(color.g - expected.g) > .00001f ||
                    Math.Abs(color.b - expected.b) > .00001f || Math.Abs(color.a - expected.a) > .00001f) mismatches.Add(key);
            }
        }
        actual["colorSchemes"] = nativeColors;
        string[] talents = player.powerups == null ? Array.Empty<string>() : Enumerable.Range(0, player.powerups.Count).Select(i => player.powerups[i]?.id ?? "").ToArray();
        actual["talentIds"] = talents; actual["abilityId"] = player.ability?.id ?? "";
        if (!talents.SequenceEqual(doc.Get("Export Talent IDs").Split(',', StringSplitOptions.RemoveEmptyEntries))) mismatches.Add("talentIds");
        if ((player.ability?.id ?? "") != doc.Get("Export Ability ID")) mismatches.Add("abilityId");
        string folder = Path.Combine(Plugin.ModContentRoot, "play_now_overrides", "receipts"); Directory.CreateDirectory(folder);
        string id = player.id;
        if (string.IsNullOrEmpty(id) || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return;
        File.WriteAllText(Path.Combine(folder, id + ".json"), JsonSerializer.Serialize(new { id, status = mismatches.Count == 0 ? "matched" : "mismatch", mismatches, actual, logoSha256 = doc.Get("Export Logo SHA256") }, new JsonSerializerOptions { WriteIndented = true }));
        if (mismatches.Count > 0) Plugin.Log.LogError($"[ExportParity] {id}: {string.Join(", ", mismatches)}");
        else Plugin.Log.LogInfo($"[ExportParity] {id}: native player fields and talent/ability IDs matched");
    }

    internal static void Install(Harmony h)
    {
        ExportTeamAppearanceRuntime.Install(h);
        JerseyLogoNativeHooks.Install();
        foreach (var method in typeof(Skaters.T2TSkeletonExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => (m.Name == "ApplySkin" || m.Name == "ApplySkinAsync") && m.GetParameters().Any(p => p.Name == "skaterData") && m.GetParameters().Any(p => p.Name == "logo")))
            h.Patch(method, prefix: new HarmonyMethod(typeof(ExportAppearanceRuntime), nameof(SkinPrefix)));
        h.Patch(AccessTools.Method(typeof(Tape2Tape.Customization.TeamAssetsRepositoryScriptableObject), "GetLogo"), prefix: new HarmonyMethod(typeof(ExportAppearanceRuntime), nameof(LogoPrefix)));
    }
}
