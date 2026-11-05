using System.Reflection;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CampaignCore;
using CampaignCreator;

string output = Path.GetFullPath(args[0]);
Directory.CreateDirectory(output);
string root = Path.Combine(output, "fixture-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var assembly = typeof(HeadProject).Assembly;
var dataType = assembly.GetType("CampaignCreator.GameData")!;
var teamType = assembly.GetType("CampaignCreator.TeamEditorView")!;
assembly.GetType("CampaignCreator.StudioArt")!.GetMethod("Use")!.Invoke(null, [null]);
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
const string Native = "00e6e2f9-cfbe-438a-9ebe-7f2287f68436";
const string Unknown = "99999999-2222-4333-8444-555555555555";
const string Icon = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aNn8AAAAASUVORK5CYII=";
int checks = 0, failures = 0;
void Check(bool pass, string name) { checks++; if (!pass) failures++; Console.WriteLine((pass ? "PASS " : "FAIL ") + name); }
CustomRewardDefinition Reward(string id, string name, string kind = CustomRewardDefinition.CustomTalents) => new() {
    Id = id, Name = name, Kind = kind, IconPng = Icon, Trigger = "While owned",
    Stat = kind == CustomRewardDefinition.CustomGoalieTalents ? "Goalie skill" : "Overall"
};
void Save(string path, string value) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, value); }
object Data(string source) {
    var data = Activator.CreateInstance(dataType, [root])!;
    dataType.GetMethod("LoadCustomRewards", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(data, [source]);
    return data;
}
IReadOnlyDictionary<string, CustomRewardDefinition> Definitions(object data) => (IReadOnlyDictionary<string, CustomRewardDefinition>)dataType.GetProperty("CustomRewards")!.GetValue(data)!;
string[] Preview(string source, object data, string label) {
    var inputs = Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(f => f, f => SHA256.HashData(File.ReadAllBytes(f)));
    var owner = new Window { Width = 1320, Height = 960 };
    try {
        var ctor = teamType.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
        var editor = (Control)ctor.Invoke([owner, new CampaignDocument(File.ReadAllText(source)), data, root, source,
            (Action)(() => { }), (Action<string, Func<string>>)((_, _) => { }), (Action)(() => { }), (Action)(() => { }), (Action)(() => { }),
            (Action<string>)(_ => { }), (Action<string, bool>)((_, _) => { }), (Action)(() => { }), new TextBlock(), null, false, null, null]);
        owner.Content = editor; owner.Show(); Dispatcher.UIThread.RunJobs(); owner.UpdateLayout();
        var tabs = editor.GetVisualDescendants().OfType<TabControl>().Single(t => t.Items.OfType<TabItem>().Any(x => x.Header?.ToString() == "Team roster"));
        tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(t => t.Header?.ToString() == "Team roster");
        Dispatcher.UIThread.RunJobs(); owner.UpdateLayout();
        var texts = editor.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").Where(t => t.StartsWith("Talents · ")).ToArray();
        using var frame = owner.CaptureRenderedFrame();
        frame!.Save(Path.Combine(output, label + ".png"));
        File.WriteAllLines(Path.Combine(output, label + ".txt"), texts);
        Check(inputs.All(f => File.Exists(f.Key) && f.Value.SequenceEqual(SHA256.HashData(File.ReadAllBytes(f.Key)))), label + ": preview preserves saved loadouts and reward definitions");
        return texts;
    } finally { owner.Close(); Dispatcher.UIThread.RunJobs(); }
}

// A native-only formatter or a library-only lookup loses these names or chooses stale titles.
var skater = Reward("11111111-2222-4333-8444-555555555555", "Campaign Skater Talent");
var chaos = Reward("22222222-2222-4333-8444-555555555555", "Campaign Chaos Talent", CustomRewardDefinition.CustomChaosTalents);
var goalie = Reward("abcdefab-2222-4333-8444-555555555555", "Campaign Goalie Talent", CustomRewardDefinition.CustomGoalieTalents);
string campaign = Path.Combine(root, "campaigns", "Labels");
Save(Path.Combine(campaign, "campaign.txt"), "Act Sequence = 1\n");
string team = Path.Combine(campaign, "player_teams", "Label Team", "team.txt");
var teamDoc = new CampaignDocument("Team Name = Label Team\nSquad Template = No Effects\nStarting Relics Mode = Custom\n");
var stale = skater.Copy(); stale.Name = "Stale Embedded Skater";
CustomRewardExport.Embed(teamDoc, [stale]);
Save(team, teamDoc.ToString());
foreach (var reward in new[] { skater, chaos, goalie }) {
    reward.Save(Path.Combine(campaign, CustomRewardDefinition.Folder));
    var copy = reward.Copy(); copy.Name = "Stale Library " + reward.Name; copy.Save(Path.Combine(root, "library", "rewards"));
}
string players = Path.Combine(Path.GetDirectoryName(team)!, "players");
Save(Path.Combine(players, "left-wing.txt"), $"Name = Test Skater\nPosition = Left Wing\nTalents = {skater.Id}, {chaos.Id}, {Native}\n");
Save(Path.Combine(players, "goalie.txt"), $"Name = Test Goalie\nPosition = Goalie\nGoalie Talents = {goalie.Id.ToUpperInvariant()}\n");
Save(Path.Combine(players, "right-wing.txt"), $"Name = Imported Skater\nPosition = Right Wing\nTalents = {Unknown}, Imported talent label\n");
Save(Path.Combine(players, "center.txt"), "Name = No Talent Skater\nPosition = Center\nTalents = \n");
Save(Path.Combine(players, "left-defense.txt"), "Name = Legacy Name Skater\nPosition = Left Defense\nTalents = Campaign Skater Talent\n");
var data = Data(team);
Check(Definitions(data)[skater.Id].Name == "Campaign Skater Talent" && Definitions(data)[chaos.Id].Name == "Campaign Chaos Talent" && Definitions(data)[goalie.Id].Name == "Campaign Goalie Talent", "Campaign definitions override conflicting library and embedded snapshots");
var texts = Preview(team, data, "campaign-roster");
Check(texts.Contains("Talents · Campaign Skater Talent, Campaign Chaos Talent, En Garde!"), "Roster displays regular, Chaos and native talent names together");
Check(texts.Contains("Talents · Campaign Goalie Talent"), "Goalie roster resolves custom IDs without case sensitivity");
Check(texts.Contains("Talents · Campaign Skater Talent"), "Roster preserves a legacy authored name assignment");
Check(texts.Contains("Talents · Name unavailable, Imported talent label") && texts.Contains("Talents · None"), "Unknown, literal imported and empty talent labels preserve existing fallbacks");
Check(!texts.Any(t => t.Contains("Stale")), "Roster displays authoritative campaign titles");

// Standalone imported teams can carry snapshots on their team and player documents.
var portable = Reward("44444444-2222-4333-8444-555555555555", "Portable Player Talent");
var portableTeamReward = Reward("55555555-2222-4333-8444-555555555555", "Portable Team Talent");
var library = Reward("66666666-2222-4333-8444-555555555555", "Library Talent");
string portableTeam = Path.Combine(root, "library", "Exported Teams", "Portable", "team.txt");
teamDoc = new CampaignDocument("Team Name = Portable\nStarting Relics Mode = Custom\n");
CustomRewardExport.Embed(teamDoc, [portableTeamReward]); Save(portableTeam, teamDoc.ToString());
var portablePlayer = new CampaignDocument($"Name = Portable Skater\nPosition = Center\nTalents = {portable.Id}, {portableTeamReward.Id}, {library.Id}\n");
CustomRewardExport.Embed(portablePlayer, [portable]);
Save(Path.Combine(Path.GetDirectoryName(portableTeam)!, "players", "center.txt"), portablePlayer.ToString());
foreach (var reward in new[] { portable, portableTeamReward }) {
    var copy = reward.Copy(); copy.Name = "Stale Library " + reward.Name; copy.Save(Path.Combine(root, "library", "rewards"));
}
library.Save(Path.Combine(root, "library", "rewards"));
data = Data(portableTeam);
Check(Definitions(data)[portable.Id].Name == "Portable Player Talent" && Definitions(data)[portableTeamReward.Id].Name == "Portable Team Talent", "Team and roster snapshots override conflicting library titles");
texts = Preview(portableTeam, data, "portable-roster");
Check(texts.Contains("Talents · Portable Player Talent, Portable Team Talent, Library Talent"), "Standalone roster displays embedded names and library fallback");
Check(!RewardCatalog.ReadEntries(root, "Talents").ContainsKey(skater.Id) && !RewardCatalog.ReadEntries(root, "Talents").ContainsKey(portable.Id), "Displaying custom talents does not insert them into native reward catalogs");
Console.WriteLine($"{checks - failures}/{checks} team roster reward-name checks passed");
return failures == 0 ? 0 : 1;
