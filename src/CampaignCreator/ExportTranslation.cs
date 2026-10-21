using System.Globalization;
using System.Text.Json;
using CampaignCore;

namespace CampaignCreator;

internal static partial class ExportTranslation
{
    // A workspace content copy is not where the running plugin reads overrides.
    internal static string RuntimeRoot(string root)
    {
        return UserContentPaths.EnsureRoot();
    }

    internal static string[] Ids(GameData game, string values, bool relics, ExportReport? report = null, CampaignDocument? source = null, string? field = null)
    {
        var map = relics ? game.Relics : game.Talents;
        return values.Split(',', StringSplitOptions.RemoveEmptyEntries).Where(raw => !raw.Trim().Equals("none", StringComparison.OrdinalIgnoreCase)).Select(raw => {
            string name = raw.Trim();
            if (map.TryGetValue(name, out var id)) return id;
            if (map.Values.Contains(name, StringComparer.OrdinalIgnoreCase)) return name;
            // Campaign relic syntax allows explicit level suffixes.
            if (relics && name.EndsWith(":2") && map.TryGetValue(name[..^2].Trim() + " LVL2", out id)) return id;
            if (relics && name.EndsWith(":2"))
            {
                string BaseName(string text) => System.Text.RegularExpressions.Regex.Replace(text, @"\s*(?:LVL2|\(Level 2\)|Lv2|Level 2|\+)\s*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
                var upgraded = RewardCatalog.ReadEntries(game.Root, "Relics").Values.Where(e => e.Level == 2 && (BaseName(e.Name).Equals(name[..^2].Trim(), StringComparison.OrdinalIgnoreCase) || BaseName(e.Asset).Equals(name[..^2].Trim(), StringComparison.OrdinalIgnoreCase))).ToArray();
                if (upgraded.Length == 1) return upgraded[0].Id;
            }
            report?.Skip(source ?? new CampaignDocument(""), field ?? (relics ? "Relics" : "Talents"), name);
            return "";
        }).Where(id => id.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static CampaignDocument Player(CampaignDocument player, CampaignDocument team, CampaignDocument? goalieDefaults, string role, GameData? game = null)
    {
        var result = AppearanceContract.Resolve(player, team, goalieDefaults ?? new CampaignDocument(""), role);
        bool personal = AppearanceFields.OverridesTeam(player) && player.Get("Logo From").Length > 0;
        foreach (bool away in new[] { false, true })
        {
            if (JerseyLogoOptions.Read(result, away).Mode != JerseyLogoMode.Image || personal) continue;
            if (JerseyLogoOptions.Read(team, away).Mode != JerseyLogoMode.Inherit)
                LogoExport.Copy(team, result, away, away);
            else if (result.Get(JerseyLogoOptions.Field(away)) == team.Get("Logo From"))
                LogoExport.CopyIdentityToJersey(team, result, away);
        }
        result.Set(PlayerExport.OwnEquipmentKey, PlayerExport.EquipmentIntent(player));
        // This is now a standalone export snapshot. Later conversion has no team
        // context, so it must retain the appearance we just inherited.
        result.Set("Override Team Settings", "yes");
        result.Set("Number Style", FieldSchema.EffectiveNumberStyle(player.Get("Number Style"), team.Get("Number Style", "Regular")));
        string inherited = string.Join(",", CustomRewardAssignments.TeamTalents(team));
        if (game != null) inherited = game.InheritedTalents(inherited, role == "goalie");
        result.Set("Talents", string.Join(",", new[] { player.Get("Talents"), inherited }.Where(s => s.Length > 0)));
        return result;
    }

    internal static Dictionary<string, object> Colors(CampaignDocument team, bool away)
    {
        var effective = AppearanceContract.Resolve(new CampaignDocument("Override Team Settings = no"), team, new CampaignDocument(""), "skater");
        object Color(string scheme, int channel)
        {
            string key = AppearanceContract.ColorKey(scheme, channel, away), value = effective.Get(key);
            if (!FieldSchema.TryRgb(value, out byte r, out byte g, out byte b)) throw new ArgumentException($"{key} must contain fixed RGB values before exporting.");
            return new { r = r / 255f, g = g / 255f, b = b / 255f, a = 1f };
        }
        return AppearanceContract.Schemes.ToDictionary(s => s + "Scheme", s => (object)new {
            primaryColor = Color(s, 0), secondaryColor = Color(s, 1), tertiaryColor = Color(s, 2) });
    }

    private static object TeamColor(CampaignDocument team, string key, string fallbackKey, string fallback)
    {
        string value = team.Get(key); if (value.Length == 0) value = team.Get(fallbackKey); if (value.Length == 0) value = fallback;
        if (!FieldSchema.TryRgb(value, out byte r, out byte g, out byte b)) throw new ArgumentException($"{key} must contain fixed RGB values before exporting.");
        return new { r = r / 255f, g = g / 255f, b = b / 255f, a = 1f };
    }

    // Same stats the game mod's ApplyStatScale multiplies (truncated, like the mod).
    private static void ApplyStatScale(CampaignDocument doc, string role, double scale)
    {
        (string key, string alias)[] fields = role == "goalie"
            ? [("Skill", "Goalie Skill"), ("Catching", ""), ("Glove", ""), ("Blocker", ""), ("Five Hole", ""), ("Standing Speed", "Stand Speed"),
               ("Butterfly Speed", ""), ("Control", ""), ("Recovery", ""), ("Pass Power", ""), ("Shot Power", ""), ("Poke Check", "Pokecheck"), ("Depth", "")]
            : [("Speed", ""), ("Shot Power", ""), ("Accuracy", ""), ("Checking", "")];
        foreach (var (key, alias) in fields)
        {
            string text = doc.Get(key); if (text.Length == 0 && alias.Length > 0) text = doc.Get(alias);
            int value = 50; // the export's default when a stat is blank
            if (text.Length > 0 && !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) continue; // random(...) etc. left for the normal check
            doc.Set(key, Math.Clamp((int)(value * scale), 0, 999).ToString(CultureInfo.InvariantCulture));
        }
    }

    internal sealed record Package(string TeamId, Dictionary<string, string> Saves, Dictionary<string, string> Overrides);
    internal static Package BuildTeam(CampaignDocument team, string teamFile, GameData game, string? sourceRoot = null, ExportReport? report = null)
    {
        game.LoadCustomRewards(teamFile);
        game.ValidateCustomLoadout(team);
        var custom = new CustomRewardExportBundle(game);
        team = ExportSelections.Logo(team, sourceRoot ?? game.Root, report);
        foreach (bool away in new[] { false, true }) team = ExportSelections.JerseyLogo(team, sourceRoot ?? game.Root, away, report);
        if (team.Get("Team Name").Length == 0) throw new ArgumentException("Name the team before exporting.");
        if (team.Get("Import Team").Length > 0) throw new ArgumentException("Import the team's complete roster into the editor before exporting.");
        if (team.Get("Team Random Talents").Length > 0 && team.Get("Team Random Talents") != "0")
            throw new ArgumentException("Choose exact team talents before exporting; random talent rolls are campaign settings.");
        double statScale = 1;
        if (team.Get("Stat Scale").Length > 0 && (!double.TryParse(team.Get("Stat Scale"), NumberStyles.Float, CultureInfo.InvariantCulture, out statScale) || statScale < 0))
            throw new ArgumentException("Stat Scale must be a plain number such as 1.2 before exporting.");
        string folder = Path.GetDirectoryName(teamFile)!;
        string players = Path.Combine(folder, "players");
        if (!Directory.Exists(players)) throw new ArgumentException("Add the team's roster before exporting.");
        string defaults = Path.Combine(folder, "goalie-equipment.cfg");
        var goalieDefaults = new CampaignDocument(File.Exists(defaults) ? File.ReadAllText(defaults) : "");
        bool twoLines = SquadBehaviors.Enabled(team.Get("Squad Template"), "two_lines", key => team.Get(key))
            || team.Get("Opponent Two Lines").Equals("yes", StringComparison.OrdinalIgnoreCase);
        bool stamina = SquadBehaviors.Enabled(team.Get("Squad Template"), "line_stamina", key => team.Get(key));
        string[] positions = RosterLines.Skaters.Concat(["Goalie"]).ToArray();
        string[] slots = PlayNowPositions.NativeFields.Append("goalieId").ToArray();
        var rosterFiles = new Dictionary<string, (string File, CampaignDocument Raw)>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.GetFiles(players, "*.txt", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
        {
            var raw = new CampaignDocument(File.ReadAllText(file));
            string position = RosterPosition.Read(file, players, raw);
            bool line2 = position.StartsWith("Line 2 ", StringComparison.OrdinalIgnoreCase);
            string basePosition = line2 ? position[7..] : position;
            if (!positions.Contains(basePosition, StringComparer.OrdinalIgnoreCase) || line2 && basePosition.Equals("Goalie", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Play Now cannot export '{Path.GetFileName(file)}': '{position}' is not a roster slot.");
            // Prepared Line 2 files are authoring content until two-line play is enabled.
            if (line2 && !twoLines) continue;
            bool goalie = basePosition.Equals("Goalie", StringComparison.OrdinalIgnoreCase);
            string declaredRole = raw.Get("Export Role").Trim();
            string expectedRole = goalie ? "goalie" : "skater";
            if (declaredRole.Length > 0 && !declaredRole.Equals(expectedRole, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Play Now cannot export '{Path.GetFileName(file)}': {position} needs a {expectedRole}, but Export Role is {declaredRole}.");
            bool goalieOnlyFields = raw.Fields.ContainsKey("Catching") || raw.Fields.ContainsKey("Goalie Skill");
            if ((!goalie && goalieOnlyFields)
                || (declaredRole.Length == 0 && raw.Get("Position").Trim().Length == 0 && ContentImport.IsGoalie(raw, file) != goalie))
                throw new ArgumentException($"Play Now cannot export '{Path.GetFileName(file)}': {position} has the wrong skater/goalie role.");
            if (!rosterFiles.TryAdd(position, (file, raw)))
                throw new ArgumentException($"Play Now has more than one player in {position}. Give each export slot exactly one player.");
        }
        string[] required = twoLines ? positions.Concat(RosterLines.Skaters.Select(p => "Line 2 " + p)).ToArray() : positions;
        string[] missing = required.Where(position => !rosterFiles.ContainsKey(position)).ToArray();
        if (missing.Length > 0)
            throw new ArgumentException("Export requires all five starting skaters and a goalie"
                + (twoLines ? ", plus a complete Line 2" : "") + ". Missing: " + string.Join(", ", missing) + ".");
        var saves = new Dictionary<string, string>(); var overrides = new Dictionary<string, string>();
        var rosterIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string tid = Guid.NewGuid().ToString();
        var model = new Dictionary<string, object> { ["Id"] = tid, ["TeamName"] = team.Get("Team Name"), ["City"] = team.Get("City"),
            ["nickname"] = team.Get("Abbreviation"), ["logoId"] = team.Get("Export Logo ID"), ["hasBigLogo"] = team.Get("Export Logo Mode") == "embedded",
            ["homeColors"] = Colors(team, false), ["awayColors"] = Colors(team, true), ["isLocked"] = false,
            ["vanillaBenchPlayerHead"] = team.Get("Bench Head", "Faces/Anyteam/Black_Helmet"), ["hatTrickObjectId"] = "Calaveras_hattrick_cap",
            ["boardTextureId"] = "", ["arenaNameTextureId"] = "" };
        model["primaryColorPlayer"] = TeamColor(team, "Jersey Primary", "Jersey Primary", "23,75,121");
        model["secondaryColorPlayer"] = TeamColor(team, "Jersey Secondary", "Jersey Secondary", "36,154,104");
        model["primaryColorTransition"] = TeamColor(team, "Transition Primary", "Jersey Primary", "23,75,121");
        model["secondaryColorTransition"] = TeamColor(team, "Transition Secondary", "Jersey Secondary", "36,154,104");
        model["tertiaryColorTransition"] = TeamColor(team, "Transition Tertiary", "Jersey Accent", "255,255,255");
        model["JerseyHomeNumberColor"] = TeamColor(team, "Number Color Home", "Number Color", "255,255,255");
        model["JerseyAwayNumberColor"] = TeamColor(team, "Number Color Away", "Number Color Home", "255,255,255");
        var relics = new List<string>(Ids(game, team.Get("Team Relics"), true, report, team, "Team Relics")
            .Concat(Ids(game, team.Get("Relics"), true, report, team, "Relics")));
        string? benchHead = ExportSelections.Asset(game, "skater", "Face", (string)model["vanillaBenchPlayerHead"], team, report, "Bench Head");
        if (benchHead == null) model.Remove("vanillaBenchPlayerHead"); else model["vanillaBenchPlayerHead"] = benchHead;
        foreach (var (position, entry) in rosterFiles)
        {
            var (file, raw) = entry;
            bool goalie = position.Equals("Goalie", StringComparison.OrdinalIgnoreCase);
            string role = goalie ? "goalie" : "skater", id = Guid.NewGuid().ToString();
            var doc = Player(ExportPlayerResolver.Resolve(raw, role), team, goalieDefaults, role, game);
            if (doc.Get("Logo From") == team.Get("Logo From")) LogoExport.Copy(team, doc);
            doc = ExportSelections.Logo(doc, sourceRoot ?? game.Root, report);
            foreach (bool away in new[] { false, true }) doc = ExportSelections.JerseyLogo(doc, sourceRoot ?? game.Root, away, report);
            if (statScale != 1) ApplyStatScale(doc, role, statScale);
            var playerModel = PlayerExport.Convert(doc, game, role, id, report);
            custom.Player(playerModel);
            saves[(role == "goalie" ? "CustomGoalie-" : "CustomForward-") + id + ".json"] = JsonSerializer.Serialize(playerModel);
            var playerSidecar = PlayerExport.TranslationSidecar(doc, game, role, playerModel, report);
            custom.EmbedPlayer(playerSidecar, playerModel);
            overrides[id + ".txt"] = playerSidecar.ToString();
            relics.AddRange(Ids(game, raw.Get("Relics"), true, report, raw, "Relics").Concat(Ids(game, raw.Get("Team Relics"), true, report, raw, "Team Relics")));
            rosterIds[position] = id;
        }
        for (int i = 0; i < positions.Length; i++) model[slots[i]] = rosterIds[positions[i]];
        model["relics"] = custom.Map(relics.Distinct(StringComparer.OrdinalIgnoreCase));
        var teamSidecar = ExportSelections.Sidecar(team, game, "skater", report);
        if (benchHead == null) teamSidecar.Remove("Bench Head");
        teamSidecar.Set("Export Version", "3");
        teamSidecar.Set("Export Relic IDs", string.Join(",", (string[])model["relics"]));
        teamSidecar.Set("Team Relics", string.Join(",", (string[])model["relics"])); teamSidecar.Set("Relics", "");
        // Team talents have already been resolved onto each exported player.
        teamSidecar.Set("Team Talents", ""); teamSidecar.Set("Talents", "");
        CustomRewardExport.Embed(teamSidecar, custom.Items);
        if (statScale != 1) teamSidecar.Set("Stat Scale", "1"); // already baked into the player stats
        if (twoLines)
            PlayNowRosterCodec.Write(teamSidecar, new PlayNowRoster(
                RosterLines.Skaters.Select(position => rosterIds[position]).ToArray(),
                RosterLines.Skaters.Select(position => rosterIds["Line 2 " + position]).ToArray(),
                rosterIds["Goalie"], stamina));
        else
            foreach (string key in new[] { PlayNowRosterCodec.VersionKey, PlayNowRosterCodec.Line1Key,
                PlayNowRosterCodec.Line2Key, PlayNowRosterCodec.GoalieKey, PlayNowRosterCodec.StaminaKey })
                teamSidecar.Remove(key);
        overrides[tid + ".team.txt"] = teamSidecar.ToString();
        saves["CustomTeam-" + tid + ".json"] = JsonSerializer.Serialize(model);
        return new(tid, saves, overrides);
    }

    internal static string Write(Package package, string save, string overrides, string? libraryRoot = null)
    {
        if (!Directory.Exists(save)) throw new DirectoryNotFoundException("Launch the game once before exporting.");
        Directory.CreateDirectory(overrides);
        var written = new List<string>();
        try
        {
            if (libraryRoot != null)
            {
                string snapshot = Path.Combine(libraryRoot, "library", "Exported Teams", package.TeamId);
                foreach (var (name, text) in package.Overrides)
                {
                    string target = name.EndsWith(".team.txt", StringComparison.OrdinalIgnoreCase)
                        ? Path.Combine(snapshot, "team.txt") : Path.Combine(snapshot, "players", name);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    using var stream = new FileStream(target, FileMode.CreateNew);
                    written.Add(target);
                    using var writer = new StreamWriter(stream); writer.Write(text);
                }
            }
            // Unique IDs prevent replacing existing teams. Publish the team last.
            foreach (var group in new[] { (overrides, package.Overrides), (save, package.Saves) })
            foreach (var (name, text) in group.Item2)
            {
                string target = Path.Combine(group.Item1, name), temp = target + ".tmp";
                try { File.WriteAllText(temp, text); File.Move(temp, target); written.Add(target); }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
        }
        catch { foreach (string file in written.AsEnumerable().Reverse()) File.Delete(file); throw; }
        return Path.Combine(save, "CustomTeam-" + package.TeamId + ".json");
    }

    internal static string ExportTeam(CampaignDocument team, string file, GameData game, string root, ExportReport? report = null)
    {
        string sourceRoot = root;
        team = ExportSelections.Logo(team, root, report);
        root = RuntimeRoot(root);
        game = new GameData(sourceRoot);
        var package = BuildTeam(team, file, game, sourceRoot, report);
        string save = Path.Combine(GameSavePaths.Root, "TeamDataModels");
        return Write(package, save, Path.Combine(RuntimeRoot(root), "play_now_overrides"), sourceRoot);
    }
}

