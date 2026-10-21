using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CampaignCore;

// A prepared operation is a durable decision to complete BOTH halves. A caught
// failure first durably changes that decision to rollback. Replacing the native
// writer avoids ever truncating a live file, including same-native/line-two saves.
public static class PlayNowSaveJournal
{
    public sealed class Record
    {
        public int Version { get; set; } = 1;
        public string Transaction { get; set; } = Guid.NewGuid().ToString("N");
        public string Kind { get; set; }
        public string Id { get; set; }
        public bool Rollback { get; set; }
        public Entry Native { get; set; }
        public Entry Companion { get; set; }
    }
    public sealed class Entry
    {
        public string Path { get; set; }
        public byte[] Before { get; set; }
        public byte[] After { get; set; }
        public string BeforeHash { get; set; }
        public string AfterHash { get; set; }
    }
    public static bool SafeId(string id) => !string.IsNullOrEmpty(id) && id.All(c =>
        c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '-' || c == '_');
    public static string Hash(byte[] bytes) => bytes == null ? "missing" : Convert.ToHexString(SHA256.HashData(bytes));
    public static byte[] Read(string path) => File.Exists(path) ? File.ReadAllBytes(path) : null;
    public static string NativePath(string root, string kind, string id)
    {
        if (!SafeId(id)) throw new InvalidDataException("Unsafe player/team identity.");
        string prefix = kind switch { "team" => "CustomTeam-", "forward" => "CustomForward-", "goalie" => "CustomGoalie-", _ => throw new InvalidDataException("Unknown record role.") };
        return Path.GetFullPath(Path.Combine(root, "TeamDataModels", prefix + id + ".json"));
    }
    public static string CompanionPath(string root, string kind, string id)
    {
        NativePath(root, kind, id); // validates role and stable identity
        return Path.GetFullPath(Path.Combine(root, id + (kind == "team" ? ".team" : "") + ".txt"));
    }
    private static string JournalPath(string root, string kind, string id)
        => Path.Combine(root, "save-journal", kind + "-" + id + ".json");
    private static void Ordinary(string path)
    {
        for (string current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked Play Now save paths are not supported.");
    }
    private static FileStream Lock(string overrides)
    {
        Ordinary(overrides);
        Directory.CreateDirectory(overrides);
        string path = Path.Combine(overrides, "save-journal.lock"); Ordinary(path);
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    private static void Atomic(string path, byte[] bytes)
    {
        Ordinary(path);
        if (bytes == null) { File.Delete(path); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string stage = path + ".stage-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(stage, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
            File.Move(stage, path, true);
        }
        finally { if (File.Exists(stage)) File.Delete(stage); }
    }
    private static void Validate(Record r, string nativeRoot, string overrides, string journal)
    {
        if (r == null || r.Version != 1 || !Guid.TryParseExact(r.Transaction, "N", out _)
            || r.Native == null || r.Companion == null || !SafeId(r.Id)
            || Path.GetFullPath(journal) != Path.GetFullPath(JournalPath(overrides, r.Kind, r.Id)))
            throw new InvalidDataException("Invalid Play Now journal identity.");
        if (r.Native.Path != NativePath(nativeRoot, r.Kind, r.Id) || r.Companion.Path != CompanionPath(overrides, r.Kind, r.Id))
            throw new InvalidDataException("Play Now journal does not belong to this repository.");
        foreach (var e in new[] { r.Native, r.Companion })
        {
            Ordinary(e.Path);
            if (e.After == null || Hash(e.Before) != e.BeforeHash || Hash(e.After) != e.AfterHash)
                throw new InvalidDataException("Play Now journal fingerprint mismatch.");
        }
        using var model = JsonDocument.Parse(r.Native.After);
        if (!model.RootElement.TryGetProperty(r.Kind == "team" ? "Id" : "id", out var id) || id.GetString() != r.Id)
            throw new InvalidDataException("Play Now native identity mismatch.");
        var doc = new CampaignDocument(Encoding.UTF8.GetString(r.Companion.After));
        if (doc.Get("Export Save Transaction") != r.Transaction)
            throw new InvalidDataException("Play Now companion revision mismatch.");
        if (r.Kind == "team")
        {
            if (!PlayNowRosterCodec.TryRead(doc, out var roster, out string error)) throw new InvalidDataException("Invalid saved roster: " + error);
            var keys = PlayNowPositions.NativeFields.Append("goalieId").ToArray();
            var expected = roster.Line1.Append(roster.GoalieId).ToArray();
            for (int i = 0; i < keys.Length; i++)
                if (!model.RootElement.TryGetProperty(keys[i], out var slot) || slot.GetString() != expected[i])
                    throw new InvalidDataException("Native fallback must be absolute Line 1 and the saved goalie.");
        }
    }
    private static void Owned(Record r)
    {
        foreach (var e in new[] { r.Native, r.Companion })
        {
            string current = Hash(Read(e.Path));
            if (current != e.BeforeHash && current != e.AfterHash)
                throw new IOException("A competing edit prevents Play Now save recovery: " + e.Path);
        }
    }
    private static void RecoverRecord(string journal, string nativeRoot, string overrides)
    {
        Ordinary(journal);
        var r = JsonSerializer.Deserialize<Record>(File.ReadAllBytes(journal));
        Validate(r, nativeRoot, overrides, journal); Owned(r);
        foreach (var e in r.Rollback ? new[] { r.Companion, r.Native } : new[] { r.Native, r.Companion })
        {
            byte[] wanted = r.Rollback ? e.Before : e.After;
            if (Hash(Read(e.Path)) != Hash(wanted)) Atomic(e.Path, wanted);
        }
        foreach (var e in new[] { r.Native, r.Companion })
            if (Hash(Read(e.Path)) != (r.Rollback ? e.BeforeHash : e.AfterHash)) throw new IOException("Play Now pair verification failed.");
        File.Delete(journal);
    }
    public static bool Recover(string nativeRoot, string overrides, string kind)
    {
        using var gate = Lock(overrides);
        string folder = Path.Combine(overrides, "save-journal"); Ordinary(folder);
        bool changed = false;
        if (Directory.Exists(folder)) foreach (string journal in Directory.GetFiles(folder, kind + "-*.json").OrderBy(p => p, StringComparer.Ordinal))
        { RecoverRecord(journal, nativeRoot, overrides); changed = true; }
        return changed;
    }
    // fault is a fixture seam at durable boundaries: prepared=0, native=1,
    // companion=2, verified=3, rollback-decision=4. Production supplies null.
    public static void Commit(string nativeRoot, string overrides, string kind, string id, string nativeJson,
        CampaignDocument companion, string expectedNativeHash, string expectedCompanionHash,
        Action validateReferences = null, Action<int> fault = null)
    {
        using var gate = Lock(overrides);
        string journal = JournalPath(overrides, kind, id); Ordinary(journal);
        if (File.Exists(journal)) RecoverRecord(journal, nativeRoot, overrides);
        Entry EntryFor(string path, byte[] after) { byte[] before = Read(path); return new Entry { Path = path, Before = before, After = after, BeforeHash = Hash(before), AfterHash = Hash(after) }; }
        var r = new Record { Kind = kind, Id = id };
        var doc = new CampaignDocument(companion.ToString());
        doc.Set("Export Save Transaction", r.Transaction);
        long.TryParse(doc.Get("Export Save Generation"), out long generation);
        doc.Set("Export Save Generation", checked(generation + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
        r.Native = EntryFor(NativePath(nativeRoot, kind, id), Encoding.UTF8.GetBytes(nativeJson));
        r.Companion = EntryFor(CompanionPath(overrides, kind, id), Encoding.UTF8.GetBytes(doc.ToString()));
        Validate(r, nativeRoot, overrides, journal);
        if (r.Native.BeforeHash != expectedNativeHash || r.Companion.BeforeHash != expectedCompanionHash)
            throw new IOException("Play Now save changed since this edit began.");
        validateReferences?.Invoke();
        Atomic(journal, JsonSerializer.SerializeToUtf8Bytes(r));
        try
        {
            fault?.Invoke(0); Owned(r); validateReferences?.Invoke();
            Atomic(r.Native.Path, r.Native.After); fault?.Invoke(1);
            Owned(r);
            Atomic(r.Companion.Path, r.Companion.After); fault?.Invoke(2);
            Owned(r); fault?.Invoke(3);
            RecoverRecord(journal, nativeRoot, overrides);
        }
        catch
        {
            r.Rollback = true;
            Atomic(journal, JsonSerializer.SerializeToUtf8Bytes(r));
            fault?.Invoke(4);
            RecoverRecord(journal, nativeRoot, overrides);
            throw;
        }
    }
}
