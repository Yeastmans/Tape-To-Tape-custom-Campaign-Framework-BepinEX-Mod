using System;
using System.Collections.Generic;
using System.Linq;

namespace CampaignCore;

public static class RandomNodePool
{
    public static readonly IReadOnlyDictionary<string, decimal> Defaults = new Dictionary<string, decimal>
    {
        ["RandomConditioning"] = 25, ["Blademaster"] = 19, ["FanNumber1"] = 16,
        ["Chaos"] = 12, ["Nurse"] = 10
    };

    // A zero-based weighted roll preserves ratios even when the total is below 1.
    // Custom pools take priority over native placement restrictions.
    public static string Pick(IReadOnlyDictionary<string, decimal> weights, double sample)
    {
        if (double.IsNaN(sample) || sample < 0 || sample > 1) throw new ArgumentOutOfRangeException(nameof(sample));
        var eligible = weights.Where(p => p.Value > 0).ToArray();
        decimal total = eligible.Sum(p => p.Value);
        if (total == 0) return null;
        decimal remaining = (decimal)sample * total;
        foreach (var entry in eligible)
        {
            if (remaining < entry.Value) return entry.Key;
            remaining -= entry.Value;
        }
        // Unity's random value can include the upper endpoint.
        return eligible[eligible.Length - 1].Key;
    }
}
