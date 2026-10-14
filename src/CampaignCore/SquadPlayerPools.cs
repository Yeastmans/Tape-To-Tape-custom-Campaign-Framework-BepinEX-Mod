using System;
using System.IO;

namespace CampaignCore;

public static class SquadPlayerPools
{
    public const string ModeKey = "Use Own Player Pools";
    public static bool Own(CampaignDocument team) => team != null &&
        (team.Get(ModeKey).Equals("yes", StringComparison.OrdinalIgnoreCase)
        || team.Get(ModeKey).Equals("true", StringComparison.OrdinalIgnoreCase));

    public static string DirectoryPath(string campaign, string squad, CampaignDocument team, string pool)
    {
        if (pool != "draft_pool" && pool != "superstars" && pool != "free_agents")
            throw new ArgumentException("Unknown player pool.", nameof(pool));
        return Path.Combine(Own(team) ? squad : campaign, "player_teams", pool);
    }

    // An enabled squad pool never falls back to shared/native candidates, even
    // before its first save or when an explicit selection is empty.
    public static string[] Read(string campaign, string squad, CampaignDocument team, string pool)
    {
        string directory = DirectoryPath(campaign, squad, team, pool);
        var selected = SelectedPlayerPool.Read(directory);
        if (selected != null || !Own(team)) return selected;
        return System.IO.Directory.Exists(directory) ? System.IO.Directory.GetFiles(directory, "*.txt") : Array.Empty<string>();
    }
}
