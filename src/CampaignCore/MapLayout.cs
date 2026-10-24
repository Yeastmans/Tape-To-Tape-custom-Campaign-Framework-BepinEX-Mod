using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CampaignCore;

public sealed class MapLayoutNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int Layer { get; set; }
    public string Type { get; set; } = "Elite";
    public double X { get; set; }
    public double Y { get; set; }
    public string Team { get; set; } = "";
    public string Stadium { get; set; } = "";
    // Zero preserves the native match setting; duration is in real-time minutes.
    public int PeriodCount { get; set; }
    public float PeriodLengthMinutes { get; set; }
    public Dictionary<string, decimal> RandomWeights { get; set; }
    // The imported source pool is separate from an authored override. Merely
    // viewing or saving a native pool must not disable native placement rules.
    public Dictionary<string, decimal> NativeRandomWeights { get; set; }
    public List<string> Outgoing { get; set; } = new List<string>();
    public int SourceLayer { get; set; } = -1;
    public int SourceNode { get; set; } = -1;
    public int TalentRewards { get; set; } = 1;
    public int RelicRewards { get; set; } = 1;
    public int GmChoices { get; set; } = 1;
    public string GmRole { get; set; } = GoalieFreeAgents.Skaters;
    public string EliteGroup { get; set; } = "";
    public string EliteTeam { get; set; } = "";
    public CustomCoachDefinition CustomCoach { get; set; }
}

/// <summary>A stable-ID editable graph. Native node indexes are derived only at the adapter boundary.</summary>
public sealed class MapLayout
{
    public const int Version = 1;
    public const double NativeStartX = 3.1, NativeLayerPitch = 6.4;
    public static readonly string[] NodeTypes = { "Challenge", "Elite", "Boss", "RandomConditioning", "Chaos", "Nurse", "Blademaster", "FanNumber1", "Coach", "GeneralManager", "NULL", "PlayerTraining", "TeamTraining", "StatTraining" };
    public string BaseSquad { get; set; } = "";
    public int BaseMap { get; set; }
    public int Template { get; set; }
    // A vanilla-based editable graph: one saved layout per act. Its NULL nodes
    // retain native pools unless the author sets a per-node override.
    public bool VanillaRandom { get; set; }
    public int LookAct { get; set; } // 0 keeps the gameplay act appearance for older files.
    public string Name { get; set; } = "";
    public List<MapLayoutNode> Nodes { get; set; } = new List<MapLayoutNode>();
    // Saved display labels and older game names may identify the same native
    // squad. Exact names win; aliases resolve only when the live choice is unique.
    public static string ResolveBaseSquad(string requested, IEnumerable<string> available)
    {
        string value = (requested ?? "").Trim();
        var names = available.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        string exact = names.FirstOrDefault(n => n.Equals(value, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;
        string identity = SquadTemplatePolicy.RuntimeIdentity(value);
        if (identity.Length == 0) return null;
        var aliases = names.Where(n => SquadTemplatePolicy.RuntimeIdentity(n) == identity).ToArray();
        return aliases.Length == 1 ? aliases[0] : null;
    }
    public IEnumerable<MapLayoutNode> InLayer(int layer) => Nodes.Where(n => n.Layer == layer);
    public int NativeIndex(MapLayoutNode node) => InLayer(node.Layer).ToList().IndexOf(node);
    private static string Encode(string value) => Uri.EscapeDataString(value);
    public string Serialize()
    {
        var d = new CampaignDocument("# Campaign Studio map layout. Start a new run after editing.\n");
        d.Set("Version", Version.ToString()); d.Set("Base Squad", BaseSquad); d.Set("Base Map", BaseMap.ToString()); d.Set("Template", Template.ToString());
        if (VanillaRandom) d.Set("Generation", "Vanilla random");
        d.Set("Map Look", LookAct.ToString(CultureInfo.InvariantCulture));
        d.Set("Map Name", Encode(Name));
        foreach (var n in Nodes) d.Set("Node " + n.Id, string.Join("|", new[] {
            n.Layer.ToString(), n.Type, n.X.ToString("R", CultureInfo.InvariantCulture), n.Y.ToString("R", CultureInfo.InvariantCulture),
            n.Team, string.Join(",", n.Outgoing), n.SourceLayer.ToString(), n.SourceNode.ToString(), n.TalentRewards.ToString(), n.RelicRewards.ToString(), n.GmChoices.ToString(), n.EliteGroup, n.EliteTeam }.Select(Encode)));
        // Optional editor metadata keeps the existing 13-field node records compatible.
        foreach (var n in Nodes.Where(n => n.Type == "Coach" && n.CustomCoach != null))
            d.Set("Custom coach " + n.Id, n.CustomCoach.Encode());
        foreach (var n in Nodes.Where(n => n.GmRole != GoalieFreeAgents.Skaters)) d.Set("Recruit role " + n.Id, n.GmRole);
        foreach (var n in Nodes.Where(n => n.Stadium.Length > 0)) d.Set("Stadium " + n.Id, Encode(n.Stadium));
        foreach (var n in Nodes.Where(n => n.PeriodCount != 0)) d.Set("Periods " + n.Id, n.PeriodCount.ToString(CultureInfo.InvariantCulture));
        foreach (var n in Nodes.Where(n => n.PeriodLengthMinutes != 0)) d.Set("Period minutes " + n.Id, n.PeriodLengthMinutes.ToString("R", CultureInfo.InvariantCulture));
        foreach (var n in Nodes.Where(n => n.RandomWeights != null)) d.Set("Random weights " + n.Id,
            string.Join("|", n.RandomWeights!.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => Encode(p.Key) + ":" + p.Value.ToString(CultureInfo.InvariantCulture))));
        foreach (var n in Nodes.Where(n => n.NativeRandomWeights != null)) d.Set("Native random weights " + n.Id,
            string.Join("|", n.NativeRandomWeights!.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => Encode(p.Key) + ":" + p.Value.ToString(CultureInfo.InvariantCulture))));
        return d.ToString();
    }
    public static MapLayout Parse(string text)
    {
        var d = new CampaignDocument(text);
        if (d.Get("Version") != "1") throw new ArgumentException("Unsupported map layout version.");
        int Int(string value) => int.Parse(value, CultureInfo.InvariantCulture);
        var map = new MapLayout { BaseSquad = d.Get("Base Squad"), BaseMap = Int(d.Get("Base Map")), Template = Int(d.Get("Template", "0")), LookAct = Int(d.Get("Map Look", "0")), Name = Uri.UnescapeDataString(d.Get("Map Name")) };
        map.VanillaRandom = d.Get("Generation").Equals("Vanilla random", StringComparison.OrdinalIgnoreCase);
        foreach (var pair in d.Fields.Where(p => p.Key.StartsWith("Node ", StringComparison.Ordinal)))
        {
            var p = pair.Value.Split('|').Select(Uri.UnescapeDataString).ToArray();
            if (p.Length != 13) throw new ArgumentException("Incomplete map node: " + pair.Key);
            map.Nodes.Add(new MapLayoutNode { Id = pair.Key.Substring(5), Layer = Int(p[0]), Type = p[1],
                X = double.Parse(p[2], CultureInfo.InvariantCulture), Y = double.Parse(p[3], CultureInfo.InvariantCulture), Team = p[4],
                Stadium = Uri.UnescapeDataString(d.Get("Stadium " + pair.Key.Substring(5))),
                GmRole = d.Get("Recruit role " + pair.Key.Substring(5), GoalieFreeAgents.Skaters),
                PeriodCount = Int(d.Get("Periods " + pair.Key.Substring(5), "0")),
                PeriodLengthMinutes = float.Parse(d.Get("Period minutes " + pair.Key.Substring(5), "0"), CultureInfo.InvariantCulture),
                Outgoing = p[5].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList(), SourceLayer = Int(p[6]), SourceNode = Int(p[7]),
                TalentRewards = Int(p[8]), RelicRewards = Int(p[9]), GmChoices = Int(p[10]), EliteGroup = p[11], EliteTeam = p[12] });
        }
        foreach (var node in map.Nodes)
        {
            string customCoach = d.Get("Custom coach " + node.Id);
            if (customCoach.Length > 0)
            {
                if (node.Type != "Coach") throw new ArgumentException("Custom Coach metadata requires a Coach node.");
                node.CustomCoach = CustomCoachDefinition.Decode(customCoach);
            }
            Dictionary<string, decimal> ReadWeights(string prefix)
            {
                string weights = d.Get(prefix + node.Id);
                if (weights.Length == 0) return d.Fields.ContainsKey(prefix + node.Id) ? new Dictionary<string, decimal>() : null;
                var result = new Dictionary<string, decimal>();
                foreach (string item in weights.Split('|'))
                {
                    var parts = item.Split(':');
                    if (parts.Length != 2 || !decimal.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
                        throw new ArgumentException("Invalid random node weights: " + node.Id);
                    result.Add(Uri.UnescapeDataString(parts[0]), value);
                }
                return result;
            }
            node.RandomWeights = ReadWeights("Random weights ");
            node.NativeRandomWeights = ReadWeights("Native random weights ");
        }
        return map;
    }
    public List<string> Validate(IEnumerable<string> teamKeys)
    {
        var errors = new List<string>();
        if (Name.IndexOfAny(new[] { '\r', '\n', '\t' }) >= 0) errors.Add("Map name must fit on one line.");
        foreach (var n in Nodes.Where(n => n.Type == "NULL" && n.RandomWeights != null))
            if (n.RandomWeights!.Any(p => !RandomNodePool.Defaults.ContainsKey(p.Key) || p.Value < 0 || p.Value > 1000000)
                || !n.RandomWeights.Values.Any(v => v > 0)) errors.Add("Random nodes need at least one positive event weight; Challenge is not allowed.");
        if (LookAct < 0 || LookAct > 3) errors.Add("Map look must be Act 1, 2 or 3.");
        if (Nodes.Count < 1 || Nodes.Count > 200) errors.Add("A map needs 1–200 nodes.");
        if (BaseMap < 0 || Template < 0 || string.IsNullOrWhiteSpace(BaseSquad)) errors.Add("Choose a base squad map.");
        if (Nodes.Any(n => string.IsNullOrWhiteSpace(n.Id) || n.Id.IndexOfAny(new[] { ',', '|', '=', '\r', '\n' }) >= 0)
            || Nodes.Select(n => n.Id).Distinct().Count() != Nodes.Count) errors.Add("Map node identities must be unique.");
        if (errors.Count > 0) return errors;
        var layers = Nodes.Select(n => n.Layer).Distinct().OrderBy(n => n).ToArray();
        if (layers.Length < 1 || layers.Length > 40 || !layers.SequenceEqual(Enumerable.Range(0, layers.Length))) errors.Add("Use 1–40 consecutive layers starting at the entry.");
        int last = layers.Last();
        if (Nodes.GroupBy(n => n.Layer).Any(g => g.Count() > 4)) errors.Add("Each layer can contain up to 4 nodes.");
        if (InLayer(last).Count() != 1 || InLayer(last).Any(n => n.Type != "Boss")) errors.Add("The last layer needs one boss.");
        var teams = new HashSet<string>(teamKeys, StringComparer.OrdinalIgnoreCase);
        var nodes = new Dictionary<string, MapLayoutNode>();
        foreach (var node in Nodes) nodes.Add(node.Id, node);
        foreach (var n in Nodes)
        {
            string label = $"Layer {n.Layer + 1}, node {NativeIndex(n) + 1}";
            if (n.CustomCoach != null)
            {
                if (n.Type != "Coach") errors.Add(label + ": Custom Coach metadata requires a Coach node.");
                try { n.CustomCoach.Validate(); }
                catch (Exception ex) { errors.Add(label + ": " + ex.Message); }
            }
            if (n.PeriodCount < 0 || n.PeriodCount > 3) errors.Add(label + ": periods must be native or 1–3.");
            if (float.IsNaN(n.PeriodLengthMinutes) || float.IsInfinity(n.PeriodLengthMinutes)
                || n.PeriodLengthMinutes != 0 && (n.PeriodLengthMinutes < 0.5f || n.PeriodLengthMinutes > 20))
                errors.Add(label + ": period length must be native or 0.5–20 minutes.");
            if (!NodeTypes.Contains(n.Type)) errors.Add(label + (n.Type.Length == 0 ? ": choose a node type." : ": unsupported node type."));
            if (n.Type == "Boss" && n.Layer != last) errors.Add(label + ": the boss must finish the map.");
            if (double.IsNaN(n.X) || double.IsInfinity(n.X) || double.IsNaN(n.Y) || double.IsInfinity(n.Y) || Math.Abs(n.X) > 1000 || Math.Abs(n.Y) > 1000) errors.Add(label + ": position is outside the supported range.");
            if (n.Team.Length > 0 && (!teams.Contains(n.Team) || (n.Type != "Elite" && n.Type != "Boss"))) errors.Add(label + ": assign a campaign team to an Elite or Boss match.");
            if (n.TalentRewards < 0 || n.TalentRewards > 10 || n.RelicRewards < 0 || n.RelicRewards > 10 || n.GmChoices < 1 || n.GmChoices > 20) errors.Add(label + ": invalid reward or recruitment count.");
            if (n.GmRole != GoalieFreeAgents.Skaters && n.GmRole != GoalieFreeAgents.Goalies) errors.Add(label + ": invalid recruitment role.");
            if (n.Outgoing.Distinct().Count() != n.Outgoing.Count) errors.Add(label + ": duplicate connection.");
            if (n.Layer < last && n.Outgoing.Count == 0) errors.Add(label + ": connect a route to the next layer.");
            foreach (string target in n.Outgoing)
                if (!nodes.TryGetValue(target, out var next) || next.Layer != n.Layer + 1) errors.Add(label + ": connections must go to the next layer.");
        }
        var reached = new HashSet<string>();
        void Visit(string id) { if (!reached.Add(id) || !nodes.TryGetValue(id, out var n)) return; foreach (var target in n.Outgoing) Visit(target); }
        foreach (var entry in InLayer(0)) Visit(entry.Id);
        if (Nodes.Any(n => !reached.Contains(n.Id))) errors.Add("Every node must be reachable from the entry.");
        return errors.Distinct().ToList();
    }
    public void Remove(MapLayoutNode node)
    {
        bool only = InLayer(node.Layer).Count() == 1;
        if (node.Type == "Boss") throw new ArgumentException("Keep the final boss.");
        foreach (var source in Nodes.Where(n => n.Outgoing.Contains(node.Id)))
        {
            source.Outgoing.Remove(node.Id);
        }
        Nodes.Remove(node);
        if (only && node.Layer == 0)
        {
            Nodes.Add(new MapLayoutNode { Layer = 0, Type = "", X = node.X, Y = node.Y });
            return;
        }
        if (only) foreach (var n in Nodes.Where(n => n.Layer > node.Layer)) { n.Layer--; n.X -= NativeLayerPitch; }
    }
    public MapLayoutNode InsertLayer(int after, double horizontalSpacing = NativeLayerPitch)
    {
        var previous = InLayer(after).ToArray();
        var next = InLayer(after + 1).ToArray();
        if ((previous.Length == 0 && after != -1) || next.Length == 0) throw new ArgumentException("Insert a layer before the boss.");
        double x = previous.Length > 0 ? previous.Average(n => n.X) + horizontalSpacing : next.Average(n => n.X);
        double y = previous.Length > 0 ? previous.Average(n => n.Y) : next.Average(n => n.Y);
        foreach (var n in Nodes.Where(n => n.Layer > after)) { n.Layer++; n.X += horizontalSpacing; }
        var added = new MapLayoutNode { Layer = after + 1, X = x, Y = y, Outgoing = next.Select(n => n.Id).ToList() };
        foreach (var n in previous) n.Outgoing = new List<string> { added.Id };
        Nodes.Add(added); return added;
    }
}
