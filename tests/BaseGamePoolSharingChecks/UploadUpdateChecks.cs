using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
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
                    check(!dialog.GetLogicalDescendants().OfType<Button>().Any(b => b.Name == "UpdateCommunityPoolsFromSaved" && b.IsVisible), "campaign management retains Replace ZIP without a pool update action");
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
