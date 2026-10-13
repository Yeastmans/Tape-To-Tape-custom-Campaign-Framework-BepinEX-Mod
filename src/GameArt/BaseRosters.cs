using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace GameArt;

/// <summary>
/// The base-game roster snapshot the Studio imports from: one campaign-format folder per native team
/// (team.txt, goalie-equipment.cfg, players/&lt;Position&gt; - &lt;Name&gt;.txt) and one loose file per distinct
/// native player (Base Game Players/&lt;Name&gt;.txt), decoded from the game's own TeamData, ForwardData and
/// GoaltenderData assets. Nothing comes from a runtime dump, so campaign edits cannot leak in.
/// Port of tools/extract_native_rosters.py + tools/build_base_rosters.py; the files are byte-for-byte what
/// those scripts wrote on Windows (UTF-8, CRLF, Python number formatting).
/// </summary>
public sealed partial class BaseRosters : IArtExtractor
{
    public string Folder => "base-rosters";
    public string Description => "Base game rosters";

    private static readonly string[] Kinds = ["TeamData", "ForwardData", "GoaltenderData"];
    private static readonly string[] Sizes = ["ExtraSmall", "Small", "Medium", "Big", "ExtraBig", "ExtraExtraBig"]; // FieldSchema.Sizes
    private static readonly string[] BaseSlots = ["Left Wing", "Right Wing", "Center", "Left Defense", "Right Defense"]; // mod slot order
    private static readonly string[] Slots = [.. BaseSlots, .. BaseSlots.Select(s => "Line 2 " + s)];
    // Placeholders the game fills at runtime; not importable teams.
    private static readonly HashSet<string> SkipTeams = new(StringComparer.Ordinal) { "RandomHome", "RandomVisitor", "defaultTeamForCustomization" };
    // Same display name, different native team: label the variants.
    private static readonly Dictionary<string, string> Variants = new(StringComparer.Ordinal)
    {
        ["CHICAO_CAMPAIGN_ACT1"] = "Campaign Act 1", ["chicago calaveras onboarding"] = "Onboarding", ["referee Squad Team"] = "Squad",
    };
    private static readonly (string Key, string Field)[] SkaterSkins =
    [
        ("Face", "headSkin"), ("Stick", "stickSkin"), ("Helmet", "helmetSkin"), ("Helmet Away", "helmetAwaySkin"),
        ("Body", "bodySkin"), ("Body Away", "bodyAwaySkin"), ("Bicep", "bicepSkin"), ("Bicep Away", "bicepAwaySkin"),
        ("Gloves", "gloveSkin"), ("Gloves Away", "gloveAwaySkin"), ("Pants", "pantsSkin"), ("Pants Away", "pantsAwaySkin"),
        ("Skates", "skateSkin"), ("Skates Away", "skateAwaySkin"), ("Glasses", "glassesSkin"),
        ("Number Skin", "numberSkin"), ("Logo Skin", "logoSkin"),
    ];
    private static readonly (string Key, string Field)[] Uniform =
        SkaterSkins.Where(s => s.Key is not ("Face" or "Glasses" or "Number Skin" or "Logo Skin")).ToArray();
    private static readonly (string Key, string Field)[] GoalieSkins =
    [
        ("Helmet Skin", "helmetSkin"), ("Skin", "skin"), ("Skin Away", "awaySkin"), ("Glove Skin", "gloveSkin"),
        ("Glove Away", "awayGloveSkin"), ("Blocker Skin", "blockerSkin"), ("Blocker Away", "awayBlockerSkin"),
        ("Pads Skin", "padsSkin"), ("Pads Away", "awayPadsSkin"), ("Stick Skin", "stickSkin"), ("Stick Away", "awayStickSkin"),
    ];
    private static readonly (string Scheme, string Piece)[] Pieces =
        [("helmetScheme", "Helmet"), ("glovesScheme", "Gloves"), ("pantsScheme", "Pants"), ("socksScheme", "Socks"), ("stickScheme", "Stick"), ("numberScheme", "Number")];
    private static readonly string[] Channels = ["primaryColor", "secondaryColor", "tertiaryColor"];
    private const string Source = "Decoded from shipped game data (TeamData, ForwardData, GoaltenderData); no runtime dump";

    /// <summary>A decoded TeamData / ForwardData / GoaltenderData asset ("file:pathId" key, like the Python records).</summary>
    private sealed record Native(string Key, string Class, AssetsFileInstance File, AssetTypeValueField Value)
    {
        public AssetTypeValueField this[string field] => Field(Value, field);
        public string Name => this["m_Name"].AsString;
    }

    public void Extract(UnityGame game, string output, IProgress<string>? progress)
        => Extract(game, output, progress, false);

    internal void ExtractCampaigns(UnityGame game, string output, IProgress<string>? progress)
        => Extract(game, output, progress, true);

    private void Extract(UnityGame game, string output, IProgress<string>? progress, bool campaigns)
    {
        var reader = new BehaviourReader(game);
        var records = new Dictionary<string, Native>(StringComparer.Ordinal);
        var order = new List<Native>();
        foreach (var (file, info, script) in reader.All())
        {
            if (!Kinds.Contains(script.Class)) continue;
            var native = new Native(Key(file.name, info.PathId), script.Class, file, reader.Read(file, info, script));
            records.Add(native.Key, native);
            order.Add(native);
        }
        if (!order.Any(r => r.Class == "TeamData")) throw new InvalidDataException("No TeamData found in the game files.");

        // Talents, abilities, bench powers and relics are separate assets: only their ids are written.
        var refIds = new Dictionary<string, string?>(StringComparer.Ordinal);
        string? RefId(string key)
        {
            if (refIds.TryGetValue(key, out var cached)) return cached;
            return refIds[key] = reader.Find(key) is { } target ? reader.ReadId(target.File, target.Info, target.Script) : null;
        }
        string Ids(Native owner, IEnumerable<AssetTypeValueField> pointers) => string.Join(", ",
            pointers.Select(p => reader.Ref(owner.File, p)).Where(k => k != null).Select(k => RefId(k!)).Where(id => id != null));
        Native Record(Native owner, AssetTypeValueField pointer)
        {
            string key = reader.Ref(owner.File, pointer)!;
            return records.TryGetValue(key, out var record) ? record
                : throw new InvalidDataException($"{owner.Name} refers to {key}, which is not a decoded player.");
        }

        List<(string, string)> SkaterFields(Native p, string position, bool overrides, Dictionary<string, string> colors)
        {
            var size = Number(p["skaterSize"]);
            var f = new List<(string, string)>
            {
                ("Name", NameOf(p)), ("Number", Scalar(p["number"])), ("Left Handed", Truthy(p["isLefty"]) ? "yes" : "no"),
                ("Number Style", CampaignCore.FieldSchema.NumberStyles[Number(p["numberFormat"])]),
                ("Skin Color", Truthy(p["isBlack"]) ? "dark" : "light"), ("Size", Sizes[size < 0 ? size + Sizes.Length : size]),
                ("Speed", Scalar(p["speed"])), ("Shot Power", Scalar(p["shotPower"])), ("Accuracy", Scalar(p["shotAccuracy"])),
                ("Checking", Scalar(p["checking"])),
                ("Ability", reader.Ref(p.File, p["ability"]) != null ? Ids(p, [p["ability"]]) : ""),
                ("Talents", Ids(p, Items(p["powerups"]))),
            };
            if (position.Length > 0) f.Add(("Position", position));
            f.Add(("Override Team Settings", overrides ? "yes" : "no"));
            f.AddRange(SkaterSkins.Select(s => (s.Key, BaseRosterHelmet.Export(s.Key, p[s.Field].AsString))));
            var offset = p.Value["sizeOffsetPercentage"];
            if (!offset.IsDummy && Math.Abs(Float(offset) - 1.0) > 1e-6) f.Add(("Size Offset", Scalar(offset)));
            f.AddRange(Sorted(colors));
            return f;
        }
        List<(string, string)> GoalieFields(Native g, bool overrides, Dictionary<string, string> colors)
        {
            var f = new List<(string, string)>
            {
                ("Name", NameOf(g)), ("Number", Scalar(g["number"])), ("Position", "Goalie"),
                ("Number Style", CampaignCore.FieldSchema.NumberStyles[Number(g["numberFormat"])]),
                ("Skill", Scalar(g["skill"])), ("Catching", Scalar(g["catchingSkill"])), ("Glove", Scalar(g["gloveSkill"])),
                ("Blocker", Scalar(g["blockerSkill"])), ("Five Hole", Scalar(g["fiveHoleSkill"])), ("Standing Speed", Scalar(g["standingSpeed"])),
                ("Butterfly Speed", Scalar(g["butterflySpeed"])), ("Control", Scalar(g["controlSkill"])), ("Recovery", Scalar(g["recoverySkill"])),
                ("Pass Power", Scalar(g["passPower"])), ("Shot Power", Scalar(g["shotPower"])), ("Poke Check", Scalar(g["pokecheckSkill"])),
                ("Depth", Scalar(g["depth"])), ("Pass Read", Scalar(g["passReadSkill"])), ("Goalie Talents", Ids(g, Items(g["powerups"]))),
                ("Override Team Settings", overrides ? "yes" : "no"),
            };
            f.AddRange(GoalieSkins.Select(s => (s.Key, g[s.Field].AsString)));
            f.Add(("Logo Skin", g["logoSkin"].AsString));
            f.AddRange(Sorted(colors));
            return f;
        }

        var teams = order.Where(r => r.Class == "TeamData" && !SkipTeams.Contains(r.Name)).ToList();
        var names = teams.GroupBy(t => t["teamName"].AsString, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var labels = new List<string>();
        var teamFolders = new Dictionary<string, string>(StringComparer.Ordinal);
        var beyond = new List<string>();
        var homeTeam = new Dictionary<string, Native>(StringComparer.Ordinal);
        foreach (var t in teams.OrderBy(t => t["teamName"].AsString, StringComparer.Ordinal).ThenBy(t => t.Name, StringComparer.Ordinal))
        {
            string teamName = t["teamName"].AsString;
            string label = names[teamName] == 1 || !Variants.TryGetValue(t.Name, out var variant) ? teamName : $"{teamName} ({variant})";
            if (labels.Contains(label)) throw new InvalidDataException($"Two native teams would share the folder {label} ({t.Name}).");
            string folder = Path.Combine(output, "Base Game Teams", Safe(label));
            teamFolders[t.Key] = Path.GetRelativePath(output, folder).Replace('\\', '/');
            var forwards = Items(t["forwards"]).Select((pointer, i) => (i, pointer))
                .Where(x => reader.Ref(t.File, x.pointer) != null).Select(x => (x.i, p: Record(t, x.pointer))).ToList();
            foreach (var (_, p) in forwards) homeTeam.TryAdd(p.Key, t);
            Native? goalie = reader.Ref(t.File, t["goalie"]) != null ? Record(t, t["goalie"]) : null;
            if (goalie != null) homeTeam.TryAdd(goalie.Key, t);
            var uniform = forwards.Count == 0 ? [] : Uniform.Select(u => (u.Key, Value: MostCommon(forwards.Select(x => x.p[u.Field].AsString)))).ToList();
            var h = t["homeColors"]; var a = t["awayColors"];
            var fields = new List<(string, string)>
            {
                ("Team Name", label), ("City", t["city"].AsString), ("Abbreviation", t["nickname"].AsString), ("Logo From", teamName),
                ("Jersey Primary", Rgb(Field(h, "jerseyScheme", "primaryColor"))), ("Jersey Secondary", Rgb(Field(h, "jerseyScheme", "secondaryColor"))),
                ("Jersey Accent", Rgb(Field(h, "jerseyScheme", "tertiaryColor"))), ("Away Primary", Rgb(Field(a, "jerseyScheme", "primaryColor"))),
                ("Away Secondary", Rgb(Field(a, "jerseyScheme", "secondaryColor"))), ("Away Accent", Rgb(Field(a, "jerseyScheme", "tertiaryColor"))),
            };
            foreach (var (kit, aw) in new[] { (h, ""), (a, " Away") })
            {
                foreach (var (scheme, piece) in Pieces)
                {
                    fields.Add((piece + aw + " Color", Rgb(Field(kit, scheme, "primaryColor"))));
                    fields.Add((piece + aw + " Secondary Color", Rgb(Field(kit, scheme, "secondaryColor"))));
                    if (piece != "Number") fields.Add((piece + aw + " Tertiary Color", Rgb(Field(kit, scheme, "tertiaryColor"))));
                }
                fields.Add(("Skates" + aw + " Color", Rgb(Field(kit, "skatesScheme", "primaryColor"))));
                fields.Add(("Blade" + aw + " Color", Rgb(Field(kit, "skatesScheme", "secondaryColor"))));
                fields.Add(("Laces" + aw + " Color", Rgb(Field(kit, "skatesScheme", "tertiaryColor"))));
            }
            fields.Add(("Number Color Home", Rgb(t["jerseyHomeNumberColor"]))); fields.Add(("Number Color Away", Rgb(t["jerseyAwayNumberColor"])));
            fields.Add(("Transition Primary", Rgb(t["primaryColorTransition"]))); fields.Add(("Transition Secondary", Rgb(t["secondaryColorTransition"])));
            fields.Add(("Transition Tertiary", Rgb(t["tertiaryColorTransition"])));
            fields.AddRange(uniform.Select(u => (u.Key, BaseRosterHelmet.Export(u.Key, u.Value))));
            fields.Add(("Bench Size", Scalar(t["benchSize"]))); fields.Add(("Bench Head", t["vanillaBenchPlayerHead"].AsString));
            fields.Add(("Team Relics", Ids(t, Items(t["relics"]))));
            if(campaigns)fields.AddRange(NativeOpponentFields(t,reader,Ids(t,Items(t["relics"]))));
            Write(Path.Combine(folder, "team.txt"), $"# Base game team: {teamName} (native {t.Name})", fields);
            var colors = Palette(h, a, t["jerseyHomeNumberColor"], t["jerseyAwayNumberColor"]);
            foreach (var (i, p) in forwards)
            {
                if (i >= Slots.Length) { beyond.Add($"{label}: {NameOf(p)}"); continue; }
                bool overrides = Uniform.Any(u => p[u.Field].AsString != uniform.First(x => x.Key == u.Key).Value);
                Write(Path.Combine(folder, "players", $"{Slots[i]} - {Safe(NameOf(p))}.txt"), $"# {label} / {Slots[i]} (native {p.Name})",
                    SkaterFields(p, Slots[i], overrides, colors));
            }
            if (goalie != null)
            {
                var goalieColors = Palette(t["homeGoalieColors"], t["awayGoalieColors"]);
                Write(Path.Combine(folder, "goalie-equipment.cfg"), $"# Goalie equipment: {label}", GoalieSkins.Select(s => (s.Key, goalie[s.Field].AsString)).ToList());
                Write(Path.Combine(folder, "players", $"Goalie - {Safe(NameOf(goalie))}.txt"), $"# {label} / Goalie (native {goalie.Name})",
                    GoalieFields(goalie, false, goalieColors));
            }
            labels.Add(label);
        }

        // One loose file per distinct player: the copy used by the most teams wins.
        var usage = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var t in teams)
            foreach (var pointer in Items(t["forwards"]).Append(t["goalie"]))
                if (reader.Ref(t.File, pointer) is { } key) usage[key] = usage.GetValueOrDefault(key) + 1;
        var byName = new Dictionary<string, List<Native>>(StringComparer.Ordinal);
        foreach (var p in order.Where(r => r.Class != "TeamData"))
        {
            string name = NameOf(p);
            if (name.Length == 0) continue;
            if (!byName.TryGetValue(name, out var group)) byName[name] = group = [];
            group.Add(p);
        }
        int players = 0;
        foreach (var (name, group) in byName.OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var roles = group.Select(p => p.Class).Distinct().Order(StringComparer.Ordinal).ToList();
            foreach (string cls in roles)
            {
                // Python max(): the first of the highest (usage, m_Name).
                Native? p = null;
                foreach (var x in group.Where(x => x.Class == cls))
                    if (p == null || Compare((usage.GetValueOrDefault(x.Key), x.Name), (usage.GetValueOrDefault(p.Key), p.Name)) > 0) p = x;
                string file = Safe(name) + (roles.Count == 1 || cls == "ForwardData" ? "" : " (Goalie)") + ".txt";
                var t = homeTeam.GetValueOrDefault(p!.Key);
                string path = Path.Combine(output, "Base Game Players", file);
                if (cls == "ForwardData")
                {
                    var colors = t != null ? Palette(t["homeColors"], t["awayColors"], t["jerseyHomeNumberColor"], t["jerseyAwayNumberColor"]) : Palette(p["colorSchemes"], null);
                    Write(path, $"# Base game player (native {p.Name})", SkaterFields(p, "", false, colors));
                }
                else
                {
                    var colors = t != null ? Palette(t["homeGoalieColors"], t["awayGoalieColors"]) : Palette(p["colorSchemes"], null);
                    Write(path, $"# Base game goalie (native {p.Name})", GoalieFields(p, false, colors));
                }
                players++;
            }
        }

        if (campaigns)
        {
            var playerFiles = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var p in order.Where(p => p.Class == "ForwardData"))
            {
                string relative = "Native Players/" + Safe(p.Key) + ".txt";
                var fields = SkaterFields(p, "Center", true, Palette(p["colorSchemes"], null));
                fields.Add(("Studio Native Player Id", p["id"].AsString));
                fields.Add(("Studio Native Player Asset", p.Name));
                Write(Path.Combine(output, relative), "# Native campaign candidate: " + p.Name, fields);
                playerFiles[p.Key] = relative;
            }
            WriteCampaignCatalog(game, output, reader, records, teamFolders, playerFiles);
        }
        WriteProvenance(Path.Combine(output, "provenance.json"), game, labels.Count, players, beyond);
        progress?.Report($"{labels.Count} base-game teams, {players} base-game players");
    }

    private static int Compare((int Usage, string Name) x, (int Usage, string Name) y) =>
        x.Usage != y.Usage ? x.Usage.CompareTo(y.Usage) : string.CompareOrdinal(x.Name, y.Name);

    private static string Key(string file, long pathId) => file + ":" + pathId.ToString(CultureInfo.InvariantCulture);

    // ---- Field access ----

    private static AssetTypeValueField Field(AssetTypeValueField parent, params string[] path)
    {
        var field = parent;
        foreach (string name in path)
        {
            var child = field[name];
            if (child == null || child.IsDummy) throw new InvalidDataException($"The game data has no field {name} under {field.FieldName}.");
            field = child;
        }
        return field;
    }

    /// <summary>The elements of a serialized list/array field.</summary>
    private static IEnumerable<AssetTypeValueField> Items(AssetTypeValueField list) => Field(list, "Array").Children;

    private static bool Truthy(AssetTypeValueField field) => field.TemplateField.ValueType switch
    {
        AssetValueType.Bool => field.AsBool,
        AssetValueType.Float => field.AsFloat != 0,
        AssetValueType.Double => field.AsDouble != 0,
        AssetValueType.String => field.AsString.Length > 0,
        _ => field.AsLong != 0,
    };

    private static int Number(AssetTypeValueField field) => field.TemplateField.ValueType switch
    {
        AssetValueType.Bool => field.AsBool ? 1 : 0,
        _ => field.AsInt,
    };

    private static double Float(AssetTypeValueField field) => field.TemplateField.ValueType switch
    {
        AssetValueType.Float => field.AsFloat,
        AssetValueType.Double => field.AsDouble,
        AssetValueType.Bool => field.AsBool ? 1 : 0,
        _ => field.AsLong,
    };

    /// <summary>A scalar as Python's str() printed it (bools were decoded as 0/1).</summary>
    private static string Scalar(AssetTypeValueField field) => field.TemplateField.ValueType switch
    {
        AssetValueType.String => field.AsString,
        AssetValueType.Float => PyFloat(field.AsFloat),
        AssetValueType.Double => PyFloat(field.AsDouble),
        AssetValueType.Bool => field.AsBool ? "1" : "0",
        AssetValueType.UInt64 => field.AsULong.ToString(CultureInfo.InvariantCulture),
        AssetValueType.Int8 or AssetValueType.UInt8 or AssetValueType.Int16 or AssetValueType.UInt16 or AssetValueType.Int32
            or AssetValueType.UInt32 or AssetValueType.Int64 => field.AsLong.ToString(CultureInfo.InvariantCulture),
        var other => throw new InvalidDataException($"Unexpected {other} value in {field.FieldName}."),
    };

    /// <summary>Python's repr() of a float: shortest round-trip digits, exponent form below 1e-4 or from 1e16.</summary>
    internal static string PyFloat(double value)
    {
        if (double.IsNaN(value)) return "nan";
        if (double.IsInfinity(value)) return value > 0 ? "inf" : "-inf";
        if (value == 0) return double.IsNegative(value) ? "-0.0" : "0.0";
        string text = value.ToString("R", CultureInfo.InvariantCulture), sign = "";
        if (text[0] == '-') { sign = "-"; text = text[1..]; }
        int exponent = 0, e = text.IndexOfAny(['E', 'e']);
        if (e >= 0) { exponent = int.Parse(text[(e + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture); text = text[..e]; }
        int dot = text.IndexOf('.');
        string whole = dot < 0 ? text : text[..dot], digits = dot < 0 ? text : text.Remove(dot, 1);
        int point = whole.Length + exponent - (digits.Length - digits.TrimStart('0').Length); // Python's decpt
        digits = digits.Trim('0');
        if (point <= -4 || point > 16)
        {
            int power = point - 1;
            return sign + digits[0] + (digits.Length > 1 ? "." + digits[1..] : "") + "e" + (power < 0 ? "-" : "+")
                + Math.Abs(power).ToString("00", CultureInfo.InvariantCulture);
        }
        if (point <= 0) return sign + "0." + new string('0', -point) + digits;
        if (point >= digits.Length) return sign + digits + new string('0', point - digits.Length) + ".0";
        return sign + digits[..point] + "." + digits[point..];
    }

    // ---- Formatting (same rules as build_base_rosters.py) ----

    private static string Rgb(AssetTypeValueField color) => string.Join(", ", new[] { "r", "g", "b" }.Select(c =>
    {
        // round(max(0.0, min(1.0, c)) * 255) with Python's comparison order (NaN -> 1.0) and banker's rounding.
        double v = Float(Field(color, c)), clamped = v < 1.0 ? v : 1.0;
        clamped = clamped > 0.0 ? clamped : 0.0;
        return ((long)Math.Round(clamped * 255, MidpointRounding.ToEven)).ToString(CultureInfo.InvariantCulture);
    }));

    /// <summary>Player-level colour keys (AppearanceContract.ColorKey) for both kits.</summary>
    private static Dictionary<string, string> Palette(AssetTypeValueField? home, AssetTypeValueField? away, AssetTypeValueField? numberHome = null, AssetTypeValueField? numberAway = null)
    {
        var doc = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (kit, a) in new[] { (home, ""), (away, " Away") })
        {
            if (kit == null || kit.IsDummy) continue;
            foreach (var (scheme, keys) in new[] {
                ("jerseyScheme", new[] { "Jersey" + a + " Color", "Jersey" + a + " Secondary Color", "Jersey" + a + " Accent Color" }),
                ("skatesScheme", new[] { "Skates" + a + " Color", "Blade" + a + " Color", "Laces" + a + " Color" }) })
                for (int i = 0; i < 3; i++) doc[keys[i]] = Rgb(Field(kit, scheme, Channels[i]));
            foreach (var (scheme, piece) in Pieces)
            {
                doc[piece + a + " Color"] = Rgb(Field(kit, scheme, "primaryColor"));
                doc[piece + a + " Secondary Color"] = Rgb(Field(kit, scheme, "secondaryColor"));
                doc[piece + a + " Tertiary Color"] = Rgb(Field(kit, scheme, "tertiaryColor"));
            }
        }
        if (numberHome != null) doc["Number Tertiary Color"] = Rgb(numberHome);
        if (numberAway != null) doc["Number Away Tertiary Color"] = Rgb(numberAway);
        return doc;
    }

    private static IEnumerable<(string, string)> Sorted(Dictionary<string, string> colors) =>
        colors.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => (c.Key, c.Value));

    /// <summary>Counter(values).most_common(1): the highest count, ties to the first seen.</summary>
    private static string MostCommon(IEnumerable<string> values)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var seen = new List<string>();
        foreach (string v in values) { if (!counts.ContainsKey(v)) seen.Add(v); counts[v] = counts.GetValueOrDefault(v) + 1; }
        string best = seen[0];
        foreach (string v in seen) if (counts[v] > counts[best]) best = v;
        return best;
    }

    private static string NameOf(Native p) =>
        string.Join(" ", new[] { PyStrip(p["firstName"].AsString), PyStrip(p["lastName"].AsString) }.Where(x => x.Length > 0));

    // str.strip(): Python also counts the \x1c-\x1f separators as whitespace.
    private static bool PySpace(char c) => char.IsWhiteSpace(c) || c is >= '\x1c' and <= '\x1f';
    private static string PyStrip(string text)
    {
        int start = 0, end = text.Length;
        while (start < end && PySpace(text[start])) start++;
        while (end > start && PySpace(text[end - 1])) end--;
        return text[start..end];
    }

    private static string Safe(string text)
    {
        foreach (char ch in "<>:\"/\\|?*") text = text.Replace(ch, '_');
        return text.TrimEnd(' ', '.');
    }

    // Python wrote these with Path.write_text on Windows: UTF-8 without BOM, "\n" written as "\r\n".
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static void WriteText(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text.Replace("\n", "\r\n"), Utf8);
    }

    private static void Write(string path, string header, List<(string Key, string Value)> fields)
    {
        int width = Math.Max(24, fields.Count == 0 ? 0 : fields.Max(f => f.Key.Length));
        var text = new StringBuilder(header).Append('\n');
        foreach (var (key, value) in fields) text.Append(key.PadRight(width)).Append(" = ").Append(value).Append('\n');
        WriteText(path, text.ToString());
    }

    private static void WriteProvenance(string path, UnityGame game, int teams, int players, List<string> beyond)
    {
        string bundle;
        using (var stream = File.OpenRead(Path.Combine(game.DataFolder, "data.unity3d")))
            bundle = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        // json.dumps(indent=2) layout; "decoder" names this extractor (the Python recorded its Il2CppDumper folder).
        var text = new StringBuilder("{\n")
            .Append("  \"bundle_sha256\": ").Append(Json(bundle)).Append(",\n")
            .Append("  \"decoder\": ").Append(Json($"GameArt BaseRosters (Cpp2IL, Unity {game.Version})")).Append(",\n")
            .Append("  \"source\": ").Append(Json(Source)).Append(",\n")
            .Append("  \"counts\": {\n")
            .Append("    \"Base Game Teams\": ").Append(teams.ToString(CultureInfo.InvariantCulture)).Append(",\n")
            .Append("    \"Base Game Players\": ").Append(players.ToString(CultureInfo.InvariantCulture)).Append("\n  },\n")
            .Append("  \"not_in_team_folders\": ")
            .Append(beyond.Count == 0 ? "[]" : "[\n" + string.Join(",\n", beyond.Select(b => "    " + Json(b))) + "\n  ]")
            .Append("\n}\n");
        WriteText(path, text.ToString());
    }

    /// <summary>A JSON string as Python's json.dumps writes it (ensure_ascii).</summary>
    private static string Json(string value)
    {
        var text = new StringBuilder("\"");
        foreach (char c in value)
            text.Append(c switch
            {
                '"' => "\\\"", '\\' => "\\\\", '\n' => "\\n", '\r' => "\\r", '\t' => "\\t", '\b' => "\\b", '\f' => "\\f",
                _ when c < ' ' || c > '~' => "\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture),
                _ => c.ToString(),
            });
        return text.Append('"').ToString();
    }

    /// <summary>
    /// Reads any MonoBehaviour in data.unity3d with its own script layout, and follows PPtrs the way the
    /// Python decoder did ("file:pathId"), using UnityGame's internal header and Cpp2IL layouts.
    /// </summary>
    private sealed class BehaviourReader
    {
        private readonly UnityGame game;
        private readonly Dictionary<string, AssetsFileInstance> files = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<(string File, long Id), (string Assembly, string Namespace, string Class)> scripts = new();
        private readonly Dictionary<string, AssetTypeTemplateField> templates = new(StringComparer.Ordinal);
        private readonly AssetTypeTemplateField header;

        public BehaviourReader(UnityGame game)
        {
            this.game = game;
            foreach (var file in game.DataFiles) files[file.name] = file;
            header = game.Header();
            foreach (var file in game.DataFiles)
                foreach (var info in file.file.GetAssetsOfType(AssetClassID.MonoScript))
                {
                    var script = game.Read(file, info);
                    scripts[(file.name, info.PathId)] = (script["m_AssemblyName"].AsString, script["m_Namespace"].AsString, script["m_ClassName"].AsString);
                }
        }

        /// <summary>"file:pathId" of a PPtr's target (null for an empty one); unknown file ids become "extN" like the Python.</summary>
        public string? Ref(AssetsFileInstance owner, AssetTypeValueField pointer)
        {
            int fileId = Field(pointer, "m_FileID").AsInt;
            long pathId = Field(pointer, "m_PathID").AsLong;
            if (pathId == 0) return null;
            var externals = owner.file.Metadata.Externals;
            string name = fileId == 0 ? owner.name
                : fileId > 0 && fileId <= externals.Count ? Path.GetFileName(externals[fileId - 1].PathName.Replace('\\', '/'))
                : "ext" + fileId.ToString(CultureInfo.InvariantCulture);
            return Key(name, pathId);
        }

        /// <summary>The MonoBehaviour a "file:pathId" key names, with its script class; null when it is anything else.</summary>
        public (AssetsFileInstance File, AssetFileInfo Info, (string Assembly, string Namespace, string Class) Script)? Find(string key)
        {
            int colon = key.LastIndexOf(':');
            if (!files.TryGetValue(key[..colon], out var file) || !long.TryParse(key[(colon + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long pathId)) return null;
            var info = file.file.GetAssetInfo(pathId);
            if (info == null || info.TypeId != (int)AssetClassID.MonoBehaviour) return null;
            return Script(file, info) is { } script ? (file, info, script) : null;
        }

        /// <summary>Every MonoBehaviour in data.unity3d with a known script, in file order (the order UnityPy listed them).</summary>
        public IEnumerable<(AssetsFileInstance File, AssetFileInfo Info, (string Assembly, string Namespace, string Class) Script)> All()
        {
            foreach (var file in game.DataFiles)
                foreach (var info in file.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
                    if (Script(file, info) is { } script) yield return (file, info, script);
        }

        private (string Assembly, string Namespace, string Class)? Script(AssetsFileInstance file, AssetFileInfo info)
        {
            var head = header.MakeValue(file.file.Reader, info.GetAbsoluteByteOffset(file.file));
            if (Ref(file, head["m_Script"]) is not { } key) return null;
            int colon = key.LastIndexOf(':');
            if (!files.TryGetValue(key[..colon], out var scriptFile)) return null;
            return scripts.TryGetValue((scriptFile.name, long.Parse(key[(colon + 1)..], CultureInfo.InvariantCulture)), out var script) ? script : null;
        }

        private AssetTypeTemplateField Template((string Assembly, string Namespace, string Class) script)
        {
            string key = script.Assembly + "|" + script.Namespace + "|" + script.Class;
            if (templates.TryGetValue(key, out var cached)) return cached;
            try { return templates[key] = game.BehaviourTemplate(script); }
            catch (Exception ex)
            {
                throw new InvalidDataException($"No layout for {script.Namespace}.{script.Class}: {ex.Message}", ex);
            }
        }

        public AssetTypeValueField Read(AssetsFileInstance file, AssetFileInfo info, (string Assembly, string Namespace, string Class) script) =>
            Template(script).MakeValue(file.file.Reader, info.GetAbsoluteByteOffset(file.file));

        /// <summary>The "id" of a talent / ability / bench power / relic asset.</summary>
        public string ReadId(AssetsFileInstance file, AssetFileInfo info, (string Assembly, string Namespace, string Class) script)
        {
            var id = Read(file, info, script)["id"];
            return id == null || id.IsDummy ? throw new InvalidDataException($"{script.Class} at {Key(file.name, info.PathId)} has no id.") : id.AsString;
        }
    }
}
