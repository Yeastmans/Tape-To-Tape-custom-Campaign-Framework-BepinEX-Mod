using CampaignCore;
using CampaignCreator;
using System.IO.Compression;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;

int checks = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
void Reject(Action action, string name) { try { action(); } catch (Exception e) when (e is ArgumentException or InvalidDataException or IOException) { Check(true, name); return; } throw new Exception(name); }
string sandbox = Path.Combine(Path.GetTempPath(), "base-pool-sharing-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(sandbox);
string Fixture(string label, bool enabled)
{
    string root = Path.Combine(sandbox, label); var selected = new BaseGamePoolSelection { Enabled = enabled };
    foreach (string kind in BaseGamePlayerPools.Kinds)
    {
        string folder = BaseGamePlayerPools.PoolDirectory(root, kind); Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "custom-one.txt"), $"Name = {label} {kind}\nPosition = Center\nSpeed = 47\n");
        selected.Pools[kind] = ["custom-one.txt"];
    }
    BaseGamePlayerPools.Save(root, selected); return root;
}
string Fingerprint(string path) => !Directory.Exists(path) ? "absent" : string.Join("|", Directory.GetFiles(path, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
    .Select(f => Path.GetRelativePath(path, f) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)))));
void AddEntry(string zip, string name, string content)
{
    using var archive = ZipFile.Open(zip, ZipArchiveMode.Update);
    using var writer = new StreamWriter(archive.CreateEntry(name).Open()); writer.Write(content);
}
try
{
    string source = Fixture("Charlotte", false), destination = Fixture("Original", false);
    string native = Path.Combine(BaseGamePlayerPools.PoolDirectory(source, "draft_pool"), "native-one.txt");
    File.WriteAllText(native, "Name = Boring Dude\nPosition = Center\nStudio Native Player Id = boring-native\nStudio Native Player Asset = boring-asset\n");
    var selection = BaseGamePlayerPools.Load(source); selection.Pools["draft_pool"] = ["native-one.txt", "custom-one.txt"]; BaseGamePlayerPools.Save(source, selection);
    File.WriteAllText(Path.Combine(BaseGamePlayerPools.PoolDirectory(source, "draft_pool"), "unchecked.txt"), "Name = PRIVATE DRAFT\nPosition = Center\n");
    File.WriteAllText(Path.Combine(source, "community-ownership.json"), "PRIVATE CODE MUST NEVER SHIP");
    File.WriteAllText(Path.Combine(BaseGamePlayerPools.Folder(destination), "old-unused.txt"), "Keep this in the backup");
    Directory.CreateDirectory(Path.Combine(destination, "campaigns")); File.WriteAllText(Path.Combine(destination, "campaigns", "sentinel.txt"), "campaign unchanged");
    string zip = Path.Combine(sandbox, "Charlotte.zip"), sourceBefore = Fingerprint(source), destinationBefore = Fingerprint(BaseGamePlayerPools.Folder(destination));
    var exported = BaseGamePoolPackages.Export(source, zip, "Charlotte's pools");
    Check(exported.Name == "Charlotte's pools" && exported.BenchCount == 2 && exported.FreeAgentCount == 1 && exported.SuperstarCount == 1, "export reports the three saved selections");
    Check(Fingerprint(source) == sourceBefore, "export never changes source data");
    using (var archive = ZipFile.OpenRead(zip))
    {
        Check(!archive.Entries.Any(e => e.FullName.Contains("unchecked") || e.FullName.Contains("ownership") || e.FullName.Contains("backup")), "sharing omits unchecked candidates and private data");
        using var reader = new StreamReader(archive.GetEntry("base_game_pools/player_teams/draft_pool/native-one.txt")!.Open());
        Check(reader.ReadToEnd().Contains("Studio Native Player Id = boring-native"), "native binding survives export");
    }
    var inspected = BaseGamePoolPackages.Inspect(zip);
    Check(inspected.BenchCount == 2 && !inspected.Enabled, "inspect preserves authored information without applying it");
    var installed = BaseGamePoolPackages.Install(zip, destination);
    var current = BaseGamePlayerPools.Load(destination);
    Check(current.Enabled && current.Pools["draft_pool"].SequenceEqual(["native-one.txt", "custom-one.txt"]), "download replaces ordered bench and enables its saved selections");
    Check(BaseGamePlayerPools.Kinds.All(k => File.ReadAllText(Path.Combine(BaseGamePlayerPools.PoolDirectory(destination, k), "custom-one.txt")).Contains("Charlotte")), "all three old pools replaced together");
    Check(File.ReadAllText(Path.Combine(destination, "campaigns", "sentinel.txt")) == "campaign unchanged", "pool installation leaves campaigns alone");
    var backups = BaseGamePoolPackages.Backups(destination);
    Check(backups.Count == 1 && backups[0].Id == installed.BackupId && backups[0].Pools?.Enabled == false, "replacement exposes dated prior configuration");
    string downloadedState = Fingerprint(BaseGamePlayerPools.Folder(destination));
    var restored = BaseGamePoolPackages.Restore(destination, backups[0].Id);
    Check(Fingerprint(BaseGamePlayerPools.Folder(destination)) == destinationBefore && !BaseGamePlayerPools.Load(destination).Enabled, "restore recovers every prior byte including unused players and disabled state");
    Check(BaseGamePoolPackages.Backups(destination).Count == 2 && BaseGamePoolPackages.Backups(destination).Any(b => b.Id == installed.BackupId), "restore retains selected backup and backs up the replaced setup");
    BaseGamePoolPackages.Restore(destination, restored.BackupId);
    Check(Fingerprint(BaseGamePlayerPools.Folder(destination)) == downloadedState, "restore can be undone through its new backup");
    string untouched = Fingerprint(BaseGamePlayerPools.Folder(destination)); int backupCount = BaseGamePoolPackages.Backups(destination).Count;
    foreach (string unsafeName in new[] { "base_game_pools/../escape.txt", "base_game_pools/C:/escape.txt", "base_game_pools/player_teams/draft_pool/CUSTOM-ONE.TXT", "base_game_pools/unexpected.dll" })
    {
        string bad = Path.Combine(sandbox, Guid.NewGuid().ToString("N") + ".zip"); File.Copy(zip, bad); AddEntry(bad, unsafeName, "unsafe");
        Reject(() => BaseGamePoolPackages.Install(bad, destination), "reject unsafe/duplicate/unrelated archive entry " + unsafeName);
        Check(Fingerprint(BaseGamePlayerPools.Folder(destination)) == untouched && BaseGamePoolPackages.Backups(destination).Count == backupCount, "invalid import leaves all pools and backups untouched");
    }
    string empty = Path.Combine(sandbox, "empty-pool.zip"); File.Copy(zip, empty);
    using (var archive = ZipFile.Open(empty, ZipArchiveMode.Update))
    {
        archive.GetEntry("base_game_pools/settings.txt")!.Delete();
        using var writer = new StreamWriter(archive.CreateEntry("base_game_pools/settings.txt").Open());
        writer.Write("schema = 1\nenabled = yes\ndraft_pool = \nfree_agents = custom-one.txt\nsuperstars = custom-one.txt\n");
    }
    Reject(() => BaseGamePoolPackages.Install(empty, destination), "reject empty pool before creating a backup or replacing files");
    Check(Fingerprint(BaseGamePlayerPools.Folder(destination)) == untouched, "empty pool rejection preserves current data");
    Reject(() => BaseGamePoolPackages.Restore(destination, "../escape"), "restore rejects external backup paths");
    string first = Path.Combine(sandbox, "first-install"); Directory.CreateDirectory(first);
    var firstInstall = BaseGamePoolPackages.Install(zip, first);
    BaseGamePoolPackages.Restore(first, firstInstall.BackupId);
    Check(!Directory.Exists(BaseGamePlayerPools.Folder(first)) && !BaseGamePlayerPools.Load(first).Enabled, "first installation can be undone to native pools with no saved overrides");
    AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    AssetChecks.Run(sandbox, Check);
    var main = new MainWindow(["--content", destination]);
    var controls = ((Control)main.Content!).GetLogicalDescendants().OfType<Button>().ToArray();
    Check(new[] { "HomeBaseGamePools", "HomeUploadBaseGamePools", "HomeDownloadBaseGamePools", "HomeRestoreBaseGamePoolsBackup" }.All(name => controls.Any(c => c.Name == name)), "home has a separate pool editing, upload, download and restore section");
    void PumpUntil(Func<bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        do { Dispatcher.UIThread.RunJobs(); if (ready()) return; Thread.Sleep(10); } while (DateTime.UtcNow < deadline);
        throw new Exception("UI operation did not finish within ten seconds.");
    }
    void Click(Control parent, string name) => parent.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    var owner = new Window { Width = 1280, Height = 850, Content = main.Content }; main.Content = null; owner.Show(); Dispatcher.UIThread.RunJobs();
    string? screenshots = Environment.GetEnvironmentVariable("POOL_SHARING_SCREENSHOTS");
    if (screenshots != null)
    {
        Directory.CreateDirectory(screenshots); owner.CaptureRenderedFrame()!.Save(Path.Combine(screenshots, "home.png"));
        var section = owner.GetLogicalDescendants().OfType<Border>().Single(b => b.Name == "HomeBaseGamePoolsSection");
        section.BringIntoView(); Dispatcher.UIThread.RunJobs(); owner.CaptureRenderedFrame()!.Save(Path.Combine(screenshots, "home-pool-section.png"));
        owner.Width = 960; owner.Height = 620; Dispatcher.UIThread.RunJobs(); section.BringIntoView(); Dispatcher.UIThread.RunJobs();
        owner.CaptureRenderedFrame()!.Save(Path.Combine(screenshots, "home-pool-section-960.png"));
    }
    int refreshed = 0;
    var restoreWindow = new BaseGamePoolRestoreWindow(destination, () => refreshed++);
    _ = restoreWindow.ShowDialog(owner); Dispatcher.UIThread.RunJobs();
    var backupList = restoreWindow.GetLogicalDescendants().OfType<ListBox>().Single(b => b.Name == "BaseGamePoolBackupList");
    backupList.SelectedItem = backupList.Items.Cast<BaseGamePoolBackup>().Single(b => b.Id == installed.BackupId);
    string uiBefore = Fingerprint(BaseGamePlayerPools.Folder(destination)); int uiBackupCount = BaseGamePoolPackages.Backups(destination).Count;
    Click(restoreWindow, "RestoreBaseGamePoolBackup"); PumpUntil(() => restoreWindow.OwnedWindows.Any(w => w.Name == "ConfirmBaseGamePoolReplacement"));
    var confirmation = restoreWindow.OwnedWindows.Single(w => w.Name == "ConfirmBaseGamePoolReplacement");
    if (screenshots != null) { Dispatcher.UIThread.RunJobs(); confirmation.CaptureRenderedFrame()!.Save(Path.Combine(screenshots, "restore-confirmation.png")); }
    Click(confirmation, "CancelBaseGamePoolReplacement");
    PumpUntil(() => restoreWindow.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == "Cancelled. Your current pools were kept."));
    Check(Fingerprint(BaseGamePlayerPools.Folder(destination)) == uiBefore && BaseGamePoolPackages.Backups(destination).Count == uiBackupCount && refreshed == 0, "cancelling the real restore dialog leaves files and backups unchanged");
    Click(restoreWindow, "RestoreBaseGamePoolBackup"); PumpUntil(() => restoreWindow.OwnedWindows.Any(w => w.Name == "ConfirmBaseGamePoolReplacement"));
    Click(restoreWindow.OwnedWindows.Single(w => w.Name == "ConfirmBaseGamePoolReplacement"), "ConfirmBaseGamePoolReplacementButton");
    PumpUntil(() => restoreWindow.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text?.StartsWith("Backup restored.") == true));
    Check(Fingerprint(BaseGamePlayerPools.Folder(destination)) == destinationBefore && BaseGamePoolPackages.Backups(destination).Count == uiBackupCount + 1 && refreshed == 1, "confirming the real restore dialog applies the backup and preserves an undo snapshot");
    if (screenshots != null) { Dispatcher.UIThread.RunJobs(); restoreWindow.CaptureRenderedFrame()!.Save(Path.Combine(screenshots, "restore-backups.png")); }
    restoreWindow.Close(); owner.Close();
    UploadUpdateChecks.Run(sandbox, Check);
    HelmetModeChecks.Run(sandbox, Check);
    Console.WriteLine("PASS " + checks + " pool sharing checks");
}
catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
finally { Directory.Delete(sandbox, true); }
return 0;
