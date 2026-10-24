using Avalonia;
using Avalonia.Headless;
using CampaignCore;
using CampaignCreator;
using System.Reflection;

int checks = 0;
void Check(bool ok, string why) { checks++; if (!ok) throw new Exception(why); }
AppBuilder.Configure<CampaignCreator.App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
string root = Path.GetFullPath(Path.Combine(args[0], "integration-" + Guid.NewGuid().ToString("N")));
string campaign = Path.Combine(root, "campaigns", "Source"), a = Path.Combine(campaign, "player_teams", "a"), b = Path.Combine(campaign, "player_teams", "b");
Directory.CreateDirectory(a); Directory.CreateDirectory(b); Directory.CreateDirectory(Path.Combine(campaign, "map_layouts"));
File.WriteAllText(Path.Combine(campaign, "campaign.txt"), "Name = Source\nAct Sequence = 1\n");
void Team(string squad, bool enabled) => File.WriteAllText(Path.Combine(squad, "team.txt"), "Team Name = Same Name\nSquad Template = No Effects\nUse Own Maps = " + (enabled ? "yes" : "no"));
Team(a, false); Team(b, false);
MapLayout Route(int coaches) {
    var result = new MapLayout { BaseSquad = "Basic Squad", LookAct = 1 };
    for (int i = 0; i <= coaches; i++) result.Nodes.Add(new() { Id = "n" + i, Layer = i, Type = i == coaches ? "Boss" : "Coach", X = 3.1 + 6.4 * i, Y = 10.4, Outgoing = i == coaches ? [] : ["n" + (i + 1)] });
    return result;
}
string shared = Path.Combine(campaign, "map_layouts", "map-1.txt"), own = SquadMapOverrides.OverridePath(a, 1);
var normal = Route(1); File.WriteAllText(shared, normal.Serialize());
Directory.CreateDirectory(Path.GetDirectoryName(own)!); File.WriteAllText(own, Route(6).Serialize());
int Budget(IEnumerable<MapLayout>? maps = null, string? squad = null, IReadOnlyList<int>? original = null, IReadOnlyDictionary<string, CampaignDocument>? changes = null)
    => CampaignRewardBudget.Calculate(root, campaign, layouts: maps, mapSquadDirectory: squad, originalStages: original, changedFiles: changes).Single(b => b.Kind == "Relics").Selections;
int inherited = Budget(); Team(a, true);
Check(Budget() > inherited, "Reward budget must use enabled squad overrides, even when both squads use the same template");
int ownBudget = Budget();
Check(Budget([Route(2)]) == ownBudget, "Shared campaign draft cannot replace an independent squad graph");
Check(Budget([Route(9)]) > ownBudget, "Other squad inherits the campaign draft during validation");
Team(a, false); Team(b, true);
File.WriteAllText(SquadMapOverrides.OverridePath(a, 1), Route(11).Serialize());
Directory.CreateDirectory(Path.GetDirectoryName(SquadMapOverrides.OverridePath(b, 1))!); File.WriteAllText(SquadMapOverrides.OverridePath(b, 1), Route(7).Serialize());
Check(Budget([Route(2)], a) > inherited, "Squad A draft must not hide squad B's independent graph");
Team(b, false);
var pendingTeam = new CampaignDocument(File.ReadAllText(Path.Combine(a, "team.txt"))); pendingTeam.Set(SquadMapOverrides.ModeKey, "yes");
Check(Budget(changes: new Dictionary<string, CampaignDocument> { [Path.GetFullPath(Path.Combine(a, "team.txt"))] = pendingTeam }) > inherited,
    "Saving a pending toggle validates its enabled maps before writing the team");
Team(a, true);
File.WriteAllText(Path.Combine(campaign, "campaign.txt"), "Name = Source\nAct Sequence = 1,2\n");
File.WriteAllText(SquadMapOverrides.OverridePath(a, 2), Route(6).Serialize());
Check(Budget([normal], original: [2]) == ownBudget, "Shared draft removal resolves override by original stage, not new slot");
Check(Budget([normal], original: [0]) == inherited, "Added map inherits and cannot take the removed slot's override");
// A retained disabled map still belongs to the whole campaign package.
Team(a, false); Team(b, false);
const string png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGMwmPDgPwAFNAKg4ubHmAAAAABJRU5ErkJggg==";
var reward = new CustomRewardDefinition { Name = "Squad only relic", Kind = CustomRewardDefinition.CustomRelics, Trigger = "While owned", Target = "Whole team", IconPng = png };
var coach = new CustomCoachDefinition { Name = "Squad only Coach", ImagePng = png }; coach.Relics.Add(reward.Id); coach.Rewards.Add(reward.Copy());
var custom = Route(1); custom.Name = "Retained squad map"; custom.Nodes[0].CustomCoach = coach;
File.WriteAllText(own, custom.Serialize());
var sharing = typeof(CampaignWorkspace).Assembly.GetType("CampaignCreator.CampaignSharing")!;
object? Share(string method, params object[] values) {
    try { return sharing.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, values); }
    catch (TargetInvocationException e) when (e.InnerException != null) { throw e.InnerException; }
}
string zip = Path.Combine(root, "campaign.zip"), fresh = Path.Combine(root, "recipient");
Directory.CreateDirectory(Path.Combine(campaign, "_map_runtime")); File.WriteAllText(Path.Combine(campaign, "_map_runtime", "run-id.txt"), "local run");
Share("Zip", campaign, zip, root); Share("Install", zip, fresh, "Imported");
string imported = Path.Combine(fresh, "campaigns", "Imported"), importedSquad = Path.Combine(imported, "player_teams", "a");
Check(!Directory.Exists(Path.Combine(imported, "_map_runtime")), "Campaign share excludes local run receipts");
Check(File.Exists(Path.Combine(imported, CustomRewardDefinition.Folder, reward.Id + CustomRewardDefinition.Extension)), "ZIP bundles dependencies found only in disabled squad maps");
var carried = MapLayout.Parse(File.ReadAllText(SquadMapOverrides.OverridePath(importedSquad, 1)));
Check(carried.Nodes[0].CustomCoach.ImagePng == png && carried.Nodes[0].CustomCoach.Rewards.Single().ToDocument().ToString() == reward.ToDocument().ToString(), "ZIP retains exact squad Coach name, image, pool and mechanics");
Check(!SquadMapOverrides.Own(new CampaignDocument(File.ReadAllText(Path.Combine(importedSquad, "team.txt")))) && File.Exists(SquadMapOverrides.OverridePath(importedSquad, 2)), "Whole campaign retains disabled setting and all override stages");
string conflictRoot = Path.Combine(root, "conflicting-recipient"); var collision = reward.Copy(); collision.Amount += 4; collision.Save(Path.Combine(conflictRoot, "library", "rewards"));
bool rejected = false; try { Share("Install", zip, conflictRoot, "Rejected"); } catch (ArgumentException) { rejected = true; }
Check(rejected && !Directory.Exists(Path.Combine(conflictRoot, "campaigns", "Rejected")), "Recipient library conflict cannot be hidden by ZIP companion precedence");
string copiedCampaign = Path.GetDirectoryName(CreatorContent.Create(root, "campaign", "Copied", campaign, null, null))!;
Check(File.Exists(SquadMapOverrides.OverridePath(Path.Combine(copiedCampaign, "player_teams", "a"), 1)), "Whole-campaign copy preserves squad overrides");
Team(a, true);
string libraryTeam = CreatorContent.Create(root, "player-team", "Library copy", a, null, null);
Check(new CampaignDocument(File.ReadAllText(libraryTeam)).Get(SquadMapOverrides.ModeKey).Length == 0 && !Directory.Exists(Path.Combine(Path.GetDirectoryName(libraryTeam)!, SquadMapOverrides.Folder)), "Standalone library team omits campaign map setting and files");
string destination = Path.Combine(root, "campaigns", "Destination"); Directory.CreateDirectory(destination); File.WriteAllText(Path.Combine(destination, "campaign.txt"), "Act Sequence = 1\n");
string copiedTeam = SquadCopy.Create(a, destination, "Imported squad", "No Effects");
Check(new CampaignDocument(File.ReadAllText(Path.Combine(copiedTeam, "team.txt"))).Get(SquadMapOverrides.ModeKey).Length == 0 && !Directory.Exists(Path.Combine(copiedTeam, SquadMapOverrides.Folder)), "Cross-campaign squad copy omits unrelated source map slots");
Check(File.Exists(own) && SquadMapOverrides.Own(new CampaignDocument(File.ReadAllText(Path.Combine(a, "team.txt")))), "Standalone copies never mutate the source squad");
string players = Path.Combine(a, "players"); Directory.CreateDirectory(players);
var assembly = typeof(CampaignWorkspace).Assembly;
object game = Activator.CreateInstance(assembly.GetType("CampaignCreator.GameData")!, [root])!;
var assets = (AssetCatalog)game.GetType().GetProperty("Assets")!.GetValue(game)!;
assets.Add("skater", "Face", "Faces/Test/Player");
foreach (string position in new[] { "Left Wing", "Center", "Right Wing", "Left Defense", "Right Defense", "Goalie" })
    File.WriteAllText(Path.Combine(players, position + ".txt"), "Name = Export " + position + "\nPosition = " + position + "\nFace = Faces/Test/Player\n");
object package = assembly.GetType("CampaignCreator.ExportTranslation")!.GetMethod("BuildTeam", BindingFlags.Static | BindingFlags.NonPublic)!
    .Invoke(null, [new CampaignDocument(File.ReadAllText(Path.Combine(a, "team.txt"))), Path.Combine(a, "team.txt"), game, root, null])!;
var sidecars = (Dictionary<string, string>)package.GetType().GetProperty("Overrides")!.GetValue(package)!;
Check(sidecars.Values.All(text => new CampaignDocument(text).Get(SquadMapOverrides.ModeKey).Length == 0), "Play Now export sidecars omit campaign map flags");
var badSource = Route(1); badSource.BaseSquad = "No such native map source"; File.WriteAllText(own, badSource.Serialize());
assembly.GetType("CampaignCreator.StudioArt")!.GetMethod("Use")!.Invoke(null, [null]);
Check(CampaignRewardBudget.Calculate(root, campaign).SelectMany(b => b.Errors).Any(e => e.Contains("unavailable", StringComparison.OrdinalIgnoreCase)),
    "Enabling own maps rejects unavailable native source even without a local artwork dump");
var goalieMap = Route(1); goalieMap.Nodes[0].Type = "GeneralManager"; goalieMap.Nodes[0].GmRole = GoalieFreeAgents.Goalies; File.WriteAllText(own, goalieMap.Serialize());
Check(CampaignRewardBudget.Calculate(root, campaign).SelectMany(b => b.Errors).Any(e => e.Contains("goalie", StringComparison.OrdinalIgnoreCase)), "Active squad map participates in campaign goalie-setting validation");
Team(a, false);
Check(!CampaignRewardBudget.Calculate(root, campaign).SelectMany(b => b.Errors).Any(e => e.Contains("goalie", StringComparison.OrdinalIgnoreCase)), "Disabled retained squad map does not change current campaign rules");
Team(a, true);
string configPath = Path.Combine(campaign, "campaign.txt");
var enabledConfig = new CampaignDocument(File.ReadAllText(configPath));
enabledConfig.Set("Use Player Teams", "yes"); enabledConfig.Set(GoalieFeatureOptions.RecruitmentKey, "On"); enabledConfig.Save(configPath);
var disabledConfig = new CampaignDocument(enabledConfig.ToString());
disabledConfig.Set("Use Player Teams", "no"); disabledConfig.Set(GoalieFeatureOptions.RecruitmentKey, "Off");
var pendingConfig = new Dictionary<string, CampaignDocument> { [Path.GetFullPath(configPath)] = disabledConfig };
Check(!CampaignRewardBudget.Calculate(root, campaign, changedFiles: pendingConfig).SelectMany(b => b.Errors).Any(e => e.Contains("goalie", StringComparison.OrdinalIgnoreCase)),
    "Pending campaign-wide disable excludes retained squad overrides before saving goalie recruitment Off");
string retained = File.ReadAllText(own); disabledConfig.Save(configPath);
Check(!CampaignRewardBudget.Calculate(root, campaign).SelectMany(b => b.Errors).Any(e => e.Contains("goalie", StringComparison.OrdinalIgnoreCase)), "Saved campaign-wide disable excludes inactive squad maps");
var reactivated = new CampaignDocument(disabledConfig.ToString()); reactivated.Set("Use Player Teams", "yes");
pendingConfig[Path.GetFullPath(configPath)] = reactivated;
Check(CampaignRewardBudget.Calculate(root, campaign, changedFiles: pendingConfig).SelectMany(b => b.Errors).Any(e => e.Contains("goalie", StringComparison.OrdinalIgnoreCase)),
    "Reactivating player teams revalidates their retained maps against the pending campaign rules");
Check(File.ReadAllText(own) == retained && SquadMapOverrides.Own(new CampaignDocument(File.ReadAllText(Path.Combine(a, "team.txt")))), "Campaign-wide toggle and validation retain the squad override and its setting");
Console.WriteLine($"PASS {checks} squad map integration checks; evidence: {root}");
