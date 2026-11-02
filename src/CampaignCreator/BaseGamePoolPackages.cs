using CampaignCore;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CampaignCreator;

public sealed record BaseGamePoolInfo(string Name, int BenchCount, int FreeAgentCount, int SuperstarCount, bool Enabled);
public sealed record BaseGamePoolBackup(string Id, DateTime CreatedUtc, string Reason, BaseGamePoolInfo? Pools, bool HasDirectory, string Error = "");
public sealed record BaseGamePoolChange(BaseGamePoolInfo? Pools, string BackupId);

public static class BaseGamePoolPackages
{
    private const string Top = "base_game_pools", Marker = "pool-package.json", BackupFolder = "base_game_pool_backups";
    private sealed record Package(int Schema, string Kind, string Name);
    private sealed record SavedFile(string Path, long Size, string Sha256);
    private sealed record Snapshot(int Schema, string Id, DateTime CreatedUtc, string Reason, bool HasDirectory, BaseGamePoolInfo? Pools, SavedFile[] Files);

    public static BaseGamePoolInfo Export(string root, string zip, string name = "Base Game Pools")
    {
        name = name.Trim();
        if (name.Length is < 1 or > 160 || name.Any(char.IsControl)) throw new ArgumentException("Enter a pool set name of 1 to 160 characters.");
        string source = BaseGamePlayerPools.Folder(root);
        SafeTree(source);
        if (!File.Exists(Path.Combine(source, "settings.txt"))) throw new InvalidDataException("Save your Base Game Pools before uploading them.");
        var selection = BaseGamePlayerPools.Load(root);
        using var stage = new Staging();
        string profile = BaseGamePlayerPools.Folder(stage.Root);
        foreach (string kind in BaseGamePlayerPools.Kinds)
            foreach (string file in BaseGamePlayerPools.SelectedFiles(root, kind, selection))
            {
                if (!ContentImport.Matches(file, "skater")) throw new InvalidDataException("Pool candidates must be skaters: " + Path.GetFileName(file));
                string target = Path.Combine(BaseGamePlayerPools.PoolDirectory(stage.Root, kind), Path.GetFileName(file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
            }
        // Embed only the rules referenced by selected candidates, never the whole library.
        var definitions = CustomRewardCatalog.Items(root, source);
        var embedded = new Dictionary<string, CustomRewardDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.GetFiles(profile, "*.txt", SearchOption.AllDirectories))
            foreach (var item in CustomRewardExport.Read(new CampaignDocument(File.ReadAllText(file)))) CustomRewardExport.AddSnapshot(embedded, item);
        foreach (var item in embedded.Values) definitions[item.Id] = item;
        foreach (string file in Directory.GetFiles(profile, "*.txt", SearchOption.AllDirectories))
        {
            var document = new CampaignDocument(File.ReadAllText(file));
            var referenced = new Dictionary<string, CustomRewardDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (string key in new[] { "Talents", "Team Talents", "Relics", "Team Relics" })
                foreach (string value in document.Get(key).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var item = definitions.GetValueOrDefault(value) ?? definitions.Values.FirstOrDefault(d => d.Name.Equals(value, StringComparison.OrdinalIgnoreCase));
                    if (item != null) referenced[item.Id] = item;
                    else if (value.StartsWith("custom_", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Missing custom reward: " + value);
                }
            CustomRewardExport.Embed(document, referenced.Values); document.Save(file);
        }
        BaseGamePlayerPools.Save(stage.Root, selection);
        File.WriteAllText(Path.Combine(profile, Marker), JsonSerializer.Serialize(new Package(1, "base-game-pools", name)));
        // Source-owned assets can live alongside a previously imported profile.
        CampaignSharing.WritePoolArchive(profile, source, root, zip);
        return Describe(name, selection);
    }

    public static BaseGamePoolInfo Inspect(string zip)
    {
        using var stage = ReadPackage(zip);
        return ValidatePackage(stage.Root);
    }

    public static BaseGamePoolChange Install(string zip, string root)
    {
        using var stage = ReadPackage(zip, root);
        var info = ValidatePackage(stage.Root);
        using var lease = Lock(root);
        var selection = BaseGamePlayerPools.Load(stage.Root); selection.Enabled = true;
        BaseGamePlayerPools.Save(stage.Root, selection);
        using var assets = CampaignSharing.InstallPoolAssets(BaseGamePlayerPools.Folder(stage.Root), root);
        string backup = Replace(BaseGamePlayerPools.Folder(stage.Root), root, "Before download: " + info.Name);
        assets.Keep();
        return new(info with { Enabled = true }, backup);
    }

    public static IReadOnlyList<BaseGamePoolBackup> Backups(string root)
    {
        string folder = Path.Combine(root, BackupFolder);
        if (!Directory.Exists(folder)) return [];
        SafeAncestors(folder);
        var result = new List<BaseGamePoolBackup>();
        foreach (string directory in Directory.GetDirectories(folder))
        {
            string id = Path.GetFileName(directory);
            if (!ValidId(id)) continue;
            try
            {
                var saved = ReadSnapshot(root, id);
                result.Add(new(id, saved.CreatedUtc, saved.Reason, saved.Pools, saved.HasDirectory));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or JsonException)
            { result.Add(new(id, Directory.GetCreationTimeUtc(directory), "Unavailable backup", null, true, ex.Message)); }
        }
        return result.OrderByDescending(b => b.CreatedUtc).ToArray();
    }

    public static BaseGamePoolChange Restore(string root, string backupId)
    {
        using var lease = Lock(root);
        var saved = ReadSnapshot(root, backupId);
        string source = Path.Combine(root, BackupFolder, backupId, "profile");
        var actual = Files(source);
        if (!actual.SequenceEqual(saved.Files)) throw new InvalidDataException("This backup is incomplete or has changed. Your current pools were kept.");
        using var stage = new Staging(root);
        string? incoming = null;
        if (saved.HasDirectory)
        {
            incoming = BaseGamePlayerPools.Folder(stage.Root); Directory.CreateDirectory(incoming);
            foreach (var file in saved.Files)
            {
                string target = Path.Combine(incoming, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(Path.Combine(source, file.Path), target);
            }
            if (!Files(incoming).SequenceEqual(saved.Files)) throw new InvalidDataException("The backup changed while loading it. Your current pools were kept.");
        }
        string backup = Replace(incoming, root, "Before restoring " + saved.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
        return new(saved.Pools, backup);
    }

    private static BaseGamePoolInfo Describe(string name, BaseGamePoolSelection selection) => new(name,
        selection.Pools["draft_pool"].Length, selection.Pools["free_agents"].Length, selection.Pools["superstars"].Length, selection.Enabled);

    private static Staging ReadPackage(string zip, string? root = null)
    {
        var stage = new Staging(root);
        try
        {
            using var archive = ZipFile.OpenRead(zip);
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in archive.Entries)
            {
                string path = entry.FullName.Replace('\\', '/');
                bool directory = path.EndsWith('/');
                string[] parts = path.TrimEnd('/').Split('/');
                if (parts.Length < 1 || parts[0] != Top || parts.Any(p => !SafePart(p))
                    || !paths.Add(path.TrimEnd('/')) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000
                    || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("The pool ZIP contains an unsafe or duplicate path.");
                if (directory) continue;
                if (parts.Length < 2 || !AllowedEntry(parts[1..])) throw new InvalidDataException("The pool ZIP contains an unrelated file: " + path);
                string destination = Path.Combine(stage.Root, Path.Combine(parts));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!); entry.ExtractToFile(destination);
            }
            ValidatePackage(stage.Root);
            return stage;
        }
        catch { stage.Dispose(); throw; }
    }

    private static bool AllowedEntry(string[] parts) => parts.Length == 1 && parts[0] is Marker or "settings.txt"
        || parts.Length == 3 && parts[0] == "player_teams" && BaseGamePlayerPools.Kinds.Contains(parts[1]) && parts[2].EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
        || parts.Length == 2 && parts[0] == CampaignSharing.LogoBundle && parts[1].EndsWith(".png", StringComparison.OrdinalIgnoreCase)
        || parts.Length >= 3 && parts[0] == CampaignSharing.HeadBundle && (parts[^1].EndsWith(".png", StringComparison.OrdinalIgnoreCase) || parts[^1] is "head-project.json" or "README.txt");

    private static bool SafePart(string part) => part.Length > 0 && part is not ("." or "..") && !part.EndsWith('.') && !part.EndsWith(' ')
        && !part.Any(c => c < 32 || "<>:\"|?*".Contains(c)) && !Regex.IsMatch(part, @"\A(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase);

    private static BaseGamePoolInfo ValidatePackage(string root)
    {
        string profile = BaseGamePlayerPools.Folder(root), marker = Path.Combine(profile, Marker);
        if (!File.Exists(marker) || !File.Exists(Path.Combine(profile, "settings.txt"))) throw new InvalidDataException("Choose a Base Game Pools package, not a campaign ZIP.");
        Package? package;
        try { package = JsonSerializer.Deserialize<Package>(File.ReadAllText(marker)); }
        catch (JsonException ex) { throw new InvalidDataException("The pool package description is invalid.", ex); }
        if (package?.Schema != 1 || package.Kind != "base-game-pools" || string.IsNullOrWhiteSpace(package.Name) || package.Name.Length > 160 || package.Name.Any(char.IsControl))
            throw new InvalidDataException("Unsupported Base Game Pools package.");
        var selected = BaseGamePlayerPools.Load(root);
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rewards = new Dictionary<string, CustomRewardDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (string kind in BaseGamePlayerPools.Kinds)
            foreach (string file in BaseGamePlayerPools.SelectedFiles(root, kind, selected))
            {
                expected.Add(Path.GetFullPath(file));
                if (!ContentImport.Matches(file, "skater")) throw new InvalidDataException("Pool candidates must be skaters.");
                var doc = new CampaignDocument(File.ReadAllText(file));
                foreach (var item in CustomRewardExport.Read(doc)) CustomRewardExport.AddSnapshot(rewards, item);
            }
        if (Directory.GetFiles(Path.Combine(profile, "player_teams"), "*", SearchOption.AllDirectories).Any(f => !expected.Contains(Path.GetFullPath(f))))
            throw new InvalidDataException("The pool ZIP contains unselected player files.");
        CampaignSharing.ValidatePoolAssets(profile, root);
        return Describe(package.Name, selected);
    }

    private static string Replace(string? incoming, string root, string reason)
    {
        string current = BaseGamePlayerPools.Folder(root), backups = Path.Combine(root, BackupFolder);
        SafeAncestors(current); SafeAncestors(backups); SafeTree(current);
        if (File.Exists(current)) throw new IOException("The Base Game Pools folder is occupied by a file.");
        DateTime now = DateTime.UtcNow;
        string id = now.ToString("yyyyMMdd-HHmmssfff-") + Guid.NewGuid().ToString("N");
        string backup = Path.Combine(backups, id), previous = Path.Combine(backup, "profile");
        bool hadDirectory = Directory.Exists(current), movedOld = false, movedNew = false;
        BaseGamePoolInfo? info = null;
        if (File.Exists(Path.Combine(current, "settings.txt")))
            try { info = Describe("Saved pools", BaseGamePlayerPools.Load(root)); } catch (Exception ex) when (ex is IOException or ArgumentException) { /* Preserve even a damaged original. */ }
        var snapshot = new Snapshot(1, id, now, reason, hadDirectory, info, Files(current));
        Directory.CreateDirectory(backup);
        try
        {
            File.WriteAllText(Path.Combine(backup, "backup.json"), JsonSerializer.Serialize(snapshot));
            if (hadDirectory) { Directory.Move(current, previous); movedOld = true; }
            if (incoming != null) { Directory.Move(incoming, current); movedNew = true; }
            return id;
        }
        catch
        {
            if (movedNew) Directory.Move(current, incoming!);
            if (movedOld) Directory.Move(previous, current);
            Directory.Delete(backup, true);
            throw;
        }
    }

    private static bool ValidId(string id) => Regex.IsMatch(id, @"\A[0-9]{8}-[0-9]{9}-[0-9a-f]{32}\z");
    private static Snapshot ReadSnapshot(string root, string id)
    {
        if (!ValidId(id)) throw new ArgumentException("Choose a saved Base Game Pools backup.");
        string folder = Path.Combine(root, BackupFolder, id); SafeAncestors(folder); SafeTree(folder);
        Snapshot? saved;
        try { saved = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(Path.Combine(folder, "backup.json"))); }
        catch (JsonException ex) { throw new InvalidDataException("This backup's record is unreadable.", ex); }
        if (saved?.Schema != 1 || saved.Id != id || saved.Files == null || saved.HasDirectory != Directory.Exists(Path.Combine(folder, "profile"))
            || saved.Files.Any(f => f == null || string.IsNullOrWhiteSpace(f.Path) || string.IsNullOrWhiteSpace(f.Sha256)
                || f.Path.Replace('\\', '/').Split('/').Any(p => !SafePart(p)) || f.Size < 0 || !Regex.IsMatch(f.Sha256, @"\A[0-9A-F]{64}\z"))
            || saved.Files.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != saved.Files.Length)
            throw new InvalidDataException("This backup is incomplete or invalid.");
        return saved;
    }

    private static SavedFile[] Files(string folder)
    {
        if (!Directory.Exists(folder)) return [];
        SafeTree(folder);
        return Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Select(file => new SavedFile(Path.GetRelativePath(folder, file), new FileInfo(file).Length, Hash(file)))
            .OrderBy(f => f.Path, StringComparer.Ordinal).ToArray();
    }
    private static string Hash(string file) { using var stream = File.OpenRead(file); return Convert.ToHexString(SHA256.HashData(stream)); }
    internal static void SafeAncestors(string path)
    {
        for (string? current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Pool files cannot be installed through a symbolic link: " + current);
    }
    private static void SafeTree(string folder)
    {
        SafeAncestors(folder);
        if (!Directory.Exists(folder)) return;
        foreach (string entry in Directory.EnumerateFileSystemEntries(folder))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("A pool folder contains a symbolic link.");
            if ((attributes & FileAttributes.Directory) != 0) SafeTree(entry);
        }
    }
    private static FileStream Lock(string root)
    {
        SafeAncestors(root); Directory.CreateDirectory(root);
        string path = Path.Combine(root, ".base-game-pools.lock"); SafeAncestors(path);
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    private sealed class Staging : IDisposable
    {
        public string Root { get; }
        public Staging(string? contentRoot = null)
        {
            string parent = contentRoot == null ? Path.GetTempPath() : Path.Combine(contentRoot, ".studio-drafts");
            SafeAncestors(parent);
            Root = Path.Combine(parent, "pool-package-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Root);
        }
        public void Dispose() { if (Directory.Exists(Root)) { SafeTree(Root); Directory.Delete(Root, true); } }
    }
}
