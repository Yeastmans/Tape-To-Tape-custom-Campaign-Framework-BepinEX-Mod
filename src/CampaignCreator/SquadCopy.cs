using CampaignCore;

namespace CampaignCreator;

public static class SquadCopy
{
    public static string Create(string source, string campaign, string name, string template, string? defaultLookRoot = null)
    {
        string campaignFile = Path.Combine(campaign, "campaign.txt");
        string campaignText = File.ReadAllText(campaignFile);
        var campaignDoc = new CampaignDocument(campaignText);
        if (SquadTemplateCatalog.Find(template) == null) throw new ArgumentException("Choose a known base-game squad template.");
        name = name.Trim();
        string stem = name.Split('.')[0];
        var reserved = new[] { "CON", "PRN", "AUX", "NUL", "draft_pool", "free_agents", "superstars", GoalieFreeAgents.Folder }.Concat(Enumerable.Range(1, 9).SelectMany(i => new[] { "COM" + i, "LPT" + i }));
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith('.') || name.EndsWith(' ') || reserved.Contains(stem, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("Choose a non-reserved squad name without filename punctuation.");
        string squads = Path.Combine(campaign, "player_teams");
        string target = Path.Combine(squads, name);
        if (Directory.Exists(target) || File.Exists(target)) throw new ArgumentException("A squad with that name already exists.");
        string stagingRoot = Path.Combine(campaign, ".studio-staging"); Directory.CreateDirectory(stagingRoot);
        string stage = Path.Combine(stagingRoot, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
        try
        {
            void Copy(string from, string to)
            {
                if ((File.GetAttributes(from) & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("Linked roster folders cannot be copied as squads.");
                Directory.CreateDirectory(to);
                foreach (string file in Directory.GetFiles(from))
                {
                    if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("Linked roster files cannot be copied as squads.");
                    string copied = Path.Combine(to, Path.GetFileName(file));
                    File.Copy(file, copied); ContentImport.ValidateCopiedFile(copied);
                }
                foreach (string dir in Directory.GetDirectories(from)) Copy(dir, Path.Combine(to, Path.GetFileName(dir)));
            }
            if (!string.IsNullOrEmpty(source)) Copy(source, stage);
            var poolMoves = new List<(string From, string To)>();
            foreach (string pool in new[] { "draft_pool", "superstars" })
            {
                string from = Path.Combine(stage, "player_teams", pool), to = Path.Combine(squads, pool);
                if (!Directory.Exists(from) || !File.Exists(Path.Combine(from, SelectedPlayerPool.Manifest))) continue;
                if (Directory.Exists(to))
                {
                    var incoming = Directory.GetFiles(from, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(from, p)).Order(StringComparer.OrdinalIgnoreCase).ToArray();
                    var current = Directory.GetFiles(to, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(to, p)).Order(StringComparer.OrdinalIgnoreCase).ToArray();
                    if (!incoming.SequenceEqual(current, StringComparer.OrdinalIgnoreCase) || incoming.Any(p => !File.ReadAllBytes(Path.Combine(from, p)).SequenceEqual(File.ReadAllBytes(Path.Combine(to, p)))))
                        throw new InvalidOperationException("This campaign already has different " + (pool == "draft_pool" ? "bench" : "superstar") + " choices. Its player teams share these choices; import into a new campaign to use this library team's setup.");
                }
                else poolMoves.Add((from, to));
            }
            string teamFile = Path.Combine(stage, "team.txt");
            var doc = new CampaignDocument(File.Exists(teamFile) ? File.ReadAllText(teamFile) : "");
            doc.Set("Team Name", name);
            doc.Set("Squad Template", template);
            if (source.Length == 0)
            {
                doc.Set("Starting Relics Mode", "Custom");
                doc.Set("Team Relics", string.Join(", ", SquadTemplateCatalog.StartingRelics(template)));
            }
            if (source.Length == 0 && defaultLookRoot != null) DefaultPlayerLook.ApplyTeamLook(defaultLookRoot, doc, stage);
            doc.Save(teamFile);
            Directory.CreateDirectory(Path.Combine(stage, "players"));
            RosterLines.Normalize(Path.Combine(stage, "players"));
            var movedPools = new List<(string From, string To)>();
            try {
                Directory.CreateDirectory(squads);
                foreach (var move in poolMoves) { Directory.Move(move.From, move.To); movedPools.Add(move); }
                Directory.Move(stage, target);
            }
            catch {
                foreach (var move in movedPools) Directory.Move(move.To, move.From);
                new CampaignDocument(campaignText).Save(campaignFile); throw;
            }
            return target;
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }
}
