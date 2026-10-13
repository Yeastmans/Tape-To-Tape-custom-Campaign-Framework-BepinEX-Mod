using System;
using System.IO;
using System.Runtime.InteropServices;
using CampaignCore;
using HarmonyLib;
using Tape2Tape.Hockey.UI;

namespace EndlessMode;

internal static class SquadStartingScreens
{
    internal static bool EmptyPool(string folder)
    {
        string path = Path.Combine(Plugin.ModFolder, "player_teams", folder);
        var selected = SelectedPlayerPool.Read(path, folder == "draft_pool" ? 9 : int.MaxValue);
        return selected != null && selected.Length == 0;
    }
    internal static bool Enabled(TeamConfig cfg, string screen)
    {
        string Read(string key) => cfg.SquadBehaviors.TryGetValue(key, out var value) ? value : "";
        // A complete two-line roster follows Double Decker's native setup even
        // when its screen settings inherit or a No Effects legacy flag is on.
        if (SquadBehaviors.RequiredValue(cfg.SquadTemplate, screen, Read) is bool required) return required;
        if (cfg.SquadBehaviors.TryGetValue("Squad Behavior " + screen, out var setting) && !string.IsNullOrWhiteSpace(setting)
            && !setting.Equals("Inherit", StringComparison.OrdinalIgnoreCase))
            return SquadBehaviors.Enabled(cfg.SquadTemplate, screen, Read);
        if (SquadTemplatePolicy.Identity(cfg.SquadTemplate) == "noeffects")
            return screen == "bench_screen" ? cfg.NoEffectsBench : cfg.NoEffectsSuperstars;
        return SquadBehaviors.Enabled(cfg.SquadTemplate, screen, Read);
    }
    // Called at the audited point after native setup flags are computed, before
    // either menu is created. The original async flow and awaits stay intact.
    internal static byte Apply(IntPtr state, byte usesIntro, IntPtr title)
    {
        try
        {
            if (title != IntPtr.Zero && new TitleScreen(title).m_CampaignTutorial?.IsActive == true) return usesIntro;
            var squad = Tape2Tape.CampaignUtil.CampaignState?.GetCurrentSquad();
            if (Plugin.IsDefaultMode || Tape2Tape.CampaignUtil.CampaignState?.runData?.isTutorialRun == true || squad == null || !(squad.id ?? "").StartsWith("Custom_")
                || !Plugin.PlayerTeamConfigs.TryGetValue(squad.id.Substring(7), out var cfg)) return usesIntro;
            bool stars = Enabled(cfg, "superstar_screen") && !EmptyPool("superstars");
            bool bench = Enabled(cfg, "bench_screen") && !EmptyPool("draft_pool");
            bool oldschool = Marshal.ReadByte(state, 0x58) != 0 && stars;
            Marshal.WriteByte(state, 0x58, (byte)(oldschool ? 1 : 0));
            Marshal.WriteByte(state, 0x59, (byte)(bench ? 0 : 1));
            // Native code now computes its combined flag and starts/stops the
            // menu music from these flags. Do not change it after music starts.
            Plugin.Log.LogInfo($"[SquadSetup] {squad.id}: superstar screen={stars}, bench screen={bench}");
            return (byte)(stars ? 1 : 0);
        }
        catch (Exception ex) { Plugin.Log.LogError("[SquadSetup] Screen routing failed: " + ex); return usesIntro; }
    }
}

[HarmonyPatch(typeof(TitleScreen), "TryAutoAddAllBenchwarmers")]
internal static class StartingScreenAutoBench
{
    [HarmonyPrefix]
    private static bool Prefix(ref bool __result)
    {
        var squad = Tape2Tape.CampaignUtil.CampaignState?.GetCurrentSquad();
        if (Plugin.IsDefaultMode || squad == null || !(squad.id ?? "").StartsWith("Custom_")
            || !Plugin.PlayerTeamConfigs.TryGetValue(squad.id.Substring(7), out var cfg)) return true;
        if (SquadStartingScreens.EmptyPool("draft_pool")) { __result = true; return false; }
        if (SquadStartingScreens.Enabled(cfg, "bench_screen")) { __result = false; return false; }
        return true;
    }
}
