using System.Collections;
using System.Reflection;
using CampaignCore;
using CampaignCreator;
using Data;
using EndlessMode;
using Tape2Tape.Customization;
using Tape2Tape.Customization.UI;
using UnityEngine;

// Real Studio producer, runtime hydration/context/save, Studio import/update.
// Only native objects/storage boundaries are doubled; all files are disposable.
static class StudioBoundaryChecks
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;
    static readonly Type Export = typeof(MainWindow).Assembly.GetType("CampaignCreator.ExportTranslation", true);
    static object Call(string name, params object[] args) => Export.GetMethod(name, Flags).Invoke(null, args);
    static T Property<T>(object value,string name) => (T)value.GetType().GetProperty(name).GetValue(value);
    internal static void Run(string root, Action<bool,string> check)
    {
        foreach(bool stamina in new[]{true,false})
        {
            string workspace=Path.Combine(root,stamina.ToString()), native=Path.Combine(workspace,"native"), library=Path.Combine(workspace,"workspace");
            Plugin.ModContentRoot=native;PlayNowRosterRuntime.Clear();PlayNowTranslation.InvalidateAll();
            string save=Path.Combine(native,"TeamDataModels"), side=PlayNowRosterSave.Overrides;
            Directory.CreateDirectory(save);
            string teamFile=Path.Combine(library,"source","team.txt"), players=Path.Combine(library,"source","players");Directory.CreateDirectory(players);
            string[] positions={"Left Wing","Center","Right Wing","Left Defense","Right Defense"};
            var teamDoc=new CampaignDocument("Team Name = Boundary\nLogo From = none\nSquad Behavior two_lines = On\nSquad Behavior line_stamina = "+(stamina?"On":"Off"));teamDoc.Save(teamFile);
            for(int line=0;line<2;line++)for(int slot=0;slot<5;slot++) {
                string position=(line==0?"":"Line 2 ")+positions[slot];
                File.WriteAllText(Path.Combine(players,position+".txt"),$"Name = Player {line}-{slot}\nPosition = {position}\nFace = Faces/Anyteam/Black_Helmet\n");
            }
            File.WriteAllText(Path.Combine(players,"Goalie.txt"),"Name = Goalie\nPosition = Goalie\nCatching = 50\n");
            object game=Activator.CreateInstance(Export.Assembly.GetType("CampaignCreator.GameData",true),library);
            ((AssetCatalog)game.GetType().GetProperty("Assets").GetValue(game)).Add("skater","Face","Faces/Anyteam/Black_Helmet");
            object package=Call("BuildTeam",teamDoc,teamFile,game,library,null);
            Call("Write",package,save,side,library);
            string id=Property<string>(package,"TeamId");
            var sourceDoc=new CampaignDocument(Property<Dictionary<string,string>>(package,"Overrides")[id+".team.txt"]);
            check(PlayNowRosterCodec.TryRead(sourceDoc,out var authored,out _),"Studio extension reads");
            var repo=new TeamRepository();repo.m_customTeamRepository._path=native;
            repo.m_forwardRepository.m_customForwardRepository._persistentDataPath=native;repo.m_goalieRepository._customGoalieRepository._persistentDataPath=native;
            foreach(string pid in authored.Line1.Concat(authored.Line2))repo.m_forwardRepository.Players.Add(new ForwardData{id=pid});
            TeamData Load() {
                var model=JsonUtility.FromJson<TeamDataModel>(File.ReadAllText(PlayNowSaveJournal.NativePath(native,"team",id)));
                var models=new Il2CppSystem.Collections.Generic.List<TeamDataModel>();models.Add(model);
                PlayNowRosterSave.TeamModelsPostfix(repo.m_customTeamRepository,models);
                var team=new TeamData{id=id,goalie=new GoaltenderData{id=model.goalieId}};
                foreach(string pid in new[]{model.leftWingerId,model.centerId,model.rightWingerId,model.leftDefensemenId,model.rightDefensemenId})team.forwards.Add(repo.m_forwardRepository.Players.Single(p=>p.id==pid));
                check(PlayNowRosterRuntime.TryHydrate(repo,model,team),"Actual Studio export must hydrate owned two-line roster");return team;
            }
            var live=Load();
            check(live.lines[0].center==authored.Line1[1] && live.lines[0].rightWinger==authored.Line1[2] && live.lines[1].center==authored.Line2[1] && live.lines[1].rightWinger==authored.Line2[2],"Distinct centers and wings retain named positions");
            live.activeLineNumber=2;
            var clone=new TeamData{id=id,goalie=live.goalie};foreach(var p in live.forwards)clone.forwards.Add(p);foreach(var l in live.lines)clone.lines.Add(new Lineup(clone){leftWinger=l.leftWinger,center=l.center,rightWinger=l.rightWinger,leftDefensemen=l.leftDefensemen,rightDefensemen=l.rightDefensemen});
            var editor=new TeamRosterData{OriginalTeamData=live,TemporaryTeamData=clone};PlayNowRosterContext.Constructed(editor);
            check(editor.m_C.id==authored.Line1[1] && editor.m_RW.id==authored.Line1[2] && live.activeLineNumber==2,"Editor shows absolute Line1 independently of selected line");
            check(editor.m_BenchForwards.Select(p=>p.id).SequenceEqual(new[]{authored.Line2[1],authored.Line2[0],authored.Line2[2],authored.Line2[3],authored.Line2[4]}),"Native bench keeps C LW RW LD RD mapping");
            (live.lines[0].center,live.lines[1].rightWinger)=(live.lines[1].rightWinger,live.lines[0].center);
            repo.SaveCustomTeam(live);
            PlayNowRosterRuntime.Clear();PlayNowTranslation.InvalidateAll();live=Load();
            check(PlayNowRosterRuntime.TryGet(live,out var saved) && saved.StaminaEnabled==stamina && saved.Line1[1]==authored.Line2[2] && saved.Line2[2]==authored.Line1[1],"Saved reload keeps positions ownership and stamina");
            Type importer=Export.Assembly.GetType("CampaignCreator.GameCustomContent",true);
            var imported=((IEnumerable)importer.GetMethod("Load",Flags,null,new[]{typeof(string),typeof(string),typeof(string)},null).Invoke(null,new object[]{library,save,side})).Cast<object>();
            string importedTeam=imported.Select(x=>Property<string>(x,"File")).Single(f=>Path.GetFileName(f)=="team.txt");
            var importedPlayers=Directory.GetFiles(Path.Combine(Path.GetDirectoryName(importedTeam),"players"),"*.txt").Select(f=>new CampaignDocument(File.ReadAllText(f))).ToArray();
            check(importedPlayers.Single(p=>p.Get("Position")=="Center").Get("Name")=="Player 1-2" && importedPlayers.Single(p=>p.Get("Position")=="Line 2 Right Wing").Get("Name")=="Player 0-1","Studio reimport preserves rearranged center and wing");
            object rebuilt=Call("BuildTeam",new CampaignDocument(File.ReadAllText(importedTeam)),importedTeam,game,library,null);
            object target=((IEnumerable)Call("ExistingTeams",save,side,library)).Cast<object>().Single(x=>Property<string>(x,"Id")==id);
            Call("WriteUpdate",rebuilt,target,save,side,library);
            var updatedDoc=new CampaignDocument(File.ReadAllText(PlayNowSaveJournal.CompanionPath(side,"team",id)));
            check(PlayNowRosterCodec.TryRead(updatedDoc,out var updated,out _) && updated.StaminaEnabled==stamina,"Studio update keeps complete canonical extension");
            var center=new CampaignDocument(File.ReadAllText(Path.Combine(side,updated.Line1[1]+".txt")));
            check(center.Get("Name")=="Player 1-2","Updated export retains actual imported Center");
        }
    }
}
