using CampaignCore;

namespace CampaignCreator;

public static class FreeAgentContent
{
    public const string Marker = "Studio Base Free Agent";
    public static readonly string[] BenchNames = ["Stu Stumple", "Maurice Cassoulet", "Buster Brewster", "Freddy Kovalski", "Ricky Kirby", "Boring Dude", "Mark Bench", "Sally Poppins", "Johnny Faceless"];
    // Extracted from native State.CampaignState.freeAgents; bench is a separate pool.
    public static readonly string[] BaseNames = LoadBaseNames();
    private static string[] LoadBaseNames()
    {
        using var stream = typeof(FreeAgentContent).Assembly.GetManifestResourceStream("CampaignCreator.FreeAgents.txt")
            ?? throw new InvalidOperationException("Free-agent catalog is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
    public static string? Source(string root, string name)
    {
        string? players = StudioArt.BaseRosters("Base Game Players");
        string? file = players == null ? null : Path.Combine(players, name + ".txt");
        return file != null && File.Exists(file) ? file : null;
    }
    private static IEnumerable<string> Directories(string campaign) => new[] { Path.Combine(campaign, "free_agents"), Path.Combine(campaign, "player_teams", "free_agents") }
        .Where(Directory.Exists);
    public static List<string> Files(string campaign) => Directories(campaign).SelectMany(dir => Directory.GetFiles(dir, "*.txt")).ToList();
    private static bool IsBaseGame(CampaignDocument doc) => BaseNames.Contains(doc.Get(Marker), StringComparer.OrdinalIgnoreCase);
    private static IEnumerable<string> SelectedFiles(string campaign) => Directories(campaign)
        .SelectMany(dir => SelectedPlayerPool.Read(dir) ?? Directory.GetFiles(dir, "*.txt"))
        .Where(File.Exists);
    public static string SelectionKey(string campaign, string file) => Path.GetRelativePath(campaign, file);
    public static HashSet<string> SelectedCustom(string campaign) => SelectedFiles(campaign)
        .Where(file => !IsBaseGame(new CampaignDocument(File.ReadAllText(file))))
        .Select(file => SelectionKey(campaign, file)).ToHashSet(StringComparer.OrdinalIgnoreCase);
    public static HashSet<string> Selected(string campaign) => SelectedFiles(campaign)
        .Select(file => new CampaignDocument(File.ReadAllText(file))).Where(IsBaseGame)
        .Select(doc => doc.Get(Marker)).ToHashSet(StringComparer.OrdinalIgnoreCase);
    public static string Create(string root, string campaign, string name, string source = "")
    {
        string directory = Path.Combine(campaign, "player_teams", "free_agents");
        var selected = SelectedPlayerPool.Read(directory);
        if (selected?.Any(file => !File.Exists(file)) == true)
            throw new FileNotFoundException("A selected free-agent file is missing. Repair the pool before adding a player.");
        string file = CreatorContent.Create(root, "player", name, source, Path.Combine(campaign, "player_teams"), null);
        // Legacy pools include every file. Explicit pools must also include new imports/players.
        if (selected != null)
            SelectedPlayerPool.Save(directory, selected.Select(path => Path.GetFileName(path)).Append(Path.GetFileName(file)));
        return file;
    }
    public static void Save(string root, string campaign, HashSet<string> selected, HashSet<string>? selectedCustom = null)
    {
        selectedCustom ??= SelectedCustom(campaign);
        var existing = Files(campaign).Select(file => (file, doc: new CampaignDocument(File.ReadAllText(file))))
            .Where(p => IsBaseGame(p.doc)).ToList();
        var additions = selected.Where(n => !existing.Any(p => p.doc.Get(Marker).Equals(n, StringComparison.OrdinalIgnoreCase)))
            .Select(name => (name, source: Source(root, name) ?? throw new InvalidOperationException("Base-game player record unavailable: " + name))).ToList();
        foreach (var item in additions)
        {
            string directory = Path.Combine(campaign, "player_teams", "free_agents");
            Directory.CreateDirectory(directory);
            string key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(item.name.ToLowerInvariant())))[..16];
            string file = Path.Combine(directory, "base-" + key + ".txt");
            if (File.Exists(file)) throw new InvalidOperationException("A base player copy already occupies this record: " + item.name);
            var doc = ContentImport.ReadDocument(item.source); doc.Set(Marker, item.name);
            doc.Set("Override Team Settings", "no"); doc.Save(file);
        }
        foreach (var item in existing.Where(p => !selected.Contains(p.doc.Get(Marker)))) File.Delete(item.file);
        foreach (string directory in Directories(campaign))
        {
            var included = Directory.GetFiles(directory, "*.txt").Where(file => IsBaseGame(new CampaignDocument(File.ReadAllText(file)))
                || selectedCustom.Contains(SelectionKey(campaign, file))).Select(file => Path.GetFileName(file));
            SelectedPlayerPool.Save(directory, included);
        }
    }
}
