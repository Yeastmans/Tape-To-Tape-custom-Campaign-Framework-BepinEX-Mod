using CampaignCore;
using EndlessMode;
using Data;
using Tape2Tape.Customization;
using System.Text.Json;
namespace Il2CppSystem.Collections.Generic { public class List<T>:System.Collections.Generic.List<T> { static int next; public IntPtr Pointer=(IntPtr)(++next); } }
namespace UnityEngine {
 public class Object { static int next; public IntPtr Pointer; public int Instance=++next; public Object(){Pointer=(IntPtr)Instance;} public int GetInstanceID()=>Instance; public T TryCast<T>() where T:class=>this as T; public static void Destroy(object x){} }
 public struct Color {public float r,g,b,a;public Color(float x,float y,float z,float w=1){r=x;g=y;b=z;a=w;}}
 public static class JsonUtility {public static readonly JsonSerializerOptions Options=new(){IncludeFields=true};public static string ToJson(object x)=>JsonSerializer.Serialize(x,x.GetType(),Options);public static T FromJson<T>(string x)=>JsonSerializer.Deserialize<T>(x,Options);}
}
namespace Data {
 public class ColorScheme {public UnityEngine.Color primaryColor=new(1,1,1),secondaryColor=new(1,1,1),tertiaryColor=new(1,1,1);}
 public class TeamColorsData {public ColorScheme jerseyScheme=new(),helmetScheme=new(),glovesScheme=new(),pantsScheme=new(),socksScheme=new(),numberScheme=new(),stickScheme=new(),skatesScheme=new();}
 public class SkaterData:UnityEngine.Object {public string id,OriginalId,firstName="Name",lastName="Player",headSkin="personal-head",stickSkin="personal-stick",bodySkin="personal-body",bodyAwaySkin="personal-away";public int number=1,speed=50;public string abilityId="ability";public string[] talentIds={"talent"};public ColorScheme StickColor=new(),SkatesColor=new();}
 public class ForwardData:SkaterData{public TeamColorsData colorSchemes=new();}
 public class GoaltenderData:SkaterData{public TeamColorsData colorSchemes=new();public string skin="personal-goalie",helmetSkin="personal-mask",awaySkin="personal-away-goalie";public int skill=51,catchingSkill=52;public float passReadSkill=.731f;}
 public class TeamData:UnityEngine.Object {public string id;public Il2CppSystem.Collections.Generic.List<ForwardData> forwards=new();public Il2CppSystem.Collections.Generic.List<Lineup> lines=new();public GoaltenderData goalie;public int activeLineNumber=1;public string logoId="old-logo";public Il2CppSystem.Collections.Generic.List<string> relics=new();public TeamColorsData homeColors{get;set;}=new();public TeamColorsData awayColors{get;set;}=new();public TeamColorsData homeGoalieColors{get;set;}=new();public TeamColorsData awayGoalieColors{get;set;}=new();}
 public class Lineup {public TeamData teamData;public string leftWinger,rightWinger,center,leftDefensemen,rightDefensemen;public Lineup(TeamData team){teamData=team;}}
 public enum SkaterColorItem {Stick,Skates}
}
namespace Tape2Tape.Customization {
 public class ForwardDataModel {public string id,firstName,lastName,headSkin,stickSkin,bodySkin,bodyAwaySkin,abilityId;public int number,speed;public string[] talentIds;}
 public class GoalieDataModel{public string id,firstName,lastName,headSkin,skin,awaySkin,helmetSkin,abilityId;public int number,skill,catchingSkill;public string[] talentIds;}
 public class TeamDataModel {static long next; [System.Text.Json.Serialization.JsonIgnore] public IntPtr Pointer=(IntPtr)(++next);public string Id,leftWingerId,rightWingerId,centerId,leftDefensemenId,rightDefensemenId,goalieId,logoId;public Il2CppSystem.Collections.Generic.List<string> relics=new();public TeamColorsData homeColors,awayColors;}
 public class PersistentDataCustomForwardRepository:UnityEngine.Object {public string _persistentDataPath;public object _customForwardCache;public void SaveCustomForward(ForwardDataModel data){}public void LoadCustomForwards(){}public void Preload(){}}
 public class PersistentDataCustomGoalieRepository:UnityEngine.Object {public string _persistentDataPath;public object _customGoalieCache;public void SaveCustomGoalie(GoalieDataModel goalieDataModel){}public void LoadCustomGoalies(){}public void Preload(){}}
 public class PersistentDataCustomTeamsRepository:UnityEngine.Object {public string _path;public object m_customTeams;public Il2CppSystem.Collections.Generic.List<TeamDataModel> Models=new();public Il2CppSystem.Collections.Generic.List<TeamDataModel> LoadCustomTeams()=>Models;public void SaveCustomTeam(TeamDataModel teamDataModel){}public void Preload(){}}
 public class ForwardRepository:UnityEngine.Object {
  public PersistentDataCustomForwardRepository m_customForwardRepository=new();public Il2CppSystem.Collections.Generic.List<ForwardData> Players=new(),Builtins=new();
  public Il2CppSystem.Collections.Generic.List<ForwardData> LoadAllForwards()=>Players;public Il2CppSystem.Collections.Generic.List<ForwardData> LoadBuiltInForwards()=>Builtins;
  public static ForwardDataModel Model(SkaterData p)=>new(){id=p.id,firstName=p.firstName,lastName=p.lastName,headSkin=p.headSkin,stickSkin=p.stickSkin,bodySkin=p.bodySkin,bodyAwaySkin=p.bodyAwaySkin,abilityId=p.abilityId,talentIds=p.talentIds,number=p.number,speed=p.speed};
  public void SaveCustomForward(ForwardData skater){PlayNowPlayerSave.Frame state=null;Exception error=null;try {PlayNowPlayerSave.ForwardPrefix(skater,out state);var m=Model(skater);if(PlayNowPlayerSave.ForwardWriterPrefix(m_customForwardRepository,m))File.WriteAllText(PlayNowSaveJournal.NativePath(m_customForwardRepository._persistentDataPath,"forward",skater.id),UnityEngine.JsonUtility.ToJson(m));}catch(Exception e){error=e;}error=PlayNowPlayerSave.Finalizer(error,state);if(error!=null)throw error;}
 }
 public class GoalieRepository:UnityEngine.Object {
  public static GoalieDataModel Model(GoaltenderData p)=>new(){id=p.id,firstName=p.firstName,lastName=p.lastName,headSkin=p.headSkin,skin=p.skin,awaySkin=p.awaySkin,helmetSkin=p.helmetSkin,abilityId=p.abilityId,talentIds=p.talentIds,number=p.number,skill=p.skill,catchingSkill=p.catchingSkill};
  public PersistentDataCustomGoalieRepository _customGoalieRepository=new();public Il2CppSystem.Collections.Generic.List<GoaltenderData> Builtins=new();public Il2CppSystem.Collections.Generic.List<GoaltenderData> LoadBuiltInGoalies()=>Builtins;
  public void SaveCustomGoalie(GoaltenderData customGoalie){PlayNowPlayerSave.Frame state=null;Exception error=null;try{PlayNowPlayerSave.GoaliePrefix(customGoalie,out state);var model=Model(customGoalie);if(PlayNowPlayerSave.GoalieWriterPrefix(_customGoalieRepository,model))File.WriteAllText(PlayNowSaveJournal.NativePath(_customGoalieRepository._persistentDataPath,"goalie",customGoalie.id),UnityEngine.JsonUtility.ToJson(model));}catch(Exception e){error=e;}error=PlayNowPlayerSave.Finalizer(error,state);if(error!=null)throw error;}
 }
 public class TeamRepository:UnityEngine.Object {
  public ForwardRepository m_forwardRepository=new();public GoalieRepository m_goalieRepository=new();public PersistentDataCustomTeamsRepository m_customTeamRepository=new();
  public ForwardData InstantiateForwardWithId(Il2CppSystem.Collections.Generic.List<ForwardData> players,string id)=>players.First(p=>p.id==id);
  public static TeamDataModel Model(TeamData t){var l=t.lines[t.activeLineNumber-1];return new(){Id=t.id,leftWingerId=l.leftWinger,rightWingerId=l.rightWinger,centerId=l.center,rightDefensemenId=l.rightDefensemen,leftDefensemenId=l.leftDefensemen,goalieId=t.goalie.id,logoId=t.logoId,relics=t.relics,homeColors=t.homeColors,awayColors=t.awayColors};}
  public int SaveCalls; public void SaveCustomTeam(TeamData customTeam){SaveCalls++;PlayNowRosterSave.SaveFrame state=null;Exception error=null;try{PlayNowRosterSave.TeamPrefix(this,customTeam,out state);var model=Model(customTeam);if(PlayNowRosterSave.TeamWriterPrefix(m_customTeamRepository,model))File.WriteAllText(PlayNowSaveJournal.NativePath(m_customTeamRepository._path,"team",customTeam.id),UnityEngine.JsonUtility.ToJson(model));}catch(Exception e){error=e;}error=PlayNowRosterSave.TeamFinalizer(error,state);if(error!=null)throw error;}
 }
}
namespace Tape2Tape.Customization.UI {
 public class CustomizeSkaterData:UnityEngine.Object {public SkaterData SkaterData;}
 public class CustomizeForwardData:CustomizeSkaterData {public string headSkin,stickSkin;public CustomizeForwardData(ForwardData data){SkaterData=data;headSkin=data.headSkin;stickSkin=data.stickSkin;}}
 public class CustomizeGoalieData:CustomizeSkaterData {public string headSkin;public CustomizeGoalieData(GoaltenderData data){SkaterData=data;headSkin=data.headSkin;}}
 public struct SelectSkaterColorEvent {public SkaterColorItem Item;public int Index;}
 public class SkaterCustomizationMenu:UnityEngine.Object {public CustomizeSkaterData CurrentSkater;public SelectSkaterColorEvent m_CurrentColorBeingPicked;public void OnPanelHeadClick(){}public void OnPanelStickClick(){}public void OnColorPicked(){}public void Hide(){}}
 public class ForwardCustomizationHandler:UnityEngine.Object {public ForwardData _forwardData;public void LoadForward(){}public void NextFaceSkin(){}public void PrevFaceSkin(){}public void NextStickSkin(){}public void PrevStickSkin(){}}
 public class GoalieCustomizationHandler:UnityEngine.Object {public GoaltenderData _goalieData;public void LoadGoalie(){}}
}
namespace HarmonyLib {public class HarmonyMethod {public HarmonyMethod(Type t,string n){}}public class Harmony {public void Patch(object method,HarmonyMethod prefix=null,HarmonyMethod postfix=null,HarmonyMethod finalizer=null){}}public static class AccessTools {public static System.Reflection.ConstructorInfo[] GetDeclaredConstructors(Type t)=>t.GetConstructors();public static object Method(Type t,string name)=>t.GetMethod(name);public static object Constructor(Type t,Type[] args)=>t.GetConstructor(args);}}
namespace EndlessMode {
 public static class Plugin {public static string ModContentRoot;public static Logger Log=new();} public class Logger {public void LogWarning(object x){}}
 public static class PlayNowTranslation {
  static readonly Dictionary<string,CampaignDocument> Cache=new();public static int Invalidations;
  public static CampaignDocument Read(string id,bool team=false){string key=id+(team?".team":"");if(Cache.TryGetValue(key,out var d))return d;string path=Path.Combine(Plugin.ModContentRoot,"play_now_overrides",key+".txt");return File.Exists(path)?Cache[key]=new CampaignDocument(File.ReadAllText(path)):null;}
  public static void RegisterPreview(string id,CampaignDocument doc)=>Cache[id]=doc; public static void Invalidate(string id,bool team=false){Cache.Remove(id+(team?".team":""));Invalidations++;}public static void InvalidateAll()=>Cache.Clear();public static void Register(TeamColorsData colors,bool away){}
  public static void Player(SkaterData player,string id){var doc=Read(id);var model=UnityEngine.JsonUtility.FromJson<ForwardDataModel>(doc.Get("Export Model"));player.headSkin=model.headSkin;player.stickSkin=model.stickSkin;player.bodySkin=model.bodySkin;player.bodyAwaySkin=model.bodyAwaySkin;player.firstName=model.firstName;player.lastName=model.lastName;player.number=model.number;player.speed=model.speed;player.abilityId=model.abilityId;player.talentIds=model.talentIds;PlayNowPlayerSave.RestorePersonal(player,doc);}
 }
}

namespace Tape2Tape.Customization.UI { public class TeamRosterData {
 public bool HasChanged = true, RosterValid = true; public Func<bool> ApplyBody; public bool CheckRoster(bool requireMaxForwards)=>RosterValid; public bool ApplyChanges()=>ApplyBody();
 public TeamData OriginalTeamData,TemporaryTeamData;
 public ForwardData m_LW,m_RW,m_C,m_LD,m_RD; public GoaltenderData m_G;
 public Il2CppSystem.Collections.Generic.List<ForwardData> m_BenchForwards=new();
}}
namespace Tape2Tape.Hockey.UI { public class LineupMenu { public Tape2Tape.Customization.UI.TeamRosterData Team; public bool m_AreSkatersLocked; public Header m_Header = new(); } public class Header { public Text m_Title = new(); } public class Text { public string text; } }
namespace EndlessMode { public static class NativeSquadBehaviorBridge {public static bool Ready=true;} }
