using System;
using Data;
using HarmonyLib;

namespace EndlessMode;

// The native team customizer clones a selected player, then replaces its styles
// with the team's colorable uniform. Exported independent looks must survive it.
internal static class ExportTeamAppearanceRuntime
{
    internal static bool BeforeTeamUniform(SkaterData data)
    {
        try
        {
            var exported = PlayNowTranslation.ReadPlayer(data);
            // Older snapshots forced Override Team Settings=yes after resolving
            // inheritance, so that field alone cannot prove the author's intent.
            return exported == null || exported.Get("Export Version") != "3"
                || !exported.Get("Export Own Equipment").Equals("yes", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning("[PlayNow] Could not read exported equipment ownership: " + ex.Message);
            return true;
        }
    }

    internal static void Install(Harmony harmony) => harmony.Patch(
        AccessTools.Method(typeof(Skaters.SkinHelper), "OverrideSkinsWithCustomTeamUniform",
            new[] { typeof(SkaterData), typeof(bool), typeof(bool) }),
        prefix: new HarmonyMethod(typeof(ExportTeamAppearanceRuntime), nameof(BeforeTeamUniform)));
}
