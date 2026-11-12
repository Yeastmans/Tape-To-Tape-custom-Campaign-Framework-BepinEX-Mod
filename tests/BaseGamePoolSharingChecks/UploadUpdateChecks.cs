using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CampaignCore;
using CampaignCreator;

internal static class UploadUpdateChecks
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly Assembly Studio = typeof(MainWindow).Assembly;
    private static object Create(string type, params object[] args) => Activator.CreateInstance(Studio.GetType("CampaignCreator." + type)!, Members, null, args, null)!;
    private static T Find<T>(Control control, string name) where T : Control => control.GetLogicalDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Click(Control parent, string name) => Find<Button>(parent, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Pump(Func<bool> ready)
    {
        var end = DateTime.UtcNow.AddSeconds(20);
        do { Dispatcher.UIThread.RunJobs(); if (ready()) return; Thread.Sleep(10); } while (DateTime.UtcNow < end);
        throw new Exception("Pool update UI did not finish.");
    }
    private static void Finish(Task task) { Pump(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static string[] TemporaryPackages() => Directory.GetFiles(Path.GetTempPath(), "t2t-pool-update-*.zip").Order(StringComparer.Ordinal).ToArray();

    internal static void Run(string sandbox, Action<bool, string> check)
    {
        string root = Path.Combine(sandbox, "saved-upload-update");
        var selection = new BaseGamePoolSelection();
        foreach (string kind in BaseGamePlayerPools.Kinds)
        {
            string folder = BaseGamePlayerPools.PoolDirectory(root, kind); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "selected.txt"), $"Name = Latest saved {kind}\nPosition = Center\nSpeed = 72\n");
            File.WriteAllText(Path.Combine(folder, "private.txt"), "Name = PRIVATE UNSELECTED\nPosition = Center\n");
            selection.Pools[kind] = ["selected.txt"];
        }
        BaseGamePlayerPools.Save(root, selection);
        using var bitmap = new SkiaSharp.SKBitmap(8, 8);
        bitmap.Erase(SkiaSharp.SKColors.SeaGreen);
        using var icon = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        var talent = new CustomRewardDefinition { Id = "e328a1b6-2a48-433a-8aca-77670f70d30f", Name = "Travelling talent", Stat = "Speed", Amount = 19, IconPng = Convert.ToBase64String(icon.ToArray()) };
        talent.Save(Path.Combine(root, "library", "rewards"));
        string candidate = Path.Combine(BaseGamePlayerPools.PoolDirectory(root, "draft_pool"), "selected.txt");
        File.AppendAllText(candidate, "Talents = " + talent.Id + "\n");
        string before = File.ReadAllText(candidate);
        var clientType = Studio.GetType("CampaignCreator.CommunityCampaignClient")!;
        var management = typeof(MainWindow).GetMethod("BuildCommunityManagementWindow", Members, null, [clientType], null);
        check(management != null, "the management dialog can use the selected category's client for saved-pool updates");

        void Exercise(string kind)
        {
            using var handler = new Service(kind);
            using var http = new HttpClient(handler);
            var store = Create("CommunityOwnershipStore", Path.Combine(root, kind + "-ownership.json"));
            var client = Create("CommunityCampaignClient", http, "https://pool-update.invalid", store, kind);
            Finish((Task)clientType.GetMethod("ImportCodeAsync", Members)!.Invoke(client, ["private-owner-code"])!);
            var main = new MainWindow(["--content", root]);
            var owner = new Window { Content = main.Content, Width = 1280, Height = 850 }; main.Content = null;
            owner.Show(); Dispatcher.UIThread.RunJobs();
            try
            {
                var dialog = (Window)management!.Invoke(main, [client])!;
                var shown = dialog.ShowDialog(owner);
                var list = Find<ListBox>(dialog, "OwnedCommunityCampaigns");
                Pump(() => list.Items.Count == 1 && Find<Button>(dialog, "ReplaceCommunityCampaign").IsEnabled);
                if (kind == "campaign")
                {
                    ExerciseCampaign(dialog, root, handler, check);
                    dialog.Close(); Finish(shown); return;
                }
                var update = Find<Button>(dialog, "UpdateCommunityPoolsFromSaved");
                check(update.IsEnabled && update.Content?.ToString() == "Update from saved pools…", "owned pool listings have a direct saved-pools update action");
                object selected = list.SelectedItem!; list.SelectedItem = null;
                check(!update.IsEnabled, "saved-pool updates require a verified selected listing");
                list.SelectedItem = selected;
                string[] tempBefore = TemporaryPackages(); int requestsBefore = handler.UploadStarts;
                Click(dialog, update.Name!);
                Pump(() => dialog.OwnedWindows.Any());
                var confirmation = dialog.OwnedWindows.Single();
                string text = string.Join(" ", confirmation.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text));
                check(text.Contains("Weekend pools") && text.Contains("Bench/draft: 1") && text.Contains("Free agents: 1") && text.Contains("Superstars: 1"), "confirmation names the existing listing and all three saved pool counts");
                check(!update.IsEnabled && !Find<Button>(dialog, "ReplaceCommunityCampaign").IsEnabled, "replacement controls stay disabled while confirmation is pending");
                Click(confirmation, "CancelSavedBaseGamePoolUpdate");
                Pump(() => update.IsEnabled);
                check(handler.UploadStarts == requestsBefore && File.ReadAllText(candidate) == before, "cancelling a saved-pool update sends no upload and preserves local content");
                check(tempBefore.SequenceEqual(TemporaryPackages()), "cancelled saved-pool updates remove their temporary package");

                Click(dialog, update.Name!); Pump(() => dialog.OwnedWindows.Any());
                Click(dialog.OwnedWindows.Single(), "ConfirmSavedBaseGamePoolUpdate");
                Pump(() => update.IsEnabled);
                check(handler.UploadStarts == requestsBefore + 1 && handler.Finishes == 1 && handler.Prepares == 0,
                    "saved-pool updates replace the owned listing without creating a duplicate post");
                using (var start = JsonDocument.Parse(handler.StartBody))
                    check(start.RootElement.GetProperty("operation").GetString() == "replace" && start.RootElement.GetProperty("rev").GetString() == "old-revision", "saved-pool updates preserve the selected revision guard");
                check(handler.StartCode == "private-owner-code" && handler.FinishBody.Length == 0, "saved-pool updates retain ownership and leave title, description and cover metadata unchanged");
                using (var archive = new ZipArchive(new MemoryStream(handler.Uploaded.ToArray()), ZipArchiveMode.Read))
                {
                    check(BaseGamePlayerPools.Kinds.All(k => archive.GetEntry("base_game_pools/player_teams/" + k + "/selected.txt") != null), "the replacement contains each of the three saved selections");
                    check(!archive.Entries.Any(e => e.FullName.Contains("private") || e.FullName.Contains("ownership")), "replacement excludes unselected players and private management access");
                    using var reader = new StreamReader(archive.GetEntry("base_game_pools/player_teams/draft_pool/selected.txt")!.Open());
                    var doc = new CampaignDocument(reader.ReadToEnd());
                    check(doc.Get("Speed") == "72" && CustomRewardExport.Read(doc).Single().Amount == 19, "replacement contains the latest saved player and their complete custom talent");
                }
                check(tempBefore.SequenceEqual(TemporaryPackages()) && File.ReadAllText(candidate) == before, "successful replacement removes the temporary package without changing saved pools");

                var invalid = new CampaignDocument(before); invalid.Set("Talents", "custom_missing"); invalid.Save(candidate);
                requestsBefore = handler.UploadStarts;
                Click(dialog, update.Name!); Pump(() => update.IsEnabled);
                check(handler.UploadStarts == requestsBefore && !dialog.OwnedWindows.Any(), "invalid saved pools fail before confirmation or any upload request");
                check(tempBefore.SequenceEqual(TemporaryPackages()), "failed packaging removes its temporary file");
                File.WriteAllText(candidate, before);

                handler.RejectStart = true;
                Click(dialog, update.Name!); Pump(() => dialog.OwnedWindows.Any());
                Click(dialog.OwnedWindows.Single(), "ConfirmSavedBaseGamePoolUpdate");
                Pump(() => update.IsEnabled);
                check(handler.Finishes == 1 && dialog.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("stale revision") == true), "stale-revision rejection is reported and never finishes a replacement");
                check(tempBefore.SequenceEqual(TemporaryPackages()) && File.ReadAllText(candidate) == before, "failed network replacement restores controls, removes its package and keeps saved pools");

                string settings = Path.Combine(BaseGamePlayerPools.Folder(root), "settings.txt");
                string savedSettings = File.ReadAllText(settings); File.Delete(settings);
                Click(dialog, "RefreshManagedCampaigns"); Pump(() => Find<Button>(dialog, "RefreshManagedCampaigns").IsEnabled);
                check(!update.IsEnabled, "saved-pool update stays disabled when there are no saved pools");
                File.WriteAllText(settings, savedSettings);
                dialog.Close(); Finish(shown);
            }
            finally { foreach (var window in owner.OwnedWindows.ToArray()) window.Close(); owner.Close(); }
        }
        Exercise("base-game-pools"); Exercise("campaign");
    }

    private static void ExerciseCampaign(Window dialog, string root, Service handler, Action<bool, string> check)
    {
        var update = Find<Button>(dialog, "ReplaceCommunityCampaign");
        check(update.Content?.ToString() == "Update ZIP…" && !dialog.GetLogicalDescendants().OfType<Button>().Any(b => b.Name == "UpdateCommunityPoolsFromSaved"),
            "campaign management offers one Update ZIP action");
        var listings = Find<ListBox>(dialog, "OwnedCommunityCampaigns");
        var owned = listings.SelectedItem;
        listings.SelectedItem = null;
        check(!update.IsEnabled, "updating a campaign requires a verified selected listing");
        listings.SelectedItem = owned;
        Window ChooseSource()
        {
            Click(dialog, update.Name!);
            Pump(() => dialog.OwnedWindows.Any());
            return dialog.OwnedWindows.Single();
        }
        var chooser = ChooseSource();
        check(chooser.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("Weekend pools") == true),
            "the source chooser identifies the public listing that will be replaced");
        check(!update.IsEnabled && !Find<Button>(chooser, "UpdateFromSavedCampaign").IsEnabled && Find<Button>(chooser, "UpdateFromCampaignZip").IsEnabled,
            "with no saved campaigns the ZIP picker remains available");
        Click(chooser, "CancelCampaignUpdate"); Pump(() => update.IsEnabled);
        check(handler.UploadStarts == 0, "cancel leaves the owned listing alone");

        var filePicker = DispatchProxy.Create<IStorageProvider, PickerResponse>();
        var pickerReply = (PickerResponse)(object)filePicker;
        typeof(TopLevel).GetField("_storageProvider", Members)!.SetValue(dialog, filePicker);
        void ChooseFile(string? file)
        {
            pickerReply.Respond = (method, args) =>
            {
                if (method.Name != nameof(IStorageProvider.OpenFilePickerAsync)) throw new NotSupportedException(method.Name);
                var options = (FilePickerOpenOptions)args![0]!;
                check(options.FileTypeFilter!.Single().Patterns!.SequenceEqual(["*.zip", "*.t2tcampaign"]), "existing ZIP picker accepts both campaign package extensions");
                IStorageFile[] files = [];
                if (file != null)
                {
                    var selected = DispatchProxy.Create<IStorageFile, PickerResponse>();
                    ((PickerResponse)(object)selected).Respond = (member, _) => member.Name == "get_Path" ? new Uri(file) : throw new NotSupportedException(member.Name);
                    files = [selected];
                }
                return Task.FromResult<IReadOnlyList<IStorageFile>>(files);
            };
            Click(ChooseSource(), "UpdateFromCampaignZip"); Pump(() => update.IsEnabled);
        }
        ChooseFile(null);
        check(handler.UploadStarts == 0, "closing the native picker sends no upload");
        string prepared = Path.Combine(root, "prepared.t2tcampaign");
        using (var archive = ZipFile.Open(prepared, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(archive.CreateEntry("Prepared/other.txt").Open())) writer.Write("existing package");
        ChooseFile(prepared);
        check(handler.UploadStarts == 0 && dialog.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("campaign.txt is missing") == true),
            "a ZIP missing its campaign manifest reports the validation error before upload");
        using (var archive = ZipFile.Open(prepared, ZipArchiveMode.Update))
        using (var writer = new StreamWriter(archive.CreateEntry("Prepared/campaign.txt").Open())) writer.Write("Name = Prepared");
        byte[] original = File.ReadAllBytes(prepared);
        ChooseFile(prepared);
        check(handler.Finishes == 1 && handler.Uploaded.ToArray().SequenceEqual(original) && File.ReadAllBytes(prepared).SequenceEqual(original),
            "an existing campaign ZIP replaces the posted download byte for byte and is kept on disk");
        CheckReplacement("old-revision");

        string campaigns = Path.Combine(root, "campaigns");
        Directory.CreateDirectory(Path.Combine(campaigns, "Incomplete"));
        foreach (string name in new[] { "Other", "Saved season" })
        {
            Directory.CreateDirectory(Path.Combine(campaigns, name));
            File.WriteAllText(Path.Combine(campaigns, name, "campaign.txt"), "Name = " + name);
        }
        string saved = Path.Combine(campaigns, "Saved season");
        string logo = Path.Combine(root, "fixture-logo.png");
        using (var bitmap = new SkiaSharp.SKBitmap(8, 8))
        {
            bitmap.Erase(SkiaSharp.SKColors.Gold);
            using var png = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(logo, png.ToArray());
        }
        string teamText = "Name = Recent edits\nLogo From = " + logo;
        string team = Path.Combine(saved, "team.txt");
        File.WriteAllText(team, teamText);
        File.WriteAllText(Path.Combine(saved, "save.txt"), "private local progress");
        string[] Packages() => Directory.GetFiles(Path.GetTempPath(), "t2t-campaign-update-*.zip").Order(StringComparer.Ordinal).ToArray();
        var before = Packages();
        void ChooseSaved(Action? beforeConfirm = null)
        {
            var choice = ChooseSource();
            var campaignsList = Find<ComboBox>(choice, "CampaignUpdatePicker");
            check(campaignsList.Items.Select(x => x!.ToString()).SequenceEqual(["Other", "Saved season"]), "the saved picker lists all and only valid campaign folders");
            campaignsList.SelectedIndex = 1;
            beforeConfirm?.Invoke();
            Click(choice, "UpdateFromSavedCampaign"); Pump(() => update.IsEnabled);
        }
        ChooseSaved();
        check(handler.Finishes == 2 && handler.UploadStarts == 2, "the chosen saved campaign replaces the same listing");
        CheckReplacement("new-revision");
        using (var zip = new ZipArchive(new MemoryStream(handler.Uploaded.ToArray()), ZipArchiveMode.Read))
        {
            check(zip.Entries.All(e => e.FullName.StartsWith("Saved season/", StringComparison.Ordinal)) && zip.GetEntry("Saved season/campaign.txt") != null && zip.GetEntry("Saved season/save.txt") == null,
                "saved update packages the chosen campaign and excludes local progress");
            using var doc = new StreamReader(zip.GetEntry("Saved season/team.txt")!.Open());
            var exported = new CampaignDocument(doc.ReadToEnd());
            var asset = zip.GetEntry("Saved season/_logos/" + exported.Get("Logo From"));
            using var bytes = new MemoryStream();
            using (var stream = asset!.Open()) stream.CopyTo(bytes);
            check(exported.Get("Name") == "Recent edits" && bytes.ToArray().SequenceEqual(File.ReadAllBytes(logo)), "the campaign exporter bundles the latest team and its portable logo");
        }
        check(Packages().SequenceEqual(before) && File.ReadAllText(team) == teamText, "a completed saved update removes its temporary ZIP and preserves the local team");
        handler.RejectStart = true;
        ChooseSaved();
        check(handler.UploadStarts == 3 && handler.Finishes == 2 && Packages().SequenceEqual(before) && dialog.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("stale revision") == true),
            "a rejected saved update reports the error, restores controls and removes its temporary ZIP");
        using (var locked = File.Open(Path.Combine(saved, "locked.bin"), FileMode.Create, FileAccess.Write, FileShare.None))
            ChooseSaved();
        check(handler.UploadStarts == 3 && Packages().SequenceEqual(before) && File.ReadAllText(team) == teamText,
            "partial campaign packaging failure deletes the temporary ZIP before any upload");
        ChooseSaved(() => File.Delete(Path.Combine(saved, "campaign.txt")));
        check(handler.UploadStarts == 3 && Packages().SequenceEqual(before), "a saved campaign removed since selection cannot replace a listing with an invalid ZIP");

        void CheckReplacement(string revision)
        {
            using var start = JsonDocument.Parse(handler.StartBody);
            check(handler.Prepares == 0 && handler.StartCode == "private-owner-code" && handler.FinishBody == "" &&
                start.RootElement.GetProperty("operation").GetString() == "replace" && start.RootElement.GetProperty("rev").GetString() == revision &&
                start.RootElement.GetProperty("name").GetString() == "Weekend pools", "campaign replacement uses the saved code, listing name and revision without overwriting metadata");
        }
    }

    public class PickerResponse : DispatchProxy
    {
        internal Func<MethodInfo, object?[]?, object?> Respond = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Respond(method!, args);
    }

    private sealed class Service(string kind) : HttpMessageHandler
    {
        internal int UploadStarts, Finishes, Prepares;
        internal bool RejectStart;
        internal string StartBody = "", StartCode = "", FinishBody = "";
        internal MemoryStream Uploaded = new();
        private string Path => (kind == "base-game-pools" ? "/managed-pools/" : "/managed/") + "12345678-abcd-1234-abcd-123456789abc - Weekend pools.zip";
        private object Post() => new { ok = true, kind, name = "Weekend pools", campaignName = "Weekend pools", author = "Owner", description = "Keep this description", path = Path, size = 200, modified = "2026-11-02T12:00:00Z", id = "existing-post", rev = Finishes > 0 ? "new-revision" : "old-revision", contentHash = "hash" };
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            object reply;
            switch (request.RequestUri!.AbsolutePath)
            {
                case "/health": reply = new { ok = true, campaignManagement = true, campaignMetadata = true, baseGamePools = true }; break;
                case "/manage/status": reply = new { ok = true, exists = true, item = Post() }; break;
                case "/manage/prepare": Prepares++; throw new Exception("An update must never reserve a new post.");
                case "/upload/start":
                    UploadStarts++; StartBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                    StartCode = request.Headers.GetValues("X-Campaign-Manage").Single();
                    if (RejectStart) return Json(new { ok = false, error = "stale revision; refresh before updating" }, HttpStatusCode.Conflict);
                    Uploaded.SetLength(0); reply = new { ok = true, path = Path, ticket = "ticket", chunkBytes = 4096 }; break;
                case "/upload/chunk":
                    byte[] bytes = await request.Content!.ReadAsByteArrayAsync(cancellationToken); Uploaded.Write(bytes);
                    reply = new { ok = true, offset = Uploaded.Length }; break;
                case "/upload/finish":
                    Finishes++; FinishBody = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken); reply = Post(); break;
                default: throw new Exception("Unexpected request: " + request.RequestUri);
            }
            return Json(reply);
        }
        private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
        protected override void Dispose(bool disposing) { if (disposing) Uploaded.Dispose(); base.Dispose(disposing); }
    }
}
