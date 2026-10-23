using EndlessMode;
using Tape2Tape.Customization;
using Tape2Tape.Customization.UI;
using UnityEngine;
using CampaignCore;
using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text;

static CampaignDocument Doc(string second="p5,p6,p7,p8,p9") {
 var d=new CampaignDocument("Export Version = 3\n");
 PlayNowRosterCodec.Write(d,new PlayNowRoster(new[]{"p0","p1","p2","p3","p4"},second.Split(','),"g",true)); return d;
}
const string Native="{\"Id\":\"t\",\"leftWingerId\":\"p0\",\"rightWingerId\":\"p2\",\"centerId\":\"p1\",\"leftDefensemenId\":\"p3\",\"rightDefensemenId\":\"p4\",\"goalieId\":\"g\"}";
static void Commit(string root,string side,string native,CampaignDocument doc,Action<int> fault=null) => PlayNowSaveJournal.Commit(root,side,"team","t",native,doc,
 PlayNowSaveJournal.Hash(PlayNowSaveJournal.Read(PlayNowSaveJournal.NativePath(root,"team","t"))),
 PlayNowSaveJournal.Hash(PlayNowSaveJournal.Read(PlayNowSaveJournal.CompanionPath(side,"team","t"))),fault:fault);
if(args.Length>0 && args[0]=="child") {
 int phase=int.Parse(args[3]);
 Commit(args[1],args[2],Native,Doc("p9,p8,p7,p6,p5"),i=>{if(i==phase)Environment.Exit(88);});return 0;
}
if(args.Length>0 && args[0]=="rollback-child") {
 Commit(args[1],args[2],Native,Doc("p9,p8,p7,p6,p5"),i=>{if(i==1)throw new IOException("injected before rollback");if(i==4)Environment.Exit(89);});return 0;
}
int pass=0,fail=0;
void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
void Test(string name,Action<string,string> run){
 string root=Path.Combine(Path.GetTempPath(),"ccf-pair-"+Guid.NewGuid().ToString("N")),side=Path.Combine(root,"overrides");
 try {run(root,side);Console.WriteLine("PASS "+name);pass++;}
 catch(Exception ex){Console.WriteLine("FAIL "+name+": "+ex);fail++;}
 finally {if(Directory.Exists(root))Directory.Delete(root,true);}
}
Test("NativeSavePersistsEditedBothLines",(root,side)=>{
 var d=Doc("p9,p8,p7,p6,p5");var m=JsonNode.Parse(Native); m["leftWingerId"]="p1";m["centerId"]="p0";
 PlayNowRosterCodec.Write(d,new PlayNowRoster(new[]{"p1","p0","p2","p3","p4"},new[]{"p9","p8","p7","p6","p5"},"g",false));
 Commit(root,side,m.ToJsonString(),d);
 var reloaded=new CampaignDocument(File.ReadAllText(PlayNowSaveJournal.CompanionPath(side,"team","t")));
 Assert(PlayNowRosterCodec.TryRead(reloaded,out var r,out _) && r.Line1.SequenceEqual(new[]{"p1","p0","p2","p3","p4"}) && r.Line2.SequenceEqual(new[]{"p9","p8","p7","p6","p5"}) && !r.StaminaEnabled,"restart positions changed");
 Assert(JsonNode.Parse(File.ReadAllText(PlayNowSaveJournal.NativePath(root,"team","t")))[ "leftWingerId"].GetValue<string>()=="p1","native fallback wrong");
});
for(int phase=0;phase<4;phase++) {int p=phase;Test("SaveFailureRecoversPreviousPair_"+p,(root,side)=>{
 Commit(root,side,Native,Doc());var native=File.ReadAllBytes(PlayNowSaveJournal.NativePath(root,"team","t"));var old=File.ReadAllBytes(PlayNowSaveJournal.CompanionPath(side,"team","t"));
 try {Commit(root,side,Native,Doc("p9,p8,p7,p6,p5"),i=>{if(i==p)throw new IOException("injected");});throw new Exception("failure ignored");} catch(IOException){}
 Assert(native.SequenceEqual(File.ReadAllBytes(PlayNowSaveJournal.NativePath(root,"team","t"))) && old.SequenceEqual(File.ReadAllBytes(PlayNowSaveJournal.CompanionPath(side,"team","t"))),"previous pair not restored");
 });}
for(int phase=0;phase<4;phase++) {int p=phase;Test("ProcessRestartFinishesPreparedPair_"+p,(root,side)=>{
 Commit(root,side,Native,Doc());
 var info=new ProcessStartInfo(Environment.ProcessPath){UseShellExecute=false};
 if(Path.GetFileNameWithoutExtension(Environment.ProcessPath).Equals("dotnet",StringComparison.OrdinalIgnoreCase))info.ArgumentList.Add(typeof(PlayNowSaveJournal).Assembly==typeof(Program).Assembly?typeof(PlayNowSaveJournal).Assembly.Location:typeof(Program).Assembly.Location);
 foreach(string a in new[]{"child",root,side,p.ToString()})info.ArgumentList.Add(a);
 using var process=Process.Start(info);Assert(process.WaitForExit(20000) && process.ExitCode==88,"child did not stop at boundary");
 Assert(PlayNowSaveJournal.Recover(root,side,"team"),"missing prepare");
 var d=new CampaignDocument(File.ReadAllText(PlayNowSaveJournal.CompanionPath(side,"team","t")));
 Assert(PlayNowRosterCodec.TryRead(d,out var r,out _) && r.Line2[0]=="p9","same-native second-line change lost");
 Assert(!PlayNowSaveJournal.Recover(root,side,"team"),"restart is not idempotent");
 });}
Test("RejectNativeCurrentLineFallback",(root,side)=>{
 var m=JsonNode.Parse(Native);m["leftWingerId"]="p5";
 try {Commit(root,side,m.ToJsonString(),Doc());throw new Exception("accepted inconsistent pair");}catch(InvalidDataException){}
 Assert(!File.Exists(PlayNowSaveJournal.NativePath(root,"team","t")),"invalid native published");
});
Test("StaleEditorRevisionRejected",(root,side)=>{
 Commit(root,side,Native,Doc());
 string n=PlayNowSaveJournal.NativePath(root,"team","t"),c=PlayNowSaveJournal.CompanionPath(side,"team","t");
 string oldNative=PlayNowSaveJournal.Hash(File.ReadAllBytes(n)),oldSide=PlayNowSaveJournal.Hash(File.ReadAllBytes(c));
 Commit(root,side,Native,Doc("p9,p8,p7,p6,p5"));var expected=File.ReadAllBytes(c);
 try {PlayNowSaveJournal.Commit(root,side,"team","t",Native,Doc(),oldNative,oldSide);throw new Exception("stale editor accepted");}catch(IOException){}
 Assert(expected.SequenceEqual(File.ReadAllBytes(c)),"stale editor changed current companion");
});
Test("UnknownThirdPartyNativeIsPreserved",(root,side)=>{
 Commit(root,side,Native,Doc());
 string n=PlayNowSaveJournal.NativePath(root,"team","t"),c=PlayNowSaveJournal.CompanionPath(side,"team","t");
 var before=File.ReadAllBytes(c);const string unknown="external revision";
 try {Commit(root,side,Native,Doc("p9,p8,p7,p6,p5"),phase=>{if(phase==0){File.WriteAllText(n,unknown);throw new IOException("external edit");}});throw new Exception("competing write accepted");}catch(IOException){}
 Assert(File.ReadAllText(n)==unknown && before.SequenceEqual(File.ReadAllBytes(c)),"recovery clobbered third-party bytes");
 try {PlayNowSaveJournal.Recover(root,side,"team");throw new Exception("unknown recovery accepted");}catch(IOException){}
 Assert(File.ReadAllText(n)==unknown,"restart clobbered unknown revision");
});
Test("MissingReferenceFailsBeforePublication",(root,side)=>{
 try {PlayNowSaveJournal.Commit(root,side,"team","t",Native,Doc(),"missing","missing",()=>throw new IOException("missing player"));throw new Exception("dangling roster accepted");}catch(IOException){}
 Assert(!File.Exists(PlayNowSaveJournal.NativePath(root,"team","t")),"dangling native team written");
});
Test("NativeShapedSaveNormalizesLine1AndReloadsBothLines",(root,_)=>{
 var (repo,t)=PersistenceFixtures.Create(root);t.activeLineNumber=2;
 (t.lines[0].leftWinger,t.lines[0].center)=(t.lines[0].center,t.lines[0].leftWinger);
 (t.lines[1].rightWinger,t.lines[1].rightDefensemen)=(t.lines[1].rightDefensemen,t.lines[1].rightWinger);
 PlayNowRosterSave.Save(repo,t);
 var model=JsonUtility.FromJson<TeamDataModel>(File.ReadAllText(PlayNowSaveJournal.NativePath(root,"team","t")));
 Assert(model.leftWingerId=="p1" && model.rightWingerId=="p2","active Line2 published as native Line1");
 PlayNowRosterRuntime.Clear();PlayNowTranslation.InvalidateAll();var rawModels=new Il2CppSystem.Collections.Generic.List<TeamDataModel>();rawModels.Add(model);PlayNowRosterSave.TeamModelsPostfix(repo.m_customTeamRepository,rawModels);
 var reloaded=new Data.TeamData{id="t",goalie=t.goalie};foreach(string id in new[]{model.leftWingerId,model.rightWingerId,model.centerId,model.leftDefensemenId,model.rightDefensemenId})reloaded.forwards.Add(repo.m_forwardRepository.Players.First(p=>p.id==id));
 Assert(PlayNowRosterRuntime.TryHydrate(repo,model,reloaded) && reloaded.lines[0].leftWinger=="p1" && reloaded.lines[1].rightWinger=="p9","native-shaped reload changed saved slots");
});
Test("ExplicitSecondLinePlayerSaveRetainsIntentAndClears",(root,_)=>{
 var(repo,t)=PersistenceFixtures.Create(root);var p=t.forwards.First(p=>p.id=="p7");
 p.headSkin="painted-entry";p.bodySkin="painted-body";var menu=PersistenceFixtures.Editor(p);
 PlayNowPlayerSave.HeadConfirmed(menu); // Deliberate choice equal to painted entry.
 p.firstName="Edited";p.abilityId="";p.talentIds=Array.Empty<string>();
 repo.m_forwardRepository.SaveCustomForward(p);
 var d=PersistenceFixtures.PlayerDoc("p7");var m=JsonNode.Parse(d.Get("Export Model"));
 Assert(m["headSkin"].GetValue<string>()=="painted-entry" && m["bodySkin"].GetValue<string>()=="personal-body","confirmed-equal choice lost or preview body saved");
 Assert(d.Get("Export Ability ID")=="" && d.Get("Export Talent IDs")=="" && d.Get("Pass Read")=="0.731" && d.Get("Helmet Away")=="authored-away-helmet","clear or omitted fields lost");
 Assert(d.Get("Export Own Equipment")=="no" && d.Get("Custom jersey Away Colors")=="yes","appearance ownership changed");
 Assert(PlayNowTranslation.Read("p7").Get("Export Ability ID")=="","cached document was not invalidated");
 Assert(t.forwards.First(x=>x.id=="p7").firstName=="Edited","second-line reference stale");
 // No subsequent team save is needed for the independently committed player.
 Assert(File.ReadAllText(PlayNowSaveJournal.NativePath(root,"forward","p7")).Contains("Edited"),"player commit depended on team Save");
});
Test("UnconfirmedPreviewAndCancelledEditorDoNotPublish",(root,_)=>{
 var(repo,t)=PersistenceFixtures.Create(root);var p=t.forwards[0];var menu=PersistenceFixtures.Editor(p);
 string native=File.ReadAllText(PlayNowSaveJournal.NativePath(root,"forward",p.id));string side=PersistenceFixtures.PlayerDoc(p.id).ToString();
 p.headSkin="hover-preview";p.StickColor.primaryColor=new Color(.2f,.3f,.4f);PlayNowPlayerSave.HidePrefix(menu);
 Assert(File.ReadAllText(PlayNowSaveJournal.NativePath(root,"forward",p.id))==native && PersistenceFixtures.PlayerDoc(p.id).ToString()==side,"Cancel wrote a file");
 try{repo.m_forwardRepository.SaveCustomForward(p);throw new Exception("cancelled session saved");}catch(IOException){}
 var menu2=PersistenceFixtures.Editor(p);p.number=77;repo.m_forwardRepository.SaveCustomForward(p);
 Assert(JsonNode.Parse(PersistenceFixtures.PlayerDoc(p.id).Get("Export Model"))["headSkin"].GetValue<string>()=="personal-head","unconfirmed preview became personal appearance");
});
Test("PersonalColorRetainsExactChannels",(root,_)=>{
 var(repo,t)=PersistenceFixtures.Create(root);var p=t.forwards[0];var menu=PersistenceFixtures.Editor(p);menu.m_CurrentColorBeingPicked=new(){Item=Data.SkaterColorItem.Skates,Index=1};
 var expected=new Color(.1234567f,.7654321f,.2222222f,.75f);PlayNowPlayerSave.ColorConfirmed(menu,expected);repo.m_forwardRepository.SaveCustomForward(p);
 var fresh=new Data.ForwardData{id=p.id};PlayNowPlayerSave.RestorePersonal(fresh,PersistenceFixtures.PlayerDoc(p.id));
 Assert(fresh.SkatesColor.secondaryColor.r==expected.r && fresh.SkatesColor.secondaryColor.a==expected.a,"omitted native personal colors lost precision");
});
Test("TeamPaletteRelicSaveDoesNotRewritePaintedPlayers",(root,_)=>{
 var(repo,t)=PersistenceFixtures.Create(root);var playerFiles=Directory.GetFiles(Path.Combine(root,"TeamDataModels"),"CustomForward-*.json").ToDictionary(f=>f,File.ReadAllBytes);
 t.relics.Clear();t.homeColors.jerseyScheme.primaryColor=new Color(.1f,.2f,.3f);t.homeGoalieColors.jerseyScheme.primaryColor=new Color(.7f,.8f,.9f);foreach(var p in t.forwards)p.bodySkin="team-paint";
 repo.SaveCustomTeam(t);var doc=PersistenceFixtures.TeamDoc();var fresh=new Data.TeamData();PlayNowRosterSave.RestorePalettes(fresh,doc);
 Assert(doc.Get("Export Relic IDs")=="" && doc.Get("Goal Sound Data")=="untouched-audio","team clear or unrelated supplemental data lost");
 Assert(fresh.homeColors.jerseyScheme.primaryColor.r==.1f && fresh.homeGoalieColors.jerseyScheme.primaryColor.r==.7f,"independent goalie palette lost");
 Assert(playerFiles.All(p=>p.Value.SequenceEqual(File.ReadAllBytes(p.Key))),"team Save serialized painted shared players");
});
Test("StaleNativePlayerSessionRejectsIndependentEdit",(root,_)=>{
 var(repo,t)=PersistenceFixtures.Create(root);var p=t.forwards[0];PersistenceFixtures.Editor(p);string path=PlayNowSaveJournal.NativePath(root,"forward",p.id);var model=JsonNode.Parse(File.ReadAllText(path));model["speed"]=99;File.WriteAllText(path,model.ToJsonString());string expected=File.ReadAllText(path);
 try{repo.m_forwardRepository.SaveCustomForward(p);throw new Exception("stale player saved");}catch(IOException){}
 Assert(File.ReadAllText(path)==expected,"stale player overwrote newer native edit");
});
Test("MissingReplacementRejectsAndBuiltinUsesOwnIdentity",(root,_)=>{
 var(repo,t)=PersistenceFixtures.Create(root);string old=File.ReadAllText(PlayNowSaveJournal.NativePath(root,"team","t"));var fresh=new Data.ForwardData{id="replacement"};t.forwards.Add(fresh);t.lines[1].center=fresh.id;
 try{repo.SaveCustomTeam(t);throw new Exception("unsaved replacement accepted");}catch(IOException){}
 Assert(File.ReadAllText(PlayNowSaveJournal.NativePath(root,"team","t"))==old,"dangling reference published");
 repo.m_forwardRepository.Builtins.Add(fresh);repo.m_forwardRepository.Players.Add(fresh);repo.SaveCustomTeam(t);
 Assert(PlayNowRosterCodec.TryRead(PersistenceFixtures.TeamDoc(),out var r,out _) && r.Line2[1]=="replacement","replacement ID lost");
 Assert(!File.Exists(PlayNowSaveJournal.CompanionPath(PlayNowRosterSave.Overrides,"forward","replacement")),"old occupant metadata grafted onto replacement");
});
Test("RawTeamRevisionSurvivesAliasedPaletteMutation",(root,_)=>{
 var(repo,t)=PersistenceFixtures.Create(root);var model=repo.m_customTeamRepository.Models[0];
 model.homeColors.jerseyScheme.primaryColor=new Color(.123f,.456f,.789f);
 PlayNowRosterSave.TeamModelsPostfix(repo.m_customTeamRepository,repo.m_customTeamRepository.Models);
 var doc=PlayNowRosterSave.ResolveDocument(model,PersistenceFixtures.TeamDoc());var frozen=JsonUtility.FromJson<Data.TeamColorsData>(doc.Get("Export Saved Palette homeColors"));
 Assert(frozen.jerseyScheme.primaryColor.r==1,"mutable translated palette replaced raw accepted file");
 string path=PlayNowSaveJournal.NativePath(root,"team","t");File.AppendAllText(path," ");string expected=File.ReadAllText(path);
 PlayNowRosterSave.TeamModelsPostfix(repo.m_customTeamRepository,repo.m_customTeamRepository.Models);
 try{repo.SaveCustomTeam(t);throw new Exception("warm cache reaccepted a stale team revision");}catch(IOException){}
 Assert(File.ReadAllText(path)==expected,"stale team overwrote independent edit");
});
Test("NewerNativePlayerBeatsStaleExportModelOnLoad",(root,_)=>{
 var(repo,t)=PersistenceFixtures.Create(root);var p=t.forwards[0];p.firstName="Native Edit";p.headSkin="native-head";p.abilityId="";p.talentIds=Array.Empty<string>();
 string json=JsonUtility.ToJson(ForwardRepository.Model(p));File.WriteAllText(PlayNowSaveJournal.NativePath(root,"forward",p.id),json);
 PlayNowPlayerSave.Observe(root,"forward",p,json);var doc=PlayNowTranslation.Read(p.id);
 Assert(JsonNode.Parse(doc.Get("Export Model"))["headSkin"].GetValue<string>()=="native-head" && doc.Get("Name")=="Native Edit Player" && doc.Get("Export Talent IDs")=="","old sidecar replayed over raw native edit");
});
Test("NestedUnrelatedSavesMaskOuterTransactions",(root,_)=>{
 var(repo,t)=PersistenceFixtures.Create(root);var p=t.forwards[0];PersistenceFixtures.Editor(p);
 PlayNowPlayerSave.ForwardPrefix(p,out var outer);try{
  PlayNowPlayerSave.ForwardPrefix(new Data.ForwardData{id="unrelated"},out var inner);try{
   Assert(PlayNowPlayerSave.ForwardWriterPrefix(repo.m_forwardRepository.m_customForwardRepository,new ForwardDataModel{id="unrelated"}),"unrelated writer borrowed player frame");
  }finally{PlayNowPlayerSave.Finalizer(null,inner);}
  Assert(!PlayNowPlayerSave.ForwardWriterPrefix(repo.m_forwardRepository.m_customForwardRepository,ForwardRepository.Model(p)),"outer player frame not restored");
 }finally{PlayNowPlayerSave.Finalizer(null,outer);}
 PlayNowRosterSave.TeamPrefix(repo,t,out var outerTeam);try{
  PlayNowRosterSave.TeamPrefix(repo,new Data.TeamData{id="unrelated"},out var innerTeam);try{
   Assert(PlayNowRosterSave.TeamWriterPrefix(repo.m_customTeamRepository,new TeamDataModel{Id="unrelated"}),"unrelated writer borrowed team frame");
  }finally{PlayNowRosterSave.TeamFinalizer(null,innerTeam);}
  Assert(!PlayNowRosterSave.TeamWriterPrefix(repo.m_customTeamRepository,TeamRepository.Model(t)),"outer team frame not restored");
 }finally{PlayNowRosterSave.TeamFinalizer(null,outerTeam);}
});
Test("ThirdPartyCompanionAfterNativePublicationIsPreserved",(root,side)=>{
 Commit(root,side,Native,Doc());string path=PlayNowSaveJournal.CompanionPath(side,"team","t");
 try{Commit(root,side,Native,Doc("p9,p8,p7,p6,p5"),phase=>{if(phase==1)File.WriteAllText(path,"third party");});throw new Exception("competing edit accepted");}catch(IOException){}
 Assert(File.ReadAllText(path)=="third party","companion edit was overwritten after native publication");
});
Test("GoalieSaveRetainsPersonalSkinOmittedPassReadAndClears",(root,_)=>{
 var(repo,t)=PersistenceFixtures.Create(root);var g=t.goalie;g.skin="painted-goalie";g.helmetSkin="painted-mask";g.headSkin="confirmed-head";
 var stage=new CustomizeGoalieData(g);PlayNowPlayerSave.GoalieEditorPostfix(stage,g);var menu=new SkaterCustomizationMenu{CurrentSkater=stage};PlayNowPlayerSave.HeadConfirmed(menu);
 g.firstName="Goalie Edit";g.skill=87;g.abilityId="";g.talentIds=Array.Empty<string>();repo.m_goalieRepository.SaveCustomGoalie(g);
 var model=JsonNode.Parse(File.ReadAllText(PlayNowSaveJournal.NativePath(root,"goalie","g")));var doc=new CampaignDocument(File.ReadAllText(PlayNowSaveJournal.CompanionPath(PlayNowRosterSave.Overrides,"goalie","g")));
 Assert(model["headSkin"].GetValue<string>()=="confirmed-head" && model["skin"].GetValue<string>()=="personal-goalie" && model["helmetSkin"].GetValue<string>()=="personal-mask" && model["skill"].GetValue<int>()==87,"goalie authority lost");
 Assert(doc.Get("Pass Read")=="0.731" && doc.Get("Export Ability ID")=="" && doc.Get("Export Talent IDs")=="","goalie supplemental value or explicit clear lost");
});
Test("AcceptedPlayerSessionCannotEscapeThroughRosterRemoval",(root,_)=>{
 var(repo,t)=PersistenceFixtures.Create(root);var p=t.forwards[0];PersistenceFixtures.Editor(p);
 File.Delete(PlayNowSaveJournal.CompanionPath(PlayNowRosterSave.Overrides,"team","t"));
 string path=PlayNowSaveJournal.NativePath(root,"forward",p.id);File.AppendAllText(path," ");string expected=File.ReadAllText(path);
 try{repo.m_forwardRepository.SaveCustomForward(p);throw new Exception("stale session escaped through native-only path");}catch(IOException){}
 Assert(File.ReadAllText(path)==expected,"accepted session overwrote newer record after membership changed");
});
Test("ProcessRestartFinishesDurableRollbackDecision",(root,side)=>{
 Commit(root,side,Native,Doc());string n=PlayNowSaveJournal.NativePath(root,"team","t"),c=PlayNowSaveJournal.CompanionPath(side,"team","t");var beforeN=File.ReadAllBytes(n);var beforeC=File.ReadAllBytes(c);
 var info=new ProcessStartInfo(Environment.ProcessPath){UseShellExecute=false};if(Path.GetFileNameWithoutExtension(Environment.ProcessPath).Equals("dotnet",StringComparison.OrdinalIgnoreCase))info.ArgumentList.Add(typeof(Program).Assembly.Location);
 foreach(string a in new[]{"rollback-child",root,side})info.ArgumentList.Add(a);
 using var process=Process.Start(info);Assert(process.WaitForExit(20000)&&process.ExitCode==89,"child did not stop after rollback decision");
 Assert(PlayNowSaveJournal.Recover(root,side,"team") && beforeN.SequenceEqual(File.ReadAllBytes(n)) && beforeC.SequenceEqual(File.ReadAllBytes(c)),"restart did not restore exact prior pair");
});
Test("NativeFloatSpellingsUseSingleDomainWithoutWeakeningOtherFields",(root,_)=>{
 const string persisted="{\"Id\":\"t\",\"homeColors\":{\"jerseyScheme\":{\"primaryColor\":{\"r\":0.5019608}}}}";
 const string equivalent="{\"Id\":\"t\",\"homeColors\":{\"jerseyScheme\":{\"primaryColor\":{\"r\":0.501960814}}}}";
 Assert(float.Parse("0.5019608",System.Globalization.CultureInfo.InvariantCulture)==float.Parse("0.501960814",System.Globalization.CultureInfo.InvariantCulture),"fixture decimal spellings differ as singles");
 PlayNowPlayerSave.MatchLoadedModel(persisted,equivalent,typeof(TeamDataModel));
 void Reject(string before,string after,Type schema){try{PlayNowPlayerSave.MatchLoadedModel(before,after,schema);throw new Exception("different native value accepted");}catch(IOException){}}
 Reject(persisted,equivalent.Replace("0.501960814","0.5019609"),typeof(TeamDataModel));
 Reject(persisted,equivalent.Replace("\"t\"","\"other\""),typeof(TeamDataModel));
 Reject("{\"id\":\"p\",\"speed\":16777216}","{\"id\":\"p\",\"speed\":16777217}",typeof(ForwardDataModel));
 Assert(PlayNowSaveJournal.Hash(Encoding.UTF8.GetBytes(persisted))!=PlayNowSaveJournal.Hash(Encoding.UTF8.GetBytes(equivalent)),"schema comparison weakened byte fingerprints");
});
Test("NativeLineupApplyPersistsWithoutSeparateTeamCustomizerSave",(root,_)=>LineupApplyChecks.Run(root,Assert));
Test("ActualStudioExportRuntimeSaveReimportUpdate",(root,_)=>StudioBoundaryChecks.Run(root,Assert));
Test("ActualGoalieHeadSaveImportReexportUpdate",(root,_)=>GoalieHeadRoundtripChecks.Run(root,Assert));
Console.WriteLine($"{pass} passed; {fail} failed");return fail==0?0:1;
