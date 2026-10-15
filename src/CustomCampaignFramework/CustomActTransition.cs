using System;
using System.Text.Json;
using Data;
using HarmonyLib;
using STS.Map;

namespace EndlessMode;

// RunManager.Start resets the entire run when the saved map's boss is in its
// completed path and CurrentAct is 3. Wrapping GenerateNewMap is too late: the
// roster, squad ID and earned progress have already been replaced at that point.
// Give the native increment branch the authored next act's predecessor instead.
// The native setter, map generation, act events and save then run normally with
// the existing RunData. This also works after restarting between boss and map.
[HarmonyPatch(typeof(RunManager), "Start")]
internal static class CustomActTransition
{
    internal sealed class Transition
    {
        internal RunData Run;
        internal int OriginalAct;
        internal int Predecessor;
    }

    [HarmonyPrefix]
    internal static void Prefix(RunManager __instance, out Transition __state)
    {
        __state = null;
        if (Plugin.IsDefaultMode || !Plugin.ProgressWritable) return;
        var run = __instance?.CampaignState?.runData;
        if (run == null || run.isRunFailed || run.isRunConceded || run.isUnableToLoad
            || string.IsNullOrEmpty(run.squadId)) return;
        try
        {
            if (!TryNextAct(run.currentMap, Plugin.ActSequence, Plugin.ActsCompleted, out int nextAct)) return;
            __state = new Transition { Run = run, OriginalAct = run._currentAct, Predecessor = nextAct - 1 };
            // Bypass the setter only for this temporary branch input. The native
            // increment immediately uses the real setter (including audio).
            run._currentAct = __state.Predecessor;
            Plugin.Log.LogInfo($"[Campaign] Continuing completed map {Plugin.ActsCompleted} to map {Plugin.ActsCompleted + 1}"
                + $" (native act {nextAct}); preserving run '{run.runID}', squad and roster.");
        }
        catch (Exception ex)
        {
            Restore(__state);
            __state = null;
            Plugin.Log.LogError("[Campaign] Could not prepare map continuation: " + ex.Message);
        }
    }

    [HarmonyFinalizer]
    internal static Exception Finalizer(Transition __state, Exception __exception)
    {
        // If Start exits or throws before consuming the increment, never leave
        // the temporary predecessor (including 0 for a return to act 1) behind.
        Restore(__state);
        return __exception;
    }

    private static void Restore(Transition state)
    {
        if (state?.Run != null && state.Run._currentAct == state.Predecessor)
            state.Run._currentAct = state.OriginalAct;
    }

    internal static bool TryNextAct(string savedMap, int[] sequence, int completedMaps, out int nextAct)
    {
        nextAct = 0;
        if (sequence == null || completedMaps <= 0 || completedMaps >= sequence.Length
            || sequence[completedMaps] < 1 || sequence[completedMaps] > 3 || string.IsNullOrEmpty(savedMap)) return false;
        using var json = JsonDocument.Parse(savedMap);
        var map = json.RootElement;
        if (!map.TryGetProperty("act", out var act) || !act.TryGetInt32(out int savedAct)
            || savedAct != sequence[completedMaps - 1]
            || !map.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array
            || !map.TryGetProperty("path", out var path) || path.ValueKind != JsonValueKind.Array) return false;

        // Match native Map.GetBossNode + path.Any: standing on the boss or merely
        // having a terminal node is insufficient. The saved path must include it.
        foreach (var node in nodes.EnumerateArray())
        {
            if (!node.TryGetProperty("layerNodeType", out var layer)
                || !layer.TryGetProperty("nodeType", out var kind) || !IsBoss(kind)) continue;
            if (!node.TryGetProperty("point", out var boss) || !Point(boss, out int x, out int y)) return false;
            foreach (var visited in path.EnumerateArray())
                if (Point(visited, out int px, out int py) && px == x && py == y)
                {
                    nextAct = sequence[completedMaps];
                    return true;
                }
            return false; // Native GetBossNode selects the first boss.
        }
        return false;
    }

    private static bool IsBoss(JsonElement kind) => kind.ValueKind == JsonValueKind.String
        ? string.Equals(kind.GetString(), "Boss", StringComparison.OrdinalIgnoreCase)
        : kind.ValueKind == JsonValueKind.Number && kind.TryGetInt32(out int value) && value == (int)NodeType.Boss;

    private static bool Point(JsonElement point, out int x, out int y)
    {
        x = y = 0;
        return point.ValueKind == JsonValueKind.Object
            && point.TryGetProperty("x", out var px) && px.TryGetInt32(out x)
            && point.TryGetProperty("y", out var py) && py.TryGetInt32(out y);
    }
}
