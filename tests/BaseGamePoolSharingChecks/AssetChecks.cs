using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CampaignCore;
using CampaignCreator;
using SkiaSharp;

internal static class AssetChecks
{
    private static readonly string[] Directions = ["S", "N", "W", "NE", "SW", "portrait"];

    // Call after Avalonia platform setup: logo validation decodes the real PNGs.
    internal static void Run(string sandbox, Action<bool, string> check)
    {
        string root = Path.Combine(sandbox, "asset-integrity");
        Directory.CreateDirectory(root);
        string? previousSavePath = Environment.GetEnvironmentVariable("T2T_GAME_SAVE_PATH");
        Environment.SetEnvironmentVariable("T2T_GAME_SAVE_PATH", Path.Combine(root, "game-save"));
        var failures = new List<Exception>();
        void RunCase(string name, Action action)
        {
            try { action(); }
            catch (Exception ex) { failures.Add(new Exception(name + ": " + ex.Message, ex)); }
        }
        try
        {
            RunCase(nameof(PortableAssetsSurviveHeadCollisions), () => PortableAssetsSurviveHeadCollisions(root, check));
            RunCase(nameof(OnlyReferencedTalentsTravel), () => OnlyReferencedTalentsTravel(root, check));
            RunCase(nameof(CorruptedBackupCannotReplaceCurrentPools), () => CorruptedBackupCannotReplaceCurrentPools(root, check));
            RunCase(nameof(FailedReplacementRollsBackAddedAssets), () => FailedReplacementRollsBackAddedAssets(root, check));
            RunCase(nameof(UnsafeAssetArchivesPreserveCurrentData), () => UnsafeAssetArchivesPreserveCurrentData(root, check));
        }
        finally { Environment.SetEnvironmentVariable("T2T_GAME_SAVE_PATH", previousSavePath); }
        if (failures.Count > 0) throw new AggregateException(failures);
    }

    private static void PortableAssetsSurviveHeadCollisions(string sandbox, Action<bool, string> check)
    {
        string source = Fixture(sandbox, "asset-source"), destination = Fixture(sandbox, "asset-destination");
        string incomingHead = MakeHead(source, "Shared head", SKColors.OrangeRed);
        MakeHead(source, "Unreferenced private head", SKColors.Purple);
        string existingHead = MakeHead(destination, "Shared head", SKColors.CornflowerBlue);
        string incomingLogo = MakeLogo(source, "team.png", SKColors.Gold);
        string existingLogo = MakeLogo(destination, "team.png", SKColors.SlateBlue);
        string logoBefore = Hash(existingLogo), existingHeadBefore = Snapshot(existingHead);
        SetCandidate(source, "draft_pool", ("Face", "Faces/CustomHeads/Shared head"), ("Logo From", incomingLogo), (JerseyLogoOptions.Field(false), incomingLogo));
        SetCandidate(source, "free_agents", ("Face", "Faces/CustomHeads/Shared head_Helmet"));
        string sourceBefore = Snapshot(source), zip = Path.Combine(sandbox, "asset-roundtrip.zip");
        BaseGamePoolPackages.Export(source, zip, "Portable artwork");

        string sharedLogo;
        using (var archive = ZipFile.OpenRead(zip))
        {
            var candidate = new CampaignDocument(ReadText(archive, "base_game_pools/player_teams/draft_pool/candidate.txt"));
            sharedLogo = candidate.Get("Logo From");
            check(!Path.IsPathRooted(sharedLogo) && !sharedLogo.Contains('/') && candidate.Get(JerseyLogoOptions.Field(false)) == sharedLogo,
                "pool ZIP rewrites identity and jersey logos to one portable image reference");
            check(ReadBytes(archive, "base_game_pools/_logos/" + sharedLogo).SequenceEqual(File.ReadAllBytes(incomingLogo)),
                "pool ZIP carries the exact referenced logo PNG bytes");
            foreach (string direction in Directions)
                check(ReadBytes(archive, "base_game_pools/_heads/Shared head/" + direction + ".png").SequenceEqual(File.ReadAllBytes(Path.Combine(incomingHead, direction + ".png"))),
                    "pool ZIP carries genuine custom head artwork: " + direction);
            check(!archive.Entries.Any(e => e.FullName.Contains("Unreferenced private head", StringComparison.Ordinal) || e.FullName.EndsWith("private-note.txt", StringComparison.Ordinal)),
                "pool export excludes unrelated heads and private files beside referenced artwork");
        }
        BaseGamePoolPackages.Install(zip, destination);
        string importedSkin = Candidate(destination, "draft_pool").Get("Face");
        check(importedSkin.StartsWith("Faces/CustomHeads/", StringComparison.Ordinal) && importedSkin != "Faces/CustomHeads/Shared head",
            "conflicting imported head receives a distinct referenced identity");
        string importedName = importedSkin["Faces/CustomHeads/".Length..];
        string importedHead = Path.Combine(destination, "custom-heads", importedName);
        var definition = CustomHeadDefinition.Read(importedHead);
        check(definition.Name == importedName && Candidate(destination, "free_agents").Get("Face") == importedSkin + "_Helmet",
            "head collision rewrites both bare and helmet candidate references consistently");
        check(Directions.All(d => File.ReadAllBytes(Path.Combine(importedHead, d + ".png")).SequenceEqual(File.ReadAllBytes(Path.Combine(incomingHead, d + ".png")))),
            "collision-renamed installed head retains all six incoming images");
        check(Snapshot(existingHead) == existingHeadBefore && Hash(existingLogo) == logoBefore,
            "pool asset installation preserves same-named existing head and logo bytes");
        check(File.ReadAllBytes(Path.Combine(GameLogoFolder(), sharedLogo)).SequenceEqual(File.ReadAllBytes(incomingLogo)),
            "pool installation writes the referenced logo into the isolated game-save logo directory");
        check(Snapshot(source) == sourceBefore, "portable asset export and installation leave the source project unchanged");

        string secondZip = Path.Combine(sandbox, "asset-roundtrip-again.zip"), clean = Fixture(sandbox, "asset-second-destination");
        BaseGamePoolPackages.Export(destination, secondZip, "Imported artwork again");
        BaseGamePoolPackages.Install(secondZip, clean);
        check(Candidate(clean, "draft_pool").Get("Face") == importedSkin
            && File.ReadAllBytes(Path.Combine(clean, "custom-heads", importedName, "S.png")).SequenceEqual(File.ReadAllBytes(Path.Combine(incomingHead, "S.png"))),
            "a collision-renamed pool set can be exported and installed again with its incoming artwork");
    }

    private static void OnlyReferencedTalentsTravel(string sandbox, Action<bool, string> check)
    {
        string source = Fixture(sandbox, "talent-source"), destination = Fixture(sandbox, "talent-destination");
        string icon = Convert.ToBase64String(Png(SKColors.SeaGreen));
        var selected = new CustomRewardDefinition { Id = "c73ff35b-1c0f-4b2e-a68a-219d43a4cf11", Name = "Portable selected talent", IconPng = icon, Stat = "Speed", Amount = 23 };
        var unrelated = new CustomRewardDefinition { Id = "d384607d-08b9-4c9e-a44b-978ac6eea602", Name = "PRIVATE UNSELECTED TALENT", IconPng = icon, Amount = 99 };
        selected.Save(Path.Combine(source, "library", "rewards"));
        unrelated.Save(Path.Combine(source, "library", "rewards"));
        var candidate = Candidate(source, "draft_pool");
        candidate.Set("Talents", selected.Id);
        CustomRewardExport.Embed(candidate, [unrelated]); // A stale snapshot must not leak into the new export.
        candidate.Save(CandidatePath(source, "draft_pool"));
        string before = Snapshot(source), zip = Path.Combine(sandbox, "selected-talents.zip");
        BaseGamePoolPackages.Export(source, zip, "Selected talent snapshot");
        using (var archive = ZipFile.OpenRead(zip))
        {
            var snapshots = archive.Entries.Where(e => e.FullName.Contains("/player_teams/", StringComparison.Ordinal))
                .SelectMany(e => CustomRewardExport.Read(new CampaignDocument(ReadText(archive, e.FullName)))).ToArray();
            check(snapshots.Length == 1 && snapshots[0].Id == selected.Id && snapshots[0].Amount == 23 && snapshots[0].Stat == "Speed" && snapshots[0].IconPng == icon,
                "pool export embeds the referenced talent's exact rule and icon snapshot");
            check(!archive.Entries.Any(e => e.FullName.Contains("library", StringComparison.Ordinal) || e.FullName.EndsWith(".reward", StringComparison.Ordinal))
                && !snapshots.Any(item => item.Id == unrelated.Id), "pool export omits the whole talent library and unreferenced stale snapshots");
        }
        BaseGamePoolPackages.Install(zip, destination);
        var imported = CustomRewardExport.Read(Candidate(destination, "draft_pool")).Single();
        check(imported.Id == selected.Id && imported.Amount == 23 && Candidate(destination, "draft_pool").Get("Talents") == selected.Id
            && !Directory.Exists(Path.Combine(destination, "library", "rewards")), "installed pool talent remains self-contained without creating or depending on the author's library");
        check(Snapshot(source) == before, "talent sharing does not rewrite the source library or stale source snapshot");
        var loaded = BaseGamePlayerPools.ReadCustomRewards(destination);
        check(loaded.Count == 1 && loaded[selected.Id].Amount == 23, "base-game runtime source resolves a downloaded talent from the selected pool without a library");
        var conflict = selected.Copy(); conflict.Amount = 99;
        var hidden = new CampaignDocument("Name = Unchecked\nPosition = Center\n"); CustomRewardExport.Embed(hidden, [conflict]);
        hidden.Save(Path.Combine(BaseGamePlayerPools.PoolDirectory(destination, "draft_pool"), "unchecked.txt"));
        check(BaseGamePlayerPools.ReadCustomRewards(destination)[selected.Id].Amount == 23, "unselected pool candidates cannot override loaded talent rules");
        var selection = BaseGamePlayerPools.Load(destination); selection.Enabled = false; BaseGamePlayerPools.Save(destination, selection);
        check(BaseGamePlayerPools.ReadCustomRewards(destination).Count == 0, "disabled base pools do not introduce custom reward definitions");
        selection.Enabled = true; BaseGamePlayerPools.Save(destination, selection);
        hidden.Save(CandidatePath(destination, "free_agents"));
        Reject(() => BaseGamePlayerPools.ReadCustomRewards(destination), check, "conflicting selected custom reward identities fail before registration");
    }

    private static void CorruptedBackupCannotReplaceCurrentPools(string sandbox, Action<bool, string> check)
    {
        string source = Fixture(sandbox, "backup-source"), destination = Fixture(sandbox, "backup-destination");
        string zip = Path.Combine(sandbox, "backup-check.zip");
        BaseGamePoolPackages.Export(source, zip);
        var installed = BaseGamePoolPackages.Install(zip, destination);
        string previous = Path.Combine(destination, "base_game_pool_backups", installed.BackupId, "profile", "player_teams", "draft_pool", "candidate.txt");
        byte[] bytes = File.ReadAllBytes(previous);
        bytes[bytes.Length - 2] = bytes[bytes.Length - 2] == (byte)'7' ? (byte)'8' : (byte)'7';
        File.WriteAllBytes(previous, bytes); // Same-length corruption must fail the content hash, not just a size check.
        string current = Snapshot(BaseGamePlayerPools.Folder(destination));
        int count = BaseGamePoolPackages.Backups(destination).Count;
        Reject(() => BaseGamePoolPackages.Restore(destination, installed.BackupId), check, "restore rejects a backup whose bytes changed without changing its file size");
        check(Snapshot(BaseGamePlayerPools.Folder(destination)) == current && BaseGamePoolPackages.Backups(destination).Count == count,
            "corrupted backup rejection preserves all current pools without creating a replacement backup");
        check(File.ReadAllBytes(previous).SequenceEqual(bytes), "failed restore retains the selected corrupted backup for inspection");
        var healthy = BaseGamePoolPackages.Install(zip, destination);
        string metadata = Path.Combine(destination, "base_game_pool_backups", installed.BackupId, "backup.json");
        var record = JsonNode.Parse(File.ReadAllText(metadata))!;
        record["Files"] = new JsonArray((JsonNode?)null); File.WriteAllText(metadata, record.ToJsonString());
        var listed = BaseGamePoolPackages.Backups(destination);
        check(listed.Count == 2 && listed.Single(b => b.Id == installed.BackupId).Error.Length > 0 && listed.Single(b => b.Id == healthy.BackupId).Error.Length == 0,
            "one malformed backup record does not hide healthy backups from the restore list");
        BaseGamePoolPackages.Restore(destination, healthy.BackupId);
        check(BaseGamePlayerPools.Load(destination).Pools["draft_pool"].Length == 1, "a healthy backup remains restorable beside damaged metadata");
    }

    private static void FailedReplacementRollsBackAddedAssets(string sandbox, Action<bool, string> check)
    {
        string source = Fixture(sandbox, "rollback-source"), destination = Fixture(sandbox, "rollback-destination");
        MakeHead(source, "Rollback incoming", SKColors.HotPink);
        string logo = MakeLogo(source, "rollback.png", SKColors.SpringGreen);
        SetCandidate(source, "draft_pool", ("Face", "Faces/CustomHeads/Rollback incoming"), ("Logo From", logo));
        MakeHead(destination, "Keep existing", SKColors.DarkBlue);
        Directory.CreateDirectory(GameLogoFolder());
        File.WriteAllBytes(Path.Combine(GameLogoFolder(), "keep-existing.png"), Png(SKColors.DarkViolet));
        string zip = Path.Combine(sandbox, "rollback-assets.zip");
        BaseGamePoolPackages.Export(source, zip);
        BaseGamePoolPackages.Inspect(zip); // Establish that the package itself is valid before inducing a destination failure.
        string blocker = Path.Combine(destination, "base_game_pool_backups");
        File.WriteAllText(blocker, "Keep the occupied backup path");
        string profileBefore = Snapshot(BaseGamePlayerPools.Folder(destination));
        string headsBefore = Snapshot(Path.Combine(destination, "custom-heads")), logosBefore = Snapshot(GameLogoFolder());
        Reject(() => BaseGamePoolPackages.Install(zip, destination), check, "pool replacement fails when its backup location is occupied by a file");
        check(Snapshot(BaseGamePlayerPools.Folder(destination)) == profileBefore, "failed replacement preserves every existing pool byte");
        check(Snapshot(Path.Combine(destination, "custom-heads")) == headsBefore, "failed replacement removes newly installed heads and preserves existing head projects");
        check(Snapshot(GameLogoFolder()) == logosBefore, "failed replacement removes newly installed logos and preserves existing logo bytes");
        check(File.ReadAllText(blocker) == "Keep the occupied backup path", "failed replacement preserves the file blocking backup creation");
    }

    private static void UnsafeAssetArchivesPreserveCurrentData(string sandbox, Action<bool, string> check)
    {
        string source = Fixture(sandbox, "unsafe-source"), destination = Fixture(sandbox, "unsafe-destination");
        string logo = MakeLogo(source, "checked.png", SKColors.Orange);
        SetCandidate(source, "draft_pool", ("Logo From", logo));
        string zip = Path.Combine(sandbox, "safe-assets.zip");
        BaseGamePoolPackages.Export(source, zip);
        string current = Snapshot(BaseGamePlayerPools.Folder(destination)), logos = Snapshot(GameLogoFolder());
        foreach (int attributes in new[] { unchecked((int)0xA1FF0000), (int)FileAttributes.ReparsePoint })
        {
            string unsafeZip = Path.Combine(sandbox, "symlink-" + attributes + ".zip");
            File.Copy(zip, unsafeZip);
            using (var archive = ZipFile.Open(unsafeZip, ZipArchiveMode.Update))
            {
                var link = archive.CreateEntry("base_game_pools/_logos/link.png");
                link.ExternalAttributes = attributes;
                using var writer = new StreamWriter(link.Open()); writer.Write("../../outside.png");
            }
            Reject(() => BaseGamePoolPackages.Install(unsafeZip, destination), check, "pool import rejects archive symlink/reparse assets before installation");
        }
        string damaged = Path.Combine(sandbox, "corrupt-logo.zip"); File.Copy(zip, damaged);
        using (var archive = ZipFile.Open(damaged, ZipArchiveMode.Update))
        {
            var image = archive.Entries.Single(e => e.FullName.StartsWith("base_game_pools/_logos/", StringComparison.Ordinal));
            string path = image.FullName; image.Delete();
            using var output = archive.CreateEntry(path).Open(); output.Write(Png(SKColors.Black));
        }
        Reject(() => BaseGamePoolPackages.Install(damaged, destination), check, "pool import rejects a logo whose actual PNG bytes disagree with its shared identity");
        foreach (bool namedByHash in new[] { true, false })
        {
            byte[] junk = System.Text.Encoding.UTF8.GetBytes("This is not a PNG image.");
            string name = namedByHash ? "t2t_" + Convert.ToHexString(SHA256.HashData(junk)).ToLowerInvariant() + ".png" : "legacy-broken.png";
            string invalid = Path.Combine(sandbox, "not-png-" + namedByHash + ".zip"); File.Copy(zip, invalid);
            using (var archive = ZipFile.Open(invalid, ZipArchiveMode.Update))
            { using var output = archive.CreateEntry("base_game_pools/_logos/" + name).Open(); output.Write(junk); }
            Reject(() => BaseGamePoolPackages.Inspect(invalid), check, "package inspection rejects non-PNG logo bytes even with a valid hash or legacy name");
            Reject(() => BaseGamePoolPackages.Install(invalid, destination), check, "invalid logo data cannot replace current pools");
        }
        check(Snapshot(BaseGamePlayerPools.Folder(destination)) == current && Snapshot(GameLogoFolder()) == logos
            && BaseGamePoolPackages.Backups(destination).Count == 0, "unsafe asset rejection preserves current pools, shared logos and backup history");
    }

    private static string Fixture(string sandbox, string label)
    {
        string root = Path.Combine(sandbox, label);
        var selection = new BaseGamePoolSelection { Enabled = false };
        foreach (string kind in new[] { "draft_pool", "free_agents", "superstars" })
        {
            Directory.CreateDirectory(BaseGamePlayerPools.PoolDirectory(root, kind));
            File.WriteAllText(CandidatePath(root, kind), $"Name = {label} {kind}\nPosition = Center\nSpeed = 47\n");
            selection.Pools[kind] = ["candidate.txt"];
        }
        BaseGamePlayerPools.Save(root, selection);
        return root;
    }

    private static string MakeHead(string root, string name, SKColor color)
    {
        string folder = Path.Combine(root, "custom-heads", name);
        Directory.CreateDirectory(folder);
        var project = new HeadProject { Folder = folder, Name = name, Version = 1, SupportsHelmet = true };
        foreach (string direction in Directions)
        {
            string file = direction + ".png", bounds = "bounds/" + file, original = "source/" + file;
            byte[] bytes = Png(color);
            foreach (string relative in new[] { file, bounds, original })
            {
                string target = Path.Combine(folder, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.WriteAllBytes(target, bytes);
            }
            project.Images.Add(new HeadImage
            {
                File = file, BoundsFile = bounds, SourceFile = original, Group = direction == "portrait" ? "Portrait" : "Head", Direction = direction,
                Width = 512, Height = 512, NativeWidth = 512, NativeHeight = 512, Left = 0, Top = 0, ContentWidth = 512, ContentHeight = 512
            });
        }
        project.Save();
        File.WriteAllText(Path.Combine(folder, "private-note.txt"), "Do not include this author note");
        CustomHeadDefinition.Read(folder);
        return folder;
    }

    private static string MakeLogo(string root, string name, SKColor color)
    {
        string folder = Path.Combine(root, "logos"); Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, name); File.WriteAllBytes(file, Png(color)); return file;
    }

    private static byte[] Png(SKColor color)
    {
        using var bitmap = new SKBitmap(512, 512);
        using (var canvas = new SKCanvas(bitmap)) canvas.Clear(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    private static string GameLogoFolder() => Path.Combine(Environment.GetEnvironmentVariable("T2T_GAME_SAVE_PATH")!, "CustomLogos");
    private static string CandidatePath(string root, string kind) => Path.Combine(BaseGamePlayerPools.PoolDirectory(root, kind), "candidate.txt");
    private static CampaignDocument Candidate(string root, string kind) => new(File.ReadAllText(CandidatePath(root, kind)));
    private static void SetCandidate(string root, string kind, params (string Key, string Value)[] fields)
    {
        var candidate = Candidate(root, kind);
        foreach (var (key, value) in fields) candidate.Set(key, value);
        candidate.Save(CandidatePath(root, kind));
    }
    private static string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
    private static string Snapshot(string folder) => !Directory.Exists(folder) ? "absent" : string.Join("|", Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
        .Select(f => Path.GetRelativePath(folder, f) + ":" + Hash(f)).Order(StringComparer.Ordinal));
    private static byte[] ReadBytes(ZipArchive archive, string entry)
    {
        using var input = (archive.GetEntry(entry) ?? throw new Exception("Missing archive entry: " + entry)).Open();
        using var memory = new MemoryStream(); input.CopyTo(memory); return memory.ToArray();
    }
    private static string ReadText(ZipArchive archive, string entry)
    {
        using var input = (archive.GetEntry(entry) ?? throw new Exception("Missing archive entry: " + entry)).Open();
        using var reader = new StreamReader(input); return reader.ReadToEnd();
    }
    private static void Reject(Action action, Action<bool, string> check, string label)
    {
        try { action(); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException) { check(true, label); return; }
        throw new Exception(label);
    }
}
