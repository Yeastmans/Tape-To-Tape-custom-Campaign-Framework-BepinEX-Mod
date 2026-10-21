// Simulated native call boundaries for the source-linked bridge. This does not
// install Harmony, call IL2CPP or exercise platform/game storage.
using System.Reflection;
using CampaignCore;
using EndlessMode;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

static class BridgeDoubleHooks
{
    internal static object Call(string name, object[] args = null) {
        try { return typeof(GoaliePersistenceBridge).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args); }
        catch (TargetInvocationException e) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
    }
    internal static T Call<T>(string name, object[] args = null) => (T)Call(name, args);
    internal static void Enable() {
        GoalieNativeAdmission.Preflight(); Call("PreflightPromises");
        typeof(GoaliePersistenceBridge).GetProperty("Enabled", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, true);
    }
}
namespace HarmonyLib {
    public sealed class HarmonyPostfix : Attribute { }
    public static class AccessTools {
        public static FieldInfo Field(System.Type type, string name) => type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
        public static MethodInfo Method(System.Type type, string name, System.Type[] parameters = null) => parameters == null
            ? type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
            : type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance, null, parameters, null);
    }
    public class HarmonyMethod { public HarmonyMethod(MethodInfo method) {} }
    public class Harmony { public Harmony(string id) {} public void Patch(MethodInfo method, HarmonyMethod prefix, HarmonyMethod postfix) {} public void Unpatch(MethodInfo method, MethodInfo patch) {} }
}
namespace Il2CppInterop.Runtime {
    public static class IL2CPP {
        public static IntPtr Il2CppObjectBaseToPtr(Il2CppSystem.Object value) => value.Pointer;
        public static unsafe IntPtr il2cpp_runtime_invoke(IntPtr method, IntPtr instance, void** args, ref IntPtr exception) {
            if (ES3.AdmissionError != null) { exception = ES3.AdmissionError.Pointer; return IntPtr.Zero; }
            var value = (Il2CppSystem.Object)NativeRegistry.Values[(IntPtr)args[0]];
            var type = (ES3Types.ES3Type)NativeRegistry.Values[(IntPtr)args[1]];
            var settings = (ES3Settings)NativeRegistry.Values[(IntPtr)args[2]];
            return ES3.Serialize(value, type, settings).Pointer;
        }
    }
}
namespace ES3Internal { public class ES3Data : Il2CppSystem.Object { public ES3Types.ES3Type type; public Il2CppStructArray<byte> bytes; } }
public class ES3File : Il2CppSystem.Object {
    public static readonly Dictionary<string, ES3File> cachedFiles = new();
    internal static bool FailPhysicalRead;
    internal static readonly Dictionary<string, Dictionary<string, ES3Internal.ES3Data>> persisted = new();
    public Dictionary<string, ES3Internal.ES3Data> cache = new(); public ES3Settings settings;
    internal static Dictionary<string, ES3Internal.ES3Data> Copy(Dictionary<string, ES3Internal.ES3Data> source)
        => source.ToDictionary(p => p.Key, p => new ES3Internal.ES3Data { type = p.Value.type, bytes = new(p.Value.bytes.ToArray()) });
    public ES3File(ES3Settings settings, bool read) { this.settings = settings; if (read && FailPhysicalRead) throw new IOException("physical read failed"); if (read && persisted.TryGetValue(settings.path, out var value)) cache = Copy(value); }
    public bool KeyExists(string key) => cache.ContainsKey(key);
    public Il2CppSystem.Object Load(string key) { var value = cache[key]; return ES3.Deserialize(value.type, value.bytes, settings); }
    public static ES3File CacheFile(ES3Settings settings) {
        BridgeDoubleHooks.Call("CachePrefix", new object[] { settings });
        return cachedFiles[settings.path] = new(settings, true);
    }
}
public static partial class ES3 {
    internal static bool FailMarker, FailStore, WrongMarkerReadback;
    internal static System.Action AfterStore;
    internal static Il2CppSystem.Exception AdmissionError;
    public static void Save(string key, Il2CppSystem.Object value, ES3Settings settings) {
        if (!ES3File.cachedFiles.TryGetValue(settings.path, out var file)) ES3File.cachedFiles[settings.path] = file = new(settings, false);
        if (key == GoalieRosterStore.MarkerKey && FailMarker) throw new IOException("marker injection failure");
        var type = ES3Internal.ES3TypeMgr.GetOrCreateES3Type(value.GetIl2CppType(), true);
        file.cache[key] = new() { type = type, bytes = Serialize(value, type, new(settings.path, file.settings)) };
        if (key == GoalieRosterStore.MarkerKey && WrongMarkerReadback) file.cache.Remove(key);
    }
    public static void StoreCachedFile(string path) {
        BridgeDoubleHooks.Call("StorePrefix", new object[] { path });
        if (FailStore) throw new IOException("native store failure");
        ES3File.persisted[path] = ES3File.Copy(ES3File.cachedFiles[path].cache);
        AfterStore?.Invoke();
    }
}
namespace State {
    public partial class CampaignState : Il2CppSystem.Object {
        public Data.RunData runData; public RunDataRepositoryV2 Repository = new();
        public RunDataRepositoryV2 _runDataRepository => Repository;
        public void ApplyNewRunStartEffects() {}
        public void SaveRunData() {
            runData.teamState = new() { goaltenderState = runData.teamData.goalie.SaveState(), forwardStates = runData.teamState.forwardStates, relicStates = runData.teamState.relicStates };
            Repository.SaveToPersistentStorage(new() { runID = runData.runID, squadId = runData.squadId,
                team = new() { skaters = new() { new SerializerV2().SerializeToModel(runData.teamData.goalie) } }, teamState = runData.teamState });
        }
    }
    public class RunDataRepositoryV2 : Il2CppSystem.Object {
        public string _fileName = "RunDataV2.es3";
        public object _talentRepository { get; } = new(); public object _relicRepository { get; } = new(); public object _abilityRepository { get; } = new();
        internal int Saves, Loads; internal bool CompleteImmediately = true, NullLoad;
        internal string Outcome = "true";
        internal readonly List<Il2CppSystem.Runtime.CompilerServices.AsyncTaskMethodBuilder<bool>> Pending = new();
        public Il2CppSystem.Threading.Tasks.Task<bool> SaveToPersistentStorage(RunDataModelV2 model) {
            var args = new object[] { this, model, null };
            if (!BridgeDoubleHooks.Call<bool>("SavePrefix", args)) return (Il2CppSystem.Threading.Tasks.Task<bool>)args[2];
            Saves++;
            var source = Il2CppSystem.Runtime.CompilerServices.AsyncTaskMethodBuilder<bool>.Create(); var task = source.Task;
            try {
                var settings = new ES3Settings(_fileName, ES3Settings.defaultSettings) { location = ES3.Location.Cache };
                ES3.Save("runData", model, settings); ES3.StoreCachedFile(_fileName);
                if (CompleteImmediately) Complete(source); else Pending.Add(source);
            } catch (System.Exception e) { source.SetException(new Il2CppSystem.Exception(e.Message)); }
            return task;
        }
        internal void Complete(Il2CppSystem.Runtime.CompilerServices.AsyncTaskMethodBuilder<bool> builder) {
            if (Outcome == "fault") builder.SetException(new Il2CppSystem.Exception("native platform failure"));
            else if (Outcome == "cancel") builder.SetException(new Il2CppSystem.OperationCanceledException());
            else builder.SetResult(Outcome == "true");
        }
        public Il2CppSystem.Threading.Tasks.Task<RunDataModelV2> LoadFromPersistentStorage() {
            var args = new object[] { this, null };
            if (!BridgeDoubleHooks.Call<bool>("LoadPrefix", args)) return (Il2CppSystem.Threading.Tasks.Task<RunDataModelV2>)args[1];
            Loads++;
            var source = Il2CppSystem.Runtime.CompilerServices.AsyncTaskMethodBuilder<RunDataModelV2>.Create(); var task = source.Task;
            var file = ES3File.CacheFile(new(_fileName, ES3Settings.defaultSettings));
            source.SetResult(NullLoad ? (RunDataModelV2)null : file.Load("runData").TryCast<RunDataModelV2>()); return task;
        }
    }
}
namespace Tape2Tape { public static class CampaignUtil { public static State.CampaignState CampaignState; } }
namespace EndlessMode {
    static partial class Plugin { internal static string ModFolder; internal static bool IsDefaultMode; internal static LogDouble Log = new(); }
    class LogDouble { internal readonly List<string> Warnings = new(); internal void LogError(string value) {} internal void LogWarning(string value) { Warnings.Add(value); } }
    static class MatchRuleRunner { internal static void Install() {} }
    static class SquadBehaviorRuntime { internal static bool ReserveEnabled; internal static bool Enabled(string squad, string behavior, bool native) => ReserveEnabled; }
}

namespace Tape2Tape { public static class T2TUtils { public static bool IsInMatch; } }
namespace ScriptableStates { public enum GameType { Rogue, QuickPlay } }
public static class GameManager { public static ScriptableStates.GameType GameType = ScriptableStates.GameType.Rogue; }
