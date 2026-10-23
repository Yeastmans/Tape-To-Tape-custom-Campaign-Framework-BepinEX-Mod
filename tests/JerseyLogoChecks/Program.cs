using Avalonia;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using System.IO.Compression;
using System.Reflection;
using CampaignCore;
using CampaignCreator;

AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
int checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
void Identity(CampaignDocument before, CampaignDocument after, string name)
{
    foreach (string key in new[] { "Logo From", "Logo Skin", "Export Logo Mode", "Export Logo ID", "Export Logo SHA256", "Export Logo PNG" })
        Check(before.Get(key) == after.Get(key), name + " preserves " + key);
}
CampaignDocument WithoutKits(CampaignDocument source)
{
    var result = new CampaignDocument(source.ToString());
    JerseyLogoOptions.Clear(result, false); JerseyLogoOptions.Clear(result, true);
    return result;
}
string root = Path.Combine(Path.GetTempPath(), "jersey-logo-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    string home = Path.Combine(root, "home.png"), away = Path.Combine(root, "away.png");
    using (var bitmap = new RenderTargetBitmap(new PixelSize(2, 2))) bitmap.Save(home);
    using (var bitmap = new RenderTargetBitmap(new PixelSize(3, 3))) bitmap.Save(away);
    var team = new CampaignDocument("Logo From = identity\nLogo Skin = Team_Logo/Custom_R\nExport Logo Mode = embedded\nExport Logo ID = identity-id\nExport Logo SHA256 = identity-hash\nExport Logo PNG = identity-png\n");
    var inherited = AppearanceContract.Resolve(new CampaignDocument("Override Team Settings = no"), team, new CampaignDocument(""), "skater");
    Check(JerseyLogoOptions.Resolve(new CampaignDocument("Override Team Settings = no"), team, false).Source == "identity"
        && JerseyLogoOptions.Resolve(new CampaignDocument("Override Team Settings = no"), team, true).Source == "identity", "MissingKitsInheritIdentityIndependently");
    Identity(AppearanceContract.Resolve(new CampaignDocument("Override Team Settings = no"), WithoutKits(team), new CampaignDocument(""), "skater"), inherited,
        "MissingKitsInheritIdentityIndependently");

    team.Set("Jersey Logo From", home); team.Set("Jersey Logo Away From", away);
    var personal = new CampaignDocument("Override Team Settings = yes\nLogo From = personal\n");
    Check(JerseyLogoOptions.Resolve(personal, team, false).Source == "personal"
        && JerseyLogoOptions.Resolve(personal, team, true).Source == "personal", "PersonalLogoWins");
    Identity(AppearanceContract.Resolve(personal, WithoutKits(team), new CampaignDocument(""), "skater"),
        AppearanceContract.Resolve(personal, team, new CampaignDocument(""), "skater"), "PersonalLogoWins");
    var stray = new CampaignDocument("Override Team Settings = no\nJersey Logo From = wrong.png\nExport Jersey Logo PNG = stale\n");
    var strayResolved = AppearanceContract.Resolve(stray, team, new CampaignDocument(""), "skater");
    Check(strayResolved.Get("Jersey Logo From") == home && strayResolved.Get("Export Jersey Logo PNG") == "",
        "Team choice replaces stray player kit and stale payload");
    Check(AppearanceContract.Resolve(new CampaignDocument("Jersey Logo From = stray.png\n"), new CampaignDocument(""),
        new CampaignDocument(""), "skater").Get("Jersey Logo From") == "", "Raw player kit field is not a personal override");

    var mixed = new CampaignDocument("Override Team Settings = no\nBody = Body/Customization/Customization_colors\nBody Away = Body/HockeyFC/HockeyFC\n");
    var mixedTeam = new CampaignDocument(team.ToString()); mixedTeam.Set("Body Away", "Body/HockeyFC/HockeyFC");
    var effective = AppearanceContract.Resolve(mixed, mixedTeam, new CampaignDocument(""), "skater");
    Check(effective.Get("Jersey Logo From") == home && effective.Get("Jersey Logo Away From") == "", "CompatibleHomeSurvivesPremadeAway");
    Identity(AppearanceContract.Resolve(mixed, WithoutKits(mixedTeam), new CampaignDocument(""), "skater"), effective,
        "CompatibleHomeSurvivesPremadeAway");

    var goalieDefaults = new CampaignDocument("Skin = Body/Figure_Skaters\n");
    var goalie = AppearanceContract.Resolve(new CampaignDocument("Override Team Settings = no"), team, goalieDefaults, "goalie");
    Check(goalie.Get("Jersey Logo From") == "" && AppearanceContract.Resolve(mixed, team, new CampaignDocument(""), "skater").Get("Jersey Logo From") == home,
        "SkaterCompatibilityDoesNotClearGoalieChoice");
    Identity(AppearanceContract.Resolve(new CampaignDocument("Override Team Settings = no"), WithoutKits(team), goalieDefaults, "goalie"), goalie,
        "SkaterCompatibilityDoesNotClearGoalieChoice");

    var source = new CampaignDocument("Jersey Logo From = \n");
    var target = new CampaignDocument(team.ToString());
    target.Set("Export Jersey Logo Mode", "embedded"); target.Set("Export Jersey Logo PNG", "stale");
    JerseyLogoOptions.Copy(source, false, target, false);
    Check(target.Get("Jersey Logo From") == "" && target.Get("Export Jersey Logo Mode") == "" && target.Get("Export Jersey Logo PNG") == ""
        && target.Get("Jersey Logo Away From") == away, "CopyInheritedClearsTargetPayload");
    Identity(team, target, "CopyInheritedClearsTargetPayload");

    var art = new CampaignDocument(team.ToString());
    var independent = LogoExport.Snapshot(new CampaignDocument("Jersey Logo Away From = " + away), root, true);
    string awayPayload = independent.Get("Export Jersey Logo Away PNG");
    foreach (string homeChoice in new[] { "", "none" })
    {
        independent.Set("Jersey Logo From", homeChoice);
        var normalized = LogoExport.Snapshot(independent, root, false);
        foreach (string suffix in new[] { "Mode", "ID", "SHA256", "PNG" })
            Check(normalized.Get("Export Jersey Logo Away " + suffix) == independent.Get("Export Jersey Logo Away " + suffix),
                "Home normalization preserves complete Away " + suffix + " for " + homeChoice);
        File.Delete(away);
        try { Check(LogoExport.Snapshot(normalized, root, true).Get("Export Jersey Logo Away PNG") == awayPayload, "Away embedded bytes survive absent source and Home " + homeChoice); }
        finally { File.WriteAllBytes(away, Convert.FromBase64String(awayPayload)); }
        File.WriteAllBytes(away, File.ReadAllBytes(home));
        try { Check(LogoExport.Snapshot(normalized, root, true).Get("Export Jersey Logo Away PNG") == awayPayload, "Away embedded bytes beat replaced source and Home " + homeChoice); }
        finally { File.WriteAllBytes(away, Convert.FromBase64String(awayPayload)); }
    }
    var copied = new CampaignDocument("");
    independent.Set("Jersey Logo From", home);
    JerseyLogoOptions.Copy(independent, false, copied, true);
    Check(!copied.Fields.Keys.Any(k => k.Contains("Away Away", StringComparison.OrdinalIgnoreCase)), "Home copy never copies Away metadata as doubled keys");
    var frozen = new CampaignDocument(independent.ToString());
    frozen.Set("Override Team Settings", "yes"); frozen.Set("Body", "Body/HockeyFC/HockeyFC");
    frozen.Set("Body Away", "Body/Customization/Customization_colors");
    var frozenResolved = AppearanceContract.Resolve(frozen, new CampaignDocument(""), new CampaignDocument(""), "skater");
    Check(frozenResolved.Get("Export Jersey Logo Away PNG") == awayPayload, "Premade Home preserves compatible frozen Away embedded payload");
    art = LogoExport.Snapshot(art, root, false);
    art = LogoExport.Snapshot(art, root, true);
    art.Set("Export Jersey Logo Away SHA256", "bad");
    bool rejected = false;
    try { LogoExport.Snapshot(art, root, true); } catch (ArgumentException) { rejected = true; }
    Check(rejected && art.Get("Export Jersey Logo PNG") != art.Get("Export Jersey Logo Away PNG"), "CorruptAwayDoesNotMasqueradeAsHome");
    Identity(team, art, "CorruptAwayDoesNotMasqueradeAsHome");
    foreach (bool slotAway in new[] { false, true })
    foreach (bool filePresent in new[] { true, false })
    {
        string file = slotAway ? away : home;
        byte[] original = File.ReadAllBytes(file);
        var incomplete = new CampaignDocument(team.ToString());
        string prefix = JerseyLogoOptions.Prefix(slotAway), field = JerseyLogoOptions.Field(slotAway);
        incomplete.Set(prefix + " Mode", "embedded");
        incomplete.Set(prefix + " ID", "custom_t2t_missing.png");
        incomplete.Set(prefix + " SHA256", "missing");
        incomplete.Remove(prefix + " PNG");
        if (!filePresent) File.Delete(file);
        string message = "";
        try { LogoExport.Snapshot(incomplete, root, slotAway); }
        catch (ArgumentException ex) { message = ex.Message; }
        finally { if (!filePresent) File.WriteAllBytes(file, original); }
        Check(message.Contains(field, StringComparison.OrdinalIgnoreCase)
            && message.Contains("embedded", StringComparison.OrdinalIgnoreCase),
            $"Incomplete embedded {(slotAway ? "away" : "home")} fails with source file {(filePresent ? "present" : "absent")}");
        Identity(team, incomplete, "Incomplete embedded metadata does not alter identity");
    }
    string gameSave = Path.Combine(root, "fake-game");
    Environment.SetEnvironmentVariable("T2T_GAME_SAVE_PATH", gameSave);
    string campaign = Path.Combine(root, "campaigns", "Portable");
    string teamFile = Path.Combine(campaign, "teams", "Portable", "team.txt");
    string playerFile = Path.Combine(campaign, "teams", "Portable", "players", "Center.txt");
    Directory.CreateDirectory(Path.GetDirectoryName(playerFile)!);
    File.WriteAllText(Path.Combine(campaign, "campaign.txt"), "Campaign Name = Portable\n");
    string first = Path.Combine(root, "first", "crest.png"), second = Path.Combine(root, "second", "crest.png");
    Directory.CreateDirectory(Path.GetDirectoryName(first)!); Directory.CreateDirectory(Path.GetDirectoryName(second)!);
    File.Copy(home, first); File.Copy(away, second);
    var portable = new CampaignDocument("Team Name = Portable\nLogo From = none\n");
    portable.Set(JerseyLogoOptions.Field(false), first); portable.Set(JerseyLogoOptions.Field(true), second); portable.Save(teamFile);
    const string personalText = "Name = Center\nPosition = Center\nOverride Team Settings = yes\nLogo From = personal-logo\nBody = Body/Customization/Customization_colors\n";
    File.WriteAllText(playerFile, personalText);
    var sharing = typeof(App).Assembly.GetType("CampaignCreator.CampaignSharing")!;
    var zipMethod = sharing.GetMethod("Zip", BindingFlags.Static | BindingFlags.NonPublic)!;
    string zipPath = Path.Combine(root, "portable.zip");
    zipMethod.Invoke(null, [campaign, zipPath, root]);
    using (var zip = ZipFile.OpenRead(zipPath))
    {
        var logos = zip.Entries.Where(e => e.FullName.Contains("/_logos/")).ToArray();
        Check(logos.Length == 2 && logos.Select(e => e.Name).Distinct().Count() == 2, "Portable kits stage distinct same-name PNGs");
        var entry = zip.GetEntry("Portable/teams/Portable/team.txt")!;
        using var reader = new StreamReader(entry.Open());
        var staged = new CampaignDocument(reader.ReadToEnd());
        Check(staged.Get(JerseyLogoOptions.Field(false)) != staged.Get(JerseyLogoOptions.Field(true))
            && !Path.IsPathRooted(staged.Get(JerseyLogoOptions.Field(false))), "Portable kit references are rewritten in package");
    }
    Check(File.ReadAllText(teamFile).Contains(first) && File.ReadAllText(teamFile).Contains(second)
        && File.ReadAllText(playerFile) == personalText, "Sharing leaves source team and independent player untouched");
    string imported = Path.Combine(root, "imported");
    sharing.GetMethod("Install", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [zipPath, imported, "Portable"]);
    var installed = new CampaignDocument(File.ReadAllText(Path.Combine(imported, "campaigns", "Portable", "teams", "Portable", "team.txt")));
    Check(LogoFiles.Resolve(imported, Path.Combine(gameSave, "CustomLogos"), installed.Get(JerseyLogoOptions.Field(false))) is { } homeFile
        && File.ReadAllBytes(homeFile).SequenceEqual(File.ReadAllBytes(first))
        && LogoFiles.Resolve(imported, Path.Combine(gameSave, "CustomLogos"), installed.Get(JerseyLogoOptions.Field(true))) is { } awayFile
        && File.ReadAllBytes(awayFile).SequenceEqual(File.ReadAllBytes(second)), "Portable install resolves each kit to its own PNG");
    string campaignLogos = Path.Combine(campaign, "logos"); Directory.CreateDirectory(campaignLogos);
    Directory.CreateDirectory(Path.Combine(root, "logos"));
    File.Copy(home, Path.Combine(campaignLogos, "relative.png"));
    File.Copy(away, Path.Combine(root, "logos", "relative.png"));
    Directory.CreateDirectory(Path.Combine(root, "_team_logos"));
    File.Copy(away, LogoFiles.NativeFile(root, "relative"));
    portable.Set("Logo From", "relative"); portable.Set(JerseyLogoOptions.Field(false), "logos/relative.png");
    portable.Set(JerseyLogoOptions.Field(true), "relative"); portable.Save(teamFile);
    foreach (string position in new[] { "Left Wing", "Center", "Right Wing", "Left Defense", "Right Defense", "Goalie" })
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(playerFile)!, position + ".txt"), "Name = " + position + "\nPosition = " + position + "\nFace = Faces/Toronto/Mathieu\n");
    var studio = typeof(App).Assembly;
    object game = Activator.CreateInstance(studio.GetType("CampaignCreator.GameData")!, root)!;
    object package = studio.GetType("CampaignCreator.ExportTranslation")!.GetMethod("BuildTeam", BindingFlags.NonPublic | BindingFlags.Static)!
        .Invoke(null, [portable, teamFile, game, root, null])!;
    var overrides = (Dictionary<string, string>)package.GetType().GetProperty("Overrides")!.GetValue(package)!;
    var exported = new CampaignDocument(overrides.Single(p => p.Key.EndsWith(".team.txt")).Value);
    string campaignPixels = Convert.ToBase64String(File.ReadAllBytes(home));
    Check(exported.Get("Export Logo PNG") == campaignPixels && exported.Get("Export Jersey Logo PNG") == campaignPixels
        && exported.Get("Export Jersey Logo Away PNG") == campaignPixels, "Creator team export resolves owning campaign before same-name global image");
    using (var preview = LogoExport.Preview(root, portable, true, campaign))
        Check(preview?.PixelSize.Width == 2, "Creator preview resolves campaign before same-name native catalog image");
    File.Delete(Path.Combine(root, "logos", "relative.png")); File.Delete(LogoFiles.NativeFile(root, "relative"));
    package = studio.GetType("CampaignCreator.ExportTranslation")!.GetMethod("BuildTeam", BindingFlags.NonPublic | BindingFlags.Static)!
        .Invoke(null, [portable, teamFile, game, root, null])!;
    overrides = (Dictionary<string, string>)package.GetType().GetProperty("Overrides")!.GetValue(package)!;
    exported = new CampaignDocument(overrides.Single(p => p.Key.EndsWith(".team.txt")).Value);
    Check(exported.Get("Export Jersey Logo PNG") == campaignPixels && exported.Get("Export Jersey Logo Away PNG") == campaignPixels,
        "Creator export finds campaign-only images without any global donor");
    zipMethod.Invoke(null, [campaign, zipPath, root]);
    using (var zip = ZipFile.OpenRead(zipPath))
    {
        using var reader = new StreamReader(zip.GetEntry("Portable/teams/Portable/team.txt")!.Open());
        var packaged = new CampaignDocument(reader.ReadToEnd());
        Check(packaged.Get("Logo From") == packaged.Get(JerseyLogoOptions.Field(false))
            && packaged.Get("Logo From") == packaged.Get(JerseyLogoOptions.Field(true)), "Campaign sharing uses same campaign pixels for relative and bare-name logo choices");
    }
    sharing.GetMethod("Install", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [zipPath, imported, "Campaign Relative"]);
    var relativeInstalled = new CampaignDocument(File.ReadAllText(Path.Combine(imported, "campaigns", "Campaign Relative", "teams", "Portable", "team.txt")));
    Check(LogoFiles.Resolve(imported, Path.Combine(gameSave, "CustomLogos"), relativeInstalled.Get(JerseyLogoOptions.Field(true))) is { } relativeImage
        && File.ReadAllBytes(relativeImage).SequenceEqual(File.ReadAllBytes(home)), "Campaign-relative kit survives portable install without global source paths");
    Console.WriteLine($"PASS {checks} jersey logo assertions");
}
finally { Directory.Delete(root, true); }
