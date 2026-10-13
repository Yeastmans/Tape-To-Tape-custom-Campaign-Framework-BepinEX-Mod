using CampaignCore;
using HarmonyLib;
using Tape2Tape.Hockey.UI;

namespace EndlessMode;

// InitData copies ShowTycoonSponsors but does not spawn the panel. Override its
// local flag before the native menu-show lifecycle loads/animates/releases it.
// Never classify the team as Tycoons: that would also enable gameplay rules.
[HarmonyPatch(typeof(GameMenu), nameof(GameMenu.InitData))]
internal static class TycoonsSponsorsMenu
{
    private static string opponentId = "";
    private static bool enabled;

    internal static void Begin(string id, string surface, string choice)
    {
        opponentId = id ?? "";
        enabled = OpponentOptions.TycoonsSponsorsEnabled(surface, choice);
    }

    internal static void End() { opponentId = ""; enabled = false; }

    [HarmonyPostfix]
    internal static void Postfix(GameMenu __instance)
    {
        if (Plugin.IsDefaultMode || __instance == null || opponentId.Length == 0 || GameManager._instance == null) return;
        var visitor = GameManager.VisitorTeam;
        if (visitor == null || !visitor.gameObject.activeInHierarchy || !visitor.gameObject.scene.isLoaded
            || visitor._teamData == null || visitor._teamData.id != opponentId) return;
        __instance.m_ShowTycoonSponsors = enabled;
        Plugin.Log.LogInfo("[TycoonsSponsors] Match menu sponsors " + (enabled ? "enabled" : "disabled") + ".");
    }
}
