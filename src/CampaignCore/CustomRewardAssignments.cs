using System;
using System.Collections.Generic;

namespace CampaignCore;

// Ownership is separate from an effect's recipients: a talent may help its
// teammates, but it remains a player's talent. Relics always belong to a team.
public static class CustomRewardAssignments
{
    public static bool FitsPlayer(CustomRewardDefinition item, bool goalie) => !item.IsRelic && item.IsGoalie == goalie;

    public static string[] TeamTalents(CampaignDocument document)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string field in new[] { "Talents", "Team Talents" })
            foreach (string raw in document.Get(field).Split(',')) {
                string value = raw.Trim();
                if (value.Length > 0 && !value.Equals("none", StringComparison.OrdinalIgnoreCase) && seen.Add(value)) result.Add(value);
            }
        return result.ToArray();
    }

    public static void ValidateField(CustomRewardDefinition item, string field, bool? goalie = null)
    {
        bool relics = field == "Relics" || field == "Team Relics";
        bool talents = field == "Talents" || field == "Goalie Talents" || field == "Team Talents"
            || field == "Random Pool" || field == "Team Random Pool";
        if ((!relics && !talents) || item.IsRelic != relics)
            throw new ArgumentException(item.Name + " is a " + item.Kind + " item and cannot be assigned in " + field + ". "
                + (item.IsRelic ? "Assign it to the team's relics." : "Assign it to the matching player's talents."));
        if (talents && (goalie.HasValue && !FitsPlayer(item, goalie.Value) || field == "Goalie Talents" && !item.IsGoalie))
            throw new ArgumentException(item.Name + " belongs to " + (item.IsGoalie ? "a goalie" : "a skater") + ". Choose a talent for this player's role.");
        if (item.NeedsRedesign) throw new ArgumentException(item.Name + " is excluded until redesigned.");
    }

    public static void Validate(CampaignDocument document, IReadOnlyDictionary<string, CustomRewardDefinition> items,
        Func<string, bool, string> resolve, bool? goalie = null)
    {
        foreach (string field in new[] { "Talents", "Goalie Talents", "Team Talents", "Relics", "Team Relics", "Random Pool", "Team Random Pool", "Ability", "Goalie Ability" })
            foreach (string raw in document.Get(field).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) {
                string value = raw.Trim();
                if (!items.TryGetValue(value, out var item)) items.TryGetValue(resolve(value, field.Contains("Relics")), out item);
                if (item != null) ValidateField(item, field, field.StartsWith("Team ", StringComparison.Ordinal) ? null : goalie);
            }
    }
}
