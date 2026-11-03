using CampaignCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CampaignCreator;

/// <summary>Selection drafts stay in memory until Save; player files are independent private copies.</summary>
public sealed class BaseGamePoolsContent
{
    public string Root { get; }
    public BaseGamePoolSelection Selection { get; private set; }
    public bool IsRecoveryDraft { get; private set; }
    private readonly Dictionary<string, HashSet<string>> currentDefaults = new(StringComparer.OrdinalIgnoreCase);
    private BaseGamePoolsContent(string root, BaseGamePoolSelection selection)
    {
        Root = root; Selection = selection;
    }
    public BaseGamePoolsContent(string root, string? cache) : this(root, BaseGamePlayerPools.Load(root))
    {
        if (!File.Exists(Path.Combine(BaseGamePlayerPools.Folder(root), "settings.txt")))
        {
            PrepareDefaults(cache, true);
            Selection.Enabled = true; // Draft only: runtime remains inert until the first explicit Save.
        }
    }
    public static BaseGamePoolsContent RecoverDefaults(string root, string? cache)
    {
        string settings = Path.Combine(BaseGamePlayerPools.Folder(root), "settings.txt");
        var profile = new BaseGamePoolsContent(root, new BaseGamePoolSelection { Enabled = true });
        if (File.Exists(settings))
        {
            // Respect a clear saved switch even if a referenced candidate is missing.
            // Ambiguous or unreadable switches start enabled in the unsaved recovery draft.
            var switches = File.ReadAllLines(settings).Select(line => CampaignDocument.TryReadField(line, out string key, out string value)
                && key.Equals("enabled", StringComparison.OrdinalIgnoreCase) ? value : null).OfType<string>().ToArray();
            if (switches.Length == 1 && switches[0].Equals("no", StringComparison.OrdinalIgnoreCase)) profile.Selection.Enabled = false;
        }
        profile.PrepareDefaults(cache, false);
        if (File.Exists(settings)) File.Copy(settings, settings + ".recovery-" + Guid.NewGuid().ToString("N") + ".bak");
        profile.IsRecoveryDraft = true;
        return profile;
    }
    public string[] Candidates(string kind)
    {
        string directory = BaseGamePlayerPools.PoolDirectory(Root, kind);
        var selected = Selection.Pools[kind];
        var result = selected.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var seenFiles = new HashSet<string>(result, StringComparer.OrdinalIgnoreCase);
        var seenNative = new HashSet<(string Id, string Asset)>();
        foreach (string file in result)
            if (NativeIdentity(Path.Combine(directory, file)) is { } identity) seenNative.Add(identity);
        if (!Directory.Exists(directory)) return result.ToArray();
        // Prefer this draft's restored copy. After reopening, the newest private copy
        // represents an unchecked identity. Selected variants always remain visible.
        foreach (string path in Directory.GetFiles(directory, "*.txt")
            .OrderByDescending(f => currentDefaults.TryGetValue(kind, out var defaults) && defaults.Contains(Path.GetFileName(f)))
            .ThenByDescending(File.GetLastWriteTimeUtc).ThenBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            string file = Path.GetFileName(path);
            if (!seenFiles.Add(file)) continue;
            if (NativeIdentity(path) is { } identity && !seenNative.Add(identity)) continue;
            result.Add(file);
        }
        return result.ToArray();
    }
    private static (string Id, string Asset)? NativeIdentity(string file)
    {
        if (!File.Exists(file)) return null;
        var doc = new CampaignDocument(File.ReadAllText(file));
        string id = doc.Get(VanillaCopyData.PlayerId), asset = doc.Get(VanillaCopyData.PlayerAsset);
        return id.Length > 0 && asset.Length > 0 ? (id, asset) : null;
    }
    public string ImportCandidate(string kind, string source, string? replace = null)
    {
        string directory = BaseGamePlayerPools.PoolDirectory(Root, kind);
        if (replace != null && !Candidates(kind).Contains(replace, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("Choose a candidate to replace.");
        if (!ContentImport.Matches(source, "skater")) throw new ArgumentException("Choose a skater for this pool.");
        var sourceDoc = ContentImport.ReadDocument(source);
        var doc = CreatorContent.CustomPlayerCopy(source, CreatorContent.AvailablePlayerName(directory, sourceDoc.Get("Name", sourceDoc.Get("Import Player"))));
        doc.Remove(VanillaCopyData.PlayerId); doc.Remove(VanillaCopyData.PlayerAsset); doc.Remove("Studio Base Pool Player");
        string file = "custom-" + Guid.NewGuid().ToString("N") + ".txt";
        Directory.CreateDirectory(directory); doc.Save(Path.Combine(directory, file));
        var current = Selection.Pools[kind];
        Selection.Pools[kind] = replace == null ? current.Concat([file]).ToArray()
            : current.Select(f => f.Equals(replace, StringComparison.OrdinalIgnoreCase) ? file : f).ToArray();
        return file;
    }
    public void SetSelected(string kind, string file, bool selected)
    {
        BaseGamePlayerPools.CustomPlayerId(kind, file);
        if (selected && !File.Exists(Path.Combine(BaseGamePlayerPools.PoolDirectory(Root, kind), file))) throw new FileNotFoundException("Candidate file is missing.");
        var current = Selection.Pools[kind];
        Selection.Pools[kind] = selected ? current.Concat([file]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : current.Where(f => !f.Equals(file, StringComparison.OrdinalIgnoreCase)).ToArray();
    }
    public void Move(string kind, string file, int delta)
    {
        var current = Selection.Pools[kind].ToList();
        int from = current.FindIndex(f => f.Equals(file, StringComparison.OrdinalIgnoreCase)), to = from + delta;
        if (from < 0 || to < 0 || to >= current.Count) return;
        (current[from], current[to]) = (current[to], current[from]); Selection.Pools[kind] = current.ToArray();
    }
    public void RemoveCandidate(string kind, string file)
    {
        BaseGamePlayerPools.CustomPlayerId(kind, file);
        if (!Candidates(kind).Contains(file, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("Choose a candidate from this pool.");
        var next = new BaseGamePoolSelection { Enabled = Selection.Enabled };
        foreach (string pool in BaseGamePlayerPools.Kinds)
            next.Pools[pool] = pool == kind ? Selection.Pools[pool].Where(f => !f.Equals(file, StringComparison.OrdinalIgnoreCase)).ToArray() : Selection.Pools[pool].ToArray();
        // Validate all current draft selections before moving any copy.
        foreach (string pool in BaseGamePlayerPools.Kinds) _ = BaseGamePlayerPools.SelectedFiles(Root, pool, next);
        string directory = BaseGamePlayerPools.PoolDirectory(Root, kind);
        var files = new List<string> { file };
        if (NativeIdentity(Path.Combine(directory, file)) is { } identity)
            files.AddRange(Directory.GetFiles(directory, "*.txt").Where(path => NativeIdentity(path) == identity)
                .Select(Path.GetFileName).OfType<string>().Where(name => !name.Equals(file, StringComparison.OrdinalIgnoreCase) && !next.Pools[kind].Contains(name, StringComparer.OrdinalIgnoreCase)));
        ContentRemoval.ArchiveFiles(directory, files, () => BaseGamePlayerPools.Save(Root, next));
        Selection = next; IsRecoveryDraft = false;
    }
    public void RestoreDefaults(string? cache) => PrepareDefaults(cache, false);
    private void PrepareDefaults(string? cache, bool preserveExisting)
    {
        if (cache == null) throw new InvalidOperationException("Read game artwork on Home to load the installed game's standard candidates.");
        string source = Path.GetFullPath(Path.Combine(cache, "base-campaigns"));
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(source, "catalog.json")));
        var catalog = json.RootElement;
        if (catalog.GetProperty("version").GetInt32() != 1) throw new InvalidDataException("Unsupported base campaign catalog. Read game artwork again.");
        var vanilla = catalog.GetProperty("campaigns").EnumerateArray().Single(c => c.GetProperty("id").GetString() == "vanilla");
        var staged = new Dictionary<string, List<(string File, CampaignDocument Document)>>();
        foreach (var (kind, field) in new[] { ("draft_pool", "bench"), ("free_agents", "freeAgents"), ("superstars", "superstars") })
        {
            staged[kind] = [];
            foreach (var entry in vanilla.GetProperty(field).EnumerateArray())
            {
                string key = entry.GetString()!;
                var metadata = catalog.GetProperty("players").GetProperty(key);
                string file = Path.GetFullPath(Path.Combine(source, metadata.GetProperty("file").GetString()!));
                if (!file.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid native candidate source path.");
                var doc = new CampaignDocument(File.ReadAllText(file)); doc.Set("Override Team Settings", "no");
                string identity = key + "|" + doc.Get(VanillaCopyData.PlayerId) + "|" + doc.Get(VanillaCopyData.PlayerAsset);
                string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
                string name = "native-" + digest + (preserveExisting ? "" : "-" + Guid.NewGuid().ToString("N")) + ".txt";
                staged[kind].Add((name, doc));
            }
            if (staged[kind].Count == 0) throw new InvalidDataException("The game's standard " + kind + " pool is empty. Read game artwork again.");
        }
        foreach (var (kind, players) in staged)
        {
            string directory = BaseGamePlayerPools.PoolDirectory(Root, kind); Directory.CreateDirectory(directory);
            foreach (var player in players)
            {
                string target = Path.Combine(directory, player.File);
                if (!preserveExisting || !File.Exists(target)) player.Document.Save(target);
            }
        }
        // A missing source or failed write never replaces any of the three selection drafts.
        foreach (var (kind, players) in staged)
        {
            Selection.Pools[kind] = players.Select(p => p.File).ToArray();
            currentDefaults[kind] = new HashSet<string>(Selection.Pools[kind], StringComparer.OrdinalIgnoreCase);
        }
    }
    public void Save()
    {
        BaseGamePlayerPools.Save(Root, Selection);
        IsRecoveryDraft = false;
    }
}
