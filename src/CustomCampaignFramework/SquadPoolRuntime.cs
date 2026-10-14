using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CampaignCore;
using State;

namespace EndlessMode;

internal static class SquadPoolRuntime
{
    private sealed class Snapshot
    {
        internal string Revision;
        internal List<PlayerConfig> Players;
    }
    private static readonly Dictionary<string, Snapshot> snapshots = new(StringComparer.OrdinalIgnoreCase);
    internal static TeamConfig Team(RunSquadScriptableObject squad) => Team(squad?.id);
    internal static TeamConfig Team(string id)
    {
        if (id == null) return null;
        string key = id.StartsWith("Custom_", StringComparison.OrdinalIgnoreCase) ? id.Substring(7) : id;
        return Plugin.PlayerTeamConfigs.TryGetValue(key, out var team) ? team : null;
    }
    internal static TeamConfig Current()
    {
        try { return Team(Tape2Tape.CampaignUtil.CampaignState?.GetCurrentSquad()); }
        catch { return null; }
    }
    internal static bool Own(TeamConfig team) => SquadPlayerPools.Own(team?.Fields);
    private static string SquadDirectory(TeamConfig team) => Path.Combine(Plugin.ModFolder, team?.IdentityKey ?? "");
    internal static string DirectoryPath(TeamConfig team, string pool) => SquadPlayerPools.DirectoryPath(Plugin.ModFolder, SquadDirectory(team), team?.Fields, pool);
    internal static string[] Selected(TeamConfig team, string pool) => SquadPlayerPools.Read(Plugin.ModFolder, SquadDirectory(team), team?.Fields, pool);
    internal static bool Inherited(TeamConfig team, string pool) => !Own(team) && VanillaCampaignRuntime.InheritedPool(pool);
    internal static bool AuthoredFreeAgents(TeamConfig team) => Own(team) || Plugin.FreeAgentPoolList.Count > 0
        || new[] { Path.Combine(Plugin.ModFolder, "free_agents"), DirectoryPath(team, "free_agents"), Path.Combine(Plugin.ModFolder, "player_teams", "free agents") }
            .Any(path => !VanillaCopyData.PoolInherited(path) && File.Exists(Path.Combine(path, SelectedPlayerPool.Manifest)));
    internal static List<PlayerConfig> Superstars(TeamConfig team) => Own(team) ? Read(team, "superstars") : Plugin.SuperstarPoolList;
    internal static List<PlayerConfig> FreeAgents(TeamConfig team) => Own(team) ? Read(team, "free_agents") : Plugin.FreeAgentPoolList;
    internal static bool IsFreeAgent(PlayerConfig config) => Plugin.FreeAgentPoolList.Contains(config)
        || snapshots.Any(pair => Path.GetFileName(pair.Key) == "free_agents" && pair.Value.Players.Contains(config));
    private static List<PlayerConfig> Read(TeamConfig team, string pool)
    {
        string directory = DirectoryPath(team, pool);
        var files = Selected(team, pool) ?? Array.Empty<string>();
        string revision = string.Join("|", files.Select(f => f + ":" + File.GetLastWriteTimeUtc(f).Ticks + ":" + (File.Exists(f) ? new FileInfo(f).Length : -1)));
        if (snapshots.TryGetValue(directory, out var snapshot) && snapshot.Revision == revision) return snapshot.Players;
        var players = new List<PlayerConfig>();
        foreach (string file in files)
        {
            if (!File.Exists(file)) throw new FileNotFoundException("Selected squad player is missing.", file);
            var config = new PlayerConfig(); Plugin.LoadPlayerFile(file, config);
            if (pool == "superstars")
            {
                if (config.Fields.Get("Override Team Settings").Length == 0) config.Fields.Set("Override Team Settings", "no");
                config.Fields.Set("Export Version", "");
            }
            players.Add(config);
        }
        snapshots[directory] = new Snapshot { Revision = revision, Players = players };
        return players;
    }
}
