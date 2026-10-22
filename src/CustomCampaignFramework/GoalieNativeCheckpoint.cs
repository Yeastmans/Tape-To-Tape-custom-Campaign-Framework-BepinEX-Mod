using System;
using System.IO;
using System.Linq;
using CampaignCore;
using ES3Internal;
using State;

namespace EndlessMode;

// One selected native cache entry. Copies own their bytes; native wrappers are
// retained only for exact in-flight array/type/cache identity association.
internal sealed class GoalieNativeCheckpoint
{
    internal readonly ES3File File;
    internal readonly ES3Data Entry;
    internal readonly byte[] Bytes;
    internal readonly string Fingerprint, Marker;
    internal readonly bool HasMarker;
    internal readonly string Folder, Path;
    internal RunDataModelV2 Model { get; private set; }
    private GoalieNativeCheckpoint(string folder, string path, ES3File file)
    {
        Folder = folder; Path = path; File = file;
        RequireProfile(file.settings);
        if (!ReadEntry(file, "runData", out var entry) || entry?.type?.type?.FullName != "State.RunDataModelV2" || entry.bytes == null)
            throw new InvalidDataException("Selected native cache has no complete runData entry.");
        Entry = entry; Bytes = GoalieNativeGraph.ObjectBytes(entry.bytes.ToArray());
        string type = entry.type.type.FullName;
        Fingerprint = GoalieRosterStore.Hash("ccf-native-bytes-v1:" + type.Length + ":" + type + ":" + Bytes.Length + ":" + Convert.ToBase64String(Bytes));
        // Marker deserialization can recursively visit the ES3 boundary. Only
        // the runData array/type pair is eligible for model association.
        HasMarker = file.KeyExists(GoalieRosterStore.MarkerKey);
        Marker = HasMarker
            ? (string)file.Load(GoalieRosterStore.MarkerKey)?.TryCast<Il2CppSystem.String>() : null;
    }
    internal static void RequireProfile(ES3Settings settings)
    {
        if (settings == null || settings.format != ES3.Format.JSON || settings.encoding == null || settings.encoding.CodePage != 65001)
            throw new InvalidDataException("Goalie checkpoints require the reviewed JSON/UTF-8 native cache profile.");
    }
    internal static ES3File Existing(string path)
    {
        if (!ES3File.cachedFiles.TryGetValue(path, out var file) || file?.settings?.path != path)
            throw new InvalidDataException("The exact native run cache is unavailable.");
        return file;
    }
    internal static GoalieNativeCheckpoint Cached(string folder, string path) => new(folder, path, Existing(path));
    private static bool ReadEntry(ES3File file, string key, out ES3Data entry)
    {
        // ES3Data is a native 16-byte struct exposed as a managed class. The
        // generated generic out wrapper only reserves a pointer-sized local.
        // The indexer instead receives the boxed value from runtime_invoke.
        var cache = file.cache;
        if (!cache.ContainsKey(key)) { entry = null; return false; }
        entry = cache[key];
        return true;
    }
    internal static GoalieNativeCheckpoint Persisted(string folder, string path, ES3Settings settings)
    {
        // A separate ES3File reads the physical container without replacing the
        // selected global cache or performing a second platform/cloud selection.
        return new GoalieNativeCheckpoint(folder, path, new ES3File(settings, true));
    }
    internal bool SameEntry()
    {
        var current = Existing(Path);
        return current.Pointer == File.Pointer && ReadEntry(current, "runData", out var entry)
            && entry.type?.Pointer == Entry.type.Pointer && entry.bytes?.Pointer == Entry.bytes.Pointer
            && entry.bytes.ToArray().SequenceEqual(Bytes);
    }
    internal bool MatchesInput(ES3Types.ES3Type type, Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte> bytes)
        => Model == null && type?.Pointer == Entry.type.Pointer && bytes?.Pointer == Entry.bytes.Pointer;
    internal void Associate(RunDataModelV2 model)
    {
        if (Model != null || model == null || !SameEntry()) throw new InvalidDataException("Native load selection changed during deserialization.");
        Model = model;
    }
    internal void Inject(string marker)
    {
        if (!SameEntry()) throw new InvalidDataException("Run cache changed before checkpoint injection.");
        bool hadMarker = ReadEntry(File, GoalieRosterStore.MarkerKey, out var previous);
        try
        {
            var settings = new ES3Settings(Path, File.settings) { location = ES3.Location.Cache };
            Il2CppSystem.String nativeMarker = marker;
            ES3.Save(GoalieRosterStore.MarkerKey, nativeMarker, settings);
            if ((string)File.Load(GoalieRosterStore.MarkerKey)?.TryCast<Il2CppSystem.String>() != marker || !SameEntry())
                throw new InvalidDataException("Native checkpoint marker readback failed.");
        }
        catch
        {
            // Restore the precise prior cache entry; do not serialize a guessed
            // previous marker or remove unrelated native statistics keys.
            if (hadMarker) {
                // The generic setter also passes a boxed header as the struct
                // payload. Native IDictionary accepts the box and unboxes it,
                // preserving both references of the exact prior cache entry.
                Il2CppSystem.String key = GoalieRosterStore.MarkerKey;
                File.cache.Cast<Il2CppSystem.Collections.IDictionary>()[key] = previous;
            }
            else File.cache.Remove(GoalieRosterStore.MarkerKey);
            throw;
        }
    }
}
