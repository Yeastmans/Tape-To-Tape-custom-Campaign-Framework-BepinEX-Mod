using CampaignCore;

namespace CampaignCreator;

internal sealed class GameData
{
    private sealed class RewardIds
    {
        public readonly Dictionary<string, string> Talents = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string> Abilities = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string> Relics = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record HeadImage(string Role, string Field, string Skin, string Path);
    private readonly Lazy<AssetCatalog> equipment;
    private readonly List<(string Role, string Field, string Skin)> dumpedHeads = [];
    private readonly Lazy<Dictionary<string, HeadImage>> nativeHeadImages;
    private readonly Lazy<List<CustomHeadDefinition>> heads;
    private readonly Lazy<Dictionary<string, string>> previews;
    private readonly Dictionary<string, CustomHeadDefinition?> describedHeads = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CustomHeadDefinition> selectedHeads = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lazy<List<string>> templates, sizes;
    private readonly Lazy<RewardIds> nativeRewards;
    private readonly Dictionary<string, CustomRewardDefinition> customRewards = new(StringComparer.OrdinalIgnoreCase);
    private Lazy<Dictionary<string, CustomRewardDefinition>> scopedRewards;
    public string Root { get; }
    public List<string> Templates => templates.Value;
    public List<string> Sizes => sizes.Value;
    public Dictionary<string, string> Abilities => nativeRewards.Value.Abilities;
    public Dictionary<string, CustomRewardDefinition> CustomRewards => scopedRewards.Value;
    public Dictionary<string, string> Talents { get { _ = CustomRewards; return nativeRewards.Value.Talents; } }
    public Dictionary<string, string> Relics { get { _ = CustomRewards; return nativeRewards.Value.Relics; } }
    public Dictionary<string, string> Previews => previews.Value;
    public AssetCatalog Assets { get { _ = Previews; return equipment.Value; } }

    public GameData(string root)
    {
        Root = root;
        equipment = new(() => ReadEquipment(root));
        nativeHeadImages = new(ReadNativeHeadImages);
        heads = new(() => CustomHeadCatalog.ReadChoices(root));
        previews = new(ReadHeadChoices);
        templates = new(() => ReadTemplates(root));
        sizes = new(() => ReadSizes(root));
        nativeRewards = new(() => ReadRewardIds(root));
        scopedRewards = new(() => ReadCustomRewards(null));
    }

    private static bool IsHeadField(string role, string field) =>
        role.Equals("skater", StringComparison.OrdinalIgnoreCase) && field.Equals("Face", StringComparison.OrdinalIgnoreCase)
        || role.Equals("goalie", StringComparison.OrdinalIgnoreCase) && field.Equals("Helmet Skin", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<string> AssetChoices(string role, string field) =>
        (IsHeadField(role, field) ? Assets : equipment.Value).Values(role, field);

    public IEnumerable<string> HeadChoices(string selected = "") =>
        HeadOptionVisibility.Choices(Root, AssetChoices("skater", "Face"), selected);

    private AssetCatalog ReadEquipment(string root)
    {
        var catalog = new AssetCatalog();
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
            catalog.Add(p[0], field, p[2]);
            string? away = field switch {
                "Body" or "Helmet" or "Bicep" or "Gloves" or "Pants" or "Skates" or "Skin" => field + " Away",
                "Glove Skin" => "Glove Away", "Blocker Skin" => "Blocker Away", "Pads Skin" => "Pads Away", "Stick Skin" => "Stick Away", _ => null
            };
            if (away != null) catalog.Add(p[0], away, p[2]);
        }
        // Keep known native goalie preset identities available without a runtime dump.
        // The Spine inventory alone omits some values retained by GoaltenderData.
        foreach (string field in FieldSchema.GoalieAssets)
            foreach (string value in GoalieEquipmentCatalog.Values(field))
                catalog.Add("goalie", field, value);
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
                else if (!line.StartsWith("#") && line.Contains('/') && field.Length > 0 && !(role == "goalie" && field == "Face") && EditorEquipmentPolicy.IsSelectable(line.Trim())) {
                    if (IsHeadField(role, field)) dumpedHeads.Add((role, field, line.Trim()));
                    else catalog.Add(role, field, line.Trim());
                }
            }
        }
        // Goalie pants are authored separately from the jersey while retaining
        // the same native body-skin choices for each piece.
        foreach (string skin in catalog.Values("goalie", "Skin"))
        {
            catalog.Add("goalie", "Goalie Pants Skin", skin);
            catalog.Add("goalie", "Goalie Pants Away", skin);
        }
        // Skater socks likewise choose from the jersey skins; the plugin swaps in only their leg pieces.
        foreach (string skin in catalog.Values("skater", "Body").ToArray())
        {
            catalog.Add("skater", "Socks", skin);
            catalog.Add("skater", "Socks Away", skin);
        }
        return catalog;
    }

    private Dictionary<string, HeadImage> ReadNativeHeadImages()
    {
        var images = new Dictionary<string, HeadImage>(StringComparer.OrdinalIgnoreCase);
        // The full native head catalog, built from the player's install (StudioArt), so previews
        // work for every content folder.
        string headRoot = StudioArt.Path("head-preview") ?? "";
        string headCatalog = Path.Combine(headRoot, "catalog.tsv");
        if (headRoot.Length > 0 && File.Exists(headCatalog)) foreach (string line in File.ReadLines(headCatalog))
        {
            var cells = line.Split('\t');
            if (cells.Length != 3 || cells[0] is not ("skater" or "goalie") || Path.GetFileName(cells[2]) != cells[2]) continue;
            string path = Path.Combine(headRoot, cells[2]);
            string field = cells[0] == "goalie" ? "Helmet Skin" : "Face";
            if (!EditorEquipmentPolicy.IsSelectable(cells[1])) continue;
            images[cells[0] + ":" + field + ":" + cells[1]] = new(cells[0], field, cells[1], path);
        }
        return images;
    }

    private Dictionary<string, string> ReadHeadChoices()
    {
        var catalog = equipment.Value;
        var images = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var custom in heads.Value)
        {
            if (custom.IsGoalie)
            {
                catalog.Add("goalie", "Helmet Skin", custom.Skin);
                images["goalie:Helmet Skin:" + custom.Skin] = custom.Asset(custom.Images.Single(i => i.Group == "Portrait").File);
                continue;
            }
            catalog.Add("skater", "Face", custom.Skin);
            images["skater:Face:" + custom.Skin] = custom.Asset(custom.Images.Single(i => i.Group == "Portrait").File);
            if (custom.SupportsHelmet)
            {
                catalog.Add("skater", "Face", custom.Skin + "_Helmet");
                images["skater:Face:" + custom.Skin + "_Helmet"] = custom.Asset(custom.Direction("S", true).File);
            }
        }
        foreach (var (role, field, skin) in dumpedHeads) catalog.Add(role, field, skin);
        foreach (var (key, image) in nativeHeadImages.Value) {
            if (!File.Exists(image.Path)) continue;
            catalog.Add(image.Role, image.Field, image.Skin);
            images[key] = image.Path;
        }
        return images;
    }

    private static List<string> ReadSizes(string root)
    {
        var sizes = FieldSchema.Sizes.ToList();
        string optionsFile = Path.Combine(root, "_editor_options.tsv");
        if (File.Exists(optionsFile))
        {
            var installed = File.ReadLines(optionsFile).Select(l => l.Split('\t')).Where(p => p.Length == 3 && p[0] == "skater" && p[1] == "Size").Select(p => p[2]).ToList();
            if (installed.Count > 0) return installed;
        }
        return sizes;
    }

    private static List<string> ReadTemplates(string root)
    {
        var choices = new List<string>();
        string squadCatalog = Path.Combine(root, "_squad_templates.tsv");
        if (File.Exists(squadCatalog)) foreach (var line in File.ReadLines(squadCatalog))
        {
            var cells = line.Split('\t');
            if (cells.Length == 4 && cells[0] != "id" && !cells[0].StartsWith("#")) choices.Add(cells[1]);
        }
        string maps = Path.Combine(root, "_game_maps.txt");
        if (choices.Count == 0 && File.Exists(maps)) foreach (var line in File.ReadLines(maps))
            if (line.StartsWith("SQUAD '") && line.IndexOf("' maps=", StringComparison.Ordinal) > 7)
            {
                var name = line[7..line.IndexOf("' maps=", StringComparison.Ordinal)];
                if (!choices.Contains(name)) choices.Add(name);
            }
        return choices;
    }

    private static RewardIds ReadRewardIds(string root)
    {
        var idsByKind = new RewardIds();
        string ids = Path.GetFullPath(Path.Combine(root, "T2T_Dumps", "_gui_data", "_export_id_map.txt"));
        if (File.Exists(ids))
        {
            Dictionary<string, string>? target = null;
            foreach (var raw in File.ReadLines(ids))
            {
                string line = raw.Trim();
                if (line.StartsWith("[")) target = line switch { "[talents]" => idsByKind.Talents, "[abilities]" => idsByKind.Abilities, "[relics]" => idsByKind.Relics, _ => null };
                else if (target != null && line.Contains('|')) { var p = line.Split('|', 2); target[p[0].Trim()] = p[1].Trim(); }
            }
        }
        foreach (string kind in new[] { "Talents", "Goalie Talents", "Abilities", "Goalie Abilities", "Relics" })
        {
            var target = kind == "Relics" ? idsByKind.Relics : kind.Contains("Abilities") ? idsByKind.Abilities : idsByKind.Talents;
            foreach (var entry in RewardCatalog.ReadEntries(root, kind).Values.Where(e => e.Id.Length > 0))
            {
                target[entry.Key] = entry.Id;
                if (entry.Asset.Length > 0) target.TryAdd(entry.Asset, entry.Id);
                if (entry.Name.Length > 0) target.TryAdd(entry.Name, entry.Id);
            }
        }
        return idsByKind;
    }

    internal void LoadCustomRewards(string? sourceFile)
    {
        scopedRewards = new(() => ReadCustomRewards(sourceFile));
    }

    private Dictionary<string, CustomRewardDefinition> ReadCustomRewards(string? sourceFile)
    {
        var native = nativeRewards.Value;
        foreach (var pair in native.Talents.Where(p => customRewards.ContainsKey(p.Value)).ToArray()) native.Talents.Remove(pair.Key);
        foreach (var pair in native.Relics.Where(p => customRewards.ContainsKey(p.Value)).ToArray()) native.Relics.Remove(pair.Key);
        customRewards.Clear();
        foreach (var item in CustomRewardDefinition.LoadFolder(Path.Combine(Root, "library", "rewards"))) customRewards[item.Id] = item;
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
        foreach (var item in embedded.Values) customRewards[item.Id] = item;
        // Explicit campaign edits own that campaign's definitions. Portable
        // snapshots only fill gaps and override the unrelated global library.
        foreach (var item in authored.Values) customRewards[item.Id] = item;
        foreach (var item in customRewards.Values.Where(d => !d.NeedsRedesign)) {
            var target = item.IsRelic ? native.Relics : native.Talents;
            target[item.Id] = item.Id;
            // IDs are the authoritative selection; duplicate display names never
            // silently replace native or other custom identities.
            target.TryAdd(item.Name, item.Id);
        }
        return customRewards;
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

    internal string DisplayTalentNames(string value, bool goalie)
    {
        string Name(string token)
        {
            // LoadCustomRewards already chose campaign definitions and portable
            // snapshots over the library. Native catalogs must stay separate.
            string id = Talents.TryGetValue(token, out var resolved) ? resolved : token;
            if (CustomRewards.TryGetValue(id, out var item) && !item.IsRelic) return item.Name;
            return RewardCatalog.DisplayNames(Root, goalie ? "Goalie Talents" : "Talents", token);
        }
        var names = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Name).ToArray();
        return names.Length == 0 ? "None" : string.Join(", ", names);
    }
    private static string HeadPrefix(string role) => role == "goalie" ? "Helmet/CustomMasks/" : "Faces/CustomHeads/";

    private CustomHeadDefinition? FindHead(string role, string skin)
    {
        string prefix = HeadPrefix(role);
        if (!skin.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        string name = skin[prefix.Length..];
        if (role != "goalie" && name.EndsWith("_Helmet", StringComparison.OrdinalIgnoreCase)) name = name[..^7];
        if (name.Length == 0 || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || name.Contains('/') || name.Contains('\\')) return null;
        string key = prefix + name;
        if (describedHeads.TryGetValue(key, out var cached)) return cached;
        string folder = Path.Combine(Root, "custom-heads", name);
        CustomHeadDefinition? head = null;
        if (!heads.IsValueCreated && File.Exists(Path.Combine(folder, "head-project.json"))) {
            try {
                var candidate = CustomHeadDefinition.ReadForCatalog(folder);
                if (key.Equals(candidate.Skin, StringComparison.OrdinalIgnoreCase)) head = candidate;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { }
        }
        // Imported older projects may have a folder name different from their declared skin.
        head ??= heads.Value.FirstOrDefault(h => key.Equals(h.Skin, StringComparison.OrdinalIgnoreCase));
        return describedHeads[key] = head;
    }

    private CustomHeadDefinition? SelectedHead(string role, string skin)
    {
        if (FindHead(role, skin) is not { } entry) return null;
        if (selectedHeads.TryGetValue(entry.Skin, out var selected)) return selected;
        try {
            selected = CustomHeadDefinition.Read(entry.Folder);
            if (!selected.IsGoalie) HeadAppearance.RegisterCustom(selected.Skin, selected.SupportsHelmet);
            selectedHeads[entry.Skin] = selected;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
        return selected;
    }

    internal bool CanSelectHead(string role, string value) =>
        !value.StartsWith(HeadPrefix(role), StringComparison.OrdinalIgnoreCase) || SelectedHead(role, value) != null;

    internal void PrepareSkaterHead(string? skin)
    {
        if (string.IsNullOrWhiteSpace(skin)) return;
        if (!skin.Contains('/')) { _ = heads.Value; return; }
        _ = SelectedHead("skater", skin);
    }

    public string ResolveAsset(string role, string field, string value)
    {
        if (!IsHeadField(role, field)) return equipment.Value.Resolve(role, field, value);
        if (value.StartsWith(HeadPrefix(role), StringComparison.OrdinalIgnoreCase)) {
            if (SelectedHead(role, value) is { } custom) {
                if (value.Equals(custom.Skin, StringComparison.OrdinalIgnoreCase)) return custom.Skin;
                if (custom.SupportsHelmet && value.Equals(custom.Skin + "_Helmet", StringComparison.OrdinalIgnoreCase)) return custom.Skin + "_Helmet";
            }
            throw new ArgumentException($"Unavailable custom head '{value}'.");
        }
        if (value.Contains('/') && nativeHeadImages.Value.TryGetValue(role + ":" + field + ":" + value, out var image)
            && File.Exists(image.Path)) return image.Skin;
        if (value.Contains('/')) {
            var catalog = equipment.Value;
            foreach (var head in dumpedHeads)
                if (head.Role == role && head.Field == field && head.Skin.Equals(value, StringComparison.OrdinalIgnoreCase)) return head.Skin;
            return catalog.Resolve(role, field, value);
        }
        string resolved = Assets.Resolve(role, field, value);
        return resolved.StartsWith(HeadPrefix(role), StringComparison.OrdinalIgnoreCase) ? ResolveAsset(role, field, resolved) : resolved;
    }

    public string? HeadPreview(string role, string value)
    {
        string field = role == "goalie" ? "Helmet Skin" : "Face";
        if (role == "skater" && string.IsNullOrWhiteSpace(value)) return null;
        if (role == "skater") value = HeadAppearance.NativeSkin(value);
        if (role == "goalie" && (string.IsNullOrWhiteSpace(value) || value.Equals("standard", StringComparison.OrdinalIgnoreCase))) value = "Helmet/Helmet_Customization_colors";
        if (FindHead(role, value) is { } custom) {
            var picture = value.Equals(custom.Skin, StringComparison.OrdinalIgnoreCase) ? custom.Images.Single(i => i.Group == "Portrait")
                : custom.SupportsHelmet ? custom.Direction("S", true) : null;
            if (picture == null) return null;
            string file = custom.Asset(picture.File);
            try { CustomHeadDefinition.ValidatePng(file); return file; }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { return null; }
        }
        string key = role + ":" + field + ":";
        if (nativeHeadImages.Value.TryGetValue(key + value, out var image) && File.Exists(image.Path)) return image.Path;
        if (value.Contains('/')) return null;
        try { value = Assets.Resolve(role, field, value); }
        catch (ArgumentException) { return null; }
        return Previews.TryGetValue(key + value, out var path) && File.Exists(path) ? path : null;
    }
}
