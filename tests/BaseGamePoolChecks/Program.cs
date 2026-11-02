using CampaignCore;
using CampaignCreator;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using System.Reflection;

int checks = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
void Reject(Action action, string name) { try { action(); } catch (Exception e) when (e is ArgumentException or InvalidDataException or IOException) { Check(true, name); return; } throw new Exception(name); }
string root = Path.Combine(Path.GetTempPath(), "base-game-pool-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var missing = BaseGamePlayerPools.Load(root);
    Check(!missing.Enabled && missing.Pools.Count == 3 && missing.Pools.Values.All(p => p.Length == 0), "missing profile is inert with three empty lists");
    var selection = new BaseGamePoolSelection { Enabled = true };
    foreach (string kind in BaseGamePlayerPools.Kinds)
    {
        string directory = BaseGamePlayerPools.PoolDirectory(root, kind); Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "a.txt"), "Name = Alice\n"); File.WriteAllText(Path.Combine(directory, "b.txt"), "Name = Bob\n");
        selection.Pools[kind] = ["b.txt", "a.txt"];
    }
    BaseGamePlayerPools.Save(root, selection);
    var loaded = BaseGamePlayerPools.Load(root);
    Check(loaded.Enabled && loaded.Pools.Values.All(p => p.SequenceEqual(["b.txt", "a.txt"])), "three ordered lists round-trip together");
    Check(BaseGamePlayerPools.SelectedFiles(root, "draft_pool", loaded).Select(File.ReadAllText).First().Contains("Bob"), "selected files resolve to their own consumer pool");
    string settings = Path.Combine(BaseGamePlayerPools.Folder(root), "settings.txt"), before = File.ReadAllText(settings);
    foreach (string[] bad in new[] { Array.Empty<string>(), new[] { "missing.txt" }, new[] { "a.txt", "A.txt" }, new[] { "../a.txt" }, new[] { "a,b.txt" }, new[] { "C:\\a.txt" }, new[] { "CON.txt" } })
    {
        selection.Pools["free_agents"] = bad;
        Reject(() => BaseGamePlayerPools.Save(root, selection), "invalid selection rejected: " + string.Join("|", bad));
        Check(File.ReadAllText(settings) == before, "invalid pool leaves entire saved profile untouched");
    }
    selection.Pools["free_agents"] = ["a.txt"];
    selection.Pools["draft_pool"] = Enumerable.Range(0, 14).Select(i => "candidate-" + i + ".txt").ToArray();
    foreach (string file in selection.Pools["draft_pool"]) File.WriteAllText(Path.Combine(BaseGamePlayerPools.PoolDirectory(root, "draft_pool"), file), "Name = Extra\n");
    selection.Pools["superstars"] = ["a.txt", "b.txt"];
    BaseGamePlayerPools.Save(root, selection);
    Check(BaseGamePlayerPools.Load(root).Pools["draft_pool"].Length == 14, "candidate list has no nine-player cap");
    selection.Enabled = false; BaseGamePlayerPools.Save(root, selection);
    Check(!BaseGamePlayerPools.Load(root).Enabled, "disabled profile retains candidate lists");
    selection.Pools["superstars"] = []; Reject(() => BaseGamePlayerPools.Save(root, selection), "disabled profile still requires every pool nonempty");
    string id = BaseGamePlayerPools.CustomPlayerId("draft_pool", "a.txt");
    Check(id.StartsWith(BaseGamePlayerPools.CustomIdPrefix) && id == BaseGamePlayerPools.CustomPlayerId("DRAFT_POOL", "A.TXT"), "portable custom identity ignores file case");
    Check(id != BaseGamePlayerPools.CustomPlayerId("superstars", "a.txt") && id != BaseGamePlayerPools.CustomPlayerId("draft_pool", "b.txt"), "identity distinguishes filename and consumer");
    Reject(() => BaseGamePlayerPools.CustomPlayerId("draft_pool", "../a.txt"), "identity rejects traversal");
    Reject(() => BaseGamePlayerPools.CustomPlayerId("draft_pool", "a.txt\n"), "identity rejects trailing newline");
    File.WriteAllText(settings, "schema = 2\nenabled = yes\n"); Reject(() => BaseGamePlayerPools.Load(root), "unsupported schema rejected");
    string editorRoot = Path.Combine(root, "editor"), cache = Path.Combine(root, "cache"), sourceFolder = Path.Combine(cache, "base-campaigns");
    Directory.CreateDirectory(sourceFolder);
    var players = new Dictionary<string, object>();
    foreach (string key in new[] { "boring", "bench2", "free1", "free2", "free3", "star1", "star2", "star3", "star4" })
    {
        File.WriteAllText(Path.Combine(sourceFolder, key + ".txt"), $"Name = {key}\nPosition = Center\nStudio Native Player Id = native_{key}\nStudio Native Player Asset = asset_{key}\nStudio Base Pool Player = legacy_{key}\n");
        players[key] = new { file = key + ".txt" };
    }
    File.WriteAllText(Path.Combine(sourceFolder, "catalog.json"), JsonSerializer.Serialize(new { version = 1, campaigns = new[] { new { id = "vanilla", bench = new[] { "boring", "bench2" }, freeAgents = new[] { "free1", "free2", "free3" }, superstars = new[] { "star1", "star2", "star3", "star4" } } }, players }));
    var originalFiles = Directory.GetFiles(sourceFolder).ToDictionary(f => f, File.ReadAllText);
    var content = new BaseGamePoolsContent(editorRoot, cache);
    Check(content.Selection.Enabled && content.Selection.Pools["draft_pool"].Length == 2 && content.Selection.Pools["free_agents"].Length == 3 && content.Selection.Pools["superstars"].Length == 4, "first open prepares enabled draft with every standard candidate");
    string profile = Path.Combine(BaseGamePlayerPools.Folder(editorRoot), "settings.txt");
    Check(!File.Exists(profile) && !BaseGamePlayerPools.Load(editorRoot).Enabled, "opening editor does not activate or save settings");
    string privateFile = BaseGamePlayerPools.SelectedFiles(editorRoot, "draft_pool", content.Selection)[0];
    File.AppendAllText(privateFile, "Speed = 5\n");
    var reopened = new BaseGamePoolsContent(editorRoot, cache);
    Check(File.ReadAllText(privateFile).Contains("Speed = 5"), "reopening never overwrites existing candidate edits");
    string importedSource = Path.Combine(sourceFolder, "boring.txt");
    var sourceDoc = new CampaignDocument(File.ReadAllText(importedSource)); sourceDoc.Set("Name", "Charlotte Chapstick"); sourceDoc.Set("Override Team Settings", "yes");
    string customSource = Path.Combine(root, "charlotte.txt"); sourceDoc.Save(customSource);
    string added = content.ImportCandidate("draft_pool", customSource);
    Check(content.Selection.Pools["draft_pool"].Length == 3 && content.Selection.Pools["draft_pool"][0] == Path.GetFileName(privateFile), "adding Charlotte preserves Boring Dude and selects the new copy");
    var importedDoc = new CampaignDocument(File.ReadAllText(Path.Combine(BaseGamePlayerPools.PoolDirectory(editorRoot, "draft_pool"), added)));
    Check(importedDoc.Get(VanillaCopyData.PlayerId) == "" && importedDoc.Get(VanillaCopyData.PlayerAsset) == "" && importedDoc.Get("Studio Base Pool Player") == "", "imports remove every native binding marker");
    Check(importedDoc.Get("Override Team Settings") == "yes", "imported player retains authored equipment intent");
    string second = content.Selection.Pools["draft_pool"][1];
    string replacement = content.ImportCandidate("draft_pool", customSource, second);
    Check(content.Selection.Pools["draft_pool"].SequenceEqual([Path.GetFileName(privateFile), replacement, added]), "replace changes only the chosen candidate and preserves order");
    content.Move("draft_pool", added, -1);
    Check(content.Selection.Pools["draft_pool"].SequenceEqual([Path.GetFileName(privateFile), added, replacement]), "moving a candidate preserves the other identities");
    for (int i = 0; i < 10; i++) content.ImportCandidate("superstars", customSource);
    Check(content.Selection.Pools["superstars"].Length == 14, "superstar additions have no three-player cap");
    content.Selection.Enabled = true; content.Save(); string savedProfile = File.ReadAllText(profile);
    content.SetSelected("draft_pool", added, false);
    Check(File.ReadAllText(profile) == savedProfile && BaseGamePlayerPools.Load(editorRoot).Pools["draft_pool"].Contains(added), "unsaved selections stay separate from the saved profile");
    content.Selection.Pools["free_agents"] = [];
    Reject(content.Save, "invalid editor draft is retained after failed save");
    Check(content.Selection.Pools["free_agents"].Length == 0 && File.ReadAllText(profile) == savedProfile, "save failure preserves draft and previous settings");
    content.RestoreDefaults(cache);
    Check(content.Selection.Enabled && content.Selection.Pools["superstars"].Length == 4 && File.ReadAllText(profile) == savedProfile, "restore defaults is an unsaved selection draft");
    Check(File.ReadAllText(privateFile).Contains("Speed = 5") && !BaseGamePlayerPools.SelectedFiles(editorRoot, "draft_pool", content.Selection).Any(f => File.ReadAllText(f).Contains("Speed = 5")), "restore defaults stages fresh native copies without modifying saved player edits");
    Check(originalFiles.All(p => File.ReadAllText(p.Key) == p.Value), "native source catalog and players remain byte-identical");
    var noCache = new BaseGamePoolsContent(editorRoot, null);
    Check(noCache.Selection.Enabled && noCache.Selection.Pools["draft_pool"].Length == 3, "saved profile opens without requiring the source cache");
    string neverOpened = Path.Combine(root, "missing-cache-profile");
    bool rejectedCache = false;
    try { _ = new BaseGamePoolsContent(neverOpened, null); } catch (InvalidOperationException) { rejectedCache = true; }
    Check(rejectedCache && !Directory.Exists(BaseGamePlayerPools.Folder(neverOpened)), "missing initial cache leaves no partial profile or settings");
    string[] pendingDefaultFiles = content.Selection.Pools["draft_pool"].ToArray();
    rejectedCache = false;
    try { content.RestoreDefaults(null); } catch (InvalidOperationException) { rejectedCache = true; }
    Check(rejectedCache && content.Selection.Pools["draft_pool"].SequenceEqual(pendingDefaultFiles) && File.ReadAllText(profile) == savedProfile, "failed default restore preserves selections and saved profile");
    string duplicateRoot = Path.Combine(root, "repeated-defaults");
    var duplicates = new BaseGamePoolsContent(duplicateRoot, cache);
    string oldCandidate = BaseGamePlayerPools.SelectedFiles(duplicateRoot, "draft_pool", duplicates.Selection)[0];
    File.AppendAllText(oldCandidate, "Speed = 4\n"); string oldCandidateBytes = File.ReadAllText(oldCandidate);
    duplicates.RestoreDefaults(cache); duplicates.RestoreDefaults(cache);
    string currentCandidate = duplicates.Selection.Pools["draft_pool"][0];
    Check(duplicates.Candidates("draft_pool").SequenceEqual(duplicates.Selection.Pools["draft_pool"]), "repeated defaults hide unchecked copies of currently selected native variants");
    string customOne = duplicates.ImportCandidate("draft_pool", customSource), customTwo = duplicates.ImportCandidate("draft_pool", customSource);
    duplicates.SetSelected("draft_pool", customOne, false); duplicates.SetSelected("draft_pool", customTwo, false);
    duplicates.SetSelected("draft_pool", currentCandidate, false);
    Check(duplicates.Candidates("draft_pool").Contains(currentCandidate) && duplicates.Candidates("draft_pool").Length == 4, "unchecked current default represents its native identity while custom copies remain distinct");
    duplicates.Save(); duplicates = new BaseGamePoolsContent(duplicateRoot, null);
    Check(duplicates.Candidates("draft_pool").Length == 4 && duplicates.Candidates("draft_pool").Contains(currentCandidate), "save and reopen retain one unchecked native representative per identity");
    duplicates.SetSelected("draft_pool", Path.GetFileName(oldCandidate), true); duplicates.SetSelected("draft_pool", currentCandidate, true);
    Check(duplicates.Candidates("draft_pool").Contains(Path.GetFileName(oldCandidate)) && duplicates.Candidates("draft_pool").Contains(currentCandidate)
        && duplicates.Selection.Pools["draft_pool"].Contains(Path.GetFileName(oldCandidate)) && duplicates.Selection.Pools["draft_pool"].Contains(currentCandidate), "checked native duplicates remain visible and selected");
    Check(File.ReadAllText(oldCandidate) == oldCandidateBytes && Directory.GetFiles(BaseGamePlayerPools.PoolDirectory(duplicateRoot, "draft_pool"), "native-*.txt").Length == 6,
        "collapsing duplicate rows preserves all earlier native files and edited bytes");
    AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    void Pump() { Dispatcher.UIThread.RunJobs(); }
    void Click(Control view, string name) { view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(); }
    int backCount = 0; string? edited = null;
    var view = new BaseGamePoolsView(content, () => cache, () => Task.FromResult<string?>(null), f => edited = f, () => backCount++);
    var owner = new Window { Width = 1280, Height = 850, Content = view }; owner.Show(); Pump();
    Check(view.GetVisualDescendants().OfType<ScrollViewer>().Count(s => (s.Name ?? "").StartsWith("BaseGamePoolList")) == 3, "all three candidate sections have independent scroll areas");
    string draftBeforeCancel = string.Join(",", content.Selection.Pools["draft_pool"]);
    Click(view, "BaseGamePoolAdd_draft_pool");
    Check(string.Join(",", content.Selection.Pools["draft_pool"]) == draftBeforeCancel && !view.HasUnsavedChanges, "cancelled import keeps selection and clean state");
    Click(view, "BaseGamePoolReplace_draft_pool_0");
    Check(string.Join(",", content.Selection.Pools["draft_pool"]) == draftBeforeCancel && !view.HasUnsavedChanges, "cancelled replacement preserves the original candidate");
    var toggle = view.GetVisualDescendants().OfType<ToggleSwitch>().Single(c => c.Name == "BaseGamePoolsEnabled"); toggle.IsChecked = false; Pump();
    Check(view.HasUnsavedChanges && File.ReadAllText(profile) == savedProfile, "enable switch remains an unsaved profile edit");
    if (OperatingSystem.IsWindows())
    {
        using (var locked = new FileStream(profile, FileMode.Open, FileAccess.Read, FileShare.Read)) Click(view, "BaseGamePoolsSave");
        Check(view.HasUnsavedChanges && !content.Selection.Enabled && File.ReadAllText(profile) == savedProfile, "manifest write failure retains the enabled draft and last complete profile");
        Check(Directory.GetFiles(BaseGamePlayerPools.Folder(editorRoot), "*.tmp").Length == 0, "failed atomic replacement cleans temporary settings files");
    }
    var checksInPool = view.GetVisualDescendants().OfType<CheckBox>().Where(c => (c.Name ?? "").StartsWith("BaseGameCandidate_draft_pool_")).ToArray();
    checksInPool[0].Focus(); checksInPool[0].IsChecked = false; Pump();
    Check(!view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "BaseGamePoolDown_draft_pool_0").IsEnabled
        && !view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "BaseGamePoolUp_draft_pool_1").IsEnabled, "unchecking a candidate immediately refreshes all move action states");
    checksInPool[0].IsChecked = true; Pump();
    Check(view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "BaseGamePoolUp_draft_pool_0").IsEnabled
        && !view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "BaseGamePoolDown_draft_pool_0").IsEnabled
        && ReferenceEquals(owner.FocusManager!.GetFocusedElement(), checksInPool[0]), "rechecking refreshes move order without replacing the focused checkbox");
    foreach (var check in checksInPool) check.IsChecked = false;
    Click(view, "BaseGamePoolsSave");
    Check(File.ReadAllText(profile) == savedProfile && content.Selection.Pools["draft_pool"].Length == 0 && view.HasUnsavedChanges, "empty UI selection blocks save and retains the draft");
    Check(view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "BaseGamePoolsFeedback").Text!.Contains("at least one"), "save rejection is visible inline");
    Click(view, "BaseGamePoolsRestore");
    Click(view, "BaseGamePoolEdit_draft_pool_0");
    Check(edited != null && File.Exists(edited) && File.ReadAllText(profile) == savedProfile, "editing opens the private player without saving pool selections");
    owner.Content = new TextBlock { Text = "Player editing" }; Pump(); owner.Content = view; view.RefreshCandidates(); Pump();
    Check(view.HasUnsavedChanges && content.Selection.Pools["draft_pool"].Length == 2, "return from player editing preserves unsaved selection state");
    Click(view, "BaseGamePoolsSave");
    Check(!view.HasUnsavedChanges && !BaseGamePlayerPools.Load(editorRoot).Enabled, "Save writes all pools and the enable switch together");
    Check(!new BaseGamePoolsContent(editorRoot, null).Selection.Enabled, "reopening an existing disabled profile respects its saved switch");
    Click(view, "BaseGamePoolsBack"); Check(backCount == 1, "Back returns through the supplied home navigation");
    string? output = args.FirstOrDefault();
    void CaptureMainContent(MainWindow window, string file)
    {
        if (output == null) return;
        var control = (Control)window.Content!; window.Content = null;
        var picture = new Window { Width = 960, Height = 620, Content = control }; picture.Show(); Pump();
        picture.CaptureRenderedFrame()!.Save(Path.Combine(output, file)); picture.Content = null; picture.Close(); window.Content = control;
    }
    if (output != null) { Directory.CreateDirectory(output); owner.CaptureRenderedFrame()!.Save(Path.Combine(output, "base-game-pools-1280.png")); owner.Width = 960; owner.Height = 620; Pump(); owner.CaptureRenderedFrame()!.Save(Path.Combine(output, "base-game-pools-960.png")); }
    owner.Close();
    typeof(App).Assembly.GetType("CampaignCreator.StudioArt")!.GetMethod("Use", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [cache]);
    var main = new MainWindow(["--content", editorRoot]);
    var home = (Control)main.Content!;
    Check(home.GetLogicalDescendants().OfType<Button>().Any(b => b.Name == "HomeBaseGamePools"), "Home exposes the Base Game Pools entry");
    await (Task)typeof(MainWindow).GetMethod("ShowBaseGamePools", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, null)!;
    var poolView = (BaseGamePoolsView)main.Content!;
    string poolFile = BaseGamePlayerPools.SelectedFiles(editorRoot, "draft_pool", BaseGamePlayerPools.Load(editorRoot))[0];
    string playerBefore = File.ReadAllText(poolFile), navigationBefore = File.ReadAllText(profile);
    var pendingCheck = poolView.GetLogicalDescendants().OfType<CheckBox>().First(); pendingCheck.IsChecked = false;
    typeof(MainWindow).GetMethod("OpenBaseGamePoolPlayer", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, [poolFile]);
    var playerEditor = (Control)main.Content!;
    Check(playerEditor.GetType().Name == "PlayerEditorView", "pool candidates use the normal player editor");
    playerEditor.GetLogicalDescendants().OfType<Button>().Single(b => b.Content?.ToString() == "Back to pools").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Check(ReferenceEquals(main.Content, poolView) && poolView.HasUnsavedChanges && !pendingCheck.IsChecked!.Value, "normal player editor returns to the same unsaved pool draft");
    Check(File.ReadAllText(poolFile) == playerBefore && File.ReadAllText(profile) == navigationBefore, "cancelled player navigation writes neither player nor pool selections");
    main.ShowHome();
    string invalidRoot = Path.Combine(root, "invalid-settings"); Directory.CreateDirectory(BaseGamePlayerPools.Folder(invalidRoot));
    string invalidSettings = Path.Combine(BaseGamePlayerPools.Folder(invalidRoot), "settings.txt"); File.WriteAllText(invalidSettings, "schema = 2\nenabled = yes\n");
    var invalidMain = new MainWindow(["--content", invalidRoot]);
    await (Task)typeof(MainWindow).GetMethod("ShowBaseGamePools", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(invalidMain, null)!;
    Check(((Control)invalidMain.Content!).GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Name == "BaseGamePoolsOpenError")
        && File.ReadAllText(invalidSettings) == "schema = 2\nenabled = yes\n", "invalid profile shows actionable inline error without replacing settings");
    Check(((Control)invalidMain.Content!).GetLogicalDescendants().OfType<Button>().Any(b => b.Name == "BaseGamePoolsRecover"), "invalid profile exposes an explicit restore-defaults recovery action");
    CaptureMainContent(invalidMain, "base-game-pools-recovery-error.png");
    ((Control)invalidMain.Content!).GetLogicalDescendants().OfType<Button>().Single(b => b.Content?.ToString() == "Back / Home").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Check(File.ReadAllText(invalidSettings) == "schema = 2\nenabled = yes\n" && Directory.GetFiles(BaseGamePlayerPools.Folder(invalidRoot)).Length == 1, "Back from the error page leaves invalid settings untouched without making backups");
    string oldPrivate = Path.Combine(BaseGamePlayerPools.PoolDirectory(invalidRoot, "draft_pool"), "old.txt"); Directory.CreateDirectory(Path.GetDirectoryName(oldPrivate)!); File.WriteAllText(oldPrivate, "Name = Kept edit\nSpeed = 71\n");
    await (Task)typeof(MainWindow).GetMethod("ShowBaseGamePools", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(invalidMain, null)!;
    ((Control)invalidMain.Content!).GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "BaseGamePoolsRecover").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
    Check(invalidMain.Content is BaseGamePoolsView { HasUnsavedChanges: true } && File.ReadAllText(invalidSettings) == "schema = 2\nenabled = yes\n", "explicit corrupt-profile recovery opens unsaved defaults without activating or replacing settings");
    CaptureMainContent(invalidMain, "base-game-pools-recovery-draft.png");
    string invalidBackup = Directory.GetFiles(BaseGamePlayerPools.Folder(invalidRoot), "settings.txt.recovery-*.bak").Single();
    Check(File.ReadAllText(invalidBackup) == "schema = 2\nenabled = yes\n" && File.ReadAllText(oldPrivate) == "Name = Kept edit\nSpeed = 71\n", "recovery backs up exact invalid settings and preserves old private player bytes");
    ((Control)invalidMain.Content!).GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "BaseGamePoolsSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
    Check(BaseGamePlayerPools.Load(invalidRoot).Pools.Values.All(p => p.Length > 0) && BaseGamePlayerPools.Load(invalidRoot).Enabled && File.ReadAllText(invalidBackup) == "schema = 2\nenabled = yes\n", "explicit Save atomically replaces the corrupt profile with valid pools while keeping its backup");
    string missingRoot = Path.Combine(root, "missing-private-candidate"); var missingContent = new BaseGamePoolsContent(missingRoot, cache); missingContent.Selection.Enabled = false; missingContent.Save();
    string missingSettings = Path.Combine(BaseGamePlayerPools.Folder(missingRoot), "settings.txt"), missingSettingsBefore = File.ReadAllText(missingSettings);
    string missingCandidate = BaseGamePlayerPools.SelectedFiles(missingRoot, "free_agents", missingContent.Selection)[0]; File.Delete(missingCandidate);
    var remainingBytes = Directory.GetFiles(BaseGamePlayerPools.Folder(missingRoot), "*.txt", SearchOption.AllDirectories).ToDictionary(f => f, File.ReadAllText);
    var missingMain = new MainWindow(["--content", missingRoot]);
    await (Task)typeof(MainWindow).GetMethod("ShowBaseGamePools", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(missingMain, null)!;
    Check(missingMain.Content is not BaseGamePoolsView && remainingBytes.All(p => File.ReadAllText(p.Key) == p.Value) && !File.Exists(missingCandidate), "missing private file opens a passive error and does not rebuild or activate the profile");
    ((Control)missingMain.Content!).GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "BaseGamePoolsRecover").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
    Check(missingMain.Content is BaseGamePoolsView { HasUnsavedChanges: true }
        && !((Control)missingMain.Content!).GetLogicalDescendants().OfType<ToggleSwitch>().Single(t => t.Name == "BaseGamePoolsEnabled").IsChecked!.Value
        && File.ReadAllText(missingSettings) == missingSettingsBefore, "missing-file recovery stages defaults while retaining the disabled profile intent");
    ((Control)missingMain.Content!).GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "BaseGamePoolsSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
    Check(!BaseGamePlayerPools.Load(missingRoot).Enabled && BaseGamePlayerPools.Load(missingRoot).Pools.Values.All(p => p.Length > 0)
        && remainingBytes.Where(p => p.Key != missingSettings).All(p => File.ReadAllText(p.Key) == p.Value), "recovered missing-file profile saves valid pools and leaves surviving private files unchanged");
    Console.WriteLine($"{checks} Base Game pool checks passed.");
}
catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
finally { Directory.Delete(root, true); }
