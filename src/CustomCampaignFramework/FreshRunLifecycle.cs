using System;
using System.IO;
using Data;
using State;
using Tape2Tape;
namespace EndlessMode;

// ResetRunData creates a new native object and GUID. Empty maps or failed loads
// alone never authorize authored defaults. Final squad selection happens later.
internal static class FreshRunLifecycle
{
    private sealed class Witness { internal CampaignState State; internal RunData Run; internal string Id, Folder; }
    private static Witness pending;
    internal static void ResetPrefix(CampaignState __instance, out RunData __state) { pending = null; __state = __instance?.runData; }
    internal static void ResetPostfix(CampaignState __instance, RunData __state, bool __runOriginal)
    {
        pending = null;
        if (!__runOriginal || Plugin.IsDefaultMode || __instance?.Pointer != CampaignUtil.CampaignState?.Pointer) return;
        var run = __instance.runData;
        if (run == null || run.Pointer == __state?.Pointer || string.IsNullOrWhiteSpace(run.runID)
            || run.runID == __state?.runID || !string.IsNullOrEmpty(run.currentMap)) return;
        pending = new Witness { State = __instance, Run = run, Id = run.runID, Folder = Path.GetFullPath(Plugin.ModFolder) };
        Plugin.Log.LogInfo("[FreshRun] Accepted native reset witness; run=" + run.runID);
    }
    internal static void Forget(RunData run) { if (pending?.Run.Pointer == run?.Pointer) pending = null; }
    internal static void HydratePrefix(RunData __0) => Forget(__0);
    internal static void MapPrefix(RunManager __instance)
    {
        var witness = pending;
        if (witness == null) { Plugin.Log.LogInfo("[FreshRun] Map creation has no fresh reset witness; existing run defaults retained."); return; }
        var state = __instance?.CampaignState; var run = state?.runData;
        // Consume before callbacks: reentrancy, failure, or repeated map creation
        // cannot reapply rewards or re-sign contracts to this same native run.
        pending = null;
        string rejected = Plugin.IsDefaultMode ? "default campaign" : state?.Pointer != witness.State.Pointer || state.Pointer != CampaignUtil.CampaignState?.Pointer ? "campaign owner changed"
            : run?.Pointer != witness.Run.Pointer || run.runID != witness.Id ? "run identity changed" : !string.IsNullOrEmpty(run.currentMap) ? "map already exists"
            : !string.Equals(Path.GetFullPath(Plugin.ModFolder), witness.Folder, StringComparison.OrdinalIgnoreCase) ? "campaign folder changed"
            : string.IsNullOrWhiteSpace(run.squadId) || run.teamData?.goalie == null ? "final squad unavailable" : null;
        if (rejected != null) { Plugin.Log.LogWarning("[FreshRun] Rejected witnessed map initialization; run=" + witness.Id + "; " + rejected); return; }
        Plugin.Log.LogInfo("[FreshRun] Consuming verified fresh map witness; run=" + witness.Id);
        CustomRewardRunStart.Complete(state);
        GoaliePersistenceBridge.Fresh(state);
    }
}
