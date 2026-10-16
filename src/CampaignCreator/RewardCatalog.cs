using System.Text.RegularExpressions;
using CampaignCore;

namespace CampaignCreator;

public static class RewardCatalog
{
    public static HashSet<string> FreeAgentBaseTalents(string root)
    {
        // Extracted from TalentRepository: native base rolls plus reachable exact
        // upgrade levels. Keeping upgrade levels checked makes native defaults honest.
        using var stream = typeof(RewardCatalog).Assembly.GetManifestResourceStream("CampaignCreator.FreeAgentBasePool.tsv")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split('\n').Skip(1).Select(s => s.Trim().Split('\t')[0]).Where(s => s.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static Dictionary<string, string> Load(string root, string kind)
    {
        kind = RewardPoolDefinition.CatalogKind(kind);
        var inventory = Inventory(root, kind);
        if (inventory != null) return inventory.ToDictionary(p => p.Key, p => p.Value.Name, StringComparer.OrdinalIgnoreCase);
        string dumps = Path.GetFullPath(Path.Combine(root, "T2T_Dumps"));
        var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (kind is "Relics" or "Talents")
        {
            string file = Path.Combine(dumps, "_gui_data", "_reward_" + kind.ToLowerInvariant() + ".txt");
            if (File.Exists(file)) foreach (var line in File.ReadLines(file).Where(l => !l.StartsWith('#')))
            {
                var p = line.Split('|'); if (p.Length < 2 || p[0].Length == 0) continue;
                string label = p[1].Trim();
                if (label.StartsWith("[Missing:", StringComparison.OrdinalIgnoreCase)) label = FieldControls.Friendly(label.Split('/').ElementAtOrDefault(1) ?? p[0]) + " (name unavailable)";
                entries[p[0]] = (label.Length > 0 ? label : p[0]) + (kind == "Relics" && p.Length >= 3 ? " · " + p[2] : "");
            }
            if (kind == "Talents")
            {
                if (entries.Count == 0) entries = ReadDump(Path.Combine(dumps, "rewards", "TALENTS_SKATER.txt"));
                foreach (var id in ReadDump(Path.Combine(dumps, "rewards", "TALENTS_GOALIE.txt")).Keys) entries.Remove(id);
            }
            return entries;
        }
        string filename = kind switch { "Goalie Talents" => "TALENTS_GOALIE.txt", "Abilities" => "ABILITIES_SKATER.txt", "Goalie Abilities" => "ABILITIES_GOALIE.txt", _ => throw new ArgumentException("Unknown reward catalog") };
        entries = ReadDump(Path.Combine(dumps, "rewards", filename));
        if (kind.Contains("Abilities"))
        {
            // Abilities are persisted by native GUID, not their display/config names.
            var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string map = Path.Combine(dumps, "_gui_data", "_export_id_map.txt"); bool abilities = false;
            if (File.Exists(map)) foreach (string line in File.ReadLines(map))
            {
                if (line.StartsWith('[')) { abilities = line.Trim() == "[abilities]"; continue; }
                var p = line.Split('|', 2); if (abilities && p.Length == 2) ids[p[0].Trim()] = p[1].Trim();
            }
            entries = entries.Where(p => ids.ContainsKey(p.Key)).ToDictionary(p => ids[p.Key], p => p.Value, StringComparer.OrdinalIgnoreCase);
        }
        return entries;
    }
    public sealed record Entry(string Key, string Name, string Description, int Level, string Id = "", string Asset = "");

    // Normal-run pool decoded from the game's repositories.
    // Ids the file doesn't list are treated as offered, since nothing proves otherwise.
    private static Dictionary<string, bool>? normalRunPool;
    public static bool OfferedInNormalRun(string id)
    {
        if (normalRunPool == null)
        {
            var pool = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            string file = Path.Combine(AppContext.BaseDirectory, "reward-data", "normal_run_pool.tsv");
            if (File.Exists(file)) foreach (string line in File.ReadLines(file).Skip(1))
            {
                var p = line.Split('\t');
                if (p.Length >= 5) pool[p[1]] = p[4] == "yes";
            }
            normalRunPool = pool;
        }
        return !normalRunPool.TryGetValue(id, out bool offered) || offered;
    }

    public static bool OfferedInBasePool(string kind, string id) => kind == RewardPoolDefinition.ChaosTalents
        ? CampaignRewardBudget.NativeChaosTalent(id) : kind == RewardPoolDefinition.ChaosRelics
        ? CampaignRewardBudget.IsChaosRelic(id) : BelongsToPool(kind, id) && OfferedInNormalRun(id);

    public static bool BelongsToPool(string kind, string id) => kind == RewardPoolDefinition.ChaosTalents
        ? CampaignRewardBudget.IsChaosTalent(id) : kind == RewardPoolDefinition.ChaosRelics ? CampaignRewardBudget.IsChaosRelic(id)
        : kind == "Relics" ? !CampaignRewardBudget.IsChaosRelic(id) : kind != "Talents" || !CampaignRewardBudget.IsChaosTalent(id);

    public static RewardPoolDefinition ReadPool(string root, CampaignDocument document, string kind)
    {
        // Legacy ordinary pools included Chaos rewards. Preserve that authored
        // selection even though new regular-pool defaults now have their own category.
        var normal = Load(root, kind).Keys.Where(id => kind is "Talents" or "Relics" ? OfferedInNormalRun(id) : OfferedInBasePool(kind, id));
        var pool = RewardPoolDefinition.Read(document, kind, normal, id => ResolveKey(root, kind, id));
        bool existing = new[] { "Pool Mode ", "Selected ", "Included ", "Excluded " }.Any(prefix => document.Get(prefix + kind).Length > 0);
        if (!existing) pool.Selected.RemoveWhere(id => !BelongsToPool(kind, id));
        return pool;
    }

    private static Dictionary<string, Entry>? Inventory(string root, string kind)
    {
        kind = RewardPoolDefinition.CatalogKind(kind);
        // Reviewed release metadata must not be hidden by a migrated old dump.
        // Keep additional locally discovered IDs so newer/custom entries survive.
        string[] catalogs = [Path.Combine(AppContext.BaseDirectory, "reward-data", "_reward_catalog.tsv"),
            Path.GetFullPath(Path.Combine(root, "T2T_Dumps", "_gui_data", "_reward_catalog.tsv"))];
        if (!catalogs.Any(File.Exists)) return null;
        var result = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in catalogs.Where(File.Exists))
        foreach (string line in File.ReadLines(file))
        {
            var p = line.Split('\t');
            if (p.Length != 7 || p[0] != kind || !int.TryParse(p[3], out int level)) continue;
            string name = p[5];
            if (string.IsNullOrWhiteSpace(name) || name.StartsWith("[Missing:", StringComparison.OrdinalIgnoreCase)) name = p[4] + " (native name missing)";
            result.TryAdd(p[1], new(p[1], name, p[6].Replace("\\n", "\n"), level, p[2], p[4]));
        }
        // Labels describe effects; native keys remain the saved identity.
        foreach (var group in result.Values.GroupBy(e => e.Name).Where(g => g.Count() > 1).ToArray())
        {
            var renamed = group.Select(e => e with { Name = EffectName(e) }).ToArray();
            foreach (var sameEffect in renamed.GroupBy(e => e.Name))
            {
                var variants = sameEffect.OrderBy(e => e.Asset, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Id, StringComparer.OrdinalIgnoreCase).ToArray();
                for (int i = 0; i < variants.Length; i++)
                {
                    var entry = variants[i];
                    result[entry.Key] = entry with { Name = entry.Name + (variants.Length > 1 ? $" (Variant {i + 1})" : "") };
                }
            }
        }
        return result;
    }

    private static string EffectName(Entry entry)
    {
        string effect = Regex.Replace(entry.Description.Split('\n')[0], "<[^>]+>", "").Trim().TrimEnd('.');
        string name = entry.Name.TrimEnd('+');
        string upgrade = entry.Name.EndsWith('+') ? "+" : entry.Level > 1 ? $" (Level {entry.Level})" : "";
        if (name.Contains("Electrolyte", StringComparison.OrdinalIgnoreCase))
        {
            var stat = Regex.Match(effect, @"\+\d+\s+(?<stat>.+?)\s+in period (?<period>\d+)", RegexOptions.IgnoreCase);
            if (stat.Success)
            {
                string attribute = stat.Groups["stat"].Value;
                if (attribute.Equals("OVR", StringComparison.OrdinalIgnoreCase))
                    return "Overall Electrolyte" + upgrade + " - Period " + stat.Groups["period"].Value;
                return attribute + " Electrolyte" + upgrade;
            }
        }
        string? label = name switch
        {
            "Briefcase" => effect.Contains("opponent the lead") ? "Prevent Opponent Lead" : "Undo One Goal",
            "Buzzer Beater" => "Double Goals - " + Regex.Match(effect, @"last \d+s of (?:a period|the game)").Value,
            "Merciless" => effect.Contains("win by mercy") ? "Overall per Mercy Win" : "Overall per First-Period Win",
            "Extra Gravy" => entry.Asset == "Playmaker" ? "Assist Boosts Scorer" : "Playmaker-Assisted Goal Boost",
            "Spin-O-Rama" => effect.Contains("Disarms", StringComparison.OrdinalIgnoreCase) || entry.Description.Contains("Disarms", StringComparison.OrdinalIgnoreCase) ? "Disarming Spin" : "Spinning Move",
            "Quest" => entry.Asset switch
            {
                "Sally Bench Quest Talent" => "Score 10 Enraged Goals",
                "Kirby Bench Quest Talent" => "Reach 100% Critical Shot Chance",
                "Mark Bench Quest Talent" => "Bench-Team Final Swap",
                "Cassoulet Bench Quest Talent" => "Get 200 Knockouts",
                "Stumple Bench Quest Talent" => "Injure All Four Body Parts",
                "Kovalski Bench Quest Talent" => "Score 20 Goals",
                "Brewster Bench Quest Talent" => "Use Ability 100 Times",
                "Boring Dude Bench Quest Talent" => "Equip Nine Talents",
                _ => null
            },
            _ => null
        };
        if (label != null) return label + upgrade;
        if (effect.Length > 0 && !effect.Contains("[Missing:", StringComparison.OrdinalIgnoreCase))
            return effect.Replace("OVR", "Overall") + upgrade;
        string context = entry.Asset switch
        {
            "cultistPowerC" => "Center", "cultistPowerRW" => "Right Wing", "cultistPowerLW" => "Left Wing",
            "cultistPowerLD" => "Left Defense", "cultistPowerRD" => "Right Defense",
            "defenseTeleport" => "Defense", "Gauntlet Bench Bonus" => "Gauntlet", "Bench Bonus" => "Standard",
            _ => "Effect unavailable"
        };
        return name + upgrade + " - " + context;
    }
    public static Dictionary<string, Entry> ReadEntries(string root, string kind)
    {
        kind = RewardPoolDefinition.CatalogKind(kind);
        var inventory = Inventory(root, kind);
        if (inventory != null) return inventory;
        string file = kind switch { "Talents" => "TALENTS_SKATER.txt", "Goalie Talents" => "TALENTS_GOALIE.txt", "Relics" => "RELICS.txt", "Abilities" => "ABILITIES_SKATER.txt", "Goalie Abilities" => "ABILITIES_GOALIE.txt", _ => throw new ArgumentException("Unknown reward catalog") };
        return ParseEntries(Path.GetFullPath(Path.Combine(root, "T2T_Dumps", "rewards", file)));
    }

    public static Dictionary<string, string> Descriptions(string root, string kind)
    {
        var entries = ReadEntries(root, kind);
        if (Inventory(root, kind) != null) return entries.ToDictionary(p => p.Key, p => Reminder(p.Value.Description), StringComparer.OrdinalIgnoreCase);
        if (!kind.Contains("Abilities")) return entries.ToDictionary(p => p.Key, p => Reminder(p.Value.Description), StringComparer.OrdinalIgnoreCase);
        string map = Path.GetFullPath(Path.Combine(root, "T2T_Dumps", "_gui_data", "_export_id_map.txt"));
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool active = false;
        if (File.Exists(map)) foreach (string line in File.ReadLines(map))
        {
            if (line.StartsWith('[')) { active = line.Trim() == "[abilities]"; continue; }
            var p = line.Split('|', 2);
            if (active && p.Length == 2 && entries.TryGetValue(p[0].Trim(), out var entry)) result[p[1].Trim()] = Reminder(entry.Description);
        }
        return result;
    }

    public static string ResolveKey(string root, string kind, string value)
    {
        var entries = ReadEntries(root, kind);
        if (entries.ContainsKey(value)) return value;
        var matches = entries.Values.Where(e => e.Id.Equals(value, StringComparison.OrdinalIgnoreCase) || e.Asset.Equals(value, StringComparison.OrdinalIgnoreCase) || e.Name.Equals(value, StringComparison.OrdinalIgnoreCase)).ToArray();
        return matches.Length == 1 ? matches[0].Key : value;
    }

    public static string DisplayNames(string root, string kind, string value)
    {
        var entries = ReadEntries(root, kind);
        if (kind is "Talents" or "Goalie Talents")
            foreach (var entry in ReadEntries(root, kind == "Talents" ? "Goalie Talents" : "Talents"))
                entries.TryAdd(entry.Key, entry.Value);
        string Name(string token)
        {
            if (token.Equals("none", StringComparison.OrdinalIgnoreCase)) return "None";
            if (entries.TryGetValue(token, out var entry)) return entry.Name;
            var matches = entries.Values.Where(e => e.Id.Equals(token, StringComparison.OrdinalIgnoreCase)
                || e.Asset.Equals(token, StringComparison.OrdinalIgnoreCase) || e.Name.Equals(token, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length == 1) return matches[0].Name;
            return Guid.TryParse(token, out _) ? "Name unavailable" : token;
        }
        var names = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Name).ToArray();
        return names.Length == 0 ? "None" : string.Join(", ", names);
    }

    public static string Reminder(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "No reminder text provided by the game.";
        text = Regex.Replace(text, @"\[Missing:[^\]]+\]", "[Text missing in the game]", RegexOptions.IgnoreCase);
        if (Regex.IsMatch(text, @"\{\d+\}")) text += "\n(Counter values are available during a match.)";
        return text;
    }

    private static Dictionary<string, string> ReadDump(string file) => ParseEntries(file).ToDictionary(p => p.Key, p => p.Value.Name, StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, Entry> ParseEntries(string file)
    {
        var entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        string? current = null;
        if (File.Exists(file)) foreach (string line in File.ReadLines(file))
        {
            var m = Regex.Match(line, @"^  \[([^\]]+)\]\s+(.+?)(?:\s+\(Lv(\d+)\))?(?:\s+CD=.*)?$");
            if (m.Success)
            {
                current = m.Groups[1].Value;
                string name = m.Groups[2].Value;
                if (name.StartsWith("[missing:", StringComparison.OrdinalIgnoreCase)) name = current + " (name unavailable)";
                entries[current] = new(current, name, "", int.TryParse(m.Groups[3].Value, out int level) ? level : 1);
            }
            else if (current != null && line.StartsWith("    "))
            {
                var entry = entries[current];
                entries[current] = entry with { Description = (entry.Description + "\n" + line.Trim()).Trim() };
            }
            else if (string.IsNullOrWhiteSpace(line)) current = null;
        }
        return entries;
    }
}

