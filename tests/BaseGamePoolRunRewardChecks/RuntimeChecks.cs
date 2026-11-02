using System.Reflection;
using CampaignCore;
using EndlessMode;

internal static class RuntimeChecks
{
    static void Call(string method, params object[] args) => Api.Invoke(typeof(CustomRewardRuntime), method, args);
    static CustomRewardDefinition[] Snapshots() => (CustomRewardDefinition[])Api.Invoke(typeof(CustomRewardRuntime), "BaseGameRewardSnapshots");
    static void Hook(string name, params object[] args) => Api.Invoke(typeof(CustomRewardRuntime).Assembly.GetType("EndlessMode." + name), "Prefix", args);
    internal static void Run(string root, Checks c)
    {
        Plugin.ModContentRoot = root; Plugin.IsDefaultMode = true;
        var first = Api.Reward(Api.FirstId, "Acquired talent"); var queued = Api.Reward(Api.SecondId, "Queued offer", 17);
        CustomRewardRuntime.Source[first.Id] = first; CustomRewardRuntime.Source[queued.Id] = queued;
        Call("SaveBaseGameRunRewards", "run-a");
        c.Check(Api.Load(root, "run-a").Count == 2, "Runtime save captures the complete definition set including unacquired offers");
        c.Check(CustomRewardRuntime.Dirty, "Publishing run definitions invalidates the in-play rule cache");
        int invalidations = CustomRewardRuntime.Invalidated.Count;
        Call("SaveBaseGameRunRewards", "run-a");
        c.Check(CustomRewardRuntime.Invalidated.Count == invalidations, "Freezing identical effective definitions does not invalidate carrier assets");
        var edited = first.Copy(); edited.Amount = 99;
        CustomRewardRuntime.Source.Clear(); CustomRewardRuntime.Source[first.Id] = edited;
        Hook("CustomRewardBaseGameReset");
        c.Check(Snapshots().Length == 0 && CustomRewardRuntime.Definitions[first.Id].Amount == 99
            && CustomRewardRuntime.Carriers[first.Id].Amount == 99, "Native reset removes old run overrides and recreates changed source carriers");
        c.Check(File.Exists(Api.PathFor(root, "run-a")), "Reset leaves archived run files intact");
        Hook("CustomRewardBaseGameRestore", new State.RunDataModelV2 { runID = "run-a" });
        c.Check(CustomRewardRuntime.Definitions[first.Id].Amount == 31 && CustomRewardRuntime.Carriers[first.Id].Amount == 31
            && CustomRewardRuntime.Definitions.ContainsKey(queued.Id), "Hydration prefix restores saved rules before native ID resolution and overrides edited pools");
        invalidations = CustomRewardRuntime.Invalidated.Count;
        Hook("CustomRewardBaseGameRestore", new State.RunDataModelV2 { runID = "run-a" });
        c.Check(CustomRewardRuntime.Invalidated.Count == invalidations, "Repeated restore of the same immutable rules keeps matching carrier assets");
        var copies = Snapshots(); copies[0].Amount = 808;
        c.Check(Snapshots().All(d => d.Amount != 808), "Snapshot consumers receive defensive definition copies");
        CustomRewardRuntime.Source.Clear(); Hook("CustomRewardBaseGameReset");
        Hook("CustomRewardBaseGameRestore", new State.RunDataModelV2 { runID = "run-a" });
        c.Check(CustomRewardRuntime.Definitions.Count == 2 && CustomRewardRuntime.Carriers[first.Id].Amount == 31,
            "Restart hydration remains independent of disabled or missing pool sources");
        var other = first.Copy(); other.Amount = 7; Api.Save(root, "run-b", other);
        Hook("CustomRewardBaseGameRestore", new State.RunDataModelV2 { runID = "run-b" });
        c.Check(CustomRewardRuntime.Definitions.Count == 1 && CustomRewardRuntime.Carriers[first.Id].Amount == 7,
            "Switching runs retires stale carriers and publishes only that run's definitions");
        string saved = File.ReadAllText(Api.PathFor(root, "run-b")); File.WriteAllText(Api.PathFor(root, "run-b"), "corrupt");
        c.Reject(() => Hook("CustomRewardBaseGameRestore", new State.RunDataModelV2 { runID = "run-b" }), "Corrupt restore aborts the hydration prefix");
        c.Check(CustomRewardRuntime.Definitions[first.Id].Amount == 7 && CustomRewardRuntime.Carriers[first.Id].Amount == 7
            && File.ReadAllText(Api.PathFor(root, "run-b")) == "corrupt", "Corrupt restore leaves previous definitions and the save file unchanged");
        c.Check(Plugin.Log.Errors.Last().Contains("Base Game saved reward data") && Plugin.Log.Errors.Last().Contains("restore", StringComparison.OrdinalIgnoreCase),
            "Rejected restore names the saved reward data and provides a recovery action");
        File.WriteAllText(Api.PathFor(root, "run-b"), saved);
        foreach (var model in new State.RunDataModelV2[] { null, new() { runID = "" }, new() { runID = "  " } })
        {
            Hook("CustomRewardBaseGameRestore", new State.RunDataModelV2 { runID = "run-b" });
            Hook("CustomRewardBaseGameRestore", new object[] { model });
            c.Check(Snapshots().Length == 0 && CustomRewardRuntime.Definitions.Count == 0,
                "Native models without a persisted run ID clear stale overrides and retain native hydration behavior");
        }
        Hook("CustomRewardBaseGameRestore", new State.RunDataModelV2 { runID = "run-b" });
        Plugin.IsDefaultMode = false;
        Hook("CustomRewardBaseGameReset"); Hook("CustomRewardBaseGameRestore", new State.RunDataModelV2 { runID = "missing" }); Call("SaveBaseGameRunRewards", "custom-campaign");
        c.Check(Snapshots().Length == 0 && CustomRewardRuntime.Definitions[first.Id].Amount == 7 && !File.Exists(Api.PathFor(root, "custom-campaign")),
            "Custom campaigns bypass default-run snapshot hooks and persistence");
        Plugin.IsDefaultMode = true; Plugin.ModContentRoot = Path.Combine(root, "different-content");
        c.Check(Snapshots().Length == 0, "Saved definitions never leak to another content root");
        Hook("CustomRewardBaseGameRestore", new State.RunDataModelV2 { runID = "run-b" });
        c.Check(CustomRewardRuntime.Definitions.Count == 0, "Missing snapshot in another content root removes prior overrides");
    }
}
namespace EndlessMode
{
    internal static partial class CustomRewardRuntime
    {
        internal static readonly Dictionary<string, CustomRewardDefinition> Definitions = new(StringComparer.OrdinalIgnoreCase);
        internal static readonly Dictionary<string, CustomRewardDefinition> Source = new(StringComparer.OrdinalIgnoreCase);
        internal static readonly Dictionary<string, CustomRewardDefinition> Carriers = new(StringComparer.OrdinalIgnoreCase);
        internal static readonly List<string> Invalidated = new();
        private static bool loaded, inPlayStale;
        internal static bool Dirty => inPlayStale;
        private static void InvalidateCoachAsset(string id) { Invalidated.Add(id); Carriers.Remove(id); }
        internal static void EnsureAssets()
        {
            if (!loaded)
            {
                Definitions.Clear(); foreach (var pair in Source) Definitions[pair.Key] = pair.Value.Copy();
                var accessor = typeof(CustomRewardRuntime).GetMethod("BaseGameRewardSnapshots", BindingFlags.Static | BindingFlags.NonPublic);
                foreach (var reward in accessor == null ? Array.Empty<CustomRewardDefinition>() : (CustomRewardDefinition[])accessor.Invoke(null, null)!) Definitions[reward.Id] = reward;
                loaded = true;
            }
            foreach (var pair in Definitions) if (!Carriers.ContainsKey(pair.Key)) Carriers[pair.Key] = pair.Value.Copy();
        }
    }
    internal static class Plugin { internal static bool IsDefaultMode; internal static string ModContentRoot; internal static readonly Logger Log = new(); }
    internal sealed class Logger { internal readonly List<string> Errors = new(); internal void LogError(string message) => Errors.Add(message); }
}
namespace State
{
    public class RunDataModelV2 { public string runID; }
    public class DeserializerV2 { public void DeserializeFromModel(Data.RunData run, RunDataModelV2 model) { } }
    public class CampaignState { public void ResetRunData() { } }
}
namespace Data { public class RunData { } }
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)] public class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string name) { } public HarmonyPatch(Type type, string name, Type[] args) { } }
    [AttributeUsage(AttributeTargets.Method)] public class HarmonyPrefix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public class HarmonyPriority : Attribute { public HarmonyPriority(int priority) { } }
    public static class Priority { public const int First = 800; }
}
