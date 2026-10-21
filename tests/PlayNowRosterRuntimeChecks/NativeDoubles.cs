using CampaignCore;
using Data;
using Tape2Tape.Customization;
namespace Il2CppSystem.Collections.Generic { public class List<T> : System.Collections.Generic.List<T> { static int next; public IntPtr Pointer = (IntPtr)(++next); } }
namespace Data
{
    public class SkaterData { public string id, OriginalId; public T TryCast<T>() where T:class => this as T; }
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
    static class PlayNowRosterSave { public static void Clear() {} public static void AcceptLoaded(TeamRepository r, TeamDataModel m, TeamData t) {} public static void Clone(TeamData a,TeamData b) {} }
    static class PlayNowTranslation { internal static Dictionary<string, CampaignDocument> Documents = new(); public static CampaignDocument Read(string id, bool team = false) => Documents.GetValueOrDefault(id); }
    static class Plugin { public static Logger Log = new(); }
    class Logger { public void LogWarning(object message) { } }
}

namespace UnityEngine { public class Object { public static void Destroy(object value) { } } }
namespace HarmonyLib {
    public class HarmonyPatch : Attribute { public HarmonyPatch(Type t,string name) {} public HarmonyPatch(Type t,string name,Type[] args) {} }
    public class HarmonyPrefix : Attribute {} public class HarmonyPostfix : Attribute {} public class HarmonyFinalizer : Attribute {}
    public class HarmonyMethod { public HarmonyMethod(Type t,string name) {} }
    public class Harmony { public void Patch(object original,HarmonyMethod prefix=null,HarmonyMethod postfix=null,HarmonyMethod finalizer=null) {} }
    public static class AccessTools { public static System.Reflection.ConstructorInfo[] GetDeclaredConstructors(Type t)=>t.GetConstructors(); public static object Method(Type t,string n)=>null; }
}
namespace Tape2Tape.Customization.UI {
    public class TeamRosterData {
        public TeamData OriginalTeamData,TemporaryTeamData;
        public ForwardData m_LW,m_RW,m_C,m_LD,m_RD; public GoaltenderData m_G;
        public Il2CppSystem.Collections.Generic.List<ForwardData> m_BenchForwards = new();
    }
}
namespace Tape2Tape {
    public class Team { static int next; public IntPtr Pointer=(IntPtr)(++next); public TeamData _teamData; public void Initialize() {} }
    public static class CampaignUtil { public static CampaignState CampaignState; }
    public class CampaignState { public Squad GetCurrentSquad()=>null; }
    public class Squad { public string id; }
}
namespace Skaters {
    public class StaminaConfig { }
    public class TeamStaminaSystem { public bool _isLinesTeam; public StaminaConfig _config; public bool IsActive=>_isLinesTeam; public int Refills, Rebuilds; public void Initialize(){} public void RefillAll()=>Refills++; public void RebuildOnIce()=>Rebuilds++; }
}
namespace UnityEngine { public static class Resources { public static T Load<T>(string name) where T:new()=>new T(); } }
namespace EndlessMode { public static class SquadBehaviorRuntime { public static bool TryEnabled(string id,string flag,out bool enabled) {enabled=false;return false;} } }

namespace EndlessMode { public static class NativeSquadBehaviorBridge { public static bool Ready=true; } }
