using Avalonia;
using Avalonia.Headless;
using System.Text.Json;
using CampaignCore;
using CampaignCreator;

internal static class ExportChecks
{
    internal static void NativeFixtures(string root, string output)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(output);
        Avalonia.AppBuilder.Configure<App>().UseSkia().UseHeadless(new Avalonia.Headless.AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        string logo = Path.Combine(output, "native-test-logo.png");
        var crest = new Avalonia.Controls.Border { Width = 32, Height = 32, Background = Avalonia.Media.Brushes.Magenta, BorderBrush = Avalonia.Media.Brushes.White, BorderThickness = new Avalonia.Thickness(5) };
        crest.Measure(new Avalonia.Size(32,32)); crest.Arrange(new Avalonia.Rect(0,0,32,32));
        using (var image = new Avalonia.Media.Imaging.RenderTargetBitmap(new Avalonia.PixelSize(32,32))) { image.Render(crest); image.Save(logo); }
        var game = new GameData(root);
        foreach (string role in new[] { "skater", "goalie" }) foreach (bool away in new[] { false, true })
        {
            string id = Guid.NewGuid().ToString("N");
            var doc = new CampaignDocument("Name = Export Parity\nNumber = 17\nLeft Handed = yes\nJersey Color = 20,80,120\nJersey Away Color = 230,160,20\nNumber Tertiary Color = 255,255,255\nNumber Away Tertiary Color = 20,20,20\n");
            if (role == "skater") doc.Set("Face",game.Assets.Values("skater", "Face").First());
            doc.Set("Logo From",logo);
            doc.Set("Talents", game.Talents.Keys.First());
            var recipe = PlayerExport.TranslationSidecar(doc, game, role, PlayerExport.Convert(doc,game,role,id));
            var lines = new List<string> { "version\t10", "request_id\t" + id, "is_goalie\t" + (role=="goalie"?"1":"0"), "focus_field\t" + (away?"Body Away":"Body") };
            lines.AddRange(recipe.Fields.Select(p=>"player\t"+p.Key+"\t"+p.Value));
            string file = Path.Combine(output, role + (away?"-away":"-home")+".tsv");
            File.WriteAllLines(file,lines);
            Console.WriteLine(file + " " + id);
        }
    }

    internal static void Run(string output)
    {
        string root = Path.Combine(Path.GetFullPath(output), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Avalonia.AppBuilder.Configure<App>().UseSkia().UseHeadless(new Avalonia.Headless.AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        int passed = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); Console.WriteLine("PASS " + label); passed++; }
        void Reject(Action action, string label) { try { action(); } catch (ArgumentException) { Check(true, label); return; } throw new Exception(label); }
        var game = new GameData(root);
        game.Talents["Skater perk"] = "talent-skater"; game.Talents["Goalie perk"] = "talent-goalie";
        game.Abilities["Dash"] = "ability-dash"; game.Relics["Steel Mitts LVL2"] = "relic-level2";
        game.Assets.Add("skater", "Face", "Faces/Test/Player");
        game.Assets.Add("skater", "Body", "Body/Customization/Customization_colors");
        game.Assets.Add("skater", "Body Away", "Body/Customization/Customization_colors");
        game.Assets.Add("skater", "Stick", "Sticks/Black");
        foreach (string key in AppearanceContract.Keys("skater"))
        {
            string skin = AppearanceContract.DefaultSkin("skater", key);
            if (skin.Length > 0) game.Assets.Add("skater", key, skin);
        }
        var doc = new CampaignDocument("Name = Export Test\nFace = Faces/Test/Player\nTalents = Skater perk\nGoalie Talents = Goalie perk\nAbility = ability-dash\n");
        var model = PlayerExport.Convert(doc, game, "skater", "test");
        Check(((string[])model["talentIds"]).SequenceEqual(new[] { "talent-skater", "talent-goalie" }), "Cross-role talents preserved by ID");
        Check((string)model["abilityId"] == "ability-dash", "Already-resolved ability IDs accepted");
        doc.Set("Ability", "none"); Check((string)PlayerExport.Convert(doc, game, "skater", "test")["abilityId"] == "", "Explicit no ability preserved");
        doc.Set("Ability", "Dash"); doc.Set("Goalie Talents", "Missing");
        Check(((string[])PlayerExport.Convert(doc, game, "skater", "test")["talentIds"]).SequenceEqual(new[] { "talent-skater" }), "Unknown cross-role talent omitted while valid talent survives");
        doc.Set("Goalie Talents", "Goalie perk");
        var team = new CampaignDocument("Team Name = Export Team\nJersey Primary = 10,20,30\nAway Primary = 40,50,60\nHelmet Color = 70,80,90\nHelmet Away Color = 100,110,120\nTeam Relics = Steel Mitts:2\n");
        doc.Set("Jersey Color", "1,2,3");
        doc.Set("Override Team Settings", "no");
        var inherited = ExportTranslation.Player(doc, team, null, "skater");
        Check(inherited.Get("Export Own Equipment") == "no", "Team export records inherited equipment before freezing player snapshot");
        Check(inherited.Get("Jersey Color") == "10,20,30" && inherited.Get("Jersey Away Color") == "40,50,60", "Team inheritance translates home and away fields");
        doc.Set("Override Team Settings", "yes");
        Check(ExportTranslation.Player(doc, team, null, "skater").Get("Jersey Color") == "10,20,30", "Equipment-style overrides retain the team's jersey palette");
        doc.Set("Stick Color", "1,2,3"); doc.Set("Skates Color", "4,5,6");
        var personal = ExportTranslation.Player(doc, team, null, "skater");
        Check(personal.Get("Export Own Equipment") == "yes", "Team export records independent equipment before freezing player snapshot");
        Check(personal.Get("Stick Color") == "1,2,3" && personal.Get("Stick Away Color") == "1,2,3"
            && personal.Get("Skates Color") == "4,5,6" && personal.Get("Skates Away Color") == "4,5,6",
            "Player-owned stick and skate colors survive export for both kits");
        var premadePlayer = new CampaignDocument(doc.ToString());
        premadePlayer.Set("Body", "Body/Tycoons/Tycoons"); premadePlayer.Set("Body Away", "Body/Golfers/Golfers");
        var ownKits = ExportTranslation.Player(premadePlayer, team, null, "skater");
        Check(ownKits.Get("Body") == premadePlayer.Get("Body") && ownKits.Get("Body Away") == premadePlayer.Get("Body Away")
            && ownKits.Get("Jersey Color") == "10,20,30", "Distinct authored premade kits survive export while retaining team-owned palette fields");
        var sidecar = PlayerExport.TranslationSidecar(inherited, game, "skater", model);
        Check(sidecar.Get("Export Own Equipment") == "no", "Inherited equipment remains inherited in the runtime sidecar");
        string roundTripFile = Path.Combine(root, "exported-player.txt");
        sidecar.Save(roundTripFile);
        var importedSnapshot = ContentImport.ReadDocument(roundTripFile);
        Check(ExportTranslation.Player(importedSnapshot, team, null, "skater").Get("Export Own Equipment") == "no",
            "Importing and re-exporting a baked inherited snapshot preserves equipment ownership");
        var upgradedSnapshot = new CampaignDocument(importedSnapshot.ToString());
        new UpgradeCleanup().Clean(upgradedSnapshot, "player", "skater");
        Check(ExportTranslation.Player(upgradedSnapshot, team, null, "skater").Get("Export Own Equipment") == "no",
            "Upgrade cleanup preserves inherited equipment ownership in an exported snapshot");
        Check(sidecar.Get("Export Talent IDs") == "talent-skater,talent-goalie" && sidecar.Get("Export Ability ID") == "ability-dash",
            "Player translation retains both talent roles and the native ability identity");
        var personalSidecar = PlayerExport.TranslationSidecar(personal, game, "skater", model);
        Check(personalSidecar.Get("Stick Color") == "1,2,3" && personalSidecar.Get("Stick Away Color") == "1,2,3"
            && personalSidecar.Get("Skates Color") == "4,5,6" && personalSidecar.Get("Skates Away Color") == "4,5,6",
            "Translation sidecar preserves the player-owned colors that the runtime applies in both kits");
        var goalie = new CampaignDocument("Name = Goalie Test\nTalents = Skater perk\nPass Read = 77\n");
        var gm = PlayerExport.Convert(goalie, game, "goalie", "goalie");
        Check((string)gm["headSkin"] == "" && (string)gm["helmetSkin"] == "Helmet/Helmet_Customization_colors" && (string)gm["skin"] == "Body/Customization_colors", "Goalie defaults use actual goalie assets");
        Check(PlayerExport.TranslationSidecar(goalie, game, "goalie", gm).Get("Pass Read") == "77", "Goalie pass-read survives model limitation");
        string teamFile = Path.Combine(root, "team.txt"), players = Path.Combine(root, "players"); Directory.CreateDirectory(players); team.Save(teamFile);
        foreach (string pos in new[] { "Left Wing", "Right Wing", "Center", "Left Defense", "Right Defense", "Goalie" })
        {
            var player = new CampaignDocument(pos == "Goalie" ? goalie.ToString() : doc.ToString());
            player.Set("Position", pos); player.Save(Path.Combine(players, pos + ".txt"));
        }
        team.Set("Goal Sound", "test.wav");
        team.Set("Goal Sound Data", Convert.ToBase64String(new byte[] { 0, 1, 2, 3, 4 }));
        team.Set("Goal Sound Volume", "42");
        var package = ExportTranslation.BuildTeam(team, teamFile, game);
        Check(package.Overrides.Values.All(text => !text.Contains("Jersey Logo", StringComparison.OrdinalIgnoreCase)),
            "Legacy no-new-field export keeps jersey metadata absent");
        string jerseyHome = Path.Combine(root, "jersey-home.png"), jerseyAway = Path.Combine(root, "jersey-away.png");
        using (var image = new Avalonia.Media.Imaging.RenderTargetBitmap(new Avalonia.PixelSize(2, 2))) image.Save(jerseyHome);
        using (var image = new Avalonia.Media.Imaging.RenderTargetBitmap(new Avalonia.PixelSize(3, 3))) image.Save(jerseyAway);
        var kitTeam = new CampaignDocument(team.ToString());
        kitTeam.Set("Jersey Logo From", jerseyHome); kitTeam.Set("Jersey Logo Away From", jerseyAway);
        var kitPackage = ExportTranslation.BuildTeam(kitTeam, teamFile, game);
        var kitTeamSidecar = new CampaignDocument(kitPackage.Overrides[kitPackage.TeamId + ".team.txt"]);
        string kitHomeHash = kitTeamSidecar.Get("Export Jersey Logo SHA256"), kitAwayHash = kitTeamSidecar.Get("Export Jersey Logo Away SHA256");
        Check(kitHomeHash.Length == 64 && kitAwayHash.Length == 64 && kitHomeHash != kitAwayHash,
            "Team export embeds separate home and away crest PNGs");
        var kitPlayers = kitPackage.Overrides.Where(pair => !pair.Key.EndsWith(".team.txt")).Select(pair => new CampaignDocument(pair.Value)).ToArray();
        Check(kitPlayers.All(player => player.Get("Export Jersey Logo SHA256") == kitHomeHash && player.Get("Export Jersey Logo Away SHA256") == kitAwayHash)
            && kitPlayers.Count(player => player.Get(PlayerExport.OwnEquipmentKey) == "yes") == 5
            && kitPlayers.Count(player => player.Get(PlayerExport.OwnEquipmentKey) == "no") == 1,
            "Every team-dependent player exports both kit payloads without changing equipment intent");
        kitTeam.Set("Jersey Logo Away From", "none");
        var nonePackage = ExportTranslation.BuildTeam(kitTeam, teamFile, game);
        Check(nonePackage.Overrides.Where(pair => !pair.Key.EndsWith(".team.txt")).Select(pair => new CampaignDocument(pair.Value))
            .All(player => player.Get("Export Jersey Logo Away Mode") == "none" && player.Get("Export Jersey Logo Away PNG") == ""),
            "Explicit no away crest survives team-dependent export");
        kitTeam.Set("Jersey Logo Away From", "missing-away.png");
        var logoReport = new ExportReport();
        var missingKit = ExportTranslation.BuildTeam(kitTeam, teamFile, game, report: logoReport);
        Check(logoReport.Skipped.Any(entry => entry.Contains("Jersey Logo Away From = missing-away.png"))
            && new CampaignDocument(missingKit.Overrides[missingKit.TeamId + ".team.txt"]).Get("Export Jersey Logo Away Mode") == "",
            "Missing away image reports its slot and does not become explicit none");
        kitTeam.Set("Jersey Logo Away From", jerseyAway);
        var corruptKit = LogoExport.Snapshot(kitTeam, root, true); corruptKit.Set("Export Jersey Logo Away SHA256", "bad");
        Reject(() => ExportTranslation.BuildTeam(corruptKit, teamFile, game), "Corrupt away kit payload stops package before writes");
        var goalSidecar = new CampaignDocument(package.Overrides[package.TeamId + ".team.txt"]);
        Check(goalSidecar.Get("Goal Sound Data") == team.Get("Goal Sound Data") && goalSidecar.Get("Goal Sound Volume") == "42" && goalSidecar.Get("Goal Sound") == "test.wav", "Team goal audio and volume survive Play Now export");
        Check(package.Saves.Count == 7 && package.Overrides.Count == 7, "Team package contains all six players and translations");
        using var json = JsonDocument.Parse(package.Saves["CustomTeam-" + package.TeamId + ".json"]);
        Check(json.RootElement.GetProperty("relics")[0].GetString() == "relic-level2", "Upgraded relic translated exactly");
        float away = json.RootElement.GetProperty("awayColors").GetProperty("helmetScheme").GetProperty("primaryColor").GetProperty("r").GetSingle();
        Check(Math.Abs(away - 100f / 255) < .00001, "Away palette has away equipment values");
        string save = Path.Combine(root, "saves"), extra = Path.Combine(root, "overrides"); Directory.CreateDirectory(save);
        string exported = ExportTranslation.Write(package, save, extra);
        Check(File.Exists(exported) && Directory.GetFiles(save).Length == 7 && Directory.GetFiles(extra).Length == 7, "Package writes complete native save and translation set");
        var broken = new ExportTranslation.Package("broken", new() { ["first.json"] = "{}", ["missing/second.json"] = "{}" }, new() { ["temporary.txt"] = "x" });
        try { ExportTranslation.Write(broken, save, extra); throw new Exception("Expected IO failure"); } catch (DirectoryNotFoundException) { }
        Check(!File.Exists(Path.Combine(save, "first.json")) && !File.Exists(Path.Combine(extra, "temporary.txt")) && File.Exists(exported), "Write failure rolls back new files and preserves prior exports");
        File.WriteAllText(Path.Combine(root, "campaign.txt"), "Name = Export custom fixture");
        byte[] rewardPng;
        using (var icon = new Avalonia.Media.Imaging.RenderTargetBitmap(new Avalonia.PixelSize(8, 8))) {
            using var bytes = new MemoryStream(); icon.Save(bytes); rewardPng = bytes.ToArray();
        }
        var customTalent = new CustomRewardDefinition { Name = "Custom export skater", Trigger = "While owned", Amount = 13, IconPng = Convert.ToBase64String(rewardPng) };
        var customGoalie = new CustomRewardDefinition { Name = "Custom export goalie", Kind = CustomRewardDefinition.CustomGoalieTalents, Trigger = "While owned", Stat = "Goalie skill", Amount = 7, IconPng = customTalent.IconPng };
        var customRelic = new CustomRewardDefinition { Name = "Custom export relic", Kind = CustomRewardDefinition.CustomRelics, Trigger = "While owned", Target = CustomRewardEffects.Team, Stat = "Speed", Amount = 3, IconPng = customTalent.IconPng };
        foreach (var item in new[] { customTalent, customGoalie, customRelic }) item.Save(Path.Combine(root, CustomRewardDefinition.Folder));
        var staleLibrary = customTalent.Copy(); staleLibrary.Amount = 99; staleLibrary.Save(Path.Combine(root, "library", "rewards"));
        string leftFile = Path.Combine(players, "Left Wing.txt"), goalieFile = Path.Combine(players, "Goalie.txt");
        var leftPlayer = new CampaignDocument(File.ReadAllText(leftFile)); leftPlayer.Set("Talents", customTalent.Id); leftPlayer.Save(leftFile);
        var goaliePlayer = new CampaignDocument(File.ReadAllText(goalieFile)); goaliePlayer.Set("Goalie Talents", customGoalie.Id); goaliePlayer.Save(goalieFile);
        team.Set("Team Relics", customRelic.Id);
        game.LoadCustomRewards(teamFile);
        var wrongSlot = new CampaignDocument(leftPlayer.ToString()); wrongSlot.Set("Relics", customTalent.Id);
        Reject(() => PlayerExport.Convert(wrongSlot, game, "skater", "bad-slot"), "A custom talent cannot be silently exported from a relic field");
        wrongSlot = new CampaignDocument(leftPlayer.ToString()); wrongSlot.Set("Talents", customRelic.Name);
        Reject(() => PlayerExport.Convert(wrongSlot, game, "skater", "bad-slot"), "A custom relic name cannot be silently dropped from a talent field");
        wrongSlot.Set("Talents", customGoalie.Id);
        Reject(() => PlayerExport.Convert(wrongSlot, game, "skater", "wrong-role"), "Goalie custom talents cannot be assigned to exported forwards");
        wrongSlot.Set("Talents", customTalent.Id);
        Reject(() => PlayerExport.Convert(wrongSlot, game, "goalie", "wrong-role"), "Skater custom talents cannot be assigned to exported goalies");
        var inheritedCustom = new CampaignDocument("Team Talents = " + customTalent.Id + "," + customGoalie.Id);
        var forwardInherited = ExportTranslation.Player(new CampaignDocument("Name = Forward"), inheritedCustom, null, "skater", game);
        var goalieInherited = ExportTranslation.Player(new CampaignDocument("Name = Goalie"), inheritedCustom, null, "goalie", game);
        Check(forwardInherited.Get("Talents") == customTalent.Id && goalieInherited.Get("Talents") == customGoalie.Id,
            "Team custom talents are inherited only by their matching player role");
        var customPackage = ExportTranslation.BuildTeam(team, teamFile, game);
        var teamRecipe = new CampaignDocument(customPackage.Overrides[customPackage.TeamId + ".team.txt"]);
        var bundled = CustomRewardExport.Read(teamRecipe).ToArray();
        Check(bundled.Length == 3 && bundled.All(d => d.IconPng == customTalent.IconPng), "Play Now team package embeds skater, goalie and relic rules with exact icons");
        Check(bundled.Single(d => d.Name == customTalent.Name).Amount == 13 && bundled.All(d => d.Id != customTalent.Id && d.Id != customGoalie.Id && d.Id != customRelic.Id), "Export freezes the campaign copy with independent identities instead of the newer library version");
        var exportedIds = bundled.Select(d => d.Id).ToHashSet();
        Check(customPackage.Overrides.Where(p => !p.Key.EndsWith(".team.txt")).Select(p => new CampaignDocument(p.Value)).SelectMany(d => d.Get("Export Talent IDs").Split(',')).Count(exportedIds.Contains) == 2,
            "Exported player loadouts refer to their bundled custom identities");
        ExportTranslation.Write(customPackage, save, extra, root);
        string snapshotTeam = Path.Combine(root, "library", "Exported Teams", customPackage.TeamId, "team.txt");
        foreach (var item in new[] { customTalent, customGoalie, customRelic }) File.Delete(Path.Combine(root, CustomRewardDefinition.Folder, item.Id + CustomRewardDefinition.Extension));
        var standalone = new GameData(root); standalone.LoadCustomRewards(snapshotTeam);
        Check(exportedIds.All(standalone.CustomRewards.ContainsKey), "Exported library team reopens all custom definitions after campaign source removal");
        var repeated = ExportTranslation.BuildTeam(teamRecipe, snapshotTeam, game);
        var repeatedRules = CustomRewardExport.Read(new CampaignDocument(repeated.Overrides[repeated.TeamId + ".team.txt"])).ToArray();
        Check(repeatedRules.Length == 3 && repeatedRules.All(d => !exportedIds.Contains(d.Id)), "Re-export carries only the newly referenced rules without retaining stale snapshots");
        foreach (var item in new[] { customTalent, customGoalie, customRelic }) item.Save(Path.Combine(root, CustomRewardDefinition.Folder));
        var invalidCustom = customTalent.Copy(); invalidCustom.Trigger = "Match won"; invalidCustom.Save(Path.Combine(root, CustomRewardDefinition.Folder));
        int previousFiles = Directory.GetFiles(save).Length;
        string rejection = "";
        try { ExportTranslation.BuildTeam(team, teamFile, game); } catch (ArgumentException ex) { rejection = ex.Message; }
        Check(rejection.Contains(customTalent.Name) && rejection.Contains("requires campaign progress"), "Campaign-only custom talent blocks team packaging for its incompatible rule before writing");
        Check(Directory.GetFiles(save).Length == previousFiles, "Rejected custom export leaves previous game exports unchanged");
        // Return the original fixture to native loadouts for the remaining checks.
        foreach (string pos in new[] { "Left Wing", "Right Wing", "Center", "Left Defense", "Right Defense", "Goalie" }) {
            var originalPlayer = new CampaignDocument(pos == "Goalie" ? goalie.ToString() : doc.ToString()); originalPlayer.Set("Position", pos); originalPlayer.Save(Path.Combine(players, pos + ".txt"));
        }
        team.Set("Team Relics", "Unknown"); Check(!string.Join("", ExportTranslation.BuildTeam(team, teamFile, game).Overrides.Values).Contains("Unknown"), "Unavailable relic omitted from exported team");
        team.Set("Team Relics", "Steel Mitts LVL2");
        doc.Save(Path.Combine(players, "Extra.txt")); Reject(() => ExportTranslation.BuildTeam(team, teamFile, game), "Unrepresentable roster fails explicitly");
        doc.Set("Number", "17"); doc.Set("Left Handed", "yes");
        Check((string)PlayerExport.Convert(doc,game,"skater","x")["numberSkin"] == "Numbers/Number_17LH", "Native number skin follows number and handedness");
        team.Set("Number Color Home", "7,8,9"); team.Set("Number Color Away", "10,11,12");
        var effective = AppearanceContract.Resolve(new CampaignDocument("Override Team Settings = no"),team,new CampaignDocument(""),"skater");
        Check(AppearanceContract.Hex(effective,"numbers2",false)=="#070809" && AppearanceContract.Hex(effective,"numbers2",true)=="#0A0B0C", "Editor and export share independent number fill channels");
        Check(effective.Get("Helmet Away")==AppearanceContract.DefaultSkin("skater", "Helmet Away") && effective.Get("Gloves").Contains("Customization"), "Default away equipment is explicit");
        using (var image = new Avalonia.Media.Imaging.RenderTargetBitmap(new Avalonia.PixelSize(8,8))) image.Save(Path.Combine(root,"crest.png"));
        var logoDoc = new CampaignDocument(doc.ToString()); logoDoc.Set("Logo From", "crest.png");
        var embedded = LogoExport.Snapshot(logoDoc,root);
        byte[] png = File.ReadAllBytes(Path.Combine(root,"crest.png"));
        Check(Convert.FromBase64String(embedded.Get("Export Logo PNG")).SequenceEqual(png), "Logo embeds exact PNG bytes");
        File.Delete(Path.Combine(root,"crest.png"));
        var logoModel = PlayerExport.Convert(embedded,game,"skater","crest");
        var logoSidecar = PlayerExport.TranslationSidecar(embedded,game,"skater",logoModel);
        Check(logoSidecar.Get("Export Logo PNG")==embedded.Get("Export Logo PNG") && (string)logoModel["logoSkin"]=="Team_Logo/Custom_L", "Embedded logo survives source deletion and preserves handedness");
        using var recipe = JsonDocument.Parse(logoSidecar.Get("Export Model"));
        Check(recipe.RootElement.GetProperty("headSkin").GetString()=="Faces/Test/Player" && logoSidecar.Get("Export Version")=="3", "Versioned recipe contains native identity and appearance");
        Reject(()=>LogoExport.Snapshot(logoDoc,root), "Missing logo blocks export instead of substituting another crest");
        foreach (string role in new[] { "skater", "goalie" })
        {
            string key = role == "goalie" ? "Skin" : "Body";
            string premade = role == "goalie" ? "Body/Figure_Skaters" : "Body/HockeyFC/HockeyFC";
            game.Assets.Add(role, key, premade); game.Assets.Add(role, key + " Away", premade);
            var preset = new CampaignDocument(embedded.ToString());
            preset.Set(key, premade); preset.Set("Logo Skin", "Team_Logo/Custom_R");
            var presetModel = PlayerExport.Convert(preset, game, role, "premade");
            var presetSidecar = PlayerExport.TranslationSidecar(preset, game, role, presetModel);
            Check((string)presetModel["logoSkin"] == "" && presetSidecar.Get("Logo From") == embedded.Get("Logo From")
                && presetSidecar.Get("Export Logo PNG") == embedded.Get("Export Logo PNG"), role + " premade home hides crest while identity payload survives");
        }
        embedded.Set("Export Logo SHA256", "bad");
        Reject(()=>LogoExport.Snapshot(embedded,root), "Corrupt embedded logo rejected");
        var snapshotFields = doc.Fields; snapshotFields["Name"] = "different";
        doc.Set("Number", "18");
        Check(doc.Get("Name") != "different" && doc.Get("Number") == "18", "Document cache preserves snapshot isolation and invalidates on edits");
        Console.WriteLine($"{passed} export checks passed. Fixtures: {root}");
    }
}
