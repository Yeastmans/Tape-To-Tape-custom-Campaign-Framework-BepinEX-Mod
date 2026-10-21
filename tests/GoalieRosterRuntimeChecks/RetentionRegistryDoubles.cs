using System.Reflection;
using CampaignCore;
namespace Data {
    public partial class TeamData {
        internal readonly HashSet<string> RetentionOwnedIds = new();
        internal IEnumerable<RetentionPlayer> GetAllSkaters() => Array.Empty<RetentionPlayer>();
    }
    public class RetentionPlayer { public List<RetentionTalent> powerups = new(); }
    public class RetentionTalent { public string id; public bool PayoffSuppressed; }
}
namespace EndlessMode {
    static partial class Plugin { internal static string ModContentRoot; }
    static partial class CustomRewardRuntime {
        private static bool loaded, creating;
        private static string definitionsScope;
        private static bool definitionSourcesReady;
        private static long definitionGeneration;
        internal static bool FailNativeAssets;
        // Native carrier/artwork construction is outside definition availability.
        private static void CreateNativeAssets() { if (FailNativeAssets) throw new InvalidOperationException("native artwork unavailable"); }
        internal static Action DuringOwnership;
        internal static void EnsureRun() { DuringOwnership?.Invoke(); }
        internal static void ObserveOwnership(Data.TeamData team) { if (CustomRewardCampaignRules.Fail) throw new InvalidOperationException("custom ownership unavailable"); }
        internal static bool Has(Data.TeamData team, string id) => team.RetentionOwnedIds.Contains(id);
        internal static bool Has(Data.RetentionPlayer player, string id) => player.powerups.Any(p => p.id == id);
        internal static bool HasMatch<T>(this List<T> list, Func<T, bool> predicate) => list.Any(predicate);
    }
    static partial class CustomRewardCampaignRules { internal static bool Fail; }
    static partial class CustomRewardLifecycle { internal static string RetainedPath; }
    static class RetentionRegistryFixture {
        internal static void Reset() {
            foreach (var name in new[] { "loaded", "creating", "definitionSourcesReady" }) typeof(CustomRewardRuntime).GetField(name, BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, false);
            typeof(CustomRewardRuntime).GetField("definitionsScope", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, null);
            CustomRewardRuntime.DuringOwnership = null; CustomRewardRuntime.Definitions.Clear(); CustomRewardRuntime.FailNativeAssets = false; CustomRewardCampaignRules.Fail = false;
            Plugin.ModContentRoot = Path.Combine(Plugin.ModFolder, "_fixture-content");
            CustomRewardLifecycle.RetainedPath = Path.Combine(Plugin.ModContentRoot, "retained.txt");
        }
        internal static void Protect(Data.TeamData team) {
            var rule = new CustomRewardDefinition { Kind = CustomRewardDefinition.CustomRelics, Trigger = "Contract expiry", Effect = CustomRewardEffects.KeepFreeAgents };
            CustomRewardRuntime.Definitions[rule.Id] = rule; team.RetentionOwnedIds.Add(rule.Id);
        }
        internal static string SaveAuthoredProtection(Data.TeamData team) {
            var rule = new CustomRewardDefinition { Name = "Keep signed players", Kind = CustomRewardDefinition.CustomRelics,
                Trigger = "Contract expiry", Effect = CustomRewardEffects.KeepFreeAgents, Target = "Whole team", Duration = "Seconds",
                IconPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aNn8AAAAASUVORK5CYII=" };
            rule.Save(Path.Combine(Plugin.ModFolder, CustomRewardDefinition.Folder)); team.RetentionOwnedIds.Add(rule.Id);
            return rule.Id;
        }
    }
}
