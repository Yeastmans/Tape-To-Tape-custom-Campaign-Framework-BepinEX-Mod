using System;
using System.IO;
using System.Linq;
using System.Text;
using CampaignCore;
using HarmonyLib;

namespace EndlessMode;

internal static partial class CustomMapLayout
{
    private static bool TryReadReceipt(string serialized, string squadId, out CampaignDocument document, out MapLayout layout, out int stage)
    {
        document = null; layout = null; stage = 0;
        if (Plugin.IsDefaultMode || string.IsNullOrEmpty(serialized)) return false;
        string run = RunId(false); if (run.Length == 0) return false;
        string hash = SaveHash(serialized), path = Path.Combine(RuntimeFolder, run, hash + ".txt");
        if (!File.Exists(path)) return false;
        var doc = new CampaignDocument(File.ReadAllText(path));
        if (doc.Get("Campaign") != Plugin.ActiveCampaign || doc.Get("Run") != run || doc.Get("Map Hash") != hash
            || !SquadMapRuntime.MatchesReceipt(doc, squadId)
            || !int.TryParse(doc.Get("Stage"), out stage) || stage != Plugin.ActsCompleted + 1) return false;
        if (doc.Get("Layout").Length > 0) {
            layout = MapLayout.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(doc.Get("Layout"))));
            var errors = layout.Validate(Plugin.ConfigTeamDirs?.Select(Path.GetFileName) ?? Enumerable.Empty<string>());
            if (errors.Count > 0) throw new ArgumentException(string.Join("; ", errors));
        }
        document = doc; return true;
    }

    internal static void RestoreRewardsBeforeHydration(string serialized, string squadId)
    {
        CustomRewardRuntime.ResetCoachRewards();
        if (TryReadReceipt(serialized, squadId, out var doc, out var layout, out _))
            CustomRewardRuntime.RestoreCoachRewards(layout, CustomRewardExport.Read(doc));
    }
}

// Native deserialization resolves owned relic IDs before the map prefab exists.
// Restore from that exact serialized map before any reward lookup can occur.
[HarmonyPatch(typeof(State.DeserializerV2), "DeserializeFromModel", new[] { typeof(Data.RunData), typeof(State.RunDataModelV2) })]
internal static class RestoreCustomCoachRewards
{
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    internal static void Prefix(State.RunDataModelV2 __1)
    {
        try { CustomMapLayout.RestoreRewardsBeforeHydration(__1?.currentMap, __1?.squadId); }
        catch (Exception ex) { Plugin.Log.LogError("[CustomCoach] Saved reward receipt rejected: " + ex.Message); }
    }
}
