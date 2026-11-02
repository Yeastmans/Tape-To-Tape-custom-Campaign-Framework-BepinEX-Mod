using CampaignCore;
using Data;
using EndlessMode;

int checks = 0, failures = 0;
void Check(bool condition, string name) { checks++; if (!condition) { failures++; Console.WriteLine("FAIL " + name); } }
const string Helmet = "Faces/Custom/Helmet_Colors", NoHelmet = "__NO_HELMET__";
// Exercise production ApplyFreshPlayerConfig at its legacy generic-config boundary.
// That boundary is intentionally inert here: the tested behavior is protecting
// native empty fields before the generic default fill can add a helmet.
foreach (var (home, away, expectedHome, expectedAway) in new[] {
    ("", "", NoHelmet, NoHelmet), (Helmet, Helmet, Helmet, Helmet),
    ("", Helmet, NoHelmet, Helmet), (Helmet, "", Helmet, NoHelmet) })
{
    var player = new ForwardData { helmetSkin = home, helmetAwaySkin = away, ability = "native" };
    var config = new PlayerConfig { Fields = new CampaignDocument(VanillaCopyData.PlayerId + " = stock-player\nOverride Team Settings = no\n") };
    CampaignSquadSetup.ApplyFreshPlayerConfig(player, config);
    Check(player.helmetSkin == expectedHome && player.helmetAwaySkin == expectedAway,
        "Native pool clone protects each kit before generic defaults: " + home + "/" + away);
}
foreach (var (home, away) in new[] { ("", ""), (Helmet, Helmet), (NoHelmet, Helmet) })
{
    var player = new ForwardData { helmetSkin = home, helmetAwaySkin = away };
    CampaignSquadSetup.ApplyFreshPlayerConfig(player, new PlayerConfig());
    Check(player.helmetSkin == home && player.helmetAwaySkin == away,
        "Independent custom player retains existing default/explicit helmet input: " + home + "/" + away);
}
foreach (bool native in new[] { false, true })
foreach (string authored in new[] { "none", Helmet })
{
    var player = new ForwardData { helmetSkin = "", helmetAwaySkin = Helmet };
    var config = new PlayerConfig { Fields = new CampaignDocument((native ? VanillaCopyData.PlayerId + " = stock-player\n" : "")
        + "Override Team Settings = yes\nHelmet = " + authored + "\nHelmet Away = none\n") };
    CampaignSquadSetup.ApplyFreshPlayerConfig(player, config);
    Check(ReferenceEquals(PatchBossLaunchMatch.LastConfig, config) && config.Fields.Get("Helmet") == authored
        && config.Fields.Get("Helmet Away") == "none" && AppearanceFields.OverridesTeam(config.Fields),
        "Explicit authored helmet choices remain intact for subsequent real config application: " + native + "/" + authored);
}
Console.WriteLine($"{checks - failures}/{checks} native-pool appearance boundary checks passed; generic application and native rendering are separate checks.");
return failures == 0 ? 0 : 1;

namespace Data
{
    public class ForwardData
    {
        public string id, name, firstName, lastName, helmetSkin, helmetAwaySkin, ability;
        public Il2CppSystem.Collections.Generic.List<Rogue.Talent> powerups = new();
        public void ResetTalentCache() { }
    }
    public class TeamData { public Il2CppSystem.Collections.Generic.List<ForwardData> forwards = new(); public List<Line> lines = new(); }
    public class Line { public string leftWinger, rightWinger, center, leftDefensemen, rightDefensemen; }
    public class RunSquadScriptableObject { public string id, squadName; public bool m_UsesFreeAgentIntro; public int m_MaxBenchwarmersOnBench; public TeamData startingTeam; public Il2CppSystem.Collections.Generic.List<ForwardData> m_CustomSwapPool; }
}
namespace Rogue { public class Talent { } }
namespace State { public class Placeholder { } }
namespace Il2CppSystem.Collections.Generic { public class List<T> : System.Collections.Generic.List<T> { } }
namespace UnityEngine
{
    public class Object { public static T Instantiate<T>(T value) => throw new Exception("Unused allocation boundary"); }
    public class Resources { public static T[] FindObjectsOfTypeAll<T>() => throw new Exception("Unused native resource boundary"); }
}
namespace HarmonyLib { public class Harmony { } }
namespace EndlessMode
{
    public class PlayerConfig { public CampaignDocument Fields = new(""); public string Ability; public List<string> Talents = new(); public int RandomTalentCount; }
    public class UniformConfig { }
    public class TeamConfig { public string IdentityKey, SquadTemplate; public CampaignDocument Fields = new(""); }
    public static class Plugin
    {
        public static string ModFolder = "unused"; public static Dictionary<string, TeamConfig> PlayerTeamConfigs = new();
        public static Logger Log = new(); public static void LoadPlayerFile(string file, PlayerConfig config) => throw new Exception("Unused load boundary");
    }
    public class Logger { public void LogInfo(string message) { } }
    public static class PatchBossLaunchMatch
    {
        public static PlayerConfig LastConfig;
        public static void ApplyPlayerConfig(ForwardData player, PlayerConfig config, UniformConfig uniform) { LastConfig = config; }
    }
    public static class NativeSquadBehaviorBridge { public static void Install() { } }
    public static class SquadStartingScreens { public static bool Enabled(TeamConfig c, string key) => false; public static bool EmptyPool(string kind, TeamConfig c) => false; }
    public static class SquadPoolRuntime
    {
        public static string DirectoryPath(TeamConfig c, string kind) => "unused";
        public static bool Inherited(TeamConfig c, string kind) => false;
        public static string[] Selected(TeamConfig c, string kind) => [];
    }
    public static class VanillaCampaignRuntime { public static ForwardData CloneCandidate(PlayerConfig c) => null; }
    public static class PlayerContractRuntime { public static void StartingCandidate(RunSquadScriptableObject s, ForwardData p) { } }
}
