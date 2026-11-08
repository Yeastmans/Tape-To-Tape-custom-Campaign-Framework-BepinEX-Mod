using HarmonyLib;
using Tape2Tape.Hockey.UI;
using Native = Il2CppSystem.Collections.Generic;

namespace EndlessMode;

// Both the live end screen and saved run history call RefreshActSummaries with
// chronological rows. Each native panel filters by MatchScore.act (the terrain
// number) and owns a single boss slot. Reusing a terrain reuses that slot.
[HarmonyPatch(typeof(RunTeamStats), "RefreshActSummaries")]
internal static class CustomRunActSummaries
{
    [HarmonyPrefix]
    internal static void Prefix(ref Native.IReadOnlyList<int> __1, ref Native.IReadOnlyList<RunMatchData> __2)
    {
        if (Plugin.IsDefaultMode || __2 == null) return;
        int count = __2.Cast<Native.IReadOnlyCollection<RunMatchData>>().Count;
        if (count == 0) return;

        var first = __2[0]?.Score;
        if (first == null) return;
        // A standalone historical act 2 or 3 might be a partial native run.
        // Only the live run's own score list can identify such a custom start.
        bool needsProjection = first.act != 1 && IsCurrentAuthoredStart(first, count);
        int[] displayActs = new int[count];
        int act = 1;
        MatchScore previous = null;
        for (int i = 0; i < count; i++)
        {
            var score = __2[i]?.Score;
            if (score == null) return;
            if (previous != null)
            {
                bool completed = previous.IsBoss && previous.Win;
                if (completed && score.act != previous.act + 1) needsProjection = true;
                // A terrain change also establishes a boundary in older
                // history that lacks a boss record.
                if (completed || score.act != previous.act) act++;
            }
            displayActs[i] = act;
            previous = score;
        }
        if (!needsProjection) return;

        var headings = new Native.List<int>();
        var matches = new Native.List<RunMatchData>();
        for (int i = 0; i < count; i++)
        {
            int ordinal = displayActs[i];
            if (i == 0 || ordinal != displayActs[i - 1]) headings.Add(ordinal);
            var row = __2[i];
            if (row.Score.act != ordinal)
            {
                // Copy the native object so scores, shootouts, stats and map
                // coordinates survive. Saved runs and other stat consumers
                // keep their original terrain and object references.
                var displayScore = row.Score.MemberwiseClone().Cast<MatchScore>();
                displayScore.act = ordinal;
                row = new RunMatchData(row.OpponentTeam, displayScore);
            }
            matches.Add(row);
        }
        __1 = headings.Cast<Native.IReadOnlyList<int>>();
        __2 = matches.Cast<Native.IReadOnlyList<RunMatchData>>();
    }

    private static bool IsCurrentAuthoredStart(MatchScore first, int count)
    {
        if (Plugin.ActSequence == null || Plugin.ActSequence.Length == 0 || Plugin.ActSequence[0] != first.act)
            return false;
        var saved = Tape2Tape.CampaignUtil.CampaignState?.runStatsState?.runMatchScores;
        return saved != null && saved.Count == count && saved[0]?.Pointer == first.Pointer;
    }
}
