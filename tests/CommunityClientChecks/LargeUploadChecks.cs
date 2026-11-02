using System.Net;
using System.Text;
using System.Text.Json;
using CampaignCreator;

internal static class LargeUploadChecks
{
    internal static async Task Run(Action<bool, string> check, string folder)
    {
        string file = Path.Combine(folder, "large-upload.zip");
        const long length = 536870929; // 512 MiB plus 17 bytes, deliberately not chunk aligned.
        await using (var stream = File.Create(file))
        {
            stream.SetLength(length);
            stream.Write([80, 75, 3, 4]);
            stream.Position = length - 1;
            stream.WriteByte(173);
        }
        var ownership = new CommunityOwnershipStore(Path.Combine(folder, "large-ownership.json"));
        using var handler = new LargeUploadHandler(check, ownership, length);
        using var http = new HttpClient(handler);
        var client = new CommunityCampaignClient(http, "https://large-upload.invalid", ownership);
        var posted = await client.UploadAsync(file, "Large campaign");
        check(posted.Size == length && posted.Revision == "large-r1", "Large create must finish with the full file size");
        var saved = ownership.Find(posted.Path, posted.Id)!;
        var replaced = await client.UploadAsync(file, "Large campaign", replacement: new(saved, posted));
        check(replaced.Id == posted.Id && replaced.Size == length && replaced.Revision == "large-r2", "Large replacement must retain identity and finish with the full size");
        check(handler.Completed == 2 && handler.Prepared == 1, "Large replacement must reuse saved ownership");
        check(new FileInfo(file).Length == length, "Large uploads must preserve the source ZIP");

        string empty = Path.Combine(folder, "empty-upload.zip");
        File.WriteAllBytes(empty, []);
        int requests = handler.Requests;
        bool rejected = false;
        try { await client.UploadAsync(empty, "Empty campaign"); }
        catch (InvalidOperationException) { rejected = true; }
        check(rejected && handler.Requests == requests, "Empty ZIPs must fail before network requests or reservations");
        File.Delete(file);
    }

    private sealed class LargeUploadHandler(Action<bool, string> check, CommunityOwnershipStore ownership, long size) : HttpMessageHandler
    {
        public int Completed, Prepared, Requests;
        private long received;
        private string operation = "";
        private const string Path = "/managed/large.zip", Code = "private-large-code";
        private const int ChunkBytes = 1048576;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            string route = request.RequestUri!.AbsolutePath;
            string code = request.Headers.TryGetValues("X-Campaign-Manage", out var codes) ? codes.Single() : "";
            HttpResponseMessage Json(object data) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json") };
            if (route == "/health") return Json(new { ok = true, campaignManagement = true, chunkedUpload = true, maxCampaignBytes = (long?)null });
            if (route == "/manage/prepare")
            {
                Prepared++;
                return Json(new { ok = true, managementCode = Code, path = Path, name = "Large campaign" });
            }
            if (route == "/upload/start")
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                check(body.RootElement.GetProperty("size").GetInt64() == size, "Start must declare actual large file length");
                check(code == Code && ownership.Find(Path)?.Code == Code, "Large upload ownership must be saved before start");
                operation = body.RootElement.GetProperty("operation").GetString()!;
                check(operation == (Completed == 0 ? "create" : "replace"), "Start must choose the correct large upload operation");
                if (operation == "replace") check(body.RootElement.GetProperty("rev").GetString() == "large-r1", "Replacement must supply selected revision");
                received = 0;
                return Json(new { ok = true, ticket = "large-ticket", chunkBytes = ChunkBytes, path = Path });
            }
            check(request.Headers.GetValues("X-Campaign-Upload").Single() == "large-ticket", "Large upload must present its server ticket");
            if (route == "/upload/chunk")
            {
                check(long.Parse(request.RequestUri.Query["?offset=".Length..]) == received, "Every large chunk must use its actual cumulative offset");
                await using var bytes = await request.Content!.ReadAsStreamAsync(cancellationToken);
                long expectedCount = Math.Min(ChunkBytes, size - received);
                check(request.Content.Headers.ContentLength == expectedCount && bytes.Length == expectedCount, "Large upload requests must stay within the advertised chunk size");
                if (received == 0) check(bytes.ReadByte() == 80 && bytes.ReadByte() == 75 && bytes.ReadByte() == 3 && bytes.ReadByte() == 4, "Large upload must stream source bytes");
                if (received + expectedCount == size)
                {
                    bytes.Position = expectedCount - 1;
                    check(bytes.ReadByte() == 173, "Final large chunk must include the source file tail");
                }
                received += expectedCount;
                return Json(new { ok = true, offset = received });
            }
            if (route == "/upload/finish")
            {
                check(received == size && code == Code, "Large upload must finish only after all bytes with saved management access");
                Completed++;
                return Json(new { ok = true, id = "id:large", path = Path, name = "Large campaign", rev = operation == "create" ? "large-r1" : "large-r2", size, modified = "2026-11-01T00:00:00Z", contentHash = "large-hash" });
            }
            throw new Exception("Unexpected large upload request: " + route);
        }
    }
}
