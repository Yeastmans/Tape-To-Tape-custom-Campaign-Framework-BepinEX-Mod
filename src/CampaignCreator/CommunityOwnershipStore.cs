using System.Diagnostics;
using System.Text.Json;

namespace CampaignCreator;

internal sealed record CommunityOwnership(string Key, string Code, string Name, string Path, string Id = "");

/// <summary>Private local management codes, kept outside campaign/export folders.</summary>
internal sealed class CommunityOwnershipStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string filePath;

    internal CommunityOwnershipStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        this.filePath = System.IO.Path.GetFullPath(filePath);
    }

    internal IReadOnlyList<CommunityOwnership> Load()
    {
        using var held = AcquireLock();
        return ReadLocked().AsReadOnly();
    }

    internal void Save(CommunityOwnership record)
    {
        using var held = AcquireLock();
        var entries = ReadLocked();
        Validate([record]);
        int existing = entries.FindIndex(x => x.Key == record.Key);
        if (existing < 0) entries.Add(record);
        else entries[existing] = record;
        Validate(entries);
        WriteLocked(entries);
    }

    internal CommunityOwnership? Find(string path, string id = "")
    {
        var entries = Load();
        if (!string.IsNullOrEmpty(id))
        {
            var identified = entries.FirstOrDefault(x => x.Id == id);
            if (identified != null) return identified;
        }
        return entries.FirstOrDefault(x => x.Path.Equals(path, StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrEmpty(id) || x.Id.Length == 0));
    }

    internal bool Remove(string key)
    {
        using var held = AcquireLock();
        var entries = ReadLocked();
        if (entries.RemoveAll(x => x.Key == key) == 0) return false;
        WriteLocked(entries);
        return true;
    }

    private FileStream AcquireLock()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(filePath)!);
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                // Keep this empty lock file in place: deleting it could separate competing writers.
                return new FileStream(filePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException ex) when ((ex.HResult & 0xffff) is 32 or 33)
            {
                if (elapsed.Elapsed >= TimeSpan.FromSeconds(10))
                    throw new IOException("The saved community management codes are busy in another Studio window. Try again shortly.", ex);
                Thread.Sleep(20);
            }
        }
    }

    private List<CommunityOwnership> ReadLocked()
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var entries = JsonSerializer.Deserialize<List<CommunityOwnership>>(stream, JsonOptions)
                ?? throw InvalidStore();
            Validate(entries);
            return entries;
        }
        catch (FileNotFoundException) { return []; }
        catch (JsonException ex) { throw InvalidStore(ex); }
    }

    private void WriteLocked(List<CommunityOwnership> entries)
    {
        string temporary = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, entries, JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            // Both files are in the same directory; never delete the old store before replacement.
            if (File.Exists(filePath)) File.Replace(temporary, filePath, null);
            else File.Move(temporary, filePath);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void Validate(IEnumerable<CommunityOwnership> entries)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Key) || string.IsNullOrWhiteSpace(entry.Code)
                || string.IsNullOrWhiteSpace(entry.Name) || string.IsNullOrWhiteSpace(entry.Path) || entry.Id == null
                || !keys.Add(entry.Key) || !paths.Add(entry.Path) || (entry.Id.Length > 0 && !ids.Add(entry.Id)))
                throw InvalidStore();
        }
    }

    private static InvalidDataException InvalidStore(Exception? inner = null) => new(
        "The saved community management codes are damaged or contain conflicting campaign identities. The file has been left unchanged.", inner);
}
