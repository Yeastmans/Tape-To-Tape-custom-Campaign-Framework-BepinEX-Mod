using CampaignCore;

namespace CampaignCreator;

// Goalie style keys belong to player files in the current mod format.
// Save team defaults even without a goalie, and mirror changes to existing goalies.
internal sealed class TeamGoalieEquipment
{
    private readonly Dictionary<string, CampaignDocument> documents = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> edits = new(StringComparer.OrdinalIgnoreCase);
    internal bool HasChanges => documents.Values.Any(doc => edits.Any(edit => doc.Get(edit.Key) != edit.Value));

    internal TeamGoalieEquipment(string teamFile, CampaignDocument? appearanceDefaults = null)
    {
        if (appearanceDefaults != null)
        {
            documents[teamFile] = appearanceDefaults;
            return;
        }
        string defaultsFile = DefaultsFile(teamFile);
        documents[defaultsFile] = new CampaignDocument(File.Exists(defaultsFile) ? File.ReadAllText(defaultsFile) : "");
        string folder = Path.Combine(Path.GetDirectoryName(teamFile)!, "players");
        if (!Directory.Exists(folder)) return;
        foreach (string file in Directory.GetFiles(folder, "*.txt"))
        {
            var doc = new CampaignDocument(File.ReadAllText(file));
            if (doc.Get("Position").Equals("Goalie", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(file).Contains("goalie", StringComparison.OrdinalIgnoreCase)
                || doc.Fields.ContainsKey("Catching") || doc.Fields.ContainsKey("Goalie Skill")) documents[file] = doc;
        }
    }

    private static string DefaultsFile(string teamFile) => Path.Combine(Path.GetDirectoryName(teamFile)!, "goalie-equipment.cfg");

    internal static void ApplyDefaults(string teamFile, CampaignDocument player)
    {
        string file = DefaultsFile(teamFile);
        if (!File.Exists(file)) return;
        foreach (var (key, value) in new CampaignDocument(File.ReadAllText(file)).Fields)
            if (GoalieEquipmentCatalog.Values(key).Count > 0) player.Set(key, value);
    }

    internal string Get(string key) => edits.TryGetValue(key, out var value) ? value
        : documents.Values.Select(doc => doc.Get(key)).FirstOrDefault(v => v.Length > 0) ?? "";
    internal void Set(string key, string value)
    {
        edits[key] = value;
    }
    internal void Save()
    {
        var pending = new Dictionary<string, CampaignDocument>();
        foreach (var (file, original) in documents)
        {
            var changes = edits.Where(e => original.Get(e.Key) != e.Value).ToArray();
            if (changes.Length == 0) continue;
            var latest = new CampaignDocument(File.Exists(file) ? File.ReadAllText(file) : "");
            if (latest.Get("Override Team Settings").Equals("yes", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var (key, value) in changes)
            {
                if (latest.Get(key) != original.Get(key) && latest.Get(key) != value)
                    throw new InvalidOperationException($"{Path.GetFileName(file)} changed outside the editor. Reopen the team before saving its equipment.");
                latest.Set(key, value);
            }
            pending[file] = latest;
        }
        foreach (var (file, doc) in pending) { doc.Save(file); documents[file] = doc; }
        edits.Clear();
    }
}
