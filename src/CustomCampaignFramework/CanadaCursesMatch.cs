using CampaignCore;
using HarmonyLib;

namespace EndlessMode;

// All native picker gates (including IsPickPending's inlined timing checks)
// call this identity predicate. Leave their timing, offers and cleanup native.
[HarmonyPatch(typeof(CanadaCursePicker), "IsCanadaMatch")]
internal static class CanadaCursesMatch
{
    private static string opponentId = "";
    private static bool enabled;

    internal static void Begin(string id, string choice)
    {
        opponentId = id ?? "";
        enabled = OpponentOptions.CanadaCursesEnabled(choice);
        if (opponentId.Length > 0)
            Plugin.Log.LogInfo("[CanadaCurses] Native curse sequence " + (enabled ? "enabled" : "disabled") + " for this opponent.");
    }

    internal static void End() { opponentId = ""; enabled = false; }

    [HarmonyPostfix]
    internal static void Postfix(ref bool __result)
    {
        if (Plugin.IsDefaultMode || opponentId.Length == 0 || GameManager._instance == null) return;
        var visitor = GameManager.VisitorTeam;
        if (visitor == null || !visitor.gameObject.activeInHierarchy || !visitor.gameObject.scene.isLoaded
            || visitor._teamData == null || visitor._teamData.id != opponentId) return;
        __result = enabled;
    }
}
