using CampaignCore;
using EndlessMode;
using System.Reflection;
int checks = 0;
void Check(bool ok, string why) { checks++; if (!ok) throw new Exception(why); }
var api = Assembly.GetExecutingAssembly().GetType("EndlessMode.SquadMapRuntime");
Check(api != null, "Runtime needs the selected squad's effective map source");
object Call(string name, params object[] values) {
    try { return api.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, values); }
    catch (TargetInvocationException e) when (e.InnerException != null) { throw e.InnerException; }
}
string campaign = Path.GetFullPath(Path.Combine(args[0], "runtime-" + Guid.NewGuid().ToString("N"))); Plugin.ModFolder = campaign;
Directory.CreateDirectory(Path.Combine(campaign, "map_layouts"));
MapLayout Map(string name) => new() { Name = name, BaseSquad = "Basic Squad", Nodes = [new() { Id = "n", Type = "Coach", Outgoing = ["boss"] }, new() { Id = "boss", Layer = 1, Type = "Boss" }] };
File.WriteAllText(Path.Combine(campaign, "map_layouts", "map-1.txt"), Map("Shared").Serialize());
foreach (string key in new[] { "a", "b" }) {
    string directory = Path.Combine(campaign, "player_teams", key); Directory.CreateDirectory(Path.Combine(directory, SquadMapOverrides.Folder));
    File.WriteAllText(Path.Combine(directory, "team.txt"), "Team Name = Same name\nUse Own Maps = yes\n");
    File.WriteAllText(SquadMapOverrides.OverridePath(directory, 1), Map(key).Serialize());
    Plugin.PlayerTeamConfigs[key] = new() { IdentityKey = Path.Combine("player_teams", key) };
}
MapLayout Load(int stage, out string source) { object[] values = [stage, "", ""]; var map = (MapLayout)Call("Load", values); source = (string)values[1]; return map; }
Tape2Tape.CampaignUtil.CampaignState.runData.squadId = "Custom_a";
Tape2Tape.CampaignUtil.CampaignState.Selected.id = "Custom_b";
Check(Load(1, out var source).Name == "a" && source.Contains("player_teams"), "Run squad ID wins over stale menu selection and display names");
File.WriteAllText(Path.Combine(campaign, "player_teams", "a", "team.txt"), "Use Own Maps = no\n");
Check(Load(1, out source).Name == "Shared", "New stage rereads saved toggle without a runtime reload");
File.WriteAllText(Path.Combine(campaign, "player_teams", "a", "team.txt"), "Use Own Maps = yes\n");
Check(Load(1, out _) .Name == "a", "Re-enabling uses retained snapshot");
Check(Load(2, out source) == null && source.EndsWith("map-2.txt"), "Missing override and campaign layout retain native generation");
File.WriteAllText(SquadMapOverrides.OverridePath(Path.Combine(campaign, "player_teams", "a"), 1), "broken");
bool rejected = false; try { Load(1, out _); } catch (ArgumentException) { rejected = true; }
Check(rejected, "Invalid selected override is rejected, never replaced with shared authored map");
Tape2Tape.CampaignUtil.CampaignState.runData.squadId = "Custom_b";
Check(Load(1, out _).Name == "b", "Another squad remains isolated from invalid A map");
Tape2Tape.CampaignUtil.CampaignState.runData.squadId = "Vanilla";
Check(Load(1, out _).Name == "Shared", "Unmodified native squads retain shared map behavior");
Plugin.IsDefaultMode = true; Check(Load(1, out _) == null, "Default mode never loads authored campaign maps"); Plugin.IsDefaultMode = false;
var receipt = new CampaignDocument("Squad = Custom_a\nMap Source = player_teams/a/map_overrides/map-1.txt\n");
Check((bool)Call("MatchesReceipt", receipt, "Custom_a") && !(bool)Call("MatchesReceipt", receipt, "Custom_b"), "Receipt matches run squad, not current authored files");
Check((bool)Call("MatchesReceipt", new CampaignDocument("Campaign = Old\n"), "Custom_b"), "Legacy campaign-only receipts remain supported");
Console.WriteLine($"PASS {checks} squad runtime source checks; evidence: {campaign}");
