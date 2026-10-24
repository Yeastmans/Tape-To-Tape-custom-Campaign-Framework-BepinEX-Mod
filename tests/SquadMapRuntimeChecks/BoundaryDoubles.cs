namespace EndlessMode {
    static class Plugin { internal static string ModFolder; internal static bool IsDefaultMode; internal static string[] ConfigTeamDirs = []; internal static Dictionary<string, TeamConfig> PlayerTeamConfigs = new(); }
    sealed class TeamConfig { internal string IdentityKey; }
    static class SquadPoolRuntime { internal static TeamConfig Team(string id) => Plugin.PlayerTeamConfigs.GetValueOrDefault(id?.Replace("Custom_", "") ?? ""); }
}
namespace Tape2Tape {
    static class CampaignUtil { internal static State CampaignState = new(); }
    sealed class State { internal Run runData = new(); internal Squad Selected = new(); internal Squad GetCurrentSquad() => Selected; }
    sealed class Run { internal string squadId; }
    sealed class Squad { internal string id; }
}
