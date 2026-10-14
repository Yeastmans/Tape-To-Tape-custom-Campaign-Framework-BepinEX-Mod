using System.Globalization;
using System.Text.Json;
using CampaignCore;

namespace CampaignCreator;

internal static class PlayerExport
{
    internal static Dictionary<string, object> Convert(CampaignDocument d, GameData game, string role, string id)
    {
        d = ExportPlayerResolver.Resolve(d, role);
        d = AppearanceContract.Resolve(d, new CampaignDocument(""), new CampaignDocument(""), role);
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
        string Skin(string field, string fallback)
        {
            string value = d.Get(field).Trim();
            if (fallback.Length == 0 || fallback.Equals("standard", StringComparison.OrdinalIgnoreCase) || fallback.Equals("team colors", StringComparison.OrdinalIgnoreCase))
                fallback = field switch { "Skin Away" => "Body/Customization_colors", "Glove Away" => "Body_Glove/Customization/Customization_colors", "Blocker Away" => "Body_Blocker/Customization/Customization_colors", "Pads Away" => "Body_Pads/Customization/Customization_colors", "Stick Away" => "Body_Stick/Customization/Customization_colors", _ => fallback };
            if (value.Length == 0 || value.Equals("standard", StringComparison.OrdinalIgnoreCase) || value.Equals("team colors", StringComparison.OrdinalIgnoreCase)) value = fallback;
            if (role == "goalie" && GoalieEquipmentCatalog.Values(field).Contains(value, StringComparer.OrdinalIgnoreCase)) return value;
            return game.Assets.Resolve(role, field, value);
        }
        string ResolveId(Dictionary<string, string> table, string name, string field)
        {
            if (name.Length == 0 || name.Equals("none", StringComparison.OrdinalIgnoreCase)) return "";
            if (table.TryGetValue(name, out var found)) return found;
            if (table.Values.Contains(name, StringComparer.OrdinalIgnoreCase)) return name;
            throw new ArgumentException($"{field} '{name}' has no exported game ID. Refresh game data; nothing was exported.");
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
            ["abilityId"] = ResolveId(game.Abilities, d.Get("Ability"), "Ability"),
            ["talentIds"] = (d.Get("Talents") + "," + d.Get("Goalie Talents")).Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => ResolveId(game.Talents, t.Trim(), "Talent")).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            ["isLefty"] = hand is "yes" or "true",
            ["isBlack"] = tone == "dark",
            ["logoSkin"] = d.Get("Logo From").Length > 0 && !d.Get("Logo From").Equals("none", StringComparison.OrdinalIgnoreCase)
                ? role == "skater" && hand is "yes" or "true" ? "Team_Logo/Custom_L" : "Team_Logo/Custom_R" : d.Get("Logo Skin"),
        };
        if (role == "goalie")
        {
            // Goalies use helmetSkin, not the skater headSkin field.
            obj["headSkin"] = "";
            foreach (var pair in new Dictionary<string, (string field, string fallback)> {
                ["skin"] = ("Skin", "Body/Customization_colors"), ["awaySkin"] = ("Skin Away", "Body/Customization_colors"),
                ["helmetSkin"] = ("Helmet Skin", "Helmet/Helmet_Customization_colors"),
                ["gloveSkin"] = ("Glove Skin", "Body_Glove/Customization/Customization_colors"), ["awayGloveSkin"] = ("Glove Away", "Body_Glove/Customization/Customization_colors"),
                ["blockerSkin"] = ("Blocker Skin", "Body_Blocker/Customization/Customization_colors"), ["awayBlockerSkin"] = ("Blocker Away", "Body_Blocker/Customization/Customization_colors"),
                ["padsSkin"] = ("Pads Skin", "Body_Pads/Customization/Customization_colors"), ["awayPadsSkin"] = ("Pads Away", "Body_Pads/Customization/Customization_colors"),
                ["stickSkin"] = ("Stick Skin", "Body_Stick/Customization/Customization_colors"), ["awayStickSkin"] = ("Stick Away", "Body_Stick/Customization/Customization_colors") })
                obj[pair.Key] = Skin(pair.Value.field, pair.Value.fallback);
            foreach (var pair in new Dictionary<string, string> { ["skill"]="Skill", ["catchingSkill"]="Catching", ["gloveSkill"]="Glove", ["blockerSkill"]="Blocker", ["fiveHoleSkill"]="Five Hole", ["standingSpeed"]="Standing Speed", ["butterflySpeed"]="Butterfly Speed", ["recoverySkill"]="Recovery", ["passPower"]="Pass Power", ["shotPower"]="Shot Power", ["pokecheckSkill"]="Poke Check", ["depth"]="Depth", ["controlSkill"]="Control" }) obj[pair.Key] = Stat(pair.Value);
        }
        else
        {
            if (d.Get("Face").Length == 0) throw new ArgumentException("Choose a face before exporting; an empty face can produce a headless player.");
            obj["headSkin"] = HeadAppearance.Base(Skin("Face", ""));
            obj["bodySkin"] = Skin("Body", "Body/Customization/Customization_colors");
            obj["bodyAwaySkin"] = Skin("Body Away", "Body/Customization/Customization_colors");
            obj["stickSkin"] = Skin("Stick", "Sticks/Black");
            obj["numberSkin"] = d.Get("Number Skin").Length > 0 ? d.Get("Number Skin") : $"Numbers/Number_{Stat("Number", 88)}{(hand is "yes" or "true" ? "LH" : "")}";
            obj["checking"] = Stat("Checking"); obj["speed"] = Stat("Speed"); obj["shotAccuracy"] = Stat("Accuracy"); obj["shotPower"] = Stat("Shot Power");
            obj["defaultSkaterType"] = 4; obj["forwardRarity"] = 1;
            var sizes = FieldSchema.Sizes;
            string wantedSize = d.Get("Size"); if (wantedSize.Length == 0) wantedSize = "Medium";
            int size = Array.FindIndex(sizes, x => x.Equals(wantedSize, StringComparison.OrdinalIgnoreCase));
            if (size < 0) throw new ArgumentException("Choose a supported player size."); obj["skaterSize"] = size;
        }
        return obj;
    }
    internal static string Export(CampaignDocument d, GameData game, string root, string role)
    {
        string sourceRoot = root;
        d = ExportPlayerResolver.Resolve(d, role);
        d = LogoExport.Snapshot(AppearanceContract.Resolve(d, new CampaignDocument(""), new CampaignDocument(""), role), root);
        root = ExportTranslation.RuntimeRoot(root);
        game = new GameData(root);
        if (d.Get("Relics").Length > 0 || d.Get("Team Relics").Length > 0)
            throw new ArgumentException("Relics act on a team in Play Now. Export this player's team to carry its relics too.");
        string id = Guid.NewGuid().ToString();
        var obj = Convert(d, game, role, id);
        var sidecar = TranslationSidecar(d, game, role, obj);
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
    internal static CampaignDocument TranslationSidecar(CampaignDocument d, GameData game, string role, Dictionary<string, object> model)
    {
        var original = d;
        d = AppearanceContract.Resolve(d, new CampaignDocument(""), new CampaignDocument(""), role);
        var result = AppearanceSidecar(d, game, role);
        result.Set("Export Version", "3");
        result.Set("Export Model", JsonSerializer.Serialize(model));
        result.Set("Export Role", role);
        foreach (var pair in d.Fields.Where(p => !p.Key.StartsWith("Export "))) if (!result.Fields.ContainsKey(pair.Key)) result.Set(pair.Key, pair.Value);
        if (AppearanceContract.SupportsJerseyLogo(d, role)) LogoExport.Copy(LogoExport.Snapshot(original, game.Root), result);
        else AppearanceContract.ClearJerseyLogo(result);
        result.Set("Export Talent IDs", string.Join(",", (string[])model["talentIds"]));
        result.Set("Export Ability ID", (string)model["abilityId"]);
        if (role == "goalie" && d.Get("Pass Read").Length > 0)
        {
            if (!double.TryParse(d.Get("Pass Read"), NumberStyles.Float, CultureInfo.InvariantCulture, out double n) || n < 0 || n > 999) throw new ArgumentException("Pass Read must be a number between 0 and 999 (goalies normally use 0.0-1.0).");
            result.Set("Pass Read", d.Get("Pass Read"));
        }
        return result;
    }
    internal static CampaignDocument AppearanceSidecar(CampaignDocument d, GameData game, string role)
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
                if (key != "Size Offset" && !(key.StartsWith("Helmet") && value.Equals("none", StringComparison.OrdinalIgnoreCase)))
                    resolved = game.Assets.Resolve(role, key, value);
                sidecar.Set(key, resolved);
            }
        }
        return sidecar;
    }
}
