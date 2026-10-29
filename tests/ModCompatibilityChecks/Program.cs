using CampaignCore;
using ModCompatibility;
using System.Text.Json;

try {
string root=Path.GetFullPath(args[0]), game=args[1];
var baseline=CompatibilityBaseline.Read(args.Length>3?args[3]:Path.Combine(root,"src/ModCompatibility/Assets/native-baseline.json"));
int count=0;
void Check(bool value,string message) { if(!value) throw new Exception(message);count++;Console.WriteLine("PASS "+message); }
void Reject(Action action,string message) { bool failed=false;try { action(); } catch(InvalidDataException) { failed=true; }Check(failed,message); }
NativeCompatibilityProfile Copy() => NativeCompatibilityProfile.Read(JsonSerializer.Serialize(baseline.Profile));
Check(NativeSymbolNames.Stable("Anthem/<Listen>d__35")==NativeSymbolNames.Stable("Anthem.<Listen>d__40"),"Compiler state-machine ordinal changes retain identity");
Check(NativeSymbolNames.Stable("Anthem/<Listen>d__35")!=NativeSymbolNames.Stable("Anthem/<Stop>d__40"),"Different source methods cannot alias");
baseline.Profile.Validate(baseline.Profile.GameHash,baseline.Id);Check(true,"Current runtime profile validates");
Reject(()=>baseline.Profile.Validate(new string('0',64),baseline.Id),"Wrong game hash is rejected");
Reject(()=>baseline.Profile.Validate(baseline.Profile.GameHash,"other"),"Different mod baseline is rejected");
var p=Copy();p.RuntimeContract="future";Reject(()=>p.Validate(p.GameHash,baseline.Id),"Unknown schema is rejected");
p=Copy();p.Sites.Add(p.Sites[0]);Reject(()=>p.Validate(p.GameHash,baseline.Id),"Duplicate hook address is rejected");
p=Copy();p.Sites[0].Target++;Reject(()=>p.Validate(p.GameHash,baseline.Id),"Call target must agree with original rel32 bytes");
p=Copy();p.FlowBytes="90909090909090";Reject(()=>p.Validate(p.GameHash,baseline.Id),"Starting-flow ABI cannot be replaced by arbitrary bytes");
p=Copy();p.SurfaceBytes="90";Reject(()=>p.Validate(p.GameHash,baseline.Id),"Rink ABI remains guarded");
var image=new NativeImage(game);
Check(baseline.Profile.Build == "26289478", "Current release targets reviewed Steam build 26289478");
Check(baseline.Profile.FlowRva == 0x4430217 && baseline.Profile.FlowBytes == "400FB6C60A4758",
    "Fresh-run profile pins the reviewed SIL flag and native continuation");
foreach (string dependency in new[] { "ProfileData$$SelectRogueRunSettings::",
    "ProfileData.<>c__DisplayClass#$$<SelectRogueRunSettings>b__0::", "State.RunDataRepositoryV2.<LoadRunData>d__#$$MoveNext::", "Events.GoalCreditedArg$$.ctor::" })
    Check(baseline.Methods.Count(m => m.Name.StartsWith(dependency, StringComparison.Ordinal)) == 1,
        "Fresh-run setting persistence remains protected: " + dependency);
p=Copy();p.RuntimeContract="t2t-native-v2-20261020";Reject(()=>p.Validate(p.GameHash,baseline.Id),"Previous starting-flow register ABI is rejected");
p=Copy();p.FlowBytes="410FB6C60A4758";Reject(()=>p.Validate(p.GameHash,baseline.Id),"Previous R14B starting flag bytes are rejected");
Check(baseline.Methods.Any(m => m.Name.StartsWith("Tape2Tape.Hockey.UI.LineupMenu$$LinkStartersAndBenchEnds::", StringComparison.Ordinal)),
    "Updated native lineup navigation remains protected");
var verified=baseline.Verify(image,"verification");
Check(verified.RawHooks.Count == 21 && verified.NamedHooks.Count == 54, "Verified profile preserves every raw and named hook");
Check(verified.NamedHooks.Count(h => h.Feature.StartsWith("goalie-", StringComparison.Ordinal)) == 40,
    "Verified profile preserves all current goalie lifecycle hooks");
foreach (string feature in new[] { "jersey-logo", "goalie-checkpoint", "goalie-roster-menu", "goalie-match-launch", "goalie-match-completion" }) {
    var altered = JsonSerializer.Deserialize<CompatibilityBaseline>(JsonSerializer.Serialize(baseline))!;
    altered.Profile.NamedHooks.RemoveAll(h => h.Feature == feature);
    Reject(() => altered.Verify(image, "missing-feature"), "Missing complete feature set is rejected: " + feature);
}
var missingRaw = JsonSerializer.Deserialize<CompatibilityBaseline>(JsonSerializer.Serialize(baseline))!;
missingRaw.Profile.RawHooks.RemoveAt(0);
Reject(() => missingRaw.Verify(image, "missing-raw"), "Missing raw callback is rejected");
Check(verified.Sites.Count==137 && baseline.Methods.Count>=200 && baseline.SquadCalls.Length==109,"Fresh metadata verifies every current hook and protected method");
Check(verified.Sites.Count(s=>s.Kind=="inline_eq")==28 && verified.Sites.Count(s=>s.Kind=="query")==109,"Exact direct and inline site inventory is preserved");
Check(verified.Sites.Count(s=>s.Inline?.LeftRegister=="rcx")==26 && verified.Sites.Count(s=>s.Inline?.LeftRegister=="rbx")==2,"Both reviewed inline register contracts are covered");
foreach(string dependency in new[]{InlineComparisonContract.SequenceEqual,"Skaters.SkinHelper$$OverrideSkinsWithCustomTeamUniform","Rogue.Talent$$Get::T Get(System.Collections.Generic.List`1<Rogue.Talent> powerUps)"})
    Check(baseline.Methods.Any(m=>m.Name.StartsWith(dependency,StringComparison.Ordinal)),"New native dependency remains protected: "+dependency);
foreach(string scope in new[]{"Movements.MovementManager$$CanBodycheck", "Goaltender.GoaltenderStates.SavingState$$OnPuckCollision",
    "Rogue.NoStickSoccerRelicController$$<AddEffect>b__3_0", "Rogue.Relics.Controllers.SpikeArmorRelicController$$AddEffect",
    "Skaters.ForwardStats$$GetStatAfterInjuryCheck", "Skaters.SkaterAbility$$GetPowerupsInShots", "Skaters.SkaterAbility.<_Pass>d__#$$MoveNext",
    "Skaters.SkaterCollision$$IsGuaranteedKnockout", "Skaters.SkaterCollision$$HandleHitted", "Skaters.Injuries.InjurySystem$$TryInjuryAttempt",
    "Tape2Tape.Team.<>c$$<HasRefBlockShotTalent>b__175_0", "AI.AIStates.AIState$$ResetToDefaultState", "AI.AIStates.AIState$$IsKnightOf"})
    Check(baseline.Methods.Count(m=>m.Name.StartsWith(scope+"::",StringComparison.Ordinal))==1,"Inlined reward query live scope remains protected: "+scope);
foreach(string target in new[]{
    "State.CampaignState$$SetCurrentNode::System.Void SetCurrentNode(STS.Map.MapNode mapNode)",
    "Rogue.NPC.GeneralManagerNPC.GeneralManagerEvent$$Initialize::System.Void Initialize()",
    "Rogue.NPC.GeneralManagerNPC.GeneralManagerEvent$$NextChoices::System.Boolean NextChoices(System.Int32 numChoicesAlreadyMade)",
    "State.DeserializerV2$$DeserializeFromModel::System.Void DeserializeFromModel(Data.RunData runData, State.RunDataModelV2 runDataModel)",
    "State.SerializerV2$$SerializeToModel::State.TeamDataModelV2 SerializeToModel(Data.TeamData input)",
    "State.SerializerV2$$SerializeToModel::State.SkaterDataModelV2 SerializeToModel(Data.GoaltenderData input)",
    "State.DeserializerV2$$InitializeTeamDataFromModel::System.Void InitializeTeamDataFromModel(State.TeamDataModelV2 team, Data.TeamData teamData)",
    "State.DeserializerV2$$ConvertGoaltenderDataModelToGoaltenderData::Data.GoaltenderData ConvertGoaltenderDataModelToGoaltenderData(State.GoaltenderDataModelV2 goalie)"})
    Check(baseline.Methods.Count(m=>m.Name==target)==1,"Exact goalie recruitment/save overload remains protected: "+target);
foreach(var inline in baseline.Sites.Where(s=>s.Site.Kind=="inline_eq"))
{
    var contract=InlineComparisonContract.Read(image,inline.Site.Method,inline.InstructionIndex,inline.Site.Feature,inline.Site.Query);
    Check(contract.Bytes==inline.Site.Bytes && contract.Inline.BlockBytes==inline.Site.Inline.BlockBytes,"Exact inline block and callback frame verified: "+inline.Site.Method+"/"+inline.Site.Query);
}
void RejectInline(Action<NativeComparisonSite> change,string label) {
    var changedProfile=Copy();change(changedProfile.Sites.First(s=>s.Kind=="inline_eq"));
    Reject(()=>changedProfile.Validate(changedProfile.GameHash,baseline.Id),label);
}
RejectInline(s=>s.Inline.LeftRegister="rax","Unsupported inline left register is rejected");
RejectInline(s=>s.Inline.EqualBranchRva++,"Changed inline native equality branch is rejected");
RejectInline(s=>s.Target++,"Changed inline custom continuation is rejected");
RejectInline(s=>s.Inline.BlockBytes="90"+s.Inline.BlockBytes[2..],"Changed inline displacement prefix is rejected");
RejectInline(s=>s.Bytes=s.Bytes[..^2],"Truncated inline patch bytes are rejected");
RejectInline(s=>s.Kind="eq","Inline metadata cannot authorize a direct-call contract");
var originalInline=baseline.Sites.First(s=>s.Site.Kind=="inline_eq");
Reject(()=>InlineComparisonContract.Read(image,originalInline.Site.Method,originalInline.InstructionIndex+1,originalInline.Site.Feature,originalInline.Site.Query),"Inline site must begin at reviewed comparison");
Reject(()=>InlineComparisonContract.Read(image,originalInline.Site.Method,originalInline.InstructionIndex,originalInline.Site.Feature,"different-squad"),"Inline literal identity cannot change");
void RejectInstruction(int offset,string hex,string label) {
    var body=image.Instructions(image.Resolve(originalInline.Site.Method).Rva);
    int at=originalInline.InstructionIndex+offset;var original=body[at];
    var decoder=Iced.Intel.Decoder.Create(64,new Iced.Intel.ByteArrayCodeReader(Convert.FromHexString(hex)));decoder.IP=original.IP;decoder.Decode(out var changedInstruction);
    try {body[at]=changedInstruction;Reject(()=>InlineComparisonContract.Read(image,originalInline.Site.Method,originalInline.InstructionIndex,originalInline.Site.Feature,originalInline.Site.Query),label);}
    finally {body[at]=original;}
}
RejectInstruction(14,"E800000000","Inline SequenceEqual target must retain its exact overload");
RejectInstruction(9,"4C634118","Inline string length offset must remain reviewed");
RejectInstruction(10,"4883C314","Inline preparation cannot modify a nonvolatile register");
RejectInstruction(13,"4531C0","Inline SequenceEqual MethodInfo argument must stay null");
RejectInstruction(3,"7400","Inline null branches must retain a single unequal path");
foreach (var method in new[] { "GameStats$$OnHit::", "GameStats$$Awake::", "GameStats$$OnDestroy::" })
{
    Check(baseline.Methods.Count(m => m.Name.StartsWith(method, StringComparison.Ordinal)) == 1,
        "Custom check events protect native dispatch and subscription lifecycle: " + method);
    var altered = JsonSerializer.Deserialize<CompatibilityBaseline>(JsonSerializer.Serialize(baseline))!;
    altered.Methods.Single(m => m.Name.StartsWith(method, StringComparison.Ordinal)).BodyHash = "changed";
    Reject(() => altered.Verify(image, "check-event-changed"), "Changed check-event native contract requires repair: " + method);
}
foreach(var method in new[]{"STS.Map.MatchMapNode$$AssignSubstitute::", "STS.Map.MatchMapNode$$ReplaceForwardWithSubstitute::", "Data.Lineup$$UpdateLineWithNewID::"})
    Check(baseline.Methods.Count(m=>m.Name.StartsWith(method,StringComparison.Ordinal))==1,"Opponent substitution preserves the native lineup contract: "+method);
foreach(var method in new[]{"Rogue.NPC.BlademasterNPC.BlademasterEvent$$OldBlademasterEvent::", "$$<OldBlademasterEvent>b__1::"})
    Check(baseline.Methods.Count(m=>m.Name.Contains(method))==1,"Bounded Blademaster adapter protects its native contract: "+method);
foreach(var method in new[]{"ScoreboardTimeRemaining$$Awake::", "ScoreboardTimeRemaining$$GeneratePeriodMarkers::",
    "ScoreboardTimeRemaining$$OnEndOfPeriod::", "PeriodIndicatorUI$$GeneratePeriodMarkers::",
    "PeriodIndicatorUI$$ClearPeriodMarkers::", "PeriodIndicatorUI$$Start::", "PeriodIndicatorUI$$OnEndOfPeriod::",
    "PeriodIndicatorUI$$UpdatePeriodMarker::", "PeriodIndicatorUI$$UpdateMercyCondition::"})
    Check(baseline.Methods.Count(m=>m.Name.Contains(method))==1,"Single-period timed HUD protects its native contract: "+method);
foreach(var method in new[]{"get_CanAwardRefereeBreakawayPenaltyShot"})
    Check(verified.Sites.Count(s=>s.Method.Contains("$$"+method+"::") && s.Feature=="eliminator" && s.Query=="eliminator" && s.Kind=="query")==1,"Eliminator keeps the native referee penalty-shot exclusion: "+method);
Check(!verified.Sites.Any(s=>s.Method.Contains("$$TryRefereeBreakawayPenaltyShot::")),"Removed wrapper query is not patched after native helper delegation");
Check(verified.Sites.Count(s=>s.Method.Contains("Referee.<_BreakawayPenaltyShot>") && s.Feature=="eliminator")==1,"Referee coroutine retains the relocated eliminator guard");
Check(verified.Sites.Count(s=>s.Method.Contains("BlademasterNodeReward$$GenerateReward") && s.Feature=="no_ability_offers" && s.Query=="tchel")==1,"Inlined Blademaster eligibility retains no-ability-offers policy");
const string nodeSelection = "State.CampaignState$$SetCurrentNode::";
Check(baseline.Methods.Count(m=>m.Name.StartsWith(nodeSelection,StringComparison.Ordinal))==1,
    "Authored match settings and reward snapshots protect native node selection");
var changedSelection=JsonSerializer.Deserialize<CompatibilityBaseline>(JsonSerializer.Serialize(baseline))!;
changedSelection.Methods.Single(m=>m.Name.StartsWith(nodeSelection,StringComparison.Ordinal)).BodyHash="changed";
Reject(()=>changedSelection.Verify(image,"selection-changed"),"Changed native node-selection lifecycle requires repair");
foreach(var method in new[]{"STS.Map.MapObject$$InitializeMap", "Tape2Tape.Hockey.UI.WorldMapActIntro$$Play",
    "Tape2Tape.Hockey.UI.WorldMapActIntro.<Play>d__30$$MoveNext",
    "Tape2Tape.Hockey.UI.WorldMapMenusHandler$$RefreshInitialCameraSettings",
    "Tape2Tape.Hockey.UI.WorldMapMenusHandler$$RestoreCamera",
    "Tape2Tape.Hockey.UI.WorldMapMenusHandler$$<RestoreCamera>g__Restore|69_0",
    "MatchPreviewMenu$$BackToMap", "DG.Tweening.ShortcutExtensions$$DOOrthoSize"})
    Check(baseline.Methods.Count(m=>m.Name.StartsWith(NativeSymbolNames.Stable(method)+"::",StringComparison.Ordinal))==1,
        "Authored map framing protects its native camera lifecycle: "+method);
foreach (var method in new[] { "MatchPreviewMenu$$BackToMap::", "DG.Tweening.ShortcutExtensions$$DOOrthoSize::" })
{
    var altered = JsonSerializer.Deserialize<CompatibilityBaseline>(JsonSerializer.Serialize(baseline))!;
    altered.Methods.Single(m => m.Name.StartsWith(method, StringComparison.Ordinal)).BodyHash = "changed";
    Reject(() => altered.Verify(image, "preview-return-changed"), "Changed preview return contract requires repair: " + method);
}
if(args.Length>2) Reject(()=>CompatibilityBaseline.Read(args[2]).Verify(image,"old-baseline"),"Previous baseline rejects the real changed game before installation");
Check(verified.GameHash==CompatibilityUpdater.Hash(Path.Combine(game,"GameAssembly.dll")),"Generated profile pins the actual game binary");
var changed=JsonSerializer.Deserialize<CompatibilityBaseline>(JsonSerializer.Serialize(baseline))!;
changed.Methods[0].BodyHash="changed";Reject(()=>changed.Verify(image,"test"),"Changed native instruction structure requires repair");
changed=JsonSerializer.Deserialize<CompatibilityBaseline>(JsonSerializer.Serialize(baseline))!;
changed.SquadCalls=[..changed.SquadCalls,"new gameplay consumer"];
Reject(()=>changed.Verify(image,"test"),"Changed squad-call coverage requires repair");
changed=JsonSerializer.Deserialize<CompatibilityBaseline>(JsonSerializer.Serialize(baseline))!;
changed.Sites[0].InstructionIndex++;Reject(()=>changed.Verify(image,"test"),"Incorrect patch instruction is rejected");
string temp=Path.Combine(Path.GetTempPath(),"t2t-compat-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
try {
 string bad=Path.Combine(temp,"baseline.json");File.WriteAllText(bad,JsonSerializer.Serialize(changed));
 Reject(()=>CompatibilityBaseline.Read(bad),"Modified baseline cannot authorize a new compatibility pack");
 var missing=CompatibilityUpdater.Ensure(temp,temp);Check(missing.State=="NotInstalled","Missing mod is reported without installing a new loader");
 Directory.CreateDirectory(Path.Combine(temp,"BepInEx/plugins"));string mod=Path.Combine(temp,"BepInEx/plugins/CustomCampaignFramework.dll");File.WriteAllText(mod,"preserve me");
 var broken=CompatibilityUpdater.Ensure(temp,temp);Check(broken.State=="NeedsRepair" && File.ReadAllText(mod)=="preserve me","Missing or broken package preserves installed mod bytes");
} finally { Directory.Delete(temp,true); }
Console.WriteLine($"{count} mod compatibility checks passed; no human gameplay confirmation.");return 0;
} catch(Exception ex) { Console.Error.WriteLine(ex);return 1; }
