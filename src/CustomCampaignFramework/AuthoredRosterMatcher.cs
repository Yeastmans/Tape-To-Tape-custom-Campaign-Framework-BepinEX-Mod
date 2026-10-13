using System;
using System.Collections.Generic;

namespace EndlessMode;

internal static class AuthoredRosterMatcher
{
    // The starting-team clone is arranged by position. A live run is not: the
    // native draft can seat a chosen skater between the authored starters.
    internal static int[] Resolve(string[] authoredNames, string[] rosterNames, bool liveRun)
    {
        var targets = new int[authoredNames.Length];
        Array.Fill(targets, -1);
        if (!liveRun)
        {
            for (int i = 0; i < Math.Min(targets.Length, rosterNames.Length); i++)
                targets[i] = i;
            return targets;
        }

        var claimed = new HashSet<int>();
        for (int slot = 0; slot < authoredNames.Length; slot++)
        {
            string expected = authoredNames[slot]?.Trim();
            if (string.IsNullOrEmpty(expected)) continue;

            int found = -1;
            for (int index = 0; index < rosterNames.Length; index++)
            {
                if (!string.Equals(expected, rosterNames[index]?.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                if (found >= 0) { found = -1; break; } // Ambiguous names must not select anyone.
                found = index;
            }
            if (found >= 0 && claimed.Add(found)) targets[slot] = found;
        }
        return targets;
    }
}
