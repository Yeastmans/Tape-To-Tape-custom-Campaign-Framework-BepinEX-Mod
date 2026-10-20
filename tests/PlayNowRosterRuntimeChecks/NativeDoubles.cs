using CampaignCore;
using Data;
namespace Il2CppSystem.Collections.Generic { public class List<T> : System.Collections.Generic.List<T> { static int next; public IntPtr Pointer = (IntPtr)(++next); } }
namespace Data
{
    public class SkaterData { public string id, OriginalId; }
    public class ForwardData : SkaterData { }
    public class GoaltenderData : SkaterData { }
    public class TeamData
    {
        static int next; public int InstanceId = ++next; public IntPtr Pointer; public string id;
        public TeamData() { Pointer = (IntPtr)InstanceId; }
        public int GetInstanceID() => InstanceId;
        public Il2CppSystem.Collections.Generic.List<ForwardData> forwards = new();
        public Il2CppSystem.Collections.Generic.List<Lineup> lines = new();
        public GoaltenderData goalie; public int activeLineNumber = 1;
    }
    public class Lineup
    {
        public TeamData teamData; public string leftWinger, rightWinger, center, leftDefensemen, rightDefensemen;
        public Lineup(TeamData t) { teamData = t; }
    }
}
namespace Tape2Tape.Customization
{
    public class TeamDataModel { public string Id, leftWingerId, rightWingerId, centerId, leftDefensemenId, rightDefensemenId, goalieId; }
    public class ForwardRepository { public Il2CppSystem.Collections.Generic.List<ForwardData> Players = new(); public Il2CppSystem.Collections.Generic.List<ForwardData> LoadAllForwards() => Players; }
    public class CustomTeamsRepository { public Il2CppSystem.Collections.Generic.List<TeamDataModel> Models = new(); public Il2CppSystem.Collections.Generic.List<TeamDataModel> LoadCustomTeams() => Models; }
    public class TeamRepository
    {
        static int next; public IntPtr Pointer = (IntPtr)(++next); public ForwardRepository m_forwardRepository = new(); public CustomTeamsRepository m_customTeamRepository = new();
        public int Calls; public string FailId;
        public ForwardData InstantiateForwardWithId(Il2CppSystem.Collections.Generic.List<ForwardData> list, string id) { Calls++; if (id == FailId) throw new Exception("fixture resolution failure"); return list.Any(p => p.id == id) ? new ForwardData { id = id } : null; }
    }
}
namespace EndlessMode
{
    static class PlayNowTranslation { internal static Dictionary<string, CampaignDocument> Documents = new(); public static CampaignDocument Read(string id, bool team = false) => Documents.GetValueOrDefault(id); }
    static class Plugin { public static Logger Log = new(); }
    class Logger { public void LogWarning(object message) { } }
}

namespace UnityEngine { public class Object { public static void Destroy(object value) { } } }
