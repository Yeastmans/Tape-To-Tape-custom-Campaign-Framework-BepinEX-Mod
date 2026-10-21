using System;
using System.Collections.Generic;

namespace CampaignCore;

public static class GoalieFeatureOptions
{
    public const string RecruitmentKey = "Goalie Recruitment";
    public const string ReserveBehaviorId = "reserve_goalie";
    public const string ReserveKey = "Squad Behavior " + ReserveBehaviorId;

    public static bool Recruitment(CampaignDocument campaign) => Enabled(campaign.Get(RecruitmentKey));
    public static bool Reserve(CampaignDocument squad) => Enabled(squad.Get(ReserveKey));

    private static bool Enabled(string value) => (value ?? "").Trim().ToLowerInvariant() is "on" or "yes" or "true" or "1";

    public static IReadOnlyList<string> ValidateNodes(bool recruitmentEnabled, IEnumerable<MapLayout> layouts)
        => ValidateNodes(recruitmentEnabled, layouts, 1);

    public static IReadOnlyList<string> ValidateNodes(bool recruitmentEnabled, IEnumerable<MapLayout> layouts, int firstMapNumber)
    {
        if (recruitmentEnabled) return Array.Empty<string>();
        var errors = new List<string>();
        int mapIndex = firstMapNumber - 1;
        foreach (var layout in layouts)
        {
            mapIndex++;
            foreach (var node in layout.Nodes)
                if (node.Type == "GeneralManager" && node.GmRole == GoalieFreeAgents.Goalies)
                    errors.Add($"Map {mapIndex}, Layer {node.Layer + 1}, node {layout.NativeIndex(node) + 1}: Goalies recruitment requires Goalie Recruitment. Enable recruitment or change this node to Skaters.");
        }
        return errors;
    }
}
