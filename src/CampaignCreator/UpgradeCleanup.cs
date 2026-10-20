using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CampaignCore;

namespace CampaignCreator;

// Upgrade-only cleanup. Uses the shipped catalog, never stale catalogs in migrated data.
public sealed class UpgradeCleanup
{
    private readonly Dictionary<string, HashSet<string>> fields;
    private readonly GameData data = new(Path.Combine(AppContext.BaseDirectory, "upgrade-catalog"));
    private static readonly StringComparer IgnoreCase = StringComparer.OrdinalIgnoreCase;
    public UpgradeCleanup()
    {
        using var stream = typeof(UpgradeCleanup).Assembly.GetManifestResourceStream("CampaignCreator.UpgradeFields.json")!;
        fields = JsonSerializer.Deserialize<Dictionary<string, string[]>>(stream)!.ToDictionary(p => p.Key, p => p.Value.ToHashSet(IgnoreCase));
        // These current controls include dynamically parsed squad keys and newer
        // opponent options which can postdate the generated release allow-list.
        fields["team"].UnionWith(SquadBehaviors.All.Select(b => b.Key));
        fields["team"].Add(SquadPlayerPools.ModeKey);
        fields["team"].UnionWith(new[] { "Golf Fairway", "Tycoons Sponsors", "Canada Curses" });
        fields["team"].UnionWith(new[] { JerseyLogoOptions.Field(false), JerseyLogoOptions.Field(true) });
        fields["player"].UnionWith(new[] { JerseyLogoOptions.Field(false), JerseyLogoOptions.Field(true) });
        fields["team"].UnionWith(RelicOpponentInteractions.All.Select(i => i.SettingKey));
        fields["player"].Add(PlayerColorModel.VersionKey);
        fields["player"].UnionWith(PlayerKitColors.ModeKeys);
        fields["player"].Add(PlayerBrawlOptions.PrinceKey);
        fields["player"].UnionWith(new[] { PlayerContractOptions.EnabledKey, PlayerContractOptions.MatchesKey });
        fields["player"].Add("Number Style");
        fields["player"].Add(PlayerExport.OwnEquipmentKey);
        fields["campaign"].Add(FreeAgentTalentWeights.Key);
        fields["campaign"].Add(FreeAgentStats.KeepKey);
        fields["campaign"].Add(FreeAgentTalentPool.DisableExtrasKey);
        fields["campaign"].Add(FreeAgentAbilities.DisableKey);
        fields["campaign"].Add(FieldSchema.RefereeHeadKey);
        fields["campaign"].UnionWith(new[] { FreeAgentTalentPool.ModeKey, FreeAgentTalentPool.SelectedKey });
        foreach (string kind in RewardPoolDefinition.Kinds.Concat(CustomRewardDefinition.Kinds).Append("Goalie Abilities"))
            fields["campaign"].UnionWith(new[] { "Pool Mode " + kind, "Selected " + kind, "Included " + kind, "Excluded " + kind });
        fields["campaign"].UnionWith(new[] { RewardPoolDefinition.IncludeChaosTalentsKey, RewardPoolDefinition.IncludeRegularTalentsKey,
            RewardPoolDefinition.IncludeChaosRelicsKey, RewardPoolDefinition.IncludeRegularRelicsKey });
        // A missing release catalog is a packaging error, not evidence every saved choice is invalid.
        if (RewardCatalog.ReadEntries(data.Root, "Talents").Count == 0 || data.Assets.Values("skater", "Body").Count == 0)
            throw new InvalidOperationException("The upgrade catalog is missing. User content was not cleaned.");
    }
    public int CleanDirectory(string root)
    {
        root = Path.GetFullPath(root);
        // Preserve legacy folder names ending in spaces during Windows file operations.
        if (OperatingSystem.IsWindows() && !root.StartsWith(@"\\?\", StringComparison.Ordinal))
            root = root.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + root[2..] : @"\\?\" + root;
        var pending = new List<(string Path, CampaignDocument Document)>();
        IEnumerable<string> Files(string folder)
        {
            if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked content cannot be cleaned: " + folder);
            foreach (string file in Directory.GetFiles(folder))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked content cannot be cleaned: " + file);
                yield return file;
            }
            foreach (string dir in Directory.GetDirectories(folder)) foreach (string file in Files(dir)) yield return file;
        }
        foreach (string name in new[] { "campaigns", "library", ".studio-drafts", ".studio-staging" })
        {
            string folder = Path.Combine(root, name);
            if (!Directory.Exists(folder)) continue;
            foreach (string file in Files(folder).Where(f => Path.GetExtension(f).Equals(".txt", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(f) == "goalie-equipment.cfg"))
            {
                var document = new CampaignDocument(File.ReadAllText(file));
                string? kind = Path.GetFileName(file).ToLowerInvariant() switch {
                    "campaign.txt" or "reward_pools.txt" => "campaign", "team.txt" => "team", "goalie-equipment.cfg" => "player", _ => null };
                if (kind == null && (document.Fields.ContainsKey("Name") || document.Fields.ContainsKey("Import Player"))) kind = "player";
                if (kind == null) continue; // Map graphs, assignments, run saves and reference files have their own formats.
                string role = ContentImport.IsGoalie(document, file) || Path.GetFileName(file) == "goalie-equipment.cfg" ? "goalie" : "skater";
                bool adopted = false;
                // The team file on disk is still the pre-cleanup one (saves happen after parsing).
                string playersDir = Path.GetDirectoryName(file) ?? "";
                string teamFile = Path.Combine(Path.GetDirectoryName(playersDir) ?? "", "team.txt");
                if (kind == "player" && role == "skater" && Path.GetFileName(playersDir).Equals("players", StringComparison.OrdinalIgnoreCase) && File.Exists(teamFile))
                    adopted = PlayerColorModel.AdoptTeamStick(document, new CampaignDocument(File.ReadAllText(teamFile))).Count > 0;
                if (Clean(document, kind, role) || adopted) pending.Add((file, document));
            }
        }
        // Parse the whole tree first; atomic per-file replacement leaves no .bak files.
        foreach (var item in pending) item.Document.Save(item.Path);
        return pending.Count;
    }
    public static bool Retired(string key) => !SquadBehaviors.All.Any(b => b.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
        && (key.Contains("random", StringComparison.OrdinalIgnoreCase) || In(key, "Import Team", "Import Player", "Player Team Copy", "Copy Player Team"));
    // The generated list holds keys the runtime parses by name. Colors (every alias
    // AppearanceFields reads) and equipment styles such as Socks and Goalie Pants are
    // read through shared helpers instead, so they come from the shared schema.
    private bool Known(string kind, string key) => fields[kind].Contains(key) || kind != "campaign"
        && (key.StartsWith(JerseyLogoOptions.Prefix(false) + " ", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith(JerseyLogoOptions.Prefix(true) + " ", StringComparison.OrdinalIgnoreCase)
            || AppearanceFields.ColorKey(key).Length > 0
            || FieldSchema.SkaterAssets.Concat(FieldSchema.GoalieAssets).Contains(key, IgnoreCase));
    public static bool RandomValue(string value) => Regex.IsMatch(value, @"\brandom\b", RegexOptions.IgnoreCase);
    public bool Clean(CampaignDocument doc, string kind, string role)
    {
        string before = doc.ToString();
        foreach (var (key, value) in doc.Fields)
        {
            if (Retired(key) || !Known(kind, key)) { doc.Remove(key); continue; }
            string cleaned = Value(key, value.Trim(), role);
            if (cleaned != value) doc.Set(key, cleaned);
        }
        if (doc.Get("Goal Sound Data").Length == 0 && doc.Fields.ContainsKey("Goal Sound Data")) doc.Set("Goal Sound", "");
        // goalie-equipment.cfg is a team's goalie style set, not a player file.
        if (kind == "player" && doc.Fields.ContainsKey("Name")) PlayerColorModel.Migrate(doc);
        if (kind is "player" or "team") AppearanceContract.PinSkates(doc);
        if (kind == "team") PlayerColorModel.StripTeamStick(doc);
        return before != doc.ToString();
    }
    // Import validation changes values only; metadata and free text remain intact.
    public void CleanImportedValues(CampaignDocument doc, string role)
    {
        foreach (var (key, value) in doc.Fields)
            doc.Set(key, Value(key, value.Trim(), role));
        if (doc.Get("Name").Length > 0 && doc.Get("Team Name").Length == 0
            && AppearanceFields.OverridesTeam(doc) && !AppearanceContract.SupportsJerseyLogo(doc, role))
            AppearanceContract.ClearJerseyLogo(doc);
        if (doc.Get("Name").Length > 0 && doc.Get("Team Name").Length == 0) PlayerColorModel.Migrate(doc);
        if (doc.Get("Team Name").Length > 0) PlayerColorModel.StripTeamStick(doc);
        AppearanceContract.PinSkates(doc);
    }
    private static bool In(string value, params string[] choices) => choices.Contains(value, IgnoreCase);
    private static bool Number(string value, double minimum, double maximum, bool integer = true)
    {
        bool One(string part, out double n) => double.TryParse(part.Trim().TrimEnd('f'), NumberStyles.Float, CultureInfo.InvariantCulture, out n)
            && double.IsFinite(n) && n >= minimum && n <= maximum && (!integer || n == Math.Truncate(n));
        return One(value, out _);
    }
    private static bool Color(string value)
    {
        var parts = Regex.Split(value, @",(?![^()]*\))");
        return parts.Length == 3 && parts.All(p => Number(p.Trim(), 0, 255));
    }
    private string Reward(string kind, string value)
    {
        if (In(value, "none")) return value;
        var entries = RewardCatalog.ReadEntries(data.Root, kind).Values;
        // Both stable IDs and legacy names are supported by the runtime.
        return entries.Any(e => In(value, e.Key, e.Id, e.Name, e.Asset)) ? value : "";
    }
    private string Value(string key, string value, string role)
    {
        if (value.Length == 0) return "";
        if (key.Equals(FreeAgentStats.KeepKey, StringComparison.OrdinalIgnoreCase)
            || key.Equals(FreeAgentAbilities.DisableKey, StringComparison.OrdinalIgnoreCase)
            || key.Equals(FreeAgentTalentPool.DisableExtrasKey, StringComparison.OrdinalIgnoreCase))
            return In(value, "yes", "no", "true", "false", "1", "0", "On", "Off") ? value : "";
        if (PlayerKitColors.ModeKeys.Contains(key, IgnoreCase)) return In(value, "yes", "no") ? value : "";
        // Preserve unknown GUIDs and invalid input for the editor's visible validation.
        // A malformed multiplier must never silently become native/default or disabled.
        if (key.Equals(FreeAgentTalentWeights.Key, StringComparison.OrdinalIgnoreCase)
            || key.Equals(FreeAgentTalentPool.ModeKey, StringComparison.OrdinalIgnoreCase)
            || key.Equals(FreeAgentTalentPool.SelectedKey, StringComparison.OrdinalIgnoreCase)) return value;
        string k = key.ToLowerInvariant();
        // Authored heads are not in the shipped native upgrade catalog. Preserve
        // their identities during upgrades and imports, including when the head
        // project will be installed later; the head loader validates its assets.
        if ((k is "face" or "squad head" or "zamboni head" or "bench head" or "referee head")
            && value.StartsWith("Faces/CustomHeads/", StringComparison.OrdinalIgnoreCase)) return value;
        if (k == "helmet skin" && role == "goalie"
            && value.StartsWith("Helmet/CustomMasks/", StringComparison.OrdinalIgnoreCase)) return value;
        if (SquadBehaviors.All.Any(b => b.Key.Equals(key, StringComparison.OrdinalIgnoreCase)))
            return In(value, "Inherit", "On", "Off", "yes", "no", "true", "false") ? value : "";
        if (k.StartsWith("export ")) return value;
        if (key.Equals(JerseyLogoOptions.Field(false), StringComparison.OrdinalIgnoreCase)
            || key.Equals(JerseyLogoOptions.Field(true), StringComparison.OrdinalIgnoreCase)) return value;
        if (k.StartsWith("pool mode ")) return In(value, "Base Game", "Custom") ? value : "";
        if (key.Equals(RewardPoolDefinition.IncludeChaosTalentsKey, StringComparison.OrdinalIgnoreCase)
            || key.Equals(RewardPoolDefinition.IncludeRegularTalentsKey, StringComparison.OrdinalIgnoreCase)
            || key.Equals(RewardPoolDefinition.IncludeChaosRelicsKey, StringComparison.OrdinalIgnoreCase)
            || key.Equals(RewardPoolDefinition.IncludeRegularRelicsKey, StringComparison.OrdinalIgnoreCase))
            return In(value, "yes", "no", "true", "false", "1", "0") ? value : "";
        if (k.StartsWith("selected ") || k.StartsWith("included "))
            return string.Join(", ", value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).Distinct(IgnoreCase));
        // Imported rewards can be newer than the editor catalog. Keep their titles
        // so they remain visible and editable instead of silently losing them.
        if (k is "relics" or "team relics" or "talents" or "goalie talents" or "team talents")
            return string.Join(", ", value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).Distinct(IgnoreCase));
        if (RandomValue(value) && k is not ("name" or "team name" or "description" or "city" or "abbreviation" or "goal sound" or "goal sound data")) return "";
        if (k is "squad template" or "template squad" or "maps" or "map source" or "squad maps")
            return In(value, "default", "(default)", "none", "no") || SquadTemplateCatalog.Find(value) != null ? value : "";
        if (k is "offside penalty" or "offsides penalty") return In(value, "Whistle", "Lose Puck", "Knockout", "default", "none", "no") ? value : "";
        if (k == "act sequence") return CampaignActSequence.TryParse(value, out _) ? value : "";
        if (k == "replace challenges" && Regex.IsMatch(value, @"^(maps:)?[1-9]\d*(\s*,\s*[1-9]\d*)*$", RegexOptions.IgnoreCase)) return value;
        if (k is "replace challenges" or "replace soccer ball" or "replace golf ball" or "use player teams" or "custom player teams" or "gauntlet map" or "dump data" or "offside" or "offsides" or "no bench bonus" or "disable bench bonus" or "override team settings")
            return In(value, "yes", "no", "true", "false", "1", "0") ? value : "";
        if (k == "left handed") return In(value, "yes", "no", "true", "false") ? value : "";
        if (key.Equals(PlayerContractOptions.EnabledKey, StringComparison.OrdinalIgnoreCase)) return In(value, "yes", "no", "true", "false", "on", "off", "1", "0") ? value : "";
        if (key.Equals(PlayerContractOptions.MatchesKey, StringComparison.OrdinalIgnoreCase)) return PlayerContractOptions.TryMatches(value.Trim(), out _) ? value : "";
        if (k == "skin color") return In(value, "light", "dark") ? value : "";
        if (k == "size") return FieldSchema.Sizes.Contains(value, IgnoreCase) ? value : "";
        if (k == "number style") return FieldSchema.NumberStyles.Contains(value, IgnoreCase) || In(value, FieldSchema.InheritNumberStyle, "RomanNumerals", "Roman") ? value : "";
        if (k == "position") return In(value, "Goalie", "Center", "Left Wing", "Right Wing", "Left Defense", "Right Defense", "Line 2 Center", "Line 2 Left Wing", "Line 2 Right Wing", "Line 2 Left Defense", "Line 2 Right Defense") ? value : "";
        if (k == "opponent preset") return OpponentOptions.Presets.Keys.Contains(value, IgnoreCase) ? value : "";
        if (k == "opponent offense") return OpponentEffects.Offense.Contains(value, IgnoreCase) ? value : "";
        if (k == "opponent defense") return OpponentEffects.Defense.Contains(value, IgnoreCase) ? value : "";
        if (RelicOpponentInteractions.All.Any(i => i.SettingKey.Equals(key, StringComparison.OrdinalIgnoreCase)))
            return RelicOpponentInteractions.Modes.FirstOrDefault(m => m.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "";
        if (k == "match puck") return OpponentOptions.Pucks.Contains(value, IgnoreCase) ? value : "";
        if (k == "match surface") return OpponentOptions.Surfaces.Contains(value, IgnoreCase) ? value : "";
        if (k is "golf fairway" or "tycoons sponsors" or "canada curses") return In(value, "yes", "no", "true", "false", "1", "0") ? value : "";
        // An explicit list that loses its last retired effect must stay explicit:
        // empty text would reactivate the team's legacy preset.
        if (k == "opponent effects") return OpponentEffects.Serialize(value.Split(',').Select(s => s.Trim()).Where(s => OpponentEffects.All.Any(e => In(s, e.Key))));
        if (k == "ability") return Reward(role == "goalie" ? "Goalie Abilities" : "Abilities", value);
        if (k.Contains("relic") || k is "talents" or "goalie talents" or "team talents" or "random pool" or "team random pool" || k.StartsWith("excluded "))
        {
            if ((k is "random pool" or "team random pool") && In(value, "all", "whole pool", "full pool")) return value;
            string category = k.Contains("relic") ? "Relics" : k.Contains("abilit") ? "Abilities" : "Talents";
            return string.Join(", ", value.Split(',').Select(s => s.Trim()).Where(s => Reward(category, s).Length > 0 || (category != "Relics" && Reward("Goalie " + category, s).Length > 0)).Distinct(IgnoreCase));
        }
        if (k == "goal sound data") { try { GoalSoundWave.Decode(Convert.FromBase64String(value)); return value; } catch { return ""; } }
        if (k == "goal sound volume") return Number(value, 0, 100) ? value : "";
        if (k == "goalie skill" || FieldSchema.Kind(key) == FieldKind.Number)
            return Number(value, k == "size offset" ? -100 : 0, k == "number" ? 99 : 999, k is not ("size offset" or "pass read" or "stat scale")) ? value : "";
        if (FieldSchema.Kind(key) == FieldKind.Color) return Color(value) ? value : "";
        string assetKey = k is "squad head" or "zamboni head" or "bench head" or "referee head" ? "Face" : key;
        string assetRole = k is "squad head" or "zamboni head" or "bench head" or "referee head" ? "skater" : role;
        var assets = data.Assets.Values(assetRole, assetKey);
        if (assets.Count > 0 || FieldSchema.SkaterAssets.Concat(FieldSchema.GoalieAssets).Contains(key, IgnoreCase))
        {
            if (In(value, "none")) return assetRole == "skater" && (assetKey is "Helmet" or "Helmet Away") ? "none" : "";
            if (In(value, "standard", "default", "team colors"))
            {
                string fallback = AppearanceContract.DefaultSkin(assetRole, assetKey);
                return assets.Contains(fallback, IgnoreCase) ? fallback : "";
            }
            try {
                string resolved = data.Assets.Resolve(assetRole, assetKey, value);
                return resolved.EndsWith("_iso", StringComparison.OrdinalIgnoreCase) || resolved.EndsWith("_lso", StringComparison.OrdinalIgnoreCase) ? "" : resolved;
            } catch (ArgumentException) { return ""; }
        }
        return value; // Supported free text, external asset paths, and native identity references.
    }
}




