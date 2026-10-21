using System.Globalization;
using System.Text.Json;
using CampaignCore;

namespace CampaignCreator;

internal static class PlayerExport
{
    internal const string OwnEquipmentKey = "Export Own Equipment";
    // Export snapshots bake Override Team Settings=yes to retain resolved styles.
    // Their separate marker records the author's choice for future team assignment.
    internal static string EquipmentIntent(CampaignDocument source)
    {
        string saved = source.Get(OwnEquipmentKey);
        return saved.Equals("yes", StringComparison.OrdinalIgnoreCase) ? "yes"
            : saved.Equals("no", StringComparison.OrdinalIgnoreCase) ? "no"
            : AppearanceFields.OverridesTeam(source) ? "yes" : "no";
    }
    internal static Dictionary<string, object> Convert(CampaignDocument d, GameData game, string role, string id, ExportReport? report = null)
    {
        d = ExportPlayerResolver.Resolve(d, role);
        game.ValidateCustomLoadout(d, role == "goalie");
        d = AppearanceContract.Resolve(d, new CampaignDocument(""), new CampaignDocument(""), role);
        d = ExportSelections.Logo(d, game.Root, report);
        string name = d.Get("Name").Trim();
        if (name.Length == 0) throw new ArgumentException("A player name is required for export.");
        if (d.Get("Random Talents").Length > 0 && d.Get("Random Talents") != "0")
            throw new ArgumentException("Choose the player's exact talents before exporting; random talent rolls are campaign settings.");
        int Stat(string field, int fallback = 50)
        {
            string text = d.Get(field);
            if (text.Length == 0) text = field switch { "Skill" => d.Get("Goalie Skill"), "Standing Speed" => d.Get("Stand Speed"), "Poke Check" => d.Get("Pokecheck"), _ => text };
            if (string.IsNullOrWhiteSpace(text)) return fallback;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n < 0 || n > 999)
                throw new ArgumentException($"{field} must be between 0 and 999.");
            return n;
        }
        string? Skin(string field, string fallback)
        {
            string value = d.Get(field).Trim();
            if (fallback.Length == 0 || fallback.Equals("standard", StringComparison.OrdinalIgnoreCase) || fallback.Equals("team colors", StringComparison.OrdinalIgnoreCase))
                fallback = field switch { "Skin Away" => "Body/Customization_colors", "Glove Away" => "Body_Glove/Customization/Customization_colors", "Blocker Away" => "Body_Blocker/Customization/Customization_colors", "Pads Away" => "Body_Pads/Customization/Customization_colors", "Stick Away" => "Body_Stick/Customization/Customization_colors", _ => fallback };
            if (value.Length == 0 || value.Equals("standard", StringComparison.OrdinalIgnoreCase) || value.Equals("team colors", StringComparison.OrdinalIgnoreCase)) value = fallback;
            string? resolved = ExportSelections.Asset(game, role, field, value, d, report);
            return resolved != null && resolved.Equals("none", StringComparison.OrdinalIgnoreCase) ? "" : resolved;
        }
        void SetSkin(Dictionary<string, object> model, string key, string field, string fallback)
        {
            string? value = Skin(field, fallback);
            if (value != null) model[key] = field == "Face" ? HeadAppearance.Base(value) : value;
        }
        var parts = name.Split(' ', 2);
        string hand = role == "goalie" ? "" : d.Get("Left Handed").Trim().ToLowerInvariant();
        string tone = role == "goalie" ? "" : d.Get("Skin Color").Trim().ToLowerInvariant();
        if (!new[] { "", "yes", "true", "no", "false" }.Contains(hand)) throw new ArgumentException("Choose a fixed handedness before exporting to Play Now.");
        if (!new[] { "", "light", "dark" }.Contains(tone)) throw new ArgumentException("Choose a fixed skin tone before exporting to Play Now.");
        var obj = new Dictionary<string, object> {
            ["id"] = id, ["firstName"] = parts[0], ["lastName"] = parts.Length > 1 ? parts[1] : "",
            ["number"] = Stat("Number", 88), ["name"] = role == "goalie" ? "defaultCustomGoalieData(Clone)" : "defaultCustomForwardData(Clone)",
            // The editor shows role-specific controls, but explicit imported or
            // advanced assignments must survive export even outside that role.
            ["abilityId"] = ExportSelections.Id(game.Abilities, d.Get("Ability"), d, "Ability", report),
            ["talentIds"] = ExportTranslation.Ids(game, d.Get("Talents"), false, report, d, "Talents")
                .Concat(ExportTranslation.Ids(game, d.Get("Goalie Talents"), false, report, d, "Goalie Talents")).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            ["isLefty"] = hand is "yes" or "true",
            ["isBlack"] = tone == "dark",
            ["logoSkin"] = d.Get("Logo From").Length > 0 && !d.Get("Logo From").Equals("none", StringComparison.OrdinalIgnoreCase)
                ? AppearanceContract.SupportsJerseyLogo(role, d.Get(role == "goalie" ? "Skin" : "Body"))
                    ? role == "skater" && hand is "yes" or "true" ? "Team_Logo/Custom_L" : "Team_Logo/Custom_R" : ""
                : d.Get("Logo Skin"),
        };
        if (d.Get("Logo From").Length == 0 || d.Get("Logo From").Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            string? logo = ExportSelections.Asset(game, role, "Logo Skin", d.Get("Logo Skin"), d, report);
            if (logo == null) obj.Remove("logoSkin"); else obj["logoSkin"] = logo;
        }
        if (role == "goalie")
        {
            // Native goalie customization saves headSkin independently of the
            // kit's helmetSkin. Face carries that exact identity (or clear),
            // without skater catalog validation or helmet-variant normalization.
            obj["headSkin"] = d.Get("Face");
            foreach (var pair in new Dictionary<string, (string field, string fallback)> {
                ["skin"] = ("Skin", "Body/Customization_colors"), ["awaySkin"] = ("Skin Away", "Body/Customization_colors"),
                ["helmetSkin"] = ("Helmet Skin", "Helmet/Helmet_Customization_colors"),
                ["gloveSkin"] = ("Glove Skin", "Body_Glove/Customization/Customization_colors"), ["awayGloveSkin"] = ("Glove Away", "Body_Glove/Customization/Customization_colors"),
                ["blockerSkin"] = ("Blocker Skin", "Body_Blocker/Customization/Customization_colors"), ["awayBlockerSkin"] = ("Blocker Away", "Body_Blocker/Customization/Customization_colors"),
                ["padsSkin"] = ("Pads Skin", "Body_Pads/Customization/Customization_colors"), ["awayPadsSkin"] = ("Pads Away", "Body_Pads/Customization/Customization_colors"),
                ["stickSkin"] = ("Stick Skin", "Body_Stick/Customization/Customization_colors"), ["awayStickSkin"] = ("Stick Away", "Body_Stick/Customization/Customization_colors") })
                SetSkin(obj, pair.Key, pair.Value.field, pair.Value.fallback);
            foreach (var pair in new Dictionary<string, string> { ["skill"]="Skill", ["catchingSkill"]="Catching", ["gloveSkill"]="Glove", ["blockerSkill"]="Blocker", ["fiveHoleSkill"]="Five Hole", ["standingSpeed"]="Standing Speed", ["butterflySpeed"]="Butterfly Speed", ["recoverySkill"]="Recovery", ["passPower"]="Pass Power", ["shotPower"]="Shot Power", ["pokecheckSkill"]="Poke Check", ["depth"]="Depth", ["controlSkill"]="Control" }) obj[pair.Key] = Stat(pair.Value);
        }
        else
        {
            if (d.Get("Face").Length == 0) throw new ArgumentException("Choose a face before exporting; an empty face can produce a headless player.");
            SetSkin(obj, "headSkin", "Face", "");
            SetSkin(obj, "bodySkin", "Body", "Body/Customization/Customization_colors");
            SetSkin(obj, "bodyAwaySkin", "Body Away", "Body/Customization/Customization_colors");
            SetSkin(obj, "stickSkin", "Stick", "Sticks/Black");
            if (d.Get("Number Skin").Length > 0) SetSkin(obj, "numberSkin", "Number Skin", "");
            else obj["numberSkin"] = $"Numbers/Number_{Stat("Number", 88)}{(hand is "yes" or "true" ? "LH" : "")}";
            obj["checking"] = Stat("Checking"); obj["speed"] = Stat("Speed"); obj["shotAccuracy"] = Stat("Accuracy"); obj["shotPower"] = Stat("Shot Power");
            obj["defaultSkaterType"] = 4; obj["forwardRarity"] = 1;
            var sizes = FieldSchema.Sizes;
            string wantedSize = d.Get("Size"); if (wantedSize.Length == 0) wantedSize = "Medium";
            int size = Array.FindIndex(sizes, x => x.Equals(wantedSize, StringComparison.OrdinalIgnoreCase));
            if (size < 0) throw new ArgumentException("Choose a supported player size."); obj["skaterSize"] = size;
        }
        return obj;
    }
    internal static string Export(CampaignDocument d, GameData game, string root, string role, string? sourceFile = null, ExportReport? report = null)
    {
        string sourceRoot = root;
        d = ExportPlayerResolver.Resolve(d, role);
        d.Set(OwnEquipmentKey, EquipmentIntent(d));
        d = ExportSelections.Logo(AppearanceContract.Resolve(d, new CampaignDocument(""), new CampaignDocument(""), role), root, report);
        root = ExportTranslation.RuntimeRoot(root);
        game = new GameData(sourceRoot); game.LoadCustomRewards(sourceFile);
        if (ExportTranslation.Ids(game, d.Get("Relics"), true, report, d, "Relics").Length > 0
            || ExportTranslation.Ids(game, d.Get("Team Relics"), true, report, d, "Team Relics").Length > 0)
            throw new ArgumentException("Relics act on a team in Play Now. Export this player's team to carry its relics too.");
        string id = Guid.NewGuid().ToString();
        var obj = Convert(d, game, role, id, report);
        var custom = new CustomRewardExportBundle(game); custom.Player(obj);
        var sidecar = TranslationSidecar(d, game, role, obj, report);
        custom.EmbedPlayer(sidecar, obj);
        string save = Path.Combine(GameSavePaths.Root, "TeamDataModels");
        if (!Directory.Exists(save)) throw new DirectoryNotFoundException("Game save folder not found. Launch the game once before exporting.");
        string target = Path.Combine(save, (role == "goalie" ? "CustomGoalie-" : "CustomForward-") + id + ".json");
        string overrides = Path.Combine(ExportTranslation.RuntimeRoot(root), "play_now_overrides"); Directory.CreateDirectory(overrides);
        string sidecarPath = Path.Combine(overrides, id + ".txt");
        string snapshot = Path.Combine(sourceRoot, "library", "Exported Players", id + ".txt");
        return Write(obj, sidecar, target, sidecarPath, snapshot);
    }
    internal static string Write(Dictionary<string, object> obj, CampaignDocument sidecar, string target, string sidecarPath, string snapshot)
    {
        // Make the sidecar available before the game can discover the player.
        sidecar.Save(sidecarPath);
        string temp = target + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(snapshot)!);
            sidecar.Save(snapshot);
            File.WriteAllText(temp, JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, target);
        }
        catch { File.Delete(sidecarPath); if (File.Exists(snapshot)) File.Delete(snapshot); throw; }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        return target;
    }
    internal static CampaignDocument TranslationSidecar(CampaignDocument d, GameData game, string role, Dictionary<string, object> model, ExportReport? report = null)
    {
        var original = d;
        d = AppearanceContract.Resolve(d, new CampaignDocument(""), new CampaignDocument(""), role);
        var result = AppearanceSidecar(d, game, role, report);
        result.Set("Export Version", "3");
        result.Set("Export Model", JsonSerializer.Serialize(model));
        result.Set("Export Role", role);
        result.Set(OwnEquipmentKey, EquipmentIntent(original));
        foreach (var pair in d.Fields.Where(p => !p.Key.StartsWith("Export "))) if (!result.Fields.ContainsKey(pair.Key)) result.Set(pair.Key, pair.Value);
        LogoExport.Copy(ExportSelections.Logo(original, game.Root, report), result);
        foreach (bool away in new[] { false, true })
            if (AppearanceContract.SupportsJerseyLogo(role, d.Get(role == "goalie" ? away ? "Skin Away" : "Skin" : away ? "Body Away" : "Body")))
                LogoExport.Copy(d, result, away, away);
            else JerseyLogoOptions.Clear(result, away);
        result.Set("Export Talent IDs", string.Join(",", (string[])model["talentIds"]));
        result.Set("Export Ability ID", (string)model["abilityId"]);
        if (role == "goalie" && d.Get("Pass Read").Length > 0)
        {
            if (!double.TryParse(d.Get("Pass Read"), NumberStyles.Float, CultureInfo.InvariantCulture, out double n) || n < 0 || n > 999) throw new ArgumentException("Pass Read must be a number between 0 and 999 (goalies normally use 0.0-1.0).");
            result.Set("Pass Read", d.Get("Pass Read"));
        }
        return ExportSelections.Sidecar(result, game, role, report);
    }
    internal static CampaignDocument AppearanceSidecar(CampaignDocument d, GameData game, string role, ExportReport? report = null)
    {
        var sidecar = new CampaignDocument("# Appearance overrides applied by the Custom Campaign Framework\n");
        string[] extras = ["Size Offset", "Glasses", "Socks", "Socks Away", "Helmet", "Helmet Away", "Bicep", "Bicep Away", "Gloves", "Gloves Away", "Pants", "Pants Away", "Skates", "Skates Away"];
        foreach (var (key, value) in d.Fields)
        {
            if (value.Length == 0) continue;
            if (FieldSchema.Kind(key, value) == FieldKind.Color)
            {
                var rgb = value.Split(',');
                if (rgb.Length != 3 || rgb.Any(v => !byte.TryParse(v.Trim(), out _))) throw new ArgumentException($"{key} must contain three RGB values from 0 to 255.");
                sidecar.Set(key, value);
            }
            else if (role == "skater" && extras.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                if (value.Equals("standard", StringComparison.OrdinalIgnoreCase)) continue;
                string resolved = value;
                if (key == "Size Offset" && (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var offset) || !float.IsFinite(offset) || offset < 0))
                    throw new ArgumentException("Size Offset must be a fixed nonnegative number.");
                if (key != "Size Offset")
                {
                    var asset = ExportSelections.Asset(game, role, key, value, d, report);
                    if (asset == null) continue;
                    resolved = asset;
                }
                sidecar.Set(key, resolved);
            }
        }
        return sidecar;
    }
}
