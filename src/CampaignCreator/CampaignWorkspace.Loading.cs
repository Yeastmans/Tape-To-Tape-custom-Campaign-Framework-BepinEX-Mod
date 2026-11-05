using CampaignCore;

namespace CampaignCreator;

public sealed partial class CampaignWorkspace
{
    internal sealed record OverviewData(List<CampaignNode> Opponents, List<CampaignNode> Squads,
        Dictionary<string, List<string>> Assignments, List<MatchNode> Nodes, int SelectedBaseAgents, int CustomAgents);
    internal sealed record OpeningData(CampaignDocument Settings, CampaignDocument Rewards, OverviewData Overview,
        Dictionary<string, (bool Base, int Count)> RewardSummary);

    // The picker runs this on a worker. Do not build controls, player editors or reward catalogs for base pools here.
    internal static OpeningData ReadOpeningData(string root, string directory, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var settings = new CampaignDocument(File.ReadAllText(Path.Combine(directory, "campaign.txt")));
        var rewards = ReadRewards(directory);
        var overview = ReadOverviewData(root, directory, settings, token);
        return new(settings, rewards, overview, ReadRewardSummary(root, directory, rewards, token));
    }
    private static CampaignDocument ReadRewards(string directory)
    {
        string path = Path.Combine(directory, "reward_pools.txt");
        return new CampaignDocument(File.Exists(path) ? File.ReadAllText(path) : "# Campaign reward pools\n");
    }
    private static OverviewData ReadOverviewData(string root, string directory, CampaignDocument settings, CancellationToken token,
        MapEditorView? liveMaps = null)
    {
        var opponents = CampaignNavigation.Teams(directory, false, token: token);
        var squads = CampaignNavigation.Teams(directory, true, token: token);
        token.ThrowIfCancellationRequested();
        var assignments = liveMaps?.AssignedOpponents ?? OpponentAssignments.Load(directory);
        var nodes = liveMaps?.MatchNodes() ?? SavedMatchNodes(root, directory, settings);
        var selectedBase = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int customAgents = 0;
        foreach (string dir in new[] { Path.Combine(directory, "free_agents"), Path.Combine(directory, "player_teams", "free_agents") })
        {
            if (!Directory.Exists(dir)) continue;
            var selected = SelectedPlayerPool.Read(dir)?.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (string file in Directory.EnumerateFiles(dir, "*.txt", SearchOption.AllDirectories).Where(f => !ContentRemoval.IsArchivedPath(f)))
            {
                string marker = CampaignNavigation.ReadSummary(file, token).Get(FreeAgentContent.Marker);
                if (!FreeAgentContent.BaseNames.Contains(marker, StringComparer.OrdinalIgnoreCase)) customAgents++;
                else if (Path.GetDirectoryName(file) == dir && (selected == null || selected.Contains(file))) selectedBase.Add(marker);
            }
        }
        return new(opponents, squads, assignments, nodes, selectedBase.Count, customAgents);
    }
    private static List<MatchNode> SavedMatchNodes(string root, string directory, CampaignDocument settings)
    {
        try { return OpponentAssignments.MatchNodes(OpponentAssignments.LoadLayouts(root, directory,
            settings.Get("Squad Template", "Basic Squad"), settings.Get("Act Sequence", CampaignActSequence.Default))); }
        catch { return []; }
    }
    private static Dictionary<string, (bool Base, int Count)> ReadRewardSummary(string root, string directory,
        CampaignDocument rewards, CancellationToken token)
    {
        var result = new Dictionary<string, (bool, int)>();
        Dictionary<string, CustomRewardDefinition>? custom = null;
        foreach (string kind in RewardPoolDefinition.Kinds)
        {
            token.ThrowIfCancellationRequested();
            string mode = rewards.Get("Pool Mode " + kind);
            bool legacy = mode.Length == 0 && (rewards.Get("Included " + kind).Length > 0 || rewards.Get("Excluded " + kind).Length > 0
                || kind == "Goalie Talents" && rewards.Get("Excluded Talents").Length > 0);
            if (!legacy && !mode.Equals("Custom", StringComparison.OrdinalIgnoreCase)) { result[kind] = (true, 0); continue; }
            var pool = RewardCatalog.ReadPool(root, rewards, kind);
            if (!pool.UseBaseGame && CustomRewardPoolSelection.ForNative(kind).Length > 0 &&
                pool.Selected.Any(id => id.StartsWith("CCF_Reward_", StringComparison.OrdinalIgnoreCase)))
            {
                custom ??= CustomRewardCatalog.Items(root, directory);
                pool.Selected.RemoveWhere(custom.ContainsKey);
            }
            result[kind] = (pool.UseBaseGame, pool.Selected.Count);
        }
        return result;
    }
}
