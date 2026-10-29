namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)] public class HarmonyPatch(Type type, string method) : Attribute { }
    public class HarmonyPrefix : Attribute { }
    public class HarmonyPostfix : Attribute { }
}
namespace Data
{
    public class ForwardData
    {
        static int next;
        public IntPtr Pointer = (IntPtr)(++next);
        public string id;
        public string FullName, equipment = "Authored kit";
        public string talent = "Authored talent";
        public int speed;
    }
    public class Lineup
    {
        public string leftWinger, rightWinger, center, leftDefensemen, rightDefensemen;
        public string[] Ids => [center, leftWinger, rightWinger, leftDefensemen, rightDefensemen];
        public void UpdateLineWithNewID(string from, string to)
        {
            if (leftWinger == from) leftWinger = to;
            if (rightWinger == from) rightWinger = to;
            if (center == from) center = to;
            if (leftDefensemen == from) leftDefensemen = to;
            if (rightDefensemen == from) rightDefensemen = to;
        }
    }
    public class TeamData
    {
        static int next;
        public IntPtr Pointer = (IntPtr)(++next);
        public string id = "CCF_Opponent";
        public string teamName = "CHI";
        public ForwardData goalie = new() { id = "goalie", FullName = "Mirror Goalie", speed = 70 };
        public bool Owned;
        public List<ForwardData> forwards = [];
        public List<Lineup> lines = [];
        public int activeLineNumber = 1;
        public bool isMultiLineup => lines?.Count > 1;
        public List<ForwardData> benchPlayers => lines[activeLineNumber == 1 ? 1 : 0].Ids.Select(id => forwards.FirstOrDefault(p => p?.id == id)).ToList();
    }
}
namespace STS.Map { public class MatchMapNode { public Data.TeamData opponent; } }
namespace Tape2Tape.Hockey.UI
{
    public class Roster { public Data.TeamData OriginalTeamData; public List<Data.ForwardData> m_BenchForwards = []; }
    public class LineupMenu { public Roster Team; public bool m_AreSkatersLocked; }
}
namespace EndlessMode
{
    public static class Plugin { public static bool IsDefaultMode; public static LogSink Log = new(); }
    public class LogSink { public void LogWarning(string text) => throw new Exception(text); public void LogInfo(string text) { } }
    public static class CustomTeamRules { public static bool IsCustom(Data.TeamData team) => team?.id?.StartsWith("CCF_") == true; }
    public static class VanillaCampaignRuntime { public static bool Owns(Data.TeamData team) => team?.Owned == true; }
}
namespace EndlessMode { public static class GoalieRosterMenu { public static void EntryLabel(Tape2Tape.Hockey.UI.LineupMenu menu) { } } }
namespace EndlessMode { public static class PlayNowRosterRuntime { public static bool TryGet(Data.TeamData team,out object roster) { roster=null; return false; } } public static class PlayNowRosterContext { public static void RefreshLocked(Tape2Tape.Hockey.UI.Roster roster) { } } }
