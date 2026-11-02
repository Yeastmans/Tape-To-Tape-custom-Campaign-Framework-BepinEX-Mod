using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CampaignCore;
using HarmonyLib;

namespace EndlessMode;

internal static partial class CustomRewardRuntime
{
    private static Dictionary<string, CustomRewardDefinition> baseGameRunRewards = new(StringComparer.OrdinalIgnoreCase);
    private static string baseGameRewardRun = "", baseGameRewardRoot = "";
    private static string BaseGameRewardRoot() => Path.GetFullPath(Plugin.ModContentRoot);

    internal static CustomRewardDefinition[] BaseGameRewardSnapshots() => Plugin.IsDefaultMode
        && string.Equals(baseGameRewardRoot, BaseGameRewardRoot(), StringComparison.OrdinalIgnoreCase)
            ? baseGameRunRewards.Values.Select(d => d.Copy()).ToArray() : Array.Empty<CustomRewardDefinition>();

    private static void SetBaseGameRunRewards(string runId, Dictionary<string, CustomRewardDefinition> snapshots)
    {
        string root = BaseGameRewardRoot();
        bool changed = baseGameRewardRun != runId || !string.Equals(baseGameRewardRoot, root, StringComparison.OrdinalIgnoreCase)
            || snapshots.Count != baseGameRunRewards.Count || snapshots.Any(p => !baseGameRunRewards.TryGetValue(p.Key, out var old)
                || old.ToDocument().ToString() != p.Value.ToDocument().ToString());
        if (!changed) return;
        foreach (string id in baseGameRunRewards.Keys.Concat(snapshots.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
            if (!snapshots.TryGetValue(id, out var next) || !Definitions.TryGetValue(id, out var current)
                || current.ToDocument().ToString() != next.ToDocument().ToString()) InvalidateCoachAsset(id);
        baseGameRunRewards = snapshots; baseGameRewardRun = runId; baseGameRewardRoot = root;
        loaded = false; inPlayStale = true;
    }

    internal static void RestoreBaseGameRunRewards(string runId)
    {
        if (!Plugin.IsDefaultMode) return;
        // Native empty/new models have no persisted run to bind. Clear stale
        // overrides, then leave their ordinary hydration behavior available.
        if (string.IsNullOrWhiteSpace(runId))
        {
            ResetBaseGameRunRewards(); EnsureAssets(); return;
        }
        // Read/validate first: a rejected snapshot must not publish partial rules
        // or let native deserialization silently bind IDs to changed pool rules.
        var snapshots = BaseGamePoolRunRewards.Load(Plugin.ModContentRoot, runId);
        SetBaseGameRunRewards(runId, snapshots);
        EnsureAssets();
    }

    internal static void ResetBaseGameRunRewards()
    {
        if (!Plugin.IsDefaultMode) return;
        foreach (string id in baseGameRunRewards.Keys) InvalidateCoachAsset(id);
        baseGameRunRewards.Clear(); baseGameRewardRun = baseGameRewardRoot = "";
        loaded = false; inPlayStale = true;
    }

    internal static void SaveBaseGameRunRewards(string runId)
    {
        if (!Plugin.IsDefaultMode) return;
        if (baseGameRewardRun.Length > 0 && (baseGameRewardRun != runId
            || !string.Equals(baseGameRewardRoot, BaseGameRewardRoot(), StringComparison.OrdinalIgnoreCase))) ResetBaseGameRunRewards();
        EnsureAssets();
        BaseGamePoolRunRewards.Save(Plugin.ModContentRoot, runId, Definitions.Values);
        SetBaseGameRunRewards(runId, BaseGamePoolRunRewards.Load(Plugin.ModContentRoot, runId));
    }
}

[HarmonyPatch(typeof(State.DeserializerV2), nameof(State.DeserializerV2.DeserializeFromModel), new[] { typeof(Data.RunData), typeof(State.RunDataModelV2) })]
internal static class CustomRewardBaseGameRestore
{
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    internal static void Prefix(State.RunDataModelV2 __1)
    {
        if (!Plugin.IsDefaultMode) return;
        try { CustomRewardRuntime.RestoreBaseGameRunRewards(__1?.runID); }
        catch (Exception)
        {
            Plugin.Log.LogError("[CustomRewards] Base Game saved reward data is damaged or does not match this run; loading stopped and the native save was kept unchanged. Restore the matching file in base_game_pool_run_rewards from a saved copy before retrying.");
            throw;
        }
    }
}

[HarmonyPatch(typeof(State.CampaignState), "ResetRunData")]
internal static class CustomRewardBaseGameReset
{
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    internal static void Prefix()
    {
        if (!Plugin.IsDefaultMode) return;
        CustomRewardRuntime.ResetBaseGameRunRewards(); CustomRewardRuntime.EnsureAssets();
    }
}
