using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CampaignCore;
using CampaignCreator;

internal static class ImportLibraryChecks
{
    internal static void Run(string output)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(output);
        string root = Path.Combine(output, "fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        // In-game custom reads the game's save folder: never the real one in a test.
        Environment.SetEnvironmentVariable("T2T_GAME_SAVE_PATH", Path.Combine(root, "fake-game"));
        int count = 0;
        void Check(bool ok, string text) { if (!ok) throw new Exception(text); Console.WriteLine("PASS " + text); count++; }
        string Put(string relative, string text) { string path = Path.Combine(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text); return path; }
        foreach (string equipmentMode in new[] { "yes", "no" })
        {
            string source = Put("sources/OwnKit-" + equipmentMode + ".txt", "Name = Source " + equipmentMode
                + "\nPosition = Center\nOverride Team Settings = " + equipmentMode
                + "\nBody = Body/Tycoons/Tycoons\nBody Away = Body/Golfers/Golfers\nSkates Color = 90,80,70\n"
                + PlayerColorModel.VersionKey + " = " + PlayerColorModel.CurrentVersion + "\n");
            string sourceBefore = File.ReadAllText(source);
            string importedPlayer = CreatorContent.Create(root, "player", "Preserved kit " + equipmentMode, source, null, null);
            var importedDoc = new CampaignDocument(File.ReadAllText(importedPlayer));
            Check(importedDoc.Get("Override Team Settings") == equipmentMode && importedDoc.Get("Body") == "Body/Tycoons/Tycoons"
                && importedDoc.Get("Body Away") == "Body/Golfers/Golfers" && importedDoc.Get("Skates Color") == "90,80,70",
                "Player import preserves equipment mode, distinct premade choices and current personal colors: " + equipmentMode);
            Check(File.ReadAllText(source) == sourceBefore, "Player import leaves its source unchanged: " + equipmentMode);
        }
        Put("library/Base Game Teams/Old Dump/team.txt", "Team Name = Old Dump\n");
        Put("library/Base Game Players/Old Dump Player.txt", "Name = Old Dump Player\nPosition = Center\n");
        var teams = ContentImport.Entries(root, "team", ContentImport.BaseGame);
        int bundledTeams = Directory.GetDirectories(Path.Combine(GameArt.ArtCache.Latest() ?? throw new Exception("Build the game art cache before this check."), "base-rosters", "Base Game Teams")).Length;
        Check(teams.Count == bundledTeams, $"Fresh content folder has all {bundledTeams} teams from the game art cache");
        Check(!teams.Any(e => e.Name.StartsWith("Old Dump")) && !ContentImport.Entries(root, "skater", ContentImport.BaseGame).Any(e => e.Name.StartsWith("Old Dump")), "Library dump folders are not base-game sources");
        string NativeName(string name) => name switch { "Manager" => "General Manager Squad", "16-Bit" => "SNES Squad", "The Officials" => "Referee Squad", "Dominator Squad" => "Eliminator Squad", "Modern Squad" => "Tchel Squad", _ => name };
        Check(SquadTemplateCatalog.All.Where(t => t.Id != "noeffects").All(t => teams.Any(e => SquadTemplatePolicy.Identity(NativeName(new CampaignDocument(File.ReadAllText(e.File)).Get("Team Name"))) == t.Id)), "All 19 native squad templates have extracted teams");
        Check(ContentImport.Entries(root, "goalie", ContentImport.BaseGame).Count > 0 && ContentImport.Entries(root, "skater", ContentImport.BaseGame).Count > 0, "Both player roles available offline");
        string campaignFile = Put("campaigns/Test/campaign.txt", "Squad Template = Basic Squad\n");
        string original = Put("campaigns/Test/teams/Mine/team.txt", "Team Name = Mine\n");
        Put("campaigns/Test/teams/Mine/players/Center.txt", "Name = My Player\nPosition = Center\n");
        Put("library/Custom Teams/Native custom/team.txt", "Team Name = Native custom\n");
        Check(!ContentImport.Entries(root, "team", ContentImport.InGame).Any(e => e.Name.StartsWith("Native custom")), "Old in-game custom dump folders are not import sources");
        Check(ContentImport.Entries(root, "team", ContentImport.CampaignCustom).All(e => !e.Name.StartsWith("Native custom")), "Native custom teams are not classified as campaign content");
        string copied = CreatorContent.Create(root, "team", "Offline copy", Path.GetDirectoryName(teams.First(e => e.Name.StartsWith("Meatballs")).File)!, Path.GetDirectoryName(campaignFile), null);
        Check(Directory.GetFiles(Path.Combine(Path.GetDirectoryName(copied)!, "players"), "*.txt").Length >= 6, "Offline team import carries the full roster");
        string save = Path.Combine(root, "fake-game", "TeamDataModels"), overrides = Path.Combine(root, "overrides"); Directory.CreateDirectory(save); Directory.CreateDirectory(overrides);
        var package = new ExportTranslation.Package("export-test", new() { ["CustomTeam-export-test.json"] = "{\"Id\":\"export-test\",\"centerId\":\"p\"}",
            ["CustomForward-p.json"] = "{\"id\":\"p\",\"firstName\":\"My\",\"lastName\":\"Player\"}" }, new() {
            ["export-test.team.txt"] = "Team Name = Mine\nLogo From = shared\nJersey Logo From = home-kit.png\nJersey Logo Away From = none\n",
            ["p.txt"] = "Name = My Player\nPosition = Center\nOverride Team Settings = yes\nLogo From = personal\n" });
        ExportTranslation.Write(package, save, overrides, root);
        Check(ContentImport.Entries(root, "team", ContentImport.InGame).Any(e => e.Name.StartsWith("Mine")), "Exported team immediately appears in in-game tab");
        var reimportedTeam = new CampaignDocument(File.ReadAllText(ContentImport.Entries(root, "team", ContentImport.InGame).Single(e => e.Name.StartsWith("Mine")).File));
        Check(reimportedTeam.Get("Jersey Logo From") == "home-kit.png" && reimportedTeam.Get("Jersey Logo Away From") == "none",
            "Current Play Now team reimport retains both authored kit selections");
        var cleanupTeam = new CampaignDocument("Team Name = Cleaned\nJersey Logo From = home-kit.png\nJersey Logo Away From = none\nExport Jersey Logo Mode = embedded\nExport Jersey Logo PNG = payload\n");
        new UpgradeCleanup().Clean(cleanupTeam, "team", "skater");
        Check(cleanupTeam.Get("Jersey Logo From") == "home-kit.png" && cleanupTeam.Get("Jersey Logo Away From") == "none"
            && cleanupTeam.Get("Export Jersey Logo PNG") == "payload", "Upgrade cleanup retains both kit selections and embedded metadata");
        Check(ContentImport.Entries(root, "team", ContentImport.CampaignCustom).Any(e => Path.GetFullPath(e.File) == Path.GetFullPath(original)) && File.ReadAllText(original) == "Team Name = Mine\n", "Export keeps campaign original unchanged and listed");
        Check(ContentImport.Entries(root, "skater", ContentImport.InGame).Any(e => e.Name.StartsWith("My Player")), "Exported team roster is available for player imports");
        var reimportedPlayer = new CampaignDocument(File.ReadAllText(ContentImport.Entries(root, "skater", ContentImport.InGame).Single(e => e.Name.StartsWith("My Player")).File));
        Check(reimportedPlayer.Get("Logo From") == "personal" && reimportedPlayer.Get("Override Team Settings") == "yes",
            "Current Play Now player reimport retains its independent shared-logo override");
        var goalie = new CampaignDocument("Name = Exported Goalie\nExport Role = goalie\n");
        PlayerExport.Write(new() { ["id"] = "g" }, goalie, Path.Combine(save, "CustomGoalie-g.json"), Path.Combine(overrides, "g.txt"), Path.Combine(root, "library/Exported Players/g.txt"));
        Check(ContentImport.Entries(root, "goalie", ContentImport.InGame).Any(e => e.Name.StartsWith("Exported Goalie") && e.Name.Contains("Studio export")) && !ContentImport.Entries(root, "goalie", ContentImport.CampaignCustom).Any(e => e.Name.StartsWith("Exported Goalie")), "Studio export is listed only under In-game custom");
        // A second export of the same player is a separate game copy: both listed, dated.
        PlayerExport.Write(new() { ["id"] = "g2" }, goalie, Path.Combine(save, "CustomGoalie-g2.json"), Path.Combine(overrides, "g2.txt"), Path.Combine(root, "library/Exported Players/g2.txt"));
        Check(ContentImport.Entries(root, "goalie", ContentImport.InGame).Count(e => e.Name.StartsWith("Exported Goalie")) == 2, "Repeated exports each get their own dated line");
        // Made in the game's own editor: converted from the game's format, no Studio copy.
        File.WriteAllText(Path.Combine(save, "CustomForward-ed1.json"), "{\"id\":\"ed1\",\"firstName\":\"Game\",\"lastName\":\"Made\",\"number\":12,\"isLefty\":true,\"isBlack\":false,\"skaterSize\":3,\"speed\":80,\"shotPower\":70,\"shotAccuracy\":60,\"checking\":50,\"headSkin\":\"Faces/Tycoons/Tycoons_Large\",\"bodySkin\":\"Body/Customization/Customization_colors\",\"talentIds\":[\"t1\",\"t2\"]}");
        File.WriteAllText(Path.Combine(save, "CustomGoalie-ed2.json"), "{\"id\":\"ed2\",\"firstName\":\"Game\",\"lastName\":\"Keeper\",\"skill\":40,\"catchingSkill\":70,\"skin\":\"Body/Customization_colors\"}");
        File.WriteAllText(Path.Combine(save, "CustomTeam-ed3.json"), "{\"Id\":\"ed3\",\"TeamName\":\"Game Team\",\"City\":\"Somewhere\",\"nickname\":\"GTM\",\"centerId\":\"ed1\",\"goalieId\":\"ed2\",\"homeColors\":{\"jerseyScheme\":{\"primaryColor\":{\"r\":1,\"g\":0,\"b\":0,\"a\":1},\"secondaryColor\":{\"r\":0,\"g\":0,\"b\":1,\"a\":1},\"tertiaryColor\":{\"r\":1,\"g\":1,\"b\":1,\"a\":1}}}}");
        var made = ContentImport.Entries(root, "skater", ContentImport.InGame).Single(e => e.Name.StartsWith("Game Made"));
        var madeDoc = new CampaignDocument(File.ReadAllText(made.File));
        Check(made.Name.Contains("Game editor") && madeDoc.Get("Size") == "Big" && madeDoc.Get("Left Handed") == "yes" && madeDoc.Get("Face") == "Faces/Tycoons/Tycoons_Large" && madeDoc.Get("Talents") == "t1, t2", "Game editor player converts from the game's format");
        Check(ContentImport.Entries(root, "goalie", ContentImport.InGame).Any(e => e.Name.StartsWith("Game Keeper") && e.Name.Contains("Game editor")), "Game editor goalie listed as a goalie");
        var gameTeam = ContentImport.Entries(root, "team", ContentImport.InGame).Single(e => e.Name.StartsWith("Game Team"));
        var gameTeamDoc = new CampaignDocument(File.ReadAllText(gameTeam.File));
        Check(gameTeamDoc.Get("Jersey Primary") == "255, 0, 0" && gameTeamDoc.Get("Abbreviation") == "GTM"
            && File.Exists(Path.Combine(Path.GetDirectoryName(gameTeam.File)!, "players", "Center - Game Made.txt"))
            && File.Exists(Path.Combine(Path.GetDirectoryName(gameTeam.File)!, "players", "Goalie - Game Keeper.txt")), "Game editor team converts with colours and roster");
        Check(!ContentImport.Entries(root, "team", ContentImport.CampaignCustom).Any(e => e.Name.StartsWith("Game Team")) && !ContentImport.Entries(root, "team", ContentImport.BaseGame).Any(e => e.Name.StartsWith("Game Team")), "Game editor team appears in no other tab");
        File.Delete(Path.Combine(save, "CustomForward-ed1.json"));
        Check(!ContentImport.Entries(root, "skater", ContentImport.InGame).Any(e => e.Name.StartsWith("Game Made")) && !File.Exists(made.File), "Deleted in the game means gone from the list");
        try { ExportTranslation.Write(package with { TeamId = "failed" }, Path.Combine(root, "missing-save"), overrides, root); throw new Exception("Expected failure"); } catch (DirectoryNotFoundException) { }
        Check(!Directory.Exists(Path.Combine(root, "library/Exported Teams/failed")), "Failed export creates no library entry");
        var blockedPackage = new ExportTranslation.Package("rollback", new() { ["CustomTeam-export-test.json"] = "{}" }, new() { ["rollback.team.txt"] = "Team Name = Failed export\n" });
        try { ExportTranslation.Write(blockedPackage, save, overrides, root); throw new Exception("Expected collision"); } catch (IOException) { }
        Check(!ContentImport.Entries(root, "team", ContentImport.InGame).Any(e => e.Name.StartsWith("Failed export")), "Failed team publication rolls back its new library snapshot");
        string failedPlayer = Path.Combine(root, "library/Exported Players/failed.txt");
        try { PlayerExport.Write(new(), new CampaignDocument("Name = Failed player\n"), Path.Combine(save, "CustomGoalie-g.json"), Path.Combine(overrides, "failed.txt"), failedPlayer); throw new Exception("Expected collision"); } catch (IOException) { }
        Check(!File.Exists(failedPlayer) && File.Exists(Path.Combine(save, "CustomGoalie-g.json")), "Failed player publication removes its snapshot and preserves existing save");
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var owner = new Window(); owner.Show(); var pending = ContentImport.Pick(owner, root, "team");
        void Pump() { for (int i = 0; i < 5; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(25); } }
        Pump(); var dialog = owner.OwnedWindows.Single(); var tabs = dialog.GetVisualDescendants().OfType<TabControl>().Single();
        Check(tabs.Items.Count == 4, "Team import picker has four separate source tabs");
        dialog.CaptureRenderedFrame()!.Save(Path.Combine(output, "base-game.png"));
        tabs.SelectedIndex = 2; Pump();
        Check(dialog.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("game's save folder") == true), "In-game tab explains it reads the game's save folder");
        Check(dialog.GetVisualDescendants().OfType<ListBox>().Single().Items.Cast<ContentImport.Entry>().Any(e => e.Name.StartsWith("Game Team")), "In-game tab lists the game editor team");
        dialog.CaptureRenderedFrame()!.Save(Path.Combine(output, "in-game-custom.png"));
        tabs.SelectedIndex = 3; Pump();
        Check(dialog.GetVisualDescendants().OfType<ListBox>().Single().Items.Cast<ContentImport.Entry>().Any(e => Path.GetFullPath(e.File) == Path.GetFullPath(original)), "Campaign tab retains original after switching from in-game");
        dialog.Close(); owner.Close(); Console.WriteLine($"{count} import library checks passed.");
    }
}
