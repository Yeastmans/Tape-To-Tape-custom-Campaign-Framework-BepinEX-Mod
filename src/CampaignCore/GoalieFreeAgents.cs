using System;
using System.IO;
using System.Linq;

namespace CampaignCore;

// Goalie recruitment is an authored campaign pool, separate from native
// ForwardData free agents. An empty pool never recruits an unrelated player.
public static class GoalieFreeAgents
{
    public const string Skaters = "Skaters", Goalies = "Goalies";
    public const string Folder = "goalie_free_agents";
    public static string DirectoryPath(string campaign) => Path.Combine(campaign, "player_teams", Folder);
    public static string[] SelectedFiles(string campaign)
    {
        string directory = DirectoryPath(campaign);
        var selected = SelectedPlayerPool.Read(directory)
            ?? (Directory.Exists(directory) ? Directory.GetFiles(directory, "*.txt").OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray() : Array.Empty<string>());
        foreach (string file in selected)
        {
            if (!File.Exists(file)) throw new FileNotFoundException("A selected goalie free agent is missing.", file);
            var document = new CampaignDocument(File.ReadAllText(file));
            if (!document.Get("Position").Equals("Goalie", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The goalie free-agent pool contains a player who is not a goalie: " + Path.GetFileName(file));
        }
        return selected;
    }
}
