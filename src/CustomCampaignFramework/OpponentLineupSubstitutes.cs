using Data;
using HarmonyLib;
using STS.Map;

namespace EndlessMode;

// Native launch substitutes opponents who also appear on the player's team.
// It replaces forwards[index], but leaves Lineup's IDs pointing at the outgoing
// skater. The second line then resolves to null during spawning and inspection.
[HarmonyPatch(typeof(MatchMapNode), "ReplaceForwardWithSubstitute")]
internal static class OpponentLineupSubstitutes
{
    internal sealed class Replacement
    {
        internal TeamData Team;
        internal ForwardData Original;
        internal string Id;
        internal int Index, Count;
    }

    [HarmonyPrefix]
    internal static void Prefix(MatchMapNode __instance, ForwardData __0, out Replacement __state)
    {
        __state = null;
        var team = __instance?.opponent;
        if (Plugin.IsDefaultMode || team == null || !team.isMultiLineup || __0 == null ||
            string.IsNullOrEmpty(__0.id) || team.forwards == null ||
            (!CustomTeamRules.IsCustom(team) && !VanillaCampaignRuntime.Owns(team))) return;
        for (int i = 0; i < team.forwards.Count; i++)
            if (team.forwards[i]?.Pointer == __0.Pointer)
            {
                __state = new Replacement { Team = team, Original = __0, Id = __0.id, Index = i, Count = team.forwards.Count };
                return;
            }
    }

    [HarmonyPostfix]
    internal static void Postfix(MatchMapNode __instance, Replacement __state)
    {
        var team = __instance?.opponent;
        if (__state == null || team == null || team.Pointer != __state.Team.Pointer ||
            team.forwards?.Count != __state.Count || team.lines == null) return;
        var replacement = team.forwards[__state.Index];
        if (replacement == null || replacement.Pointer == __state.Original.Pointer ||
            string.IsNullOrEmpty(replacement.id) || replacement.id == __state.Id) return;
        foreach (var line in team.lines)
            if (line != null) line.UpdateLineWithNewID(__state.Id, replacement.id);
    }
}
