// The suite targets selected reward pools. Recruitment weighting, native asset
// creation and copied native squad identity are outside this managed boundary.
namespace EndlessMode;
internal static class CustomRewardRuntime {
    internal static readonly Dictionary<string, CampaignCore.CustomRewardDefinition> Definitions = new(StringComparer.OrdinalIgnoreCase);
    internal static readonly List<(string Signal, string Team)> Signals = new();
    internal static void SignalAll(string signal, string team) => Signals.Add((signal, team));
    internal static bool InMatch = true;
    internal static void EnsureAssets() { }
    internal static void Safe(Action action) => action();
}
internal static class VanillaCampaignRuntime { internal static bool InheritedSquad(TeamConfig _) => false; }
internal static class FreeAgentTalentWeightRuntime
{
    internal static bool ReadingNativeWeights => false;
    internal static bool UseNativePool(Rogue.TalentCategory _) => false;
    internal static bool TrySelect(Rogue.Powerups.Repository.TalentRepository repository, Rogue.TalentCategory category,
        Il2CppSystem.Collections.Generic.List<Rogue.Talent> excluded, bool clone, int requested, bool mustBeInPool, bool matchAnyCategory,
        Data.ForwardData forward, out Il2CppSystem.Collections.Generic.List<Rogue.Talent> result) { result = null; return false; }
}
