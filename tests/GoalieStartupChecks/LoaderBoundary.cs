using CampaignCore;
namespace LoaderBoundary;
// Native configuration construction is outside folder classification. The actual
// loader body is extracted unchanged; file selection/validation uses production Core.
static partial class Plugin
{
    internal static bool PlayerTeamsEnabled = true;
    internal static string ModFolder = "";
    internal static Dictionary<string, TeamConfig> PlayerTeamConfigs = new();
    internal static Dictionary<string, PlayerConfig> DraftPoolConfigs = new();
    internal static List<PlayerConfig> SuperstarPoolList = new(), FreeAgentPoolList = new();
    internal static Logger Log = new();
    internal static void LoadPlayerFile(string path, PlayerConfig config) => config.Fields = new(File.ReadAllText(path));
    internal static void LoadTeamFile(string path, TeamConfig config) { }
    internal static void LoadPlayersFolder(string path, TeamConfig config) { }
    internal static void LoadTeamGoalieGear(string path, TeamConfig config) { }
}
class PlayerConfig { internal CampaignDocument Fields = new(""); }
class TeamConfig { }
class Logger { internal void LogInfo(string message) { } }
static class CustomFreeAgentTemplates { internal static void Invalidate() { } }
