using CampaignCore;
using EndlessMode;
using ES3Internal;
using State;

static class CacheInteropBoundaryChecks
{
    internal static void Run(Action<bool, string> check)
    {
        string path = "isolated-cache-ABI-check.es3";
        var file = new ES3File(new ES3Settings(path, ES3Settings.defaultSettings), false);
        var entry = new ES3Data { type = ES3TypeMgr.GetOrCreateES3Type(Il2CppInterop.Runtime.Il2CppType.Of<RunDataModelV2>(), true), bytes = new(new byte[] { 123, 125 }) };
        ES3File.cachedFiles[path] = file; file.cache["runData"] = entry;
        var selected = GoalieNativeCheckpoint.Cached(Path.GetTempPath(), path);
        check(selected.SameEntry() && selected.Entry.type.Pointer == entry.type.Pointer && selected.Entry.bytes.Pointer == entry.bytes.Pointer,
            "SafeCacheReadPreservesExactTypeAndArrayOwners");
        file.cache.Remove("runData");
        check(!selected.SameEntry(), "MissingEntryRejectsWithoutIndexerAccess");
        file.cache["runData"] = new() { type = entry.type, bytes = new(entry.bytes.ToArray()) };
        check(!selected.SameEntry(), "EqualBytesWithDifferentArrayOwnerStillRejects");
        file.cache["runData"] = entry;
        var prior = new ES3Data { type = ES3TypeMgr.GetOrCreateES3Type(Il2CppInterop.Runtime.Il2CppType.Of<Il2CppSystem.String>(), true), bytes = new(new byte[] { 123, 125 }) };
        file.cache[GoalieRosterStore.MarkerKey] = prior; file.cache["unrelated"] = entry;
        int writes = Il2CppSystem.Collections.IDictionary.BoxedWrites;
        int genericWrites = ES3.GenericSaveCalls;
        ES3.WrongMarkerReadback = true;
        try { selected.Inject("replacement"); throw new Exception("Expected failed marker readback"); }
        catch (KeyNotFoundException) { }
        finally { ES3.WrongMarkerReadback = false; }
        check(Il2CppSystem.Collections.IDictionary.BoxedWrites == writes + 1, "FailedReadbackUsesBoxedNativeRollbackBoundary");
        var restored = file.cache[GoalieRosterStore.MarkerKey];
        check(restored.type.Pointer == prior.type.Pointer && restored.bytes.Pointer == prior.bytes.Pointer,
            "RollbackRestoresBothExactPriorNativeEntryReferences");
        check(file.cache["unrelated"] == entry && selected.SameEntry(), "RollbackPreservesUnrelatedKeyAndSelectedRunIdentity");
        file.cache.Remove(GoalieRosterStore.MarkerKey); ES3.WrongMarkerReadback = true;
        try { selected.Inject("replacement"); throw new Exception("Expected failed marker readback"); }
        catch (KeyNotFoundException) { }
        finally { ES3.WrongMarkerReadback = false; }
        check(!file.cache.ContainsKey(GoalieRosterStore.MarkerKey) && Il2CppSystem.Collections.IDictionary.BoxedWrites == writes + 1,
            "AbsentPriorMarkerRollsBackByRemovalWithoutFabricatedEntry");
        selected.Inject("confirmed-marker");
        check((string)file.Load(GoalieRosterStore.MarkerKey).TryCast<Il2CppSystem.String>() == "confirmed-marker" && selected.SameEntry(),
            "ConcreteMarkerWriteRoundTripsWithoutChangingSelectedRun");
        check(ES3.GenericSaveCalls == genericWrites, "MarkerInjectionDoesNotSelectCompetingGenericSaveOverload");
        var convert = typeof(GoalieNativeCheckpoint).GetMethod("MarkerText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        object Text(Il2CppSystem.Object value) => convert.Invoke(null, new object[] { value });
        int conversions = Il2CppInterop.Runtime.IL2CPP.StringConversions;
        check(Text(null) == null && Text(new Il2CppSystem.Object()) == null
            && Il2CppInterop.Runtime.IL2CPP.StringConversions == conversions, "NullAndNonStringMarkersRejectedBeforeNativeStringConversion");
        check((string)Text((Il2CppSystem.String)"native marker") == "native marker"
            && Il2CppInterop.Runtime.IL2CPP.StringConversions == conversions + 1, "MarkerReadUsesExactManagedStringClassStoreAndNativeConversion");
        file.cache[GoalieRosterStore.MarkerKey] = entry;
        var wrongMarker = GoalieNativeCheckpoint.Cached(Path.GetTempPath(), path);
        check(wrongMarker.HasMarker && wrongMarker.Marker == null, "NonStringCachedMarkerRemainsPresentButCannotAuthorizeCheckpoint");
        ES3File.cachedFiles.Remove(path);
    }
}
