using CampaignCore;
namespace EndlessMode {
static class Plugin {
    internal static string ModFolder, ModContentRoot;
    internal static bool IsDefaultMode;
    internal static string ActiveCampaign = "Campaign";
    internal static int ActsCompleted = 0;
    internal static string[] ConfigTeamDirs = Array.Empty<string>();
    internal static readonly Logger Log = new();
    internal sealed class Logger { internal void LogError(object value) => Console.WriteLine(value); internal void LogInfo(object value) => Console.WriteLine(value); }
    internal static Dictionary<string, string> NodeAssignments = new();
    internal static Dictionary<string, string> GmForcedLayers = new(); internal static int GmOverrideLayer;
    internal static void LoadNodeAssignments(bool includeLayouts) { }
    internal static string NodeKey(int stage, int layer, int node) => $"{stage}|{layer}|{node}";
}
sealed class TeamConfig { internal string IdentityKey; }
static class SquadPoolRuntime { internal static TeamConfig Team(string id) => null; }
static class CustomRewardLifecycle { internal static bool LoadRetained(Dictionary<string, CustomRewardDefinition> target) => true; }
static partial class CustomRewardRuntime {
    internal static readonly Dictionary<string, CustomRewardDefinition> Definitions = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Carrier> relics = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Carrier> talents = new(StringComparer.OrdinalIgnoreCase);
    internal static Dictionary<string, Carrier> NativeAssets => relics;
    private static bool loaded, creating, definitionSourcesReady;
    private static bool inPlayStale;
    private static string definitionsScope;
    private static long definitionGeneration;
    private static void CreateNativeAssets() {
        foreach (var d in Definitions.Values) if (!relics.ContainsKey(d.Id)) relics.Add(d.Id, new Carrier(d));
    }
}
sealed class Carrier : UnityEngine.ScriptableObject {
    internal CustomRewardDefinition Rule;
    internal int Amount => Rule.Amount;
    internal UnityEngine.Sprite sprite = new(), icon = new();
    internal Carrier(CustomRewardDefinition rule) { Rule = rule.Copy(); }
}
static class PatchBossLaunchMatch { internal static object AllRelicCache; }
static partial class CustomMapLayout {
    private static bool comparingSave;
    internal static bool Active;
    private static MapLayout appliedLayout;
    private static STS.Map.Map appliedMap;
    private static int appliedStage;
    private static string appliedFolder;
    private static string appliedSquad, appliedSource;
    private static Dictionary<string, CustomRewardDefinition> pendingCoachRewards;
    private static readonly Dictionary<string, string> Stadiums = new();
    internal static MapLayout RestoreForCheck(STS.Map.MapManager manager, string serialized) {
        ClearApplied(); manager.Serialized = serialized; RestoreReceiptCore(manager); return appliedLayout;
    }
    internal static STS.Map.MapManager Applied(MapLayout layout) {
        Active = true; appliedLayout = layout; appliedStage = Plugin.ActsCompleted + 1; appliedFolder = Plugin.ModFolder;
        appliedSquad = SquadMapRuntime.CurrentId; appliedSource = "player_teams/a/map_overrides/map-1.txt";
        appliedMap = new STS.Map.Map(); return new STS.Map.MapManager { CurrentMap = appliedMap };
    }
}
}
namespace UnityEngine {
    [Flags] enum HideFlags { None = 0, DontUnloadUnusedAsset = 32 }
    class Object { public HideFlags hideFlags = HideFlags.DontUnloadUnusedAsset; }
    class ScriptableObject : Object { }
    class Texture : Object { }
    class Sprite : Object { public Texture texture = new(); }
}
namespace State { class DeserializerV2 { } class RunDataModelV2 { public string currentMap; public string squadId = "Custom_a"; } }
namespace Data { class RunData { } }
namespace STS.Map { class Map { public IntPtr Pointer = new(1); } class MapManager { public Map CurrentMap; internal string Serialized; internal string SaveMap() => Serialized; } }
namespace Tape2Tape {
    static class CampaignUtil { internal static TestState CampaignState = new(); }
    sealed class TestState { internal Run runData = new(); internal Squad GetCurrentSquad() => new(); }
    sealed class Run { internal string squadId = "Custom_a"; }
    sealed class Squad { internal string id = "Custom_a"; }
}
namespace HarmonyLib {
    [AttributeUsage(AttributeTargets.Class)] class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string method, Type[] arguments) { } }
    [AttributeUsage(AttributeTargets.Method)] class HarmonyPrefix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] class HarmonyPriority : Attribute { public HarmonyPriority(int value) { } }
    static class Priority { public const int First = 800; }
}
