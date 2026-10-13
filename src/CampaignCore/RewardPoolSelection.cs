using System;
using System.Collections.Generic;
using System.Linq;

namespace CampaignCore;

public static class RewardPoolSelection
{
    // Never mutate the native repository or shrink an eligible pool below the
    // number its caller needs. Over-restrictive selections restore only enough
    // excluded candidates to keep that reward screen usable.
    public static List<T> Filter<T>(IEnumerable<T> source, int requested, Func<T, bool> excluded, out int restored)
    {
        var input = source.ToList();
        var result = input.Where(item => !excluded(item)).ToList();
        int minimum = Math.Min(input.Count, Math.Max(1, requested));
        restored = 0;
        foreach (var item in input.Where(excluded))
        {
            if (result.Count >= minimum) break;
            result.Add(item); restored++;
        }
        return result;
    }
    public static List<T> Shuffle<T>(IEnumerable<T> source, Func<int, int> next)
    {
        var pool = source.ToList();
        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = next(i + 1);
            var value = pool[i]; pool[i] = pool[j]; pool[j] = value;
        }
        return pool;
    }
}
