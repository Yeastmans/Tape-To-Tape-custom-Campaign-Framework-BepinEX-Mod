using System;
using System.Linq;
using HarmonyLib;

namespace EndlessMode;

internal static class CustomRewardPlayNow
{
    // Non-default mode installs these classes through the existing PatchAll.
    // Default mode needs only owned custom rewards and exported companion data.
    internal static void InstallForDefault()
    {
        var harmony = new Harmony("com.mods.customcampaign.exportedrewards");
        foreach (var type in typeof(CustomRewardRuntime).Assembly.GetTypes().Where(t =>
            t.Name.StartsWith("CustomReward", StringComparison.Ordinal) || t.Name.StartsWith("CustomTalent", StringComparison.Ordinal) || t.Name.StartsWith("CustomRelic", StringComparison.Ordinal))) {
            if (!type.IsDefined(typeof(HarmonyPatch), false)) continue;
            try { harmony.CreateClassProcessor(type).Patch(); }
            catch (Exception ex) { Plugin.Log.LogError("[CustomRewards] Export hook " + type.Name + ": " + ex); }
        }
    }
}
