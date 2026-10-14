using CampaignCore;

namespace CampaignCreator;

/// <summary>Validate the entire batch before publishing independent campaign copies.</summary>
public static class PlayerPoolImport
{
    public static string[] IntoRoster(string root, string teamFile, IReadOnlyList<string> slots, string[] sources)
    {
        if (!File.Exists(teamFile)) throw new FileNotFoundException("The destination team is missing.", teamFile);
        var valid = RosterLines.Skaters.Concat(RosterLines.Skaters.Select(p => "Line 2 " + p)).ToArray();
        if (slots.Distinct(StringComparer.OrdinalIgnoreCase).Count() != slots.Count
            || slots.Any(p => !valid.Contains(p, StringComparer.OrdinalIgnoreCase)))
            throw new ArgumentException("Choose empty skater slots in the team roster.");
        string directory = Path.Combine(Path.GetDirectoryName(teamFile)!, "players");
        var existing = RosterLines.Assign(directory);
        if (slots.Any(p => existing.Values.Contains(p, StringComparer.OrdinalIgnoreCase)))
            throw new ArgumentException("The roster changed. Reopen the team before importing players.");
        return CopyBatch(root, directory, sources, slots.Count, documents =>
        {
            var names = existing.Keys.Select(f => new CampaignDocument(File.ReadAllText(f)).Get("Name").Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var doc in documents)
                if (names.Contains(doc.Get("Name"))) throw new ArgumentException($"A player named '{doc.Get("Name")}' already exists on this team.");
            var remaining = slots.ToList();
            var assigned = new string?[documents.Length];
            // Reserve every exact match before assigning unmatched players to another opening.
            for (int i = 0; i < documents.Length; i++)
            {
                string? match = remaining.FirstOrDefault(p => p.Equals(documents[i].Get("Position"), StringComparison.OrdinalIgnoreCase));
                if (match != null) { assigned[i] = match; remaining.Remove(match); }
            }
            for (int i = 0; i < documents.Length; i++)
            {
                if (assigned[i] != null) continue;
                string position = documents[i].Get("Position").Replace("Line 2 ", "", StringComparison.OrdinalIgnoreCase);
                string? match = remaining.FirstOrDefault(p => p.Replace("Line 2 ", "", StringComparison.OrdinalIgnoreCase).Equals(position, StringComparison.OrdinalIgnoreCase));
                if (match != null) { assigned[i] = match; remaining.Remove(match); }
            }
            for (int i = 0; i < documents.Length; i++)
            {
                if (assigned[i] == null) { assigned[i] = remaining[0]; remaining.RemoveAt(0); }
                documents[i].Set("Position", assigned[i]!);
            }
        });
    }

    public static string?[] IntoSlots(string root, string campaign, string folder, IReadOnlyList<string?> slots, string[] sources)
    {
        int limit = folder switch { "draft_pool" => 9, "superstars" => 3, _ => throw new ArgumentException("Unknown starting pool.") };
        if (slots.Count != limit) throw new ArgumentException("The starting pool has an invalid slot count.");
        var result = slots.ToArray();
        var empty = Enumerable.Range(0, result.Length).Where(i => result[i] == null).ToArray();
        var files = CopyBatch(root, Path.Combine(campaign, "player_teams", folder), sources, empty.Length);
        for (int i = 0; i < files.Length; i++) result[empty[i]] = Path.GetFileName(files[i]);
        return result;
    }

    public static string[] IntoFreeAgents(string root, string campaign, string[] sources)
    {
        string directory = Path.Combine(campaign, "player_teams", "free_agents");
        var selected = SelectedPlayerPool.Read(directory);
        if (selected?.Any(f => !File.Exists(f)) == true) throw new FileNotFoundException("A selected free-agent file is missing. Repair the pool before importing.");
        var files = CopyBatch(root, directory, sources, int.MaxValue);
        try
        {
            // A legacy pool includes all files. Explicit pools retain every previous exclusion.
            if (selected != null) SelectedPlayerPool.Save(directory, selected.Concat(files).Select(Path.GetFileName)!);
            return files;
        }
        catch
        {
            foreach (string file in files) File.Delete(file);
            throw;
        }
    }

    private static string[] CopyBatch(string root, string directory, string[] sources, int maximum, Action<CampaignDocument[]>? prepare = null)
    {
        if (sources.Length == 0) throw new ArgumentException("Check at least one player.");
        if (sources.Length > maximum) throw new ArgumentException($"Only {maximum} empty slots are available. Uncheck players to fit.");
        if (sources.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != sources.Length)
            throw new ArgumentException("A player cannot be selected twice.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var documents = sources.Select(source =>
        {
            if (!ContentImport.Matches(source, "skater")) throw new ArgumentException($"'{Path.GetFileName(source)}' is not a skater file.");
            var original = new CampaignDocument(File.ReadAllText(source));
            string name = CreatorContent.ValidateName(original.Get("Name", original.Get("Import Player")));
            if (!names.Add(name)) throw new ArgumentException($"Two selected players are named '{name}'. Uncheck one or rename it in the library.");
            CreatorContent.ValidatePlayerName(directory, name);
            var doc = CreatorContent.CustomPlayerCopy(source, name);
            doc.Remove(VanillaCopyData.PlayerId); doc.Remove(VanillaCopyData.PlayerAsset);
            return doc;
        }).ToArray();
        prepare?.Invoke(documents);
        string staging = Path.Combine(root, ".studio-staging", "pool-" + Guid.NewGuid().ToString("N"));
        var written = new List<string>();
        try
        {
            Directory.CreateDirectory(staging);
            var staged = documents.Select(doc =>
            {
                string file = Path.Combine(staging, "custom-" + Guid.NewGuid().ToString("N") + ".txt");
                doc.Save(file); return file;
            }).ToArray();
            Directory.CreateDirectory(directory);
            foreach (string file in staged)
            {
                string target = Path.Combine(directory, Path.GetFileName(file));
                File.Move(file, target); written.Add(target);
            }
            return written.ToArray();
        }
        catch
        {
            foreach (string file in written) File.Delete(file);
            throw;
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                foreach (string file in Directory.GetFiles(staging)) File.Delete(file);
                Directory.Delete(staging);
            }
        }
    }
}
