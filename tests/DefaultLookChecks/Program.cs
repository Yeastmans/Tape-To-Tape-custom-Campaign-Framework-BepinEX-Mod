using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CampaignCore;
using CampaignCreator;

var root = Path.GetFullPath("artifacts/default-look-checks/" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
void Check(bool pass, string message) { if (!pass) throw new Exception(message); Console.WriteLine("PASS " + message); }
MainWindow OpenWindow(string content)
{
    var result = new MainWindow(["--content", content]);
    // Startup now waits for the release check before offering defaults. Supply
    // that unrelated boundary deterministically; exercise the real startup and
    // prompt flow without depending on GitHub timing or opening update dialogs.
    typeof(MainWindow).GetField("releaseCheck", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
        .SetValue(result, Task.FromResult(true));
    return result;
}
CampaignDocument Create(string name, string position = "Center", string source = "") => new(File.ReadAllText(
    CreatorContent.Create(root, "player", name, source, null, null, initialPlayerPosition: position)));
CampaignDocument CreateTeam(string kind, string name, string source = "") => new(File.ReadAllText(
    CreatorContent.Create(root, kind, name, source, null, null)));
CampaignDocument GoalieLook(string teamFile) => new(File.ReadAllText(Path.Combine(Path.GetDirectoryName(teamFile)!, "goalie-equipment.cfg")));
Check(Create("Before").Get("Jersey Color") == "", "No default retains blank appearance");
Check(CreateTeam("team", "Before team").Get("Jersey Primary") == "", "No default retains blank team colors");
var look = new CampaignDocument("Jersey Primary = 12, 34, 56\nAway Primary = 65, 43, 21\nNumber Color Home = 9, 8, 7\nNumber Color Away = 8, 7, 6\nGloves Color = 4, 5, 6\nSocks Away Tertiary Color = 3, 2, 1\nBody = Body/Customization/Customization_colors\nSocks = Body/HockeyFC/HockeyFC\nSkin = Body/Customization_colors\nPads Skin = Body_Pads/Customization/Customization_colors\nLogo From = Default crest\nName = Do not copy\nSpeed = 999\n");
DefaultPlayerLook.Save(root, key => look.Get(key));
Check(DefaultPlayerLook.Load(root).Get("Speed") == "", "Defaults contain appearance only");
foreach (string kind in new[] { "team", "player-team" })
{
    var team = CreateTeam(kind, "New " + kind);
    Check(team.Get("Jersey Primary") == "12, 34, 56" && team.Get("Away Primary") == "65, 43, 21", kind + " starts with home and away team colors");
    Check(team.Get("Number Color Home") == "9, 8, 7" && team.Get("Number Color Away") == "8, 7, 6"
        && team.Get("Gloves Color") == "4, 5, 6" && team.Get("Socks Away Tertiary Color") == "3, 2, 1", kind + " starts with number and equipment colors");
    Check(team.Get("Body") == look.Get("Body") && team.Get("Body Away") == look.Get("Body") && team.Get("Skin") == "",
        kind + " starts with the default styles; a blank away piece takes the home choice");
    Check(team.Get("Helmet") == AppearanceContract.DefaultSkin("skater", "Helmet") && team.Get("Stick") == "",
        kind + " fills pieces the default left blank with the game's own style (the stick is each player's)");
    Check(team.Get("Socks") == look.Get("Socks") && team.Get("Socks Away") == "", kind + " keeps the sock style separate; blank away socks follow the jersey");
    Check(team.Get("Logo From") == "Default crest", kind + " starts with the default logo");
    var goalieLook = GoalieLook(Path.Combine(root, "library", kind == "team" ? "teams" : "player_teams", "New " + kind, "team.txt"));
    Check(goalieLook.Get("Skin") == look.Get("Skin") && goalieLook.Get("Skin Away") == look.Get("Skin") && goalieLook.Get("Pads Skin") == look.Get("Pads Skin")
        && goalieLook.Get("Goalie Pants Skin") == AppearanceContract.DefaultSkin("goalie", "Goalie Pants Skin"), kind + " goalie equipment starts with the default");
}
string draftRoot = Path.Combine(root, ".studio-drafts", "new-team");
var draftTeam = new CampaignDocument(File.ReadAllText(CreatorContent.Create(draftRoot, "player-team", "Draft player team", "", null, null, defaultLookRoot: root)));
Check(draftTeam.Get("Jersey Primary") == "12, 34, 56", "Home player-team draft reads the saved content-root default");
string campaign = Path.Combine(root, "campaigns", "Team defaults");
Directory.CreateDirectory(campaign);
File.WriteAllText(Path.Combine(campaign, "campaign.txt"), "Squad Template = Basic Squad\n");
string campaignSquad = SquadCopy.Create("", campaign, "Blank campaign squad", "Basic Squad", root);
Check(new CampaignDocument(File.ReadAllText(Path.Combine(campaignSquad, "team.txt"))).Get("Gloves Color") == "4, 5, 6", "Blank campaign squad starts with saved equipment colors");
Check(new CampaignDocument(File.ReadAllText(Path.Combine(campaignSquad, "team.txt"))).Get("Body") == look.Get("Body")
    && GoalieLook(Path.Combine(campaignSquad, "team.txt")).Get("Pads Skin") == look.Get("Pads Skin"), "Blank campaign squad starts with the default styles and goalie equipment");
string campaignOpponent = CreatorContent.Create(root, "team", "Blank campaign opponent", "", campaign, null);
Check(new CampaignDocument(File.ReadAllText(campaignOpponent)).Get("Gloves") == AppearanceContract.DefaultSkin("skater", "Gloves")
    && GoalieLook(campaignOpponent).Get("Skin") == look.Get("Skin"), "Blank campaign opponent starts with the whole default look");
string teamImport = Path.Combine(root, "team-import");
Directory.CreateDirectory(teamImport);
File.WriteAllText(Path.Combine(teamImport, "team.txt"), "Team Name = Source\nJersey Primary = 1, 2, 3\nGloves Color = 6, 5, 4\n");
foreach (string kind in new[] { "team", "player-team" })
{
    var team = CreateTeam(kind, "Imported " + kind, teamImport);
    Check(team.Get("Jersey Primary") == "1, 2, 3" && team.Get("Gloves Color") == "6, 5, 4", kind + " import retains authored colors");
}
string importedSquad = SquadCopy.Create(teamImport, campaign, "Imported campaign squad", "Basic Squad", root);
Check(new CampaignDocument(File.ReadAllText(Path.Combine(importedSquad, "team.txt"))).Get("Jersey Primary") == "1, 2, 3", "Imported campaign squad retains authored colors");
var skater = Create("Skater");
// Base-game model: the kit is the team's, so a new player carries no colors at all.
Check(skater.Get("Jersey Color") == "" && skater.Get("Jersey Away Color") == "" && skater.Get("Number Tertiary Color") == ""
    && PlayerColorModel.PlayerKeys.All(k => skater.Get(k) == ""), "New skater carries no kit colors; the team supplies them");
Check(skater.Get("Body") == look.Get("Body") && skater.Get("Skin") == "", "Skater gets only skater equipment");
Check(skater.Get("Body Away") == look.Get("Body") && skater.Get("Helmet") == AppearanceContract.DefaultSkin("skater", "Helmet") && skater.Get("Socks") == look.Get("Socks"),
    "New skater's custom look fills every piece, including socks");
Check(skater.Get("Logo From") == "" && skater.Get("Logo Skin") == "", "New skater takes no logo of its own; the team's logo applies");
var goalie = Create("Goalie", "Goalie");
Check(goalie.Get("Skin") == look.Get("Skin") && goalie.Get("Pads Skin") == look.Get("Pads Skin") && goalie.Get("Body") == "", "Goalie gets goalie equipment");
Check(goalie.Get("Name") == "Goalie" && goalie.Get("Speed") == "", "Identity and stats are independent");
Check(goalie.Get("Override Team Settings") == "no", "New player starts with team colors while the saved look remains available");
string imported = Path.Combine(root, "import.txt");
File.WriteAllText(imported, "Name = Original\nPosition = Center\nJersey Color = 1, 2, 3\n");
Check(Create("Imported", source: imported).Get("Jersey Color") == "", "Imported player drops kit colors; the team owns the kit");
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
var window = OpenWindow(root); window.Show(); Dispatcher.UIThread.RunJobs();
Check(!window.OwnedWindows.Any(w => w.Name == "DefaultLookPrompt"), "Saved default suppresses startup prompt");
void Click(string name) { window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
string CurrentHomeDraft() => ((ValueTuple<string, string>)typeof(MainWindow)
    .GetField("homeDraft", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
    .GetValue(window)!).Item2;
Click("HomeCreateTeams");
Check(new CampaignDocument(File.ReadAllText(CurrentHomeDraft())).Get("Away Primary") == "65, 43, 21", "Home Create player team opens with default away color");
window.ShowHome(); Dispatcher.UIThread.RunJobs();
Click("HomeTypeTeamsteam");
Click("HomeCreateTeams");
Check(new CampaignDocument(File.ReadAllText(CurrentHomeDraft())).Get("Jersey Primary") == "12, 34, 56", "Home Create opponent opens with default home color");
window.ShowHome(); Dispatcher.UIThread.RunJobs();
Click("HomeSetDefault");
using (var screenshot = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(1280, 900)))
{
    window.Width = 1280; window.Height = 900; Dispatcher.UIThread.RunJobs();
    screenshot.Render(window); screenshot.Save(Path.Combine(root, "default-look.png"));
}
Check(window.GetVisualDescendants().OfType<TabItem>().Any(t => Equals(t.Header, "Team look")) && !window.GetVisualDescendants().OfType<TabItem>().Any(t => Equals(t.Header, "Team roster")), "Default editor exposes team look without roster");
Check(window.GetVisualDescendants().OfType<Control>().Single(c => c.Name == "TeamLogo").GetVisualDescendants().OfType<Button>().Any(b => Equals(b.Content, "Choose logo"))
    && window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Default crest"), "Default look shows its logo in Team look");
Click("SaveDefaultLook");
Check(DefaultPlayerLook.Load(root).Get("Pads Skin") == look.Get("Pads Skin"), "UI save retains goalie equipment");
Check(DefaultPlayerLook.Load(root).Get("Logo From") == "Default crest", "UI save retains the default logo");
Check(Create("After UI Save").Get("Body") == look.Get("Body"), "Saved look (player styles) remains usable after editor round trip");
string beforeCancel = File.ReadAllText(DefaultPlayerLook.FilePath(root));
Click("HomeSetDefault");
foreach (var picker in window.GetVisualDescendants().OfType<ComboBox>().ToArray()) picker.SelectedIndex = 0;
window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Cancel")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
Check(File.ReadAllText(DefaultPlayerLook.FilePath(root)) == beforeCancel, "Cancel leaves saved defaults unchanged");
DefaultPlayerLook.Save(root, _ => "");
Check(Create("Cleared").Get("Jersey Color") == "", "Cleared defaults do not reuse old colors");
Check(CreateTeam("team", "Cleared team").Get("Jersey Primary") == "", "Cleared defaults do not color a new team");
Check(new CampaignDocument(File.ReadAllText(Path.Combine(root, "library", "teams", "New team", "team.txt"))).Get("Jersey Primary") == "12, 34, 56", "Changing defaults preserves existing team colors");
Check(new CampaignDocument(File.ReadAllText(Path.Combine(root, "library", "players", "Skater.txt"))).Get("Body") == look.Get("Body"), "Changing defaults preserves existing players");
window.Close();
string freshRoot = Path.Combine(root, "fresh");
var fresh = OpenWindow(freshRoot); fresh.Show(); Dispatcher.UIThread.RunJobs();
var prompt = fresh.OwnedWindows.Single(w => w.Name == "DefaultLookPrompt");
Check(prompt.IsVisible, "Missing default prompts on startup");
prompt.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "DefaultLookLater").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
fresh.ShowHome(); Dispatcher.UIThread.RunJobs();
Check(!fresh.OwnedWindows.Any(), "Later does not prompt again on returning home");
fresh.Close();
fresh = OpenWindow(freshRoot); fresh.Show(); Dispatcher.UIThread.RunJobs();
prompt = fresh.OwnedWindows.Single(w => w.Name == "DefaultLookPrompt");
prompt.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SetDefaultNow").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
Check(fresh.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "SaveDefaultLook"), "Next launch prompts again and Set default opens look editor");
fresh.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SaveDefaultLook").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
fresh.Close();
fresh = OpenWindow(freshRoot); fresh.Show(); Dispatcher.UIThread.RunJobs();
Check(!fresh.OwnedWindows.Any(), "Saving default stops prompt on future launches");
fresh.Close();
