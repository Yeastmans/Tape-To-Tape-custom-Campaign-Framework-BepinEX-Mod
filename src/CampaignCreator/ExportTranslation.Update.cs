using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CampaignCore;

namespace CampaignCreator;

internal static partial class ExportTranslation
{
    internal sealed record ExistingTeam(string Id, string Name, string City, string Fingerprint, DateTime Modified)
    {
        public override string ToString() => $"{Name}{(City.Length == 0 ? "" : " · " + City)} · {Modified:g} · {Id}";
    }

    private static readonly string[] ExportRosterSlots = ["leftWingerId", "rightWingerId", "centerId", "leftDefensemenId", "rightDefensemenId", "goalieId"];
    private static bool SafeExportId(string id) => id.Length > 0 && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    private static string SnapshotFolder(string root, string id) => Path.Combine(root, "library", "Exported Teams", id);
    private static string Text(JsonObject model, string key) => model[key]?.GetValue<string>() ?? "";
    private static string Fingerprint(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
    private static bool HasExportEvidence(string id, string overrides, string root)
        => File.Exists(Path.Combine(overrides, id + ".team.txt")) || File.Exists(Path.Combine(SnapshotFolder(root, id), "team.txt"));

    internal static IReadOnlyList<ExistingTeam> ExistingTeams(string save, string overrides, string libraryRoot)
    {
        var result = new List<ExistingTeam>();
        if (!Directory.Exists(save)) return result;
        foreach (string file in Directory.EnumerateFiles(save, "CustomTeam-*.json"))
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(file);
                var model = JsonNode.Parse(bytes) as JsonObject;
                if (model == null) continue;
                string id = Text(model, "Id");
                if (!SafeExportId(id) || !Path.GetFileName(file).Equals("CustomTeam-" + id + ".json", StringComparison.OrdinalIgnoreCase)
                    || !HasExportEvidence(id, overrides, libraryRoot)) continue;
                result.Add(new(id, Text(model, "TeamName"), Text(model, "City"), Convert.ToHexString(SHA256.HashData(bytes)), File.GetLastWriteTime(file)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException) { }
        }
        return result.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.Id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static string WriteUpdate(Package package, ExistingTeam selected, string save, string overrides, string libraryRoot)
    {
        if (!SafeExportId(selected.Id)) throw new ArgumentException("Choose a valid exported team.");
        string targetFile = Path.Combine(save, "CustomTeam-" + selected.Id + ".json");
        if (!File.Exists(targetFile) || !HasExportEvidence(selected.Id, overrides, libraryRoot))
            throw new IOException("The selected export is no longer available. Choose the export again.");
        byte[] targetBytes = File.ReadAllBytes(targetFile);
        if (Convert.ToHexString(SHA256.HashData(targetBytes)) != selected.Fingerprint)
            throw new IOException("The selected team changed while the export chooser was open. Choose it again.");
        var previous = JsonNode.Parse(targetBytes) as JsonObject ?? throw new IOException("The selected team cannot be read.");
        if (Text(previous, "Id") != selected.Id) throw new IOException("The selected team's identity changed. Choose it again.");

        // Inspect every native team before reusing a roster ID. An unreadable team
        // makes reference ownership uncertain, so stop instead of editing a player
        // that may belong to another team.
        var nativeTeams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sharedPlayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.EnumerateFiles(save, "CustomTeam-*.json"))
        {
            byte[] bytes = File.ReadAllBytes(file);
            nativeTeams[file] = Convert.ToHexString(SHA256.HashData(bytes));
            var model = JsonNode.Parse(bytes) as JsonObject ?? throw new IOException("A saved team cannot be read. No export was changed.");
            if (Path.GetFullPath(file).Equals(Path.GetFullPath(targetFile), StringComparison.OrdinalIgnoreCase))
            {
                if (nativeTeams[file] != selected.Fingerprint) throw new IOException("The selected team changed during export. Choose it again.");
                continue;
            }
            foreach (string slot in ExportRosterSlots) if (Text(model, slot) is { Length: > 0 } id) sharedPlayers.Add(id);
        }
        var next = JsonNode.Parse(package.Saves["CustomTeam-" + package.TeamId + ".json"]) as JsonObject
            ?? throw new ArgumentException("The export package has no team.");
        var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var reused = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string snapshot = SnapshotFolder(libraryRoot, selected.Id);
        for (int i = 0; i < ExportRosterSlots.Length; i++)
        {
            string slot = ExportRosterSlots[i], incoming = Text(next, slot), old = Text(previous, slot);
            if (!SafeExportId(incoming)) throw new ArgumentException("The export roster is incomplete.");
            string oldFile = Path.Combine(save, (i == 5 ? "CustomGoalie-" : "CustomForward-") + old + ".json");
            // A generic sidecar may belong to a standalone player later assigned
            // in-game. Only this team's saved roster proves ownership for reuse.
            bool owned = SafeExportId(old) && File.Exists(Path.Combine(snapshot, "players", old + ".txt"));
            bool canReuse = owned && !sharedPlayers.Contains(old) && !reused.Contains(old) && File.Exists(oldFile);
            if (canReuse)
            {
                var player = JsonNode.Parse(File.ReadAllText(oldFile)) as JsonObject;
                canReuse = player != null && Text(player, "id") == old;
            }
            string replacement = canReuse ? old : incoming;
            if (canReuse) reused.Add(old);
            ids.Add(incoming, replacement);
            next[slot] = replacement;
        }
        next["Id"] = selected.Id;
        var writes = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var expected = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        void Add(string file, string? text, bool mayReplace)
        {
            EnsureOrdinaryPath(file);
            bool exists = File.Exists(file);
            if (exists && !mayReplace) throw new IOException("An export ID already exists. Nothing was changed; export again.");
            expected[file] = exists ? Fingerprint(file) : null;
            writes.Add(file, text);
        }
        foreach (var (incoming, replacement) in ids)
        {
            string prefix = Text(next, "goalieId") == replacement ? "CustomGoalie-" : "CustomForward-";
            var player = JsonNode.Parse(package.Saves[prefix + incoming + ".json"]) as JsonObject ?? throw new ArgumentException("An exported player cannot be read.");
            player["id"] = replacement;
            var sidecar = new CampaignDocument(package.Overrides[incoming + ".txt"]);
            sidecar.Set("Export Model", player.ToJsonString());
            Add(Path.Combine(overrides, replacement + ".txt"), sidecar.ToString(), reused.Contains(replacement));
            Add(Path.Combine(snapshot, "players", replacement + ".txt"), sidecar.ToString(), true);
            Add(Path.Combine(save, prefix + replacement + ".json"), player.ToJsonString(), reused.Contains(replacement));
        }
        string teamSidecar = package.Overrides[package.TeamId + ".team.txt"];
        Add(Path.Combine(overrides, selected.Id + ".team.txt"), teamSidecar, true);
        Add(Path.Combine(snapshot, "team.txt"), teamSidecar, true);
        string snapshotPlayers = Path.Combine(snapshot, "players");
        if (Directory.Exists(snapshotPlayers))
            foreach (string file in Directory.EnumerateFiles(snapshotPlayers, "*.txt"))
                if (!writes.ContainsKey(file)) Add(file, null, true);
        // Native team publication is last, after its full roster and companion data.
        Add(targetFile, next.ToJsonString(), true);
        CommitUpdate(writes, expected, () =>
        {
            string[] current = Directory.GetFiles(save, "CustomTeam-*.json");
            if (current.Length != nativeTeams.Count || current.Any(f => !nativeTeams.TryGetValue(f, out var hash) || Fingerprint(f) != hash))
                throw new IOException("Saved teams changed during export. Nothing was updated; choose the team again.");
        });
        return targetFile;
    }

    // No linked files/directories are followed when replacing user content.
    private static void EnsureOrdinaryPath(string file)
    {
        for (string? path = Path.GetFullPath(file); path != null; path = Path.GetDirectoryName(path))
            if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked export files or folders cannot be updated.");
    }

    private static void CommitUpdate(Dictionary<string, string?> writes, Dictionary<string, string?> expected, Action validate)
    {
        string transaction = Guid.NewGuid().ToString("N");
        var stages = new Dictionary<string, (string Temp, string Backup)>(StringComparer.OrdinalIgnoreCase);
        var applied = new List<string>();
        var createdDirectories = new List<string>();
        bool preserveRecovery = false;
        try
        {
            foreach (var (file, text) in writes)
            {
                string directory = Path.GetDirectoryName(file)!;
                var missing = new Stack<string>();
                for (string? parent = directory; parent != null && !Directory.Exists(parent); parent = Path.GetDirectoryName(parent)) missing.Push(parent);
                while (missing.Count > 0) { string parent = missing.Pop(); Directory.CreateDirectory(parent); createdDirectories.Add(parent); }
                string temp = Path.Combine(directory, ".t2t-export-" + transaction + "-" + Path.GetFileName(file) + ".tmp");
                string backup = temp + ".bak";
                stages.Add(file, (temp, backup));
                if (expected[file] != null)
                {
                    File.Copy(file, backup, false);
                    if (Fingerprint(backup) != expected[file]) throw new IOException("Export files changed while preparing the update. Try again.");
                }
                if (text != null) File.WriteAllText(temp, text, new UTF8Encoding(false));
            }
            validate();
            foreach (var (file, text) in writes)
            {
                EnsureOrdinaryPath(file);
                if ((File.Exists(file) ? Fingerprint(file) : null) != expected[file])
                    throw new IOException("Export files changed during the update. Try again after closing the game.");
                if (text == null) File.Delete(file); else File.Move(stages[file].Temp, file, true);
                applied.Add(file);
            }
        }
        catch (Exception failure)
        {
            var errors = new List<Exception> { failure };
            foreach (string file in applied.AsEnumerable().Reverse())
            {
                try
                {
                    if (expected[file] == null) File.Delete(file);
                    else File.Move(stages[file].Backup, file, true);
                }
                catch (Exception rollback) { errors.Add(rollback); }
            }
            if (errors.Count > 1)
            {
                preserveRecovery = true;
                throw new AggregateException("Export failed and some files could not be restored. Recovery copies were kept beside the export files (.t2t-export-" + transaction + ").", errors);
            }
            throw;
        }
        finally
        {
            if (!preserveRecovery)
            {
                foreach (var stage in stages.Values)
                    foreach (string file in new[] { stage.Temp, stage.Backup })
                        if (File.Exists(file)) File.Delete(file);
                foreach (string directory in createdDirectories.AsEnumerable().Reverse())
                    if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
            }
        }
    }
}
