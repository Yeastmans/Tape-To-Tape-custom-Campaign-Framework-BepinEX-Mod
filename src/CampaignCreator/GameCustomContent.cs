using System.Globalization;
using System.Text.Json;
using CampaignCore;

namespace CampaignCreator;

// "In-game custom" is exactly the custom teams and players saved in the game's own
// folder (TeamDataModels), read each time the list is built. Studio exports keep
// their authored fields while their live sidecar supplies the current lineup.
// Game-editor content is converted into a cached campaign-format file. The game's
// files are never changed.
internal static class GameCustomContent
{
    internal sealed record Item(string File, string Detail);

    internal static string Folder => Path.Combine(GameSavePaths.Root, "TeamDataModels");
    internal static string Cache(string root) => Path.Combine(root, ".studio-cache", "in-game-custom");

    private static readonly string[] Slots = ["leftWingerId", "rightWingerId", "centerId", "leftDefensemenId", "rightDefensemenId", "goalieId"];
    private static readonly string[] Positions = ["Left Wing", "Right Wing", "Center", "Left Defense", "Right Defense", "Goalie"];

    internal static List<Item> Load(string root)
        => Load(root, Folder, Path.Combine(UserContentPaths.Root, "play_now_overrides"));

    // The folder parameter also keeps fixture checks away from the live game save.
    internal static List<Item> Load(string root, string saveFolder)
        => Load(root, saveFolder, Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(saveFolder)!)!, "runtime", "play_now_overrides"));

    private static List<Item> Load(string root, string saveFolder, string overrideFolder)
    {
        var items = new List<Item>();
        if (!Directory.Exists(saveFolder)) return items;
        string cache = Cache(root);
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var models = new Dictionary<string, (string File, JsonElement Json)>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.EnumerateFiles(saveFolder, "Custom*.json"))
        {
            try
            {
                using var json = JsonDocument.Parse(File.ReadAllText(file));
                var model = json.RootElement.Clone();
                string id = Text(model, "id", Text(model, "Id"));
                if (id.Length > 0) models[id] = (file, model);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { }
        }
        string exportedPlayers = Path.Combine(root, "library", "Exported Players");
        string exportedTeams = Path.Combine(root, "library", "Exported Teams");
        string? TeamPlayerSnapshot(string id) => Directory.Exists(exportedTeams)
            ? Directory.EnumerateFiles(exportedTeams, id + ".txt", SearchOption.AllDirectories).FirstOrDefault() : null;
        foreach (var (id, (file, model)) in models.OrderBy(m => File.GetLastWriteTime(m.Value.File)))
        {
            string kind = Path.GetFileName(file).Split('-')[0];
            string when = File.GetLastWriteTime(file).ToString("MMM d h:mm tt", CultureInfo.InvariantCulture);
            if (kind == "CustomTeam")
            {
                string snapshot = Path.Combine(exportedTeams, id, "team.txt");
                if (File.Exists(snapshot))
                {
                    string liveFile = Path.Combine(overrideFolder, id + ".team.txt");
                    if (File.Exists(liveFile))
                    {
                        var live = new CampaignDocument(File.ReadAllText(liveFile));
                        if (PlayNowRosterCodec.TryRead(live, out var roster, out _))
                        {
                            string cacheTeam = Path.Combine(cache, "teams", id);
                            var team = new CampaignDocument(File.ReadAllText(snapshot));
                            PlayNowRosterCodec.Write(team, roster);
                            Write(Path.Combine(cacheTeam, "team.txt"), team, written);
                            string[] positions = ["Left Wing", "Center", "Right Wing", "Left Defense", "Right Defense"];
                            foreach (var (pid, position) in roster.Line1.Zip(positions).Concat(roster.Line2.Zip(positions.Select(p => "Line 2 " + p))).Append((roster.GoalieId, "Goalie")))
                            {
                                string snapshotPlayer = Path.Combine(exportedTeams, id, "players", pid + ".txt");
                                string runtimePlayer = Path.Combine(Path.GetDirectoryName(liveFile)!, pid + ".txt");
                                CampaignDocument? playerDoc = File.Exists(runtimePlayer) ? new CampaignDocument(File.ReadAllText(runtimePlayer))
                                    : File.Exists(snapshotPlayer) ? new CampaignDocument(File.ReadAllText(snapshotPlayer))
                                    : models.TryGetValue(pid, out var nativePlayer) ? Player(nativePlayer.Json, position == "Goalie") : null;
                                if (playerDoc == null) continue;
                                playerDoc.Set("Position", position);
                                Write(Path.Combine(cacheTeam, "players", position + " - " + pid + ".txt"), playerDoc, written);
                            }
                            items.Add(new(Path.Combine(cacheTeam, "team.txt"), "Studio export · " + when));
                            continue;
                        }
                    }
                    items.Add(new(snapshot, "Studio export · " + when));
                    continue;
                }
                string folder = Path.Combine(cache, "teams", id);
                Write(Path.Combine(folder, "team.txt"), Team(model), written);
                for (int i = 0; i < Slots.Length; i++)
                {
                    string pid = Text(model, Slots[i]);
                    if (pid.Length == 0 || !models.TryGetValue(pid, out var player)) continue;
                    var doc = Player(player.Json, i == 5);
                    doc.Set("Position", Positions[i]);
                    // Team members wear the team's look, as in the game.
                    doc.Set("Override Team Settings", "no");
                    Write(Path.Combine(folder, "players", Positions[i] + " - " + Safe(doc.Get("Name")) + ".txt"), doc, written);
                }
                items.Add(new(Path.Combine(folder, "team.txt"), "Game editor · " + when));
            }
            else if (kind is "CustomForward" or "CustomGoalie")
            {
                string? snapshot = File.Exists(Path.Combine(exportedPlayers, id + ".txt")) ? Path.Combine(exportedPlayers, id + ".txt") : TeamPlayerSnapshot(id);
                if (snapshot != null) { items.Add(new(snapshot, "Studio export · " + when)); continue; }
                string target = Path.Combine(cache, "players", id + ".txt");
                Write(target, Player(model, kind == "CustomGoalie"), written);
                items.Add(new(target, "Game editor · " + when));
            }
        }
        // Drop cached conversions of anything since deleted in the game.
        if (Directory.Exists(cache))
            foreach (string stale in Directory.EnumerateFiles(cache, "*", SearchOption.AllDirectories).Where(f => !written.Contains(f)).ToList())
                File.Delete(stale);
        return items;
    }

    private static void Write(string file, CampaignDocument doc, HashSet<string> written)
    {
        string text = doc.ToString();
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        if (!File.Exists(file) || File.ReadAllText(file) != text) File.WriteAllText(file, text);
        written.Add(Path.GetFullPath(file));
    }

    private static string Safe(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Trim().TrimEnd('.');
    }

    private static string Text(JsonElement model, string key, string fallback = "")
        => model.TryGetProperty(key, out var v) ? v.ValueKind switch {
            JsonValueKind.String => v.GetString() ?? fallback,
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.True => "yes", JsonValueKind.False => "no",
            JsonValueKind.Array => string.Join(", ", v.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : x.GetRawText()).Where(x => !string.IsNullOrEmpty(x))),
            _ => fallback } : fallback;

    private static string Rgb(JsonElement color)
    {
        int C(string channel) => color.TryGetProperty(channel, out var v) && v.TryGetDouble(out double d) ? (int)Math.Round(Math.Clamp(d, 0, 1) * 255) : 0;
        return $"{C("r")}, {C("g")}, {C("b")}";
    }

    internal static CampaignDocument Player(JsonElement model, bool goalie)
    {
        var doc = new CampaignDocument("# Custom player from the game's save folder\n");
        doc.Set("Name", (Text(model, "firstName") + " " + Text(model, "lastName")).Trim());
        doc.Set("Number", Text(model, "number"));
        void Map(string json, string field) { if (model.TryGetProperty(json, out _)) doc.Set(field, Text(model, json)); }
        if (goalie)
        {
            doc.Set("Position", "Goalie");
            foreach (var (json, field) in new[] { ("skill", "Skill"), ("catchingSkill", "Catching"), ("gloveSkill", "Glove"), ("blockerSkill", "Blocker"),
                ("fiveHoleSkill", "Five Hole"), ("standingSpeed", "Standing Speed"), ("butterflySpeed", "Butterfly Speed"), ("controlSkill", "Control"),
                ("recoverySkill", "Recovery"), ("passPower", "Pass Power"), ("shotPower", "Shot Power"), ("pokecheckSkill", "Poke Check"),
                ("depth", "Depth"), ("passReadSkill", "Pass Read"), ("talentIds", "Goalie Talents"),
                ("helmetSkin", "Helmet Skin"), ("skin", "Skin"), ("awaySkin", "Skin Away"), ("gloveSkin", "Glove Skin"), ("awayGloveSkin", "Glove Away"),
                ("blockerSkin", "Blocker Skin"), ("awayBlockerSkin", "Blocker Away"), ("padsSkin", "Pads Skin"), ("awayPadsSkin", "Pads Away"),
                ("stickSkin", "Stick Skin"), ("awayStickSkin", "Stick Away"), ("logoSkin", "Logo Skin") })
                Map(json, field);
        }
        else
        {
            Map("headSkin", "Face");
            if (model.TryGetProperty("isLefty", out var lefty)) doc.Set("Left Handed", lefty.ValueKind == JsonValueKind.True ? "yes" : "no");
            if (model.TryGetProperty("isBlack", out var black)) doc.Set("Skin Color", black.ValueKind == JsonValueKind.True ? "dark" : "light");
            if (model.TryGetProperty("skaterSize", out var size) && size.TryGetInt32(out int s) && s >= 0 && s < FieldSchema.Sizes.Length) doc.Set("Size", FieldSchema.Sizes[s]);
            foreach (var (json, field) in new[] { ("speed", "Speed"), ("shotPower", "Shot Power"), ("shotAccuracy", "Accuracy"), ("checking", "Checking"),
                ("abilityId", "Ability"), ("talentIds", "Talents"), ("bodySkin", "Body"), ("bodyAwaySkin", "Body Away"), ("bicepSkin", "Bicep"),
                ("bicepAwaySkin", "Bicep Away"), ("gloveSkin", "Gloves"), ("gloveAwaySkin", "Gloves Away"), ("pantsSkin", "Pants"), ("pantsAwaySkin", "Pants Away"),
                ("skateSkin", "Skates"), ("skateAwaySkin", "Skates Away"), ("helmetSkin", "Helmet"), ("helmetAwaySkin", "Helmet Away"),
                ("stickSkin", "Stick"), ("glassesSkin", "Glasses"), ("numberSkin", "Number Skin"), ("logoSkin", "Logo Skin") })
                Map(json, field);
        }
        // The game's player format carries no colours, so a loose player wears the team look it joins.
        doc.Set("Override Team Settings", "no");
        return doc;
    }

    internal static CampaignDocument Team(JsonElement model)
    {
        var doc = new CampaignDocument("# Custom team from the game's save folder\n");
        doc.Set("Team Name", Text(model, "TeamName", Text(model, "teamName")));
        doc.Set("City", Text(model, "City", Text(model, "city")));
        doc.Set("Abbreviation", Text(model, "nickname"));
        if (Text(model, "logoId").Length > 0) doc.Set("Logo From", Text(model, "logoId"));
        foreach (var (json, away) in new[] { ("homeColors", false), ("awayColors", true) })
        {
            if (!model.TryGetProperty(json, out var kit)) continue;
            string a = away ? " Away" : "";
            string[] Channels(string first, string second, string third) => [first, second, third];
            foreach (var (scheme, keys) in new[] {
                ("jerseyScheme", away ? Channels("Away Primary", "Away Secondary", "Away Accent") : Channels("Jersey Primary", "Jersey Secondary", "Jersey Accent")),
                ("skatesScheme", Channels("Skates" + a + " Color", "Blade" + a + " Color", "Laces" + a + " Color")),
                ("helmetScheme", Channels("Helmet" + a + " Color", "Helmet" + a + " Secondary Color", "Helmet" + a + " Tertiary Color")),
                ("glovesScheme", Channels("Gloves" + a + " Color", "Gloves" + a + " Secondary Color", "Gloves" + a + " Tertiary Color")),
                ("pantsScheme", Channels("Pants" + a + " Color", "Pants" + a + " Secondary Color", "Pants" + a + " Tertiary Color")),
                ("socksScheme", Channels("Socks" + a + " Color", "Socks" + a + " Secondary Color", "Socks" + a + " Tertiary Color")),
                ("stickScheme", Channels("Stick" + a + " Color", "Stick" + a + " Secondary Color", "Stick" + a + " Tertiary Color")),
                ("numberScheme", Channels("Number" + a + " Color", "Number" + a + " Secondary Color", away ? "Number Color Away" : "Number Color Home")) })
            {
                if (!kit.TryGetProperty(scheme, out var colors)) continue;
                foreach (var (channel, key) in new[] { "primaryColor", "secondaryColor", "tertiaryColor" }.Zip(keys))
                    if (key.Length > 0 && colors.TryGetProperty(channel, out var c)) doc.Set(key, Rgb(c));
            }
        }
        // Current native saves retain digit fill in numberScheme; older exports may only carry the legacy field.
        bool nativeHomeNumber = doc.Fields.ContainsKey("Number Color Home"), nativeAwayNumber = doc.Fields.ContainsKey("Number Color Away");
        foreach (var (json, key) in new[] { ("JerseyHomeNumberColor", "Number Color Home"), ("jerseyHomeNumberColor", "Number Color Home"),
            ("JerseyAwayNumberColor", "Number Color Away"), ("jerseyAwayNumberColor", "Number Color Away"),
            ("primaryColorTransition", "Transition Primary"), ("secondaryColorTransition", "Transition Secondary"), ("tertiaryColorTransition", "Transition Tertiary") })
            if (!(key == "Number Color Home" && nativeHomeNumber || key == "Number Color Away" && nativeAwayNumber)
                && model.TryGetProperty(json, out var c) && c.ValueKind == JsonValueKind.Object) doc.Set(key, Rgb(c));
        if (Text(model, "vanillaBenchPlayerHead").Length > 0) doc.Set("Bench Head", Text(model, "vanillaBenchPlayerHead"));
        if (Text(model, "relics").Length > 0) doc.Set("Team Relics", Text(model, "relics"));
        return doc;
    }
}
