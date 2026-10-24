using System;
using System.Collections.Generic;
using System.Linq;

namespace CampaignCore;

public static class CustomCoachPool
{
    // This filters eligibility, never restores outsiders to fill a requested count.
    public static List<T> Eligible<T>(CustomCoachDefinition coach, IEnumerable<T> candidates,
        Func<T, string> id, Func<T, bool> eligible)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return candidates.Where(item => coach.Relics.Contains(id(item)) && eligible(item) && seen.Add(id(item))).ToList();
    }
    public static void ValidateAvailable(CustomCoachDefinition coach, IEnumerable<string> available)
    {
        coach.Validate();
        var catalog = new HashSet<string>(available, StringComparer.OrdinalIgnoreCase);
        var missing = coach.Relics.Where(id => !catalog.Contains(id)).ToArray();
        if (missing.Length > 0) throw new ArgumentException(coach.Name + ": remove or replace unavailable relics: " + string.Join(", ", missing));
    }
    public static void AddDependencies(IDictionary<string, CustomRewardDefinition> target, CustomCoachDefinition coach)
    {
        coach.Validate();
        // Validate the entire set before touching the caller's collection.
        var combined = new Dictionary<string, CustomRewardDefinition>(target, StringComparer.OrdinalIgnoreCase);
        foreach (var item in coach.Rewards) CustomRewardExport.AddSnapshot(combined, item);
        foreach (var item in coach.Rewards) target[item.Id] = item.Copy();
    }
}
