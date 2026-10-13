using System.Text.Json;
using CampaignCore;

namespace CampaignCreator;

public sealed record SquadTemplateOption(string Id, string Name, string Description, int Lines, int BenchLimit, bool StartingChoice, string Details)
{
    public IReadOnlyList<string> LockedPositions { get; init; } = [];
    public IReadOnlyList<string> StartingPositions { get; init; } = [];
    public override string ToString() => Name;
}

public static class SquadTemplateCatalog
{
    public static IReadOnlyList<string> StartingRelics(string template)
    {
        using var stream = typeof(SquadTemplateCatalog).Assembly.GetManifestResourceStream("CampaignCreator.TemplateStartingRelics.tsv")!;
        using var reader = new StreamReader(stream);
        string id = SquadTemplatePolicy.Identity(template);
        return reader.ReadToEnd().Split('\n').Select(line => line.TrimEnd('\r').Split('\t'))
            .Where(p => p.Length >= 3 && p[0] == id).Select(p => p[2]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public static IReadOnlyList<SquadTemplateOption> All { get; } = Load();
    public static SquadTemplateOption? Find(string value) => All.FirstOrDefault(t => t.Id == SquadTemplatePolicy.Identity(value));
    private static IReadOnlyList<SquadTemplateOption> Load()
    {
        using var stream = typeof(SquadTemplateCatalog).Assembly.GetManifestResourceStream("CampaignCreator.SquadTemplates.json")!;
        using var json = JsonDocument.Parse(stream);
        var native = json.RootElement.GetProperty("templates").EnumerateArray().Select(t => {
            string desc = t.GetProperty("description").GetString()!;
            string id = t.GetProperty("id").GetString()!;
            int lines = Math.Max(1, t.GetProperty("authored_lines").GetArrayLength());
            bool choice = t.GetProperty("free_agent_intro").GetBoolean();
            int bench = t.GetProperty("bench_capacity").GetInt32();
            string[] positions = ["Left Wing", "Right Wing", "Center", "Left Defense", "Right Defense"];
            var empty = t.GetProperty("empty_entries").EnumerateArray().Select(e => e.GetInt32()).ToHashSet();
            var locked = t.GetProperty("locked_positions").EnumerateArray().Select(p => p.GetString()!).ToArray();
            var starters = lines == 2 ? positions.Append("Goalie").ToArray()
                : positions.Where((p, i) => i < t.GetProperty("forward_array_length").GetInt32() && !empty.Contains(i) && !locked.Contains(p, StringComparer.OrdinalIgnoreCase)).Append("Goalie").ToArray();
            string details = desc + "\n" + (lines == 2 ? "Two editable lines · 10 skaters + goalie" : "Editable starting roster")
                + "\n" + (bench == 0 ? "This template has no bench." : "Bench choices: 9 slots. Use the base-game bench toggle to switch to custom choices.")
                + "\n" + (choice ? "Starting superstar choices are editable." : "This template skips the starting superstar choice.")
                + "\n" + string.Join(" · ", t.GetProperty("maps").EnumerateArray().Select(m => "Map " + m.GetProperty("sequence").GetInt32() + ": " + m.GetProperty("variants").GetArrayLength() + " layout(s)"));
            if (id == "generalmanager") details += "\nAdd General Manager nodes to your maps so players can recruit these free agents.";
            return new SquadTemplateOption(id, t.GetProperty("name").GetString()!, desc, lines, bench, choice, details)
            { LockedPositions = locked, StartingPositions = starters };
        }).ToArray();
        var free = new SquadTemplateOption("noeffects", "No Effects", "Build the complete starting team yourself.", 1, 0, false,
            "All five starting skaters and the goalie are editable, with no template-locked positions or automatic starting relics. Enable Bench draft and Superstar selection in Squad behaviors, then choose their players on the campaign page. Uses Basic Squad's game maps; each behavior can be overridden for this team.")
        { StartingPositions = ["Left Wing", "Center", "Right Wing", "Left Defense", "Right Defense", "Goalie"] };
        return new[] { free }.Concat(native).ToArray();
    }
    public static string ResolveCampaign(string campaign)
    {
        string file = Path.Combine(campaign, "campaign.txt");
        var doc = new CampaignDocument(File.Exists(file) ? File.ReadAllText(file) : "");
        return doc.Get("Squad Template", "Basic Squad");
    }
}

