using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CampaignCore;
using CampaignCreator;

void Check(bool pass, string name) { if (!pass) throw new Exception(name); Console.WriteLine("PASS " + name); }
var root = Path.GetFullPath("artifacts/team-look-fill-checks/" + Guid.NewGuid().ToString("N"));
string inGame = Path.Combine(root, "library", "Exported Teams", "Source");
string campaign = Path.Combine(root, "campaigns", "Sample", "opponents", "Other");
Directory.CreateDirectory(inGame);
// In-game custom lists what the game's save folder holds; this Studio export is team "Source".
Environment.SetEnvironmentVariable("T2T_GAME_SAVE_PATH", Path.Combine(root, "fake-game"));
string gameSave = Path.Combine(root, "fake-game", "TeamDataModels"); Directory.CreateDirectory(gameSave);
File.WriteAllText(Path.Combine(gameSave, "CustomTeam-Source.json"), "{\"Id\":\"Source\"}");
Directory.CreateDirectory(campaign);
string sourceFile = Path.Combine(inGame, "team.txt");
File.WriteAllText(sourceFile, "Team Name = Source\nCity = Other city\nDescription = Do not copy\nLogo From = Other logo\nJersey Logo From = source-home.png\nJersey Logo Away From = source-away.png\nTeam Relics = Other relic\nJersey Primary = 12, 34, 56\nAway Primary = 65, 43, 21\nBody = standard\n");
File.WriteAllText(Path.Combine(inGame, "goalie-equipment.cfg"), "Skin = standard\nGoalie Pants Skin = standard\n");
File.WriteAllText(Path.Combine(campaign, "team.txt"), "Team Name = Campaign source\nJersey Primary = 3, 2, 1\n");
var look = TeamLookCopy.Load(sourceFile);
Check(look.Get("Jersey Primary") == "12, 34, 56" && look.Get("Away Primary") == "65, 43, 21", "Fill source reads both kits");
Check(look.Get("Skin").Length > 0 && look.Get("Goalie Pants Skin").Length > 0, "Fill source reads goalie equipment defaults");
Check(look.Get("Team Name") == "" && look.Get("Team Relics") == "" && look.Get("Logo From") == "", "Fill source contains appearance fields only");
var target = new CampaignDocument("Team Name = Target\nCity = Keep city\nDescription = Keep description\nLogo From = Keep logo\nJersey Logo From = keep-home.png\nJersey Logo Away From = keep-away.png\nTeam Relics = Keep relic\nSquad Template = Basic Squad\nJersey Primary = 1, 1, 1\n");
TeamLookCopy.Copy(target, look, key => look.Get(key));
Check(target.Get("Jersey Primary") == "12, 34, 56" && target.Get("Skin") == look.Get("Skin"), "Fill replaces team and goalie look fields");
Check(target.Get("Team Name") == "Target" && target.Get("City") == "Keep city" && target.Get("Description") == "Keep description"
    && target.Get("Logo From") == "Keep logo" && target.Get("Jersey Logo From") == "keep-home.png"
    && target.Get("Jersey Logo Away From") == "keep-away.png"
    && target.Get("Team Relics") == "Keep relic" && target.Get("Squad Template") == "Basic Squad",
    "Fill preserves identity, logo, roster settings and gameplay fields");
var inheritedKit = TeamLookCopy.Kit(false, key => key == "Jersey Logo From" ? "" : key == "Export Jersey Logo PNG" ? "stale" : "");
Check(inheritedKit.Get("Jersey Logo Away From") == "" && inheritedKit.Get("Export Jersey Logo Away PNG") == "",
    "Copying an inherited kit clears destination payload instead of freezing the inherited image");
Check(ContentImport.Entries(root, "team", ContentImport.InGame).Any(e => e.File == sourceFile)
    && ContentImport.Entries(root, "team", ContentImport.CampaignCustom).Any(e => e.File == Path.Combine(campaign, "team.txt")),
    "In-game custom and Campaign custom offer their own teams");
DefaultPlayerLook.Save(root, key => key switch { "Logo From" => "Default identity", "Jersey Logo From" => "Default home",
    "Jersey Logo Away From" => "none", "Export Jersey Logo PNG" => "home-embedded", _ => "" });
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
var window = new MainWindow(["--content", root]);
window.Show();
void Pump() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
void Click(Visual parent, string name)
{
    parent.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name)
        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Pump();
}
Pump();
Click(window, "HomeSetDefault");
Click(window, "FillLook");
var dialog = window.OwnedWindows.Single(w => w.Name == "FillTeamLookDialog");
dialog.UpdateLayout();
var sourceTabs = dialog.GetVisualDescendants().OfType<TabControl>().Single(t => t.Name == "FillLookSourceTabs");
Check(sourceTabs.Items.Cast<TabItem>().Select(t => t.Header?.ToString()).SequenceEqual(
    [ContentImport.BaseGame, ContentImport.InGame, ContentImport.CampaignCustom]), "Fill dialog offers exactly the three team locations");
sourceTabs.SelectedIndex = 1;
Pump(); dialog.UpdateLayout();
var teams = dialog.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "FillLookTeams");
teams.SelectedItem = teams.Items.Cast<ContentImport.Entry>().Single(e => e.File == sourceFile);
Pump();
Click(dialog, "FillLookSelected");
Check(!window.OwnedWindows.Any(w => w.Name == "FillTeamLookDialog"), "Fill dialog closes after selecting a team");
Click(window, "SaveDefaultLook");
Check(DefaultPlayerLook.Load(root).Get("Jersey Primary") == "12, 34, 56"
    && DefaultPlayerLook.Load(root).Get("Skin").Length > 0, "Fill look updates the visible draft and saves its goalie look");
Check(DefaultPlayerLook.Load(root).Get("Logo From") == "Default identity"
    && DefaultPlayerLook.Load(root).Get("Jersey Logo From") == "Default home"
    && DefaultPlayerLook.Load(root).Get("Jersey Logo Away From") == "none"
    && DefaultPlayerLook.Load(root).Get("Export Jersey Logo PNG") == "home-embedded", "Fill look preserves all three logo selections and payload");
// Exercise the actual copy controls and persistence, including edits not saved yet.
var beforeCopy = new CampaignDocument("""
Jersey Primary = 10, 20, 30
Away Primary = 90, 80, 70
Body = Body/Customization/Customization_colors
Body Away = Body/Meatballs
Socks =
Socks Away = Body/Meatballs
Bicep = Bicep/Customization/Customization_colors
Bicep Away = Bicep/Meatballs
Gloves Color = 1, 2, 3
Gloves Away Color = 4, 5, 6
Number Color Home = 7, 8, 9
Number Color Away = 9, 8, 7
Skin = Body/Customization/Customization_colors
Skin Away = Body/Mid_Cheese
Goalie Pants Skin = Body/Customization/Customization_colors
Goalie Pants Away = Body/Mid_Cheese
Glove Skin = Glove/Customization/Customization_colors
Glove Away = Glove/Meatballs
Blocker Skin = Blocker/Customization/Customization_colors
Blocker Away = Blocker/Meatballs
Pads Skin = Pads/Customization/Customization_colors
Pads Away = Pads/Meatballs
Stick Skin = Stick/Customization/Customization_colors
Stick Away = Stick/Meatballs
Helmet Skin = Helmet/Helmet_Prisoners
Logo From = Keep crest
Jersey Logo From = home-crest.png
Jersey Logo Away From = none
Export Jersey Logo Mode = embedded
Export Jersey Logo ID = home-id
Export Jersey Logo SHA256 = home-hash
Export Jersey Logo PNG = home-payload
Number Style = Roman numerals
Stick Color = 22, 33, 44
""");
DefaultPlayerLook.Save(root, key => beforeCopy.Get(key));
Click(window, "HomeSetDefault");
Check(window.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "CopyKitToAway" && b.IsVisible),
    "Home kit offers Copy to Away");
var editor = window.GetVisualDescendants().Single(v => v.GetType().Name == "TeamEditorView");
editor.GetType().GetMethod("ColorsDialog", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
    .Invoke(editor, new object[] { "Jersey", new[] { "Jersey Primary", "Jersey Secondary", "Jersey Accent" } });
Pump();
var colorDialog = window.OwnedWindows.Last(w => w.IsVisible);
colorDialog.GetVisualDescendants().OfType<ColorView>().Single().Color = Avalonia.Media.Colors.Red;
Pump(); colorDialog.Close(); Pump();
beforeCopy.Set("Jersey Primary", "255, 0, 0");
Check(DefaultPlayerLook.Load(root).Get("Jersey Primary") == "10, 20, 30", "Source color edit remains unsaved before copying");
using (var frame = window.CaptureRenderedFrame()) frame!.Save(Path.Combine(root, "copy-to-away.png"));
Click(window, "CopyKitToAway");
Check(DefaultPlayerLook.Load(root).Get("Away Primary") == "90, 80, 70", "Copy stays in the draft until Save");
Click(window, "SaveDefaultLook");
var copiedHome = DefaultPlayerLook.Load(root);
foreach (var (home, awayKey) in new[] { ("Jersey Primary", "Away Primary"), ("Body", "Body Away"),
    ("Socks", "Socks Away"), ("Bicep", "Bicep Away"), ("Gloves Color", "Gloves Away Color"),
    ("Number Color Home", "Number Color Away"), ("Skin", "Skin Away"),
    ("Goalie Pants Skin", "Goalie Pants Away"), ("Glove Skin", "Glove Away"),
    ("Blocker Skin", "Blocker Away"), ("Pads Skin", "Pads Away"), ("Stick Skin", "Stick Away") })
    Check(copiedHome.Get(home) == beforeCopy.Get(home) && copiedHome.Get(awayKey) == beforeCopy.Get(home),
        "Copy to Away saves " + home + " and preserves the source");
Check(copiedHome.Get("Socks Away") == "", "Copy clears the destination sock override to follow its copied jersey");
Check(copiedHome.Get("Jersey Logo From") == "home-crest.png" && copiedHome.Get("Jersey Logo Away From") == "home-crest.png"
    && copiedHome.Get("Export Jersey Logo Away PNG") == "home-payload", "Copy to Away retains source crest and payload");
foreach (string key in new[] { "Logo From", "Number Style", "Helmet Skin", "Stick Color" })
    Check(copiedHome.Get(key) == beforeCopy.Get(key), "Copy preserves shared setting " + key);
DefaultPlayerLook.Save(root, key => beforeCopy.Get(key));
Click(window, "HomeSetDefault");
var kitTabs = window.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.TabStrip>().Single(t => t.Name == "TeamKitTabs");
kitTabs.SelectedIndex = 1; Pump();
Check(window.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "CopyKitToHome" && b.IsVisible),
    "Away kit offers Copy to Home");
using (var frame = window.CaptureRenderedFrame()) frame!.Save(Path.Combine(root, "copy-to-home.png"));
Click(window, "CopyKitToHome");
Click(window, "SaveDefaultLook");
var copiedAway = DefaultPlayerLook.Load(root);
Check(copiedAway.Get("Jersey Primary") == "90, 80, 70" && copiedAway.Get("Away Primary") == "90, 80, 70",
    "Copy to Home saves away colors and preserves source");
Check(copiedAway.Get("Skin") == "Body/Mid_Cheese" && copiedAway.Get("Goalie Pants Skin") == "Body/Mid_Cheese"
    && copiedAway.Get("Number Color Home") == "9, 8, 7", "Copy to Home maps goalie styles and number colors");
Check(copiedAway.Get("Jersey Logo From") == "none" && copiedAway.Get("Jersey Logo Away From") == "none"
    && copiedAway.Get("Export Jersey Logo PNG") == "", "Copy to Home retains explicit no-logo choice without old payload");
string freshTeamDirectory = Path.Combine(root, "campaigns", "Fresh", "teams", "Fresh");
Directory.CreateDirectory(freshTeamDirectory);
var freshTeam = new CampaignDocument("Team Name = Fresh\n");
DefaultPlayerLook.ApplyTeamLook(root, freshTeam, freshTeamDirectory);
Check(freshTeam.Get("Jersey Logo From") == "none" && freshTeam.Get("Jersey Logo Away From") == "none"
    && freshTeam.Get("Logo From") == "Keep crest", "New team inherits saved default identity and kit choices");
var freshPlayer = new CampaignDocument("Name = New skater\nPosition = Center\n");
DefaultPlayerLook.Apply(root, freshPlayer);
Check(freshPlayer.Get("Logo From") == "" && freshPlayer.Get("Jersey Logo From") == ""
    && freshPlayer.Get("Jersey Logo Away From") == "", "Default look does not assign personal logo overrides to new players");
window.Close();
