namespace UnityEngine {
 public class Object { public static T Instantiate<T>(T source) where T: ICloneable => (T)source.Clone(); }
}
namespace Il2CppSystem.Collections.Generic { public class List<T>:System.Collections.Generic.List<T> {} }
namespace Data {
 public class ForwardData:ICloneable { public string id,firstName,lastName=""; public int speed; public object Clone()=>MemberwiseClone(); }
 public class GoaltenderData:ICloneable { public string id,firstName,lastName=""; public int skill; public object Clone()=>MemberwiseClone(); }
 public class Lineup {public string leftWinger,rightWinger,center,leftDefensemen,rightDefensemen;}
 public class TeamData { static int counter; public IntPtr Pointer=new(++counter); public Il2CppSystem.Collections.Generic.List<ForwardData> forwards=new(); public Il2CppSystem.Collections.Generic.List<Lineup> lines=new(); public GoaltenderData goalie; public bool leftWingerLineUpCreationLocked,rightWingerLineUpCreationLocked,centerLineUpCreationLocked,leftDefensemenLineUpCreationLocked,rightDefensemenLineUpCreationLocked; }
}
namespace State { public class RunSquadScriptableObject {public string id; public Data.TeamData startingTeam;} }
namespace EndlessMode {
 public static class VanillaCampaignRuntime {public static bool InheritedSquad(TeamConfig team)=>false;}
 public class PlayerConfig {public string Name; public int Speed;}
 public class UniformConfig {}
 public class TeamConfig {public PlayerConfig LW,RW,C,LD,RD,L2_LW,L2_RW,L2_C,L2_LD,L2_RD,Goalie; public UniformConfig Uniform=new();}
 public static class PatchChooseMetaUI {public static bool SlotIsConfigured(PlayerConfig p)=>p!=null && !string.IsNullOrEmpty(p.Name);}
 public static class PatchBossLaunchMatch {public static void ApplyPlayerConfig(Data.ForwardData f,PlayerConfig p,UniformConfig u){f.firstName=p.Name;f.lastName="";f.speed=p.Speed;}}
 public class Logger { public void LogInfo(string text){} }
 public static class Plugin {public static string ModFolder; public static int GamesPlayed; public static Dictionary<string,TeamConfig> PlayerTeamConfigs=new(); public static Dictionary<string,PlayerConfig> DraftPoolConfigs=new(); public static List<PlayerConfig> SuperstarPoolList=new(); public static Dictionary<string,PlayerConfig> FreeAgentSignedConfigs=new(); public static Logger Log=new();}
}
