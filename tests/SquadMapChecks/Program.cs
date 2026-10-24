using CampaignCore;
using System.Reflection;

int checks = 0;
void Check(bool ok, string why) { checks++; if (!ok) throw new Exception(why); }
var api = typeof(MapLayout).Assembly.GetType("CampaignCore.SquadMapOverrides");
Check(api != null, "Squad map inheritance resolver must exist");
object? Call(string name, params object?[] values) {
    try { return api!.GetMethod(name)!.Invoke(null, values); }
    catch (TargetInvocationException e) when (e.InnerException != null) { throw e.InnerException; }
}
string campaign = Path.Combine(Path.GetFullPath(args[0]), "core-" + Guid.NewGuid().ToString("N"));
string a = Path.Combine(campaign, "player_teams", "a"), b = Path.Combine(campaign, "player_teams", "b");
Directory.CreateDirectory(a); Directory.CreateDirectory(b);
Directory.CreateDirectory(Path.Combine(campaign, "map_layouts"));
void Team(string squad, bool enabled) => File.WriteAllText(Path.Combine(squad, "team.txt"), "Team Name = Same name\nUse Own Maps = " + (enabled ? "yes" : "no"));
Team(a, false); Team(b, true);
File.WriteAllText(Path.Combine(campaign, "campaign.txt"), "Act Sequence = 1,2,3\n");
MapLayout Map(string name) => new() { Name = name, BaseSquad = "Basic Squad", Nodes = [new() { Id = "entry", Type = "Coach", Outgoing = ["boss"] }, new() { Id = "boss", Type = "Boss", Layer = 1 }] };
string Shared(int stage) => Path.Combine(campaign, "map_layouts", $"map-{stage}.txt");
string Own(string squad, int stage) => (string)Call("OverridePath", squad, stage)!;
string Resolve(string? squad, int stage) => (string)Call("ResolvePath", campaign, squad, stage)!;
string Revision() => (string)Call("Revision", campaign, a)!;
void Save(Dictionary<int, MapLayout?> changes, string? revision = null) => Call("Save", campaign, a, revision ?? Revision(), changes);
void Reject(Action action, string why) { bool rejected = false; try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException) { rejected = true; } Check(rejected, why); }
File.WriteAllText(Shared(1), Map("Campaign one").Serialize());
File.WriteAllText(Shared(2), Map("Campaign two").Serialize());
Check(Resolve(null, 1) == Shared(1) && Resolve(a, 1) == Shared(1), "Legacy/disabled squads inherit campaign");
Team(a, true);
Check(Resolve(a, 3) == Shared(3) && !File.Exists(Shared(3)), "Missing authored map retains native fallback");
Save([]);
Check(!Directory.Exists(Path.Combine(a, "map_overrides")), "No-op save must not create any override folder");
string sharedBefore = File.ReadAllText(Shared(2));
Save(new() { [2] = Map("Squad A two") });
Check(Resolve(a, 2) == Own(a, 2) && Resolve(a, 1) == Shared(1), "Only changed map becomes independent");
Check(Resolve(b, 2) == Shared(2), "Squads with identical display names stay isolated by directory");
Check(File.ReadAllText(Shared(2)) == sharedBefore, "Saving squad maps never writes campaign maps");
string existing = File.ReadAllText(Own(a, 2));
Team(a, false); Check(Resolve(a, 2) == Shared(2) && File.ReadAllText(Own(a, 2)) == existing, "Disabling retains edits");
Team(a, true); Check(Resolve(a, 2) == Own(a, 2), "Resolver rereads enabled flag from disk");
File.WriteAllText(Own(a, 2), "malformed");
Check(Resolve(a, 2) == Own(a, 2), "Invalid override stays selected so caller rejects it explicitly");
File.WriteAllText(Own(a, 2), existing);
var before = Revision(); File.WriteAllText(Shared(1), Map("Changed shared one").Serialize());
Reject(() => Save(new() { [2] = Map("Stale") }, before), "Concurrent shared edits reject stale saves");
Check(File.ReadAllText(Own(a, 2)) == existing, "Stale rejection leaves saved override intact");
before = Revision(); File.WriteAllText(Path.Combine(campaign, "campaign.txt"), "Act Sequence = 2,1,3\n");
Reject(() => Save(new() { [2] = null }, before), "Sequence change rejects stale reset");
File.WriteAllText(Path.Combine(campaign, "campaign.txt"), "Act Sequence = 1,2,3\n");
before = Revision(); File.WriteAllText(Shared(1), File.ReadAllText(Shared(1)));
Check(Revision() == before, "Revision is content based, not timestamp based");
Save(new() { [2] = null });
Check(!File.Exists(Own(a, 2)) && Resolve(a, 2) == Shared(2), "Reset removes only selected override");
Check(Directory.GetFiles(Path.Combine(campaign, "map_backups"), "map-2.txt", SearchOption.AllDirectories).Any(p => File.ReadAllText(p) == existing), "Reset retains recoverable backup");
Save(new() { [1] = Map("A1"), [2] = Map("A2"), [3] = Map("A3") });
Directory.CreateDirectory(Path.GetDirectoryName(Own(b, 2))!); File.WriteAllText(Own(b, 2), Map("B2").Serialize()); Team(b, false);
Call("Remap", campaign, new[] { 3, 1, 0 }, Path.Combine(campaign, "map_backups", "remap"));
Check(MapLayout.Parse(File.ReadAllText(Own(a, 1))).Name == "A3" && MapLayout.Parse(File.ReadAllText(Own(a, 2))).Name == "A1", "Renumbering follows original maps without overwrite collisions");
Check(!File.Exists(Own(a, 3)) && !File.Exists(Own(b, 2)), "New maps inherit and removed disabled overrides are removed");
Check(Directory.GetFiles(Path.Combine(campaign, "map_backups", "remap"), "map-2.txt", SearchOption.AllDirectories).Any(p => MapLayout.Parse(File.ReadAllText(p)).Name == "B2"), "Removed disabled override retained in backup");
Reject(() => Save(new() { [0] = Map("Bad") }), "Stage zero rejected");
Reject(() => Save(new() { [4] = Map("Bad") }), "Squad cannot add maps beyond campaign sequence");
Reject(() => Call("Save", campaign, b + "-outside", "", new Dictionary<int, MapLayout?>()), "Saving outside a real campaign squad rejected");
Console.WriteLine($"PASS {checks} squad map core checks; evidence: {campaign}");
