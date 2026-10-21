using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace CampaignCore;

// A prepare is immutable save intent, not evidence that native persistence ran.
// Only the exact marker selected by native storage may commit/recover it.
// Old records are deliberately retained: native ES3 backups can select them.
public static class GoalieRosterStore
{
    public const string FolderName = "_goalie_rosters";
    public const string MarkerKey = "ccf.goalieCheckpoint/v1";
    public static Action<string> Fault { get; set; }
    [DataContract]
    private sealed class Journal
    {
        [DataMember(Order = 1)] public int Version = 1;
        [DataMember(Order = 2)] public string Payload;
        [DataMember(Order = 3)] public string Marker;
        [DataMember(Order = 4)] public bool Committed;
    }
    [DataContract]
    private sealed class CheckpointMarker
    {
        [DataMember(Order = 1)] public int Version = 1;
        [DataMember(Order = 2)] public string Transaction;
        [DataMember(Order = 3)] public long Revision;
        [DataMember(Order = 4)] public string Campaign;
        [DataMember(Order = 5)] public string Run;
        [DataMember(Order = 6)] public string Squad;
        [DataMember(Order = 7)] public string Native;
        [DataMember(Order = 8)] public string PayloadHash;
    }
    public static string Hash(string value)
    {
        using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "");
    }
    public static string CampaignIdentity(string folder) => Hash(Path.GetFullPath(folder).TrimEnd('\\', '/').ToUpperInvariant());
    private static string Serialize<T>(T value)
    {
        using var stream = new MemoryStream();
        new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
    private static T Deserialize<T>(string text)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
    }
    private static string MarkerFor(GoalieRosterState state, string payload) => Serialize(new CheckpointMarker {
        Transaction = state.CheckpointId, Revision = state.Revision, Campaign = state.CampaignId, Run = state.RunId,
        Squad = state.SquadId, Native = state.NativeFingerprint, PayloadHash = Hash(payload) });
    private static string Name(GoalieRosterState state, string marker)
        => "r-" + state.Revision.ToString("D20", CultureInfo.InvariantCulture) + "-" + Hash(marker) + ".record";
    private static void Ordinary(string path)
    {
        for (string current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked goalie checkpoint paths are not supported.");
    }
    private static FileStream Lock(string folder)
    {
        Ordinary(folder); Directory.CreateDirectory(folder);
        return new FileStream(Path.Combine(folder, "journal.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    private static void Atomic(string path, string value, string operation)
    {
        Ordinary(path);
        string stage = path + ".stage-" + Guid.NewGuid().ToString("N");
        try
        {
            Fault?.Invoke(operation + ":before-write");
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            using (var stream = new FileStream(stage, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
            Fault?.Invoke(operation + ":after-write");
            Fault?.Invoke(operation + ":before-publish");
            if (File.Exists(path)) File.Replace(stage, path, null); else File.Move(stage, path);
            Fault?.Invoke(operation + ":after-publish");
        }
        finally { if (File.Exists(stage)) File.Delete(stage); }
    }
    private static GoalieRosterState Validate(Journal journal, string token)
    {
        if (journal == null || journal.Version != 1 || string.IsNullOrEmpty(journal.Payload) || string.IsNullOrEmpty(journal.Marker))
            throw new InvalidDataException("Invalid goalie checkpoint record.");
        var state = Deserialize<GoalieRosterState>(journal.Payload);
        if (state == null) throw new InvalidDataException("Missing goalie checkpoint state.");
        state.Validate();
        if (!Guid.TryParseExact(state.CheckpointId, "N", out _) || state.Revision < 1
            || journal.Marker != MarkerFor(state, journal.Payload) || Path.GetFileName(token) != Name(state, journal.Marker)
            || Path.GetFileName(Path.GetDirectoryName(token)) != FolderName
            || state.CampaignId != CampaignIdentity(Path.GetDirectoryName(Path.GetDirectoryName(token))))
            throw new InvalidDataException("Goalie checkpoint identity/hash mismatch.");
        return state;
    }
    public static string Prepare(string campaignFolder, GoalieRosterState state)
    {
        state = state.Clone(); state.Validate();
        if (state.CampaignId != CampaignIdentity(campaignFolder)) throw new InvalidDataException("Wrong campaign identity.");
        string folder = Path.GetFullPath(Path.Combine(campaignFolder, FolderName));
        using var held = Lock(folder);
        // Filename sequence survives corrupt record contents. A crash before the
        // rename did not publish a prepare; after rename the next sequence wins.
        long last = Directory.EnumerateFiles(folder, "r-*.record").Select(Path.GetFileName)
            .Select(n => n.Length >= 23 && long.TryParse(n.Substring(2, 20), NumberStyles.None, CultureInfo.InvariantCulture, out long r) ? r : 0)
            .DefaultIfEmpty(0).Max();
        state.Revision = checked(Math.Max(last, state.Revision) + 1);
        state.CheckpointId = Guid.NewGuid().ToString("N");
        string payload = Serialize(state), marker = MarkerFor(state, payload);
        string token = Path.Combine(folder, Name(state, marker));
        Atomic(token, Serialize(new Journal { Payload = payload, Marker = marker }), "prepare");
        return token;
    }
    public static string Marker(string token)
    {
        Ordinary(token);
        var journal = Deserialize<Journal>(File.ReadAllText(token)); Validate(journal, token);
        return journal.Marker;
    }
    public static void Commit(string token, string nativeCheckpointHash)
    {
        Ordinary(token);
        using var held = Lock(Path.GetDirectoryName(token));
        var journal = Deserialize<Journal>(File.ReadAllText(token)); Validate(journal, token);
        if (Hash(journal.Marker) != nativeCheckpointHash) throw new InvalidDataException("Native marker does not match this prepare.");
        if (journal.Committed) return;
        journal.Committed = true;
        Atomic(token, Serialize(journal), "commit");
    }
    public static GoalieRosterState Load(string campaignFolder, string runId, string nativeCheckpointHash)
    {
        if (nativeCheckpointHash == null || nativeCheckpointHash.Length != 64
            || nativeCheckpointHash.Any(c => !(c >= '0' && c <= '9' || c >= 'A' && c <= 'F'))) return null;
        string folder = Path.GetFullPath(Path.Combine(campaignFolder, FolderName));
        if (!Directory.Exists(folder)) return null;
        Ordinary(folder);
        // Exact checkpoint lookup only; neither timestamps nor "latest" are used.
        var matches = Directory.GetFiles(folder, "r-*-" + nativeCheckpointHash + ".record");
        if (matches.Length != 1) return null;
        string token = matches[0];
        try
        {
            Ordinary(token);
            var journal = Deserialize<Journal>(File.ReadAllText(token));
            var state = Validate(journal, token);
            if (state.RunId != runId || state.CampaignId != CampaignIdentity(campaignFolder)) return null;
            // Successful native readback proves which durable intent completed.
            // Commit receipt failure does not invalidate that matching intent.
            if (!journal.Committed) { try { Commit(token, nativeCheckpointHash); } catch (IOException) { } }
            return state.Clone();
        }
        catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is SerializationException || ex is ArgumentException) { return null; }
    }
}
