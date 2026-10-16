using CampaignCore;

namespace CampaignCreator;

internal sealed class GameData
{
    public AssetCatalog Assets { get; } = new();
    public Dictionary<string, string> Previews { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Templates { get; } = new();
    public Dictionary<string, string> Talents { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Abilities { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Relics { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, CustomRewardDefinition> CustomRewards { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Sizes { get; } = FieldSchema.Sizes.ToList();
    public string Root { get; }
    public GameData(string root)
    {
        // The runtime dump only contains worn equipment. Merge the full packaged
        // skin inventory, retaining exact identities and home/away aliases.
        using (var stream = typeof(App).Assembly.GetManifestResourceStream("CampaignCreator.EquipmentCatalog.tsv"))
        if (stream != null)
        using (var reader = new StreamReader(stream))
        while (reader.ReadLine() is { } row)
        {
            var p = row.Split('\t');
            if (p.Length < 5 || p[4] == "true" || (p[3].Length == 0 && p[1] != "Logos") || p[0] is not ("skater" or "goalie") || p[2].EndsWith("_iso", StringComparison.OrdinalIgnoreCase) || p[2].EndsWith("_lso", StringComparison.OrdinalIgnoreCase)) continue;
            string field = p[1] == "Logos" ? "Logo Skin" : p[3].Replace("Goalie ", "");
            if (p[0] == "goalie") field = field switch { "Helmet" => "Helmet Skin", "Glove" => "Glove Skin", "Blocker" => "Blocker Skin", "Pads" => "Pads Skin", "Stick" => "Stick Skin", _ => field };
            else field = field switch { "Number" => "Number Skin", "Logo" => "Logo Skin", _ => field };
            if (!EditorEquipmentPolicy.IsSelectable(p[2])) continue;
            Assets.Add(p[0], field, p[2]);
            string? away = field switch {
                "Body" or "Helmet" or "Bicep" or "Gloves" or "Pants" or "Skates" or "Skin" => field + " Away",
                "Glove Skin" => "Glove Away", "Blocker Skin" => "Blocker Away", "Pads Skin" => "Pads Away", "Stick Skin" => "Stick Away", _ => null
            };
            if (away != null) Assets.Add(p[0], away, p[2]);
        }
        // Keep known native goalie preset identities available without a runtime dump.
        // The Spine inventory alone omits some values retained by GoaltenderData.
        foreach (string field in FieldSchema.GoalieAssets)
            foreach (string value in GoalieEquipmentCatalog.Values(field))
                Assets.Add("goalie", field, value);
        Root = root;
        foreach (var custom in CustomHeadCatalog.Load(root))
        {
            if (custom.IsGoalie)
            {
                Assets.Add("goalie", "Helmet Skin", custom.Skin);
                Previews["goalie:Helmet Skin:" + custom.Skin] = custom.Asset(custom.Images.Single(i => i.Group == "Portrait").File);
                continue;
            }
            Assets.Add("skater", "Face", custom.Skin);
            Previews["skater:Face:" + custom.Skin] = custom.Asset(custom.Images.Single(i => i.Group == "Portrait").File);
            if (custom.SupportsHelmet)
            {
                Assets.Add("skater", "Face", custom.Skin + "_Helmet");
                Previews["skater:Face:" + custom.Skin + "_Helmet"] = custom.Asset(custom.Direction("S", true).File);
            }
        }
        string optionsFile = Path.Combine(root, "_editor_options.tsv");
        if (File.Exists(optionsFile))
        {
            var sizes = File.ReadLines(optionsFile).Select(l => l.Split('\t')).Where(p => p.Length == 3 && p[0] == "skater" && p[1] == "Size").Select(p => p[2]).ToList();
            if (sizes.Count > 0) { Sizes.Clear(); Sizes.AddRange(sizes); }
        }
        var skins = Path.Combine(root, "_game_skins.txt");
        if (File.Exists(skins))
        {
            string role = "skater", field = "";
            foreach (var line in File.ReadLines(skins))
            {
                if (line.StartsWith("["))
                {
                    field = line[1..line.IndexOf(']')]; role = field.StartsWith("Goalie ") ? "goalie" : "skater";
                    if (role == "goalie") field = field[7..] switch {
                        "Helmet" => "Helmet Skin", "Glove" => "Glove Skin", "Blocker" => "Blocker Skin",
                        "Pads" => "Pads Skin", "Stick" => "Stick Skin", var f => f };
                    else if (field == "Number") field = "Number Skin";
                    else if (field == "Logo") field = "Logo Skin";
                }
                else if (!line.StartsWith("#") && line.Contains('/') && field.Length > 0 && !(role == "goalie" && field == "Face") && EditorEquipmentPolicy.IsSelectable(line.Trim())) Assets.Add(role, field, line.Trim());
            }
        }
        // Goalie pants are authored separately from the jersey while retaining
        // the same native body-skin choices for each piece.
        foreach (string skin in Assets.Values("goalie", "Skin"))
        {
            Assets.Add("goalie", "Goalie Pants Skin", skin);
            Assets.Add("goalie", "Goalie Pants Away", skin);
        }
        // Skater socks likewise choose from the jersey skins; the plugin swaps in only their leg pieces.
        foreach (string skin in Assets.Values("skater", "Body").ToArray())
        {
            Assets.Add("skater", "Socks", skin);
            Assets.Add("skater", "Socks Away", skin);
        }
        // The full native head catalog, built from the player's install (StudioArt), so previews
        // work for every content folder.
        string headRoot = StudioArt.Path("head-preview") ?? "";
        string headCatalog = Path.Combine(headRoot, "catalog.tsv");
        if (headRoot.Length > 0 && File.Exists(headCatalog)) foreach (string line in File.ReadLines(headCatalog))
        {
            var cells = line.Split('\t');
            if (cells.Length != 3 || cells[0] is not ("skater" or "goalie") || Path.GetFileName(cells[2]) != cells[2]) continue;
            string path = Path.Combine(headRoot, cells[2]);
            if (!File.Exists(path)) continue;
            string field = cells[0] == "goalie" ? "Helmet Skin" : "Face";
            if (!EditorEquipmentPolicy.IsSelectable(cells[1])) continue;
            Assets.Add(cells[0], field, cells[1]);
            Previews[cells[0] + ":" + field + ":" + cells[1]] = path;
        }
        string squadCatalog = Path.Combine(root, "_squad_templates.tsv");
        if (File.Exists(squadCatalog)) foreach (var line in File.ReadLines(squadCatalog))
        {
            var cells = line.Split('\t');
            if (cells.Length == 4 && cells[0] != "id" && !cells[0].StartsWith("#")) Templates.Add(cells[1]);
        }
        string maps = Path.Combine(root, "_game_maps.txt");
        if (Templates.Count == 0 && File.Exists(maps)) foreach (var line in File.ReadLines(maps))
            if (line.StartsWith("SQUAD '") && line.IndexOf("' maps=", StringComparison.Ordinal) > 7)
            {
                var name = line[7..line.IndexOf("' maps=", StringComparison.Ordinal)];
                if (!Templates.Contains(name)) Templates.Add(name);
            }
        string ids = Path.GetFullPath(Path.Combine(root, "T2T_Dumps", "_gui_data", "_export_id_map.txt"));
        if (File.Exists(ids))
        {
            Dictionary<string, string>? target = null;
            foreach (var raw in File.ReadLines(ids))
            {
                string line = raw.Trim();
                if (line.StartsWith("[")) target = line switch { "[talents]" => Talents, "[abilities]" => Abilities, "[relics]" => Relics, _ => null };
                else if (target != null && line.Contains('|')) { var p = line.Split('|', 2); target[p[0].Trim()] = p[1].Trim(); }
            }
        }
        foreach (string kind in new[] { "Talents", "Goalie Talents", "Abilities", "Goalie Abilities", "Relics" })
        {
            var target = kind == "Relics" ? Relics : kind.Contains("Abilities") ? Abilities : Talents;
            foreach (var entry in RewardCatalog.ReadEntries(root, kind).Values.Where(e => e.Id.Length > 0))
            {
                target[entry.Key] = entry.Id;
                if (entry.Asset.Length > 0) target.TryAdd(entry.Asset, entry.Id);
                if (entry.Name.Length > 0) target.TryAdd(entry.Name, entry.Id);
            }
        }
        LoadCustomRewards(null);
    }
    internal void LoadCustomRewards(string? sourceFile)
    {
        foreach (var pair in Talents.Where(p => CustomRewards.ContainsKey(p.Value)).ToArray()) Talents.Remove(pair.Key);
        foreach (var pair in Relics.Where(p => CustomRewards.ContainsKey(p.Value)).ToArray()) Relics.Remove(pair.Key);
        CustomRewards.Clear();
        foreach (var item in CustomRewardDefinition.LoadFolder(Path.Combine(Root, "library", "rewards"))) CustomRewards[item.Id] = item;
        var embedded = new Dictionary<string, CustomRewardDefinition>(StringComparer.OrdinalIgnoreCase);
        var authored = CustomRewardCatalog.CampaignFor(sourceFile) is { } campaign
            ? CustomRewardDefinition.LoadFolder(Path.Combine(campaign, CustomRewardDefinition.Folder)).ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, CustomRewardDefinition>(StringComparer.OrdinalIgnoreCase);
        void Embedded(string file) {
            if (File.Exists(file)) foreach (var item in CustomRewardExport.Read(new CampaignDocument(File.ReadAllText(file))))
                if (!authored.ContainsKey(item.Id)) CustomRewardExport.AddSnapshot(embedded, item);
        }
        if (sourceFile != null) {
            var parent = Directory.GetParent(Path.GetDirectoryName(Path.GetFullPath(sourceFile))!);
            string teamFile = parent == null ? "" : Path.Combine(parent.FullName, "team.txt");
            Embedded(teamFile);
            string players = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(sourceFile))!, "players");
            if (Path.GetFileName(sourceFile).Equals("team.txt", StringComparison.OrdinalIgnoreCase) && Directory.Exists(players))
                foreach (string player in Directory.EnumerateFiles(players, "*.txt", SearchOption.AllDirectories)) Embedded(player);
            Embedded(sourceFile);
        }
        foreach (var item in embedded.Values) CustomRewards[item.Id] = item;
        // Explicit campaign edits own that campaign's definitions. Portable
        // snapshots only fill gaps and override the unrelated global library.
        foreach (var item in authored.Values) CustomRewards[item.Id] = item;
        foreach (var item in CustomRewards.Values.Where(d => !d.NeedsRedesign)) {
            var target = item.IsRelic ? Relics : Talents;
            target[item.Id] = item.Id;
            // IDs are the authoritative selection; duplicate display names never
            // silently replace native or other custom identities.
            target.TryAdd(item.Name, item.Id);
        }
    }
    internal void ValidateCustomLoadout(CampaignDocument document, bool? goalie = null)
        => CustomRewardAssignments.Validate(document, CustomRewards, (value, relics) => {
            var expected = relics ? Relics : Talents;
            var other = relics ? Talents : Relics;
            return expected.TryGetValue(value, out var id) || other.TryGetValue(value, out id) ? id : value;
        }, goalie);

    internal string InheritedTalents(string values, bool goalie) => string.Join(",", values.Split(',', StringSplitOptions.RemoveEmptyEntries)
        .Where(value => {
            string id = Talents.TryGetValue(value.Trim(), out var resolved) ? resolved : value.Trim();
            return !CustomRewards.TryGetValue(id, out var item) || CustomRewardAssignments.FitsPlayer(item, goalie);
        }));
    public string? HeadPreview(string role, string value)
    {
        string field = role == "goalie" ? "Helmet Skin" : "Face";
        if (role == "goalie" && (string.IsNullOrWhiteSpace(value) || value.Equals("standard", StringComparison.OrdinalIgnoreCase))) value = "Helmet/Helmet_Customization_colors";
        try { value = Assets.Resolve(role, field, value); }
        catch (ArgumentException) { return null; }
        return Previews.TryGetValue(role + ":" + field + ":" + value, out var path) && File.Exists(path) ? path : null;
    }
}

