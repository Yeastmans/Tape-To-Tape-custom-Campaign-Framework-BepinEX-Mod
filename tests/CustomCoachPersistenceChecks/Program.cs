using CampaignCore;
using EndlessMode;
int checks = 0;
void Check(bool ok, string why) { checks++; if (!ok) throw new Exception(why); }
string root = Path.GetFullPath(Path.Combine(args[0], "persistence-" + Guid.NewGuid().ToString("N")));
Plugin.ModContentRoot = root; Plugin.ModFolder = Path.Combine(root, "campaign");
Directory.CreateDirectory(Plugin.ModFolder);
const string png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGMwmPDgPwAFNAKg4ubHmAAAAABJRU5ErkJggg==";
var saved = new CustomRewardDefinition { Name = "Saved mechanics", Kind = CustomRewardDefinition.CustomRelics,
    Trigger = "While owned", Target = "Whole team", IconPng = png, Amount = 2 };
var coach = new CustomCoachDefinition { Name = "Applied Coach" };
coach.Relics.Add(saved.Id); coach.Relics.Add("native-relic"); coach.Rewards.Add(saved.Copy());
var layout = new MapLayout { BaseSquad = "Basic Squad", Nodes = [
    new() { Id = "coach", Type = "Coach", X = 3, Y = 10, Outgoing = ["boss"], CustomCoach = coach.Copy() },
    new() { Id = "boss", Type = "Boss", Layer = 1, X = 9, Y = 10 }] };
CustomRewardRuntime.CommitCoachRewards(CustomRewardRuntime.PrepareCoachRewards(layout));
var manager = CustomMapLayout.Applied(layout);
const string nativeMap = "exact saved native map";
CustomMapLayout.SaveReceipt(manager, nativeMap);
string receiptPath = Directory.GetFiles(Path.Combine(Plugin.ModFolder, "_map_runtime"), "*.txt", SearchOption.AllDirectories).Single(p => Path.GetFileName(p) != "run-id.txt");
var receiptDocument = new CampaignDocument(File.ReadAllText(receiptPath));
Check(receiptDocument.Get("Squad") == "Custom_a" && receiptDocument.Get("Map Source").Contains("map_overrides"), "New map receipt must retain selected squad and applied source identity");
CustomRewardRuntime.ResetCoachRewards();
var edited = saved.Copy(); edited.Amount = 9; edited.Name = "Future mechanics";
edited.Save(Path.Combine(Plugin.ModFolder, CustomRewardDefinition.Folder));
CustomRewardRuntime.EnsureAssets();
Check(CustomRewardRuntime.NativeAssets[saved.Id].Amount == 9, "Preloaded authoring asset reproduces edited mechanics");
var preloaded = CustomRewardRuntime.NativeAssets[saved.Id];
RestoreCustomCoachRewards.Prefix(new State.RunDataModelV2 { currentMap = nativeMap });
Check(CustomRewardRuntime.NativeAssets[saved.Id].Amount == 2, "Hydration restores saved mechanics before owned native rewards are resolved");
RestoreCustomCoachRewards.Prefix(new State.RunDataModelV2 { currentMap = nativeMap, squadId = "Custom_b" });
Check(CustomRewardRuntime.CoachRewardSnapshots().Length == 0, "Pre-hydration rejects another saved squad even if current menu/run selection is still A");
Tape2Tape.CampaignUtil.CampaignState.runData.squadId = "Custom_b";
RestoreCustomCoachRewards.Prefix(new State.RunDataModelV2 { currentMap = nativeMap, squadId = "Custom_a" });
Check(CustomRewardRuntime.Definitions[saved.Id].Amount == 2, "Hydration uses incoming saved squad before native CampaignState updates");
Check(CustomMapLayout.RestoreForCheck(manager, nativeMap) == null, "Map restoration rejects another current run squad");
Tape2Tape.CampaignUtil.CampaignState.runData.squadId = "Custom_a";
Directory.CreateDirectory(Path.Combine(Plugin.ModFolder, "player_teams", "a", SquadMapOverrides.Folder));
File.WriteAllText(Path.Combine(Plugin.ModFolder, "player_teams", "a", "team.txt"), "Use Own Maps = no\n");
File.WriteAllText(SquadMapOverrides.OverridePath(Path.Combine(Plugin.ModFolder, "player_teams", "a"), 1), "deleted/replaced authoring source");
Check(CustomMapLayout.RestoreForCheck(manager, nativeMap)?.Serialize() == layout.Serialize(), "Continue restores exact applied layout despite disabled toggle and changed authoring file");
var legacyReceipt = new CampaignDocument(receiptDocument.ToString()); legacyReceipt.Remove("Squad"); legacyReceipt.Remove("Map Source"); legacyReceipt.Save(receiptPath);
RestoreCustomCoachRewards.Prefix(new State.RunDataModelV2 { currentMap = nativeMap, squadId = "Custom_b" });
Check(CustomRewardRuntime.Definitions[saved.Id].Amount == 2, "Legacy campaign-only receipt still restores through exact run/map/stage guards");
receiptDocument.Save(receiptPath);
CustomRewardRuntime.RegisterCoach(coach);
Check(CustomRewardRuntime.Definitions[saved.Id].Amount == 2, "Applied Coach restores its saved relic definition");
Check(CustomRewardRuntime.NativeAssets[saved.Id].Amount == 2, "An already cached future asset must be replaced before saved reward lookup");
Check(CustomRewardDefinition.LoadFolder(Path.Combine(Plugin.ModFolder, CustomRewardDefinition.Folder)).Single().Amount == 9,
    "Restoring a run never rewrites authoring files");
Check(!ReferenceEquals(preloaded, CustomRewardRuntime.NativeAssets[saved.Id]) && preloaded.Amount == 9
    && preloaded.hideFlags == UnityEngine.HideFlags.None && preloaded.sprite.texture.hideFlags == UnityEngine.HideFlags.None,
    "Retired native carriers keep their mechanics and are unloadable after their last native owner leaves");
// Later acts may use native layouts. Owned dependencies still need a receipt.
Plugin.ActsCompleted = 1; CustomMapLayout.Active = false;
const string laterMap = "later native map without custom nodes";
CustomMapLayout.SaveReceipt(manager, laterMap);
CustomRewardRuntime.ResetCoachRewards();
RestoreCustomCoachRewards.Prefix(new State.RunDataModelV2 { currentMap = laterMap });
Check(CustomRewardRuntime.Definitions[saved.Id].Amount == 2, "Later native maps retain dependencies acquired in earlier acts");
// Guards must run before publication, not just when a map prefab is shown.
void Guard(string map, string why) {
    RestoreCustomCoachRewards.Prefix(new State.RunDataModelV2 { currentMap = map });
    CustomRewardRuntime.EnsureAssets();
    Check(CustomRewardRuntime.CoachRewardSnapshots().Length == 0 && CustomRewardRuntime.Definitions[saved.Id].Amount == 9, why);
}
Guard(nativeMap, "A previous-stage receipt cannot override this run stage");
Guard("unmatched native map", "An unmatched native map cannot activate a receipt");
Plugin.ActsCompleted = 0;
string runPath = Path.Combine(Plugin.ModFolder, "_map_runtime", "run-id.txt");
string runId = File.ReadAllText(runPath);
File.WriteAllText(runPath, Guid.NewGuid().ToString("N"));
Guard(nativeMap, "A receipt from another run cannot override authored definitions");
File.WriteAllText(runPath, runId);
Plugin.ActiveCampaign = "Different campaign";
Guard(nativeMap, "A receipt for another campaign cannot activate dependencies");
Plugin.ActiveCampaign = "Campaign";
RestoreCustomCoachRewards.Prefix(new State.RunDataModelV2 { currentMap = nativeMap });
Check(CustomRewardRuntime.Definitions[saved.Id].Amount == 2, "Returning to the exact receipt restores its snapshot");
Plugin.IsDefaultMode = true;
RestoreCustomCoachRewards.Prefix(new State.RunDataModelV2 { currentMap = nativeMap });
CustomRewardRuntime.EnsureAssets();
Check(!CustomRewardRuntime.Definitions.ContainsKey(saved.Id) && CustomRewardRuntime.NativeAssets.Count == 0, "Default mode releases run-owned Coach definitions and cached carriers");
Plugin.IsDefaultMode = false;
RestoreCustomCoachRewards.Prefix(new State.RunDataModelV2 { currentMap = nativeMap });
CustomRewardRuntime.ResetCoachRewards(); CustomRewardRuntime.EnsureAssets();
Check(CustomRewardRuntime.Definitions[saved.Id].Amount == 9 && CustomRewardRuntime.NativeAssets[saved.Id].Amount == 9,
    "A new run returns to the current authoring definition");
var conflict = coach.Copy(); conflict.Rewards[0].Amount = 77;
CustomRewardRuntime.RegisterCoach(coach);
bool rejected = false;
try { CustomRewardRuntime.RegisterCoach(conflict); } catch (ArgumentException) { rejected = true; }
Check(rejected && CustomRewardRuntime.Definitions[saved.Id].Amount == 2, "Two incompatible snapshots cannot change an already established run identity");
CustomRewardRuntime.ResetCoachRewards(); CustomRewardRuntime.EnsureAssets();
var prepared = CustomRewardRuntime.PrepareCoachRewards(layout);
Check(CustomRewardRuntime.Definitions[saved.Id].Amount == 9, "Preparing a map cannot publish its snapshot before native generation succeeds");
CustomRewardRuntime.CommitCoachRewards(prepared);
Check(CustomRewardRuntime.Definitions[saved.Id].Amount == 2, "Successful native generation commits prepared dependencies");
Console.WriteLine($"PASS {checks} custom Coach persistence checks");
