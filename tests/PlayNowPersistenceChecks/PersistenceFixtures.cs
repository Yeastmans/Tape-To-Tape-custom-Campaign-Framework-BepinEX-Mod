using CampaignCore;
using EndlessMode;
using Data;
using Tape2Tape.Customization;
using Tape2Tape.Customization.UI;
using UnityEngine;
using System.Text.Json.Nodes;
internal static class PersistenceFixtures {
 internal static (TeamRepository Repo,TeamData Team) Create(string root) {
  Plugin.ModContentRoot=root;PlayNowTranslation.InvalidateAll();PlayNowRosterRuntime.Clear();
  string side=PlayNowRosterSave.Overrides;Directory.CreateDirectory(side);Directory.CreateDirectory(Path.Combine(root,"TeamDataModels"));
  var repo=new TeamRepository();repo.m_customTeamRepository._path=root;repo.m_forwardRepository.m_customForwardRepository._persistentDataPath=root;repo.m_goalieRepository._customGoalieRepository._persistentDataPath=root;
  var team=new TeamData{id="t",goalie=new GoaltenderData{id="g"}};team.relics.Add("old-relic");
  for(int i=0;i<10;i++){var p=new ForwardData{id="p"+i};repo.m_forwardRepository.Players.Add(p);if(i<5)team.forwards.Add(p);}
  team.lines.Add(new Lineup(team){leftWinger="p0",rightWinger="p2",center="p1",leftDefensemen="p3",rightDefensemen="p4"});
  var d=new CampaignDocument("");d.Set("Export Version","3");d.Set("Export Relic IDs","old-relic");d.Set("Goal Sound Data","untouched-audio");
  PlayNowRosterCodec.Write(d,new PlayNowRoster(new[]{"p0","p1","p2","p3","p4"},new[]{"p5","p6","p7","p8","p9"},"g",true));
  File.WriteAllText(PlayNowSaveJournal.CompanionPath(side,"team","t"),d.ToString());
  foreach(var p in repo.m_forwardRepository.Players.Cast<SkaterData>().Append(team.goalie)) {
   bool goalie=p is GoaltenderData;string kind=goalie?"goalie":"forward";string json=goalie?JsonUtility.ToJson(GoalieRepository.Model((GoaltenderData)p)):JsonUtility.ToJson(ForwardRepository.Model(p));
   var doc=new CampaignDocument("");doc.Set("Export Version","3");doc.Set("Export Role",goalie?"goalie":"skater");doc.Set("Export Model",json);doc.Set("Export Ability ID","ability");doc.Set("Export Talent IDs","talent");doc.Set("Export Own Equipment","no");doc.Set("Override Team Settings","no");doc.Set("Pass Read","0.731");doc.Set("Helmet Away","authored-away-helmet");doc.Set("Custom jersey Away Colors","yes");doc.Set("Jersey Away Color","7,8,9");
   File.WriteAllText(PlayNowSaveJournal.NativePath(root,kind,p.id),json);File.WriteAllText(PlayNowSaveJournal.CompanionPath(side,kind,p.id),doc.ToString());
   PlayNowPlayerSave.Observe(root,kind,p,json);
  }
  var model=TeamRepository.Model(team);File.WriteAllText(PlayNowSaveJournal.NativePath(root,"team","t"),JsonUtility.ToJson(model));repo.m_customTeamRepository.Models.Add(model);PlayNowRosterSave.TeamModelsPostfix(repo.m_customTeamRepository,repo.m_customTeamRepository.Models);
  if(!PlayNowRosterRuntime.TryHydrate(repo,model,team))throw new Exception("fixture hydration failed");return(repo,team);
 }
 internal static SkaterCustomizationMenu Editor(ForwardData p){var stage=new CustomizeForwardData(p);var menu=new SkaterCustomizationMenu{CurrentSkater=stage};PlayNowPlayerSave.EditorInitialized(menu,stage,true);return menu;}
 internal static CampaignDocument PlayerDoc(string id)=>new(File.ReadAllText(PlayNowSaveJournal.CompanionPath(PlayNowRosterSave.Overrides,"forward",id)));
 internal static CampaignDocument TeamDoc()=>new(File.ReadAllText(PlayNowSaveJournal.CompanionPath(PlayNowRosterSave.Overrides,"team","t")));
}
