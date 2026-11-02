using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CampaignCreator;

internal sealed record CommunityCampaignPost(string Name, string Path, long Size, string Modified,
    string Id, string Revision, string ContentHash, CommunityPresentation? Presentation = null, string MetadataWarning = "", string Kind = "campaign");
internal sealed record ManagedCampaign(CommunityOwnership Ownership, CommunityCampaignPost? Post, string AccessError = "")
{
    public override string ToString() => (Post?.Name ?? Ownership.Name) + (Post == null ? " (not posted)" : "");
}

internal sealed class CommunityCampaignClient(HttpClient http, string workerUrl, CommunityOwnershipStore ownership, string kind = "campaign")
{
    internal string Kind { get; } = ValidKind(kind);
    private string ContentName => Kind == "base-game-pools" ? "Base Game Pools set" : "campaign";
    private string ContentNames => Kind == "base-game-pools" ? "Base Game Pools sets" : "campaigns";

    private async Task<JsonDocument> SendAsync(string route, object? body = null, string? code = null)
    {
        using var request = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, workerUrl + route);
        if (body != null) request.Content = JsonContent.Create(body);
        if (code != null) request.Headers.Add("X-Campaign-Manage", code);
        using var response = await http.SendAsync(request);
        return await ReadResponse(response);
    }

    private static async Task<JsonDocument> ReadResponse(HttpResponseMessage response)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); }
        catch (JsonException) { throw new InvalidOperationException($"The community service returned HTTP {(int)response.StatusCode}. Please retry shortly."); }
        var value = document.RootElement;
        if (!response.IsSuccessStatusCode || !value.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True)
        {
            string error = Text(value, "error");
            document.Dispose();
            throw new InvalidOperationException(error.Length > 0 ? error : "The community request failed.");
        }
        return document;
    }

    private static string Text(JsonElement item, string field) => item.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static string ReadKind(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object) throw new InvalidDataException("The community service returned an invalid post.");
        return item.TryGetProperty("kind", out var kind)
            ? ValidKind(kind.ValueKind == JsonValueKind.String ? kind.GetString() : null) : "campaign";
    }

    private static string ValidKind(string? kind) => kind is "campaign" or "base-game-pools" ? kind
        : throw new InvalidDataException("The community service returned an unsupported content category.");

    private static void RequireCategory(string kind, string path, string expected)
    {
        ValidKind(kind);
        string pattern = kind == "base-game-pools"
            ? @"^/managed-pools/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12} - [^/\\\x00-\x1f]+\.zip\z"
            : @"^/(?:managed/)?[^/\\\x00-\x1f]+\.zip\z";
        if (kind != expected || !Regex.IsMatch(path, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new InvalidDataException(expected == "base-game-pools"
                ? "The management access does not identify a Base Game Pools set."
                : "The management access does not identify a campaign.");
    }

    private static CommunityCampaignPost ReadPost(JsonElement item)
    {
        string kind = ReadKind(item);
        var presentation = CommunityPresentation.Parse(item, Text(item, "name"));
        string warning = Text(item, "metadataWarning");
        if (warning.Length == 0 && item.TryGetProperty("metadataSaved", out var saved) && saved.ValueKind == JsonValueKind.False)
            warning = kind == "base-game-pools"
                ? "The Base Game Pools ZIP was uploaded, but its description and icon could not be saved."
                : "The campaign ZIP was uploaded, but its description and icon could not be saved.";
        return new(presentation.DisplayName, Text(item, "path"),
            item.TryGetProperty("size", out var size) && size.TryGetInt64(out long bytes) ? bytes : 0,
            Text(item, "modified"), Text(item, "id"), Text(item, "rev"), Text(item, "contentHash"), presentation, warning, kind);
    }

    private async Task EnsureManagementAsync(bool requireMetadata = false)
    {
        using var health = await SendAsync("/health");
        if (!health.RootElement.TryGetProperty("campaignManagement", out var supported) || supported.ValueKind != JsonValueKind.True)
            throw new InvalidOperationException($"The community service needs its campaign-management update before you can upload, replace or delete {ContentNames}.");
        if (Kind == "base-game-pools" && (!health.RootElement.TryGetProperty("baseGamePools", out var pools) || pools.ValueKind != JsonValueKind.True))
            throw new InvalidOperationException("The community service needs its Base Game Pools sharing update before you can manage pool sets.");
        if (requireMetadata && (!health.RootElement.TryGetProperty("campaignMetadata", out var metadata) || metadata.ValueKind != JsonValueKind.True))
            throw new InvalidOperationException($"The community service needs its campaign-details update before you can upload {ContentName} descriptions and icons.");
    }

    internal async Task<IReadOnlyList<ManagedCampaign>> OwnedAsync()
    {
        var saved = ownership.Load(); // Never hide a damaged credential file as an empty list.
        await EnsureManagementAsync();
        var result = new List<ManagedCampaign>();
        foreach (var entry in saved)
        {
            CommunityCampaignPost? post;
            try
            {
                RequireCategory(Kind, entry.Path, Kind);
                // Verify the saved code before enabling any management action.
                using var status = await SendAsync("/manage/status", new { }, entry.Code);
                post = status.RootElement.TryGetProperty("exists", out var exists) && exists.ValueKind == JsonValueKind.True
                    ? ReadPost(status.RootElement.GetProperty("item")) : null;
                if (post != null) RequireCategory(post.Kind, post.Path, Kind);
                if (post != null && (post.Id.Length == 0 || post.Revision.Length == 0 ||
                    (entry.Id.Length > 0 ? post.Id != entry.Id : !post.Path.Equals(entry.Path, StringComparison.OrdinalIgnoreCase))))
                    throw new InvalidOperationException($"The saved code does not match this {ContentName}.");
            }
            catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or HttpRequestException or TaskCanceledException)
            {
                result.Add(new(entry, null, "Access could not be verified: " + ex.Message));
                continue;
            }
            var current = entry;
            if (post != null && (entry.Id != post.Id || entry.Path != post.Path))
            {
                current = entry with { Id = post.Id, Path = post.Path };
                ownership.Save(current);
            }
            result.Add(new(current, post));
        }
        return result;
    }

    private CommunityOwnership SavedFor(ManagedCampaign campaign)
    {
        var saved = ownership.Load().SingleOrDefault(x => x.Key == campaign.Ownership.Key);
        if (saved == null || saved.Code != campaign.Ownership.Code || campaign.Post == null ||
            (saved.Id.Length > 0 ? saved.Id != campaign.Post.Id : !saved.Path.Equals(campaign.Post.Path, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"No matching saved management code was found for this {ContentName}. Refresh your uploads or restore its access code.");
        RequireCategory(Kind, saved.Path, Kind);
        RequireCategory(campaign.Post.Kind, campaign.Post.Path, Kind);
        if (campaign.Post.Id.Length == 0 || campaign.Post.Revision.Length == 0)
            throw new InvalidOperationException($"Refresh your uploads before changing this {ContentName}.");
        return saved;
    }

    internal async Task<CommunityCampaignPost> UploadAsync(string zipPath, string name, IProgress<double>? progress = null, ManagedCampaign? replacement = null,
        CommunityPresentation? metadata = null)
    {
        metadata = metadata?.ValidateForUpload(name); // Reject invalid details before reserving ownership or uploading bytes.
        await using var stream = File.OpenRead(zipPath);
        if (stream.Length <= 0) throw new InvalidOperationException(Kind == "base-game-pools" ? "Base Game Pools ZIPs must not be empty." : "Campaign ZIPs must not be empty.");
        ownership.Load(); // Validate persistence before reserving an upload.
        await EnsureManagementAsync(metadata != null);
        CommunityOwnership saved;
        if (replacement == null)
        {
            object prepareBody = Kind == "base-game-pools" ? new { name, kind = Kind } : new { name };
            using var prepared = await SendAsync("/manage/prepare", prepareBody);
            string code = Text(prepared.RootElement, "managementCode"), path = Text(prepared.RootElement, "path");
            if (code.Length == 0 || path.Length == 0) throw new InvalidDataException("The community service did not return management access. Nothing was uploaded.");
            RequireCategory(ReadKind(prepared.RootElement), path, Kind);
            saved = new(Guid.NewGuid().ToString("N"), code, name, path);
            ownership.Save(saved); // Durable before the first request that can publish bytes.
        }
        else saved = SavedFor(replacement);

        using var start = await SendAsync("/upload/start", new { name, size = stream.Length,
            operation = replacement == null ? "create" : "replace", rev = replacement?.Post?.Revision }, saved.Code);
        if (Kind == "base-game-pools" || start.RootElement.TryGetProperty("path", out _))
        {
            string sessionPath = Text(start.RootElement, "path");
            RequireCategory(Kind, sessionPath, Kind);
            if (!sessionPath.Equals(saved.Path, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The upload session does not match the saved management access. Nothing was uploaded.");
        }
        string ticket = Text(start.RootElement, "ticket");
        int chunkSize = start.RootElement.GetProperty("chunkBytes").GetInt32();
        if (ticket.Length == 0 || chunkSize < 1 || chunkSize > 8 * 1024 * 1024) throw new InvalidDataException("The upload session is invalid.");
        byte[] buffer = new byte[chunkSize]; long offset = 0;
        while (offset < stream.Length)
        {
            int count = (int)Math.Min(buffer.Length, stream.Length - offset);
            await stream.ReadExactlyAsync(buffer.AsMemory(0, count));
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, workerUrl + "/upload/chunk?offset=" + offset);
                    request.Headers.Add("X-Campaign-Upload", ticket);
                    request.Content = new ByteArrayContent(buffer, 0, count);
                    request.Content.Headers.ContentType = new("application/octet-stream");
                    using var response = await http.SendAsync(request);
                    using var chunk = await ReadResponse(response);
                    if (chunk.RootElement.GetProperty("offset").GetInt64() != offset + count) throw new InvalidDataException("The upload service returned an incorrect position.");
                    break;
                }
                catch (Exception ex) when (attempt < 2 && ex is HttpRequestException or TaskCanceledException)
                { await Task.Delay(1000 * (attempt + 1)); }
            }
            offset += count; progress?.Report(99.0 * offset / stream.Length);
        }
        using var finishRequest = new HttpRequestMessage(HttpMethod.Post, workerUrl + "/upload/finish");
        finishRequest.Headers.Add("X-Campaign-Upload", ticket);
        finishRequest.Headers.Add("X-Campaign-Manage", saved.Code);
        if (metadata != null) finishRequest.Content = JsonContent.Create(new { metadata });
        using var finishResponse = await http.SendAsync(finishRequest);
        using var finish = await ReadResponse(finishResponse);
        var posted = ReadPost(finish.RootElement);
        RequireCategory(posted.Kind, posted.Path, Kind);
        if (posted.Id.Length == 0 || posted.Revision.Length == 0 ||
            (saved.Id.Length > 0 ? posted.Id != saved.Id : !posted.Path.Equals(saved.Path, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException($"The upload returned an unexpected {ContentName}. Your management code is saved; refresh your uploads to check the result.");
        ownership.Save(saved with { Path = posted.Path, Id = posted.Id });
        progress?.Report(100);
        return posted;
    }

    internal async Task DeleteAsync(ManagedCampaign campaign)
    {
        var saved = SavedFor(campaign);
        using var result = await SendAsync("/manage/delete", new { rev = campaign.Post!.Revision }, saved.Code);
        if (!result.RootElement.TryGetProperty("deleted", out var deleted) || deleted.ValueKind != JsonValueKind.True || Text(result.RootElement, "id") != campaign.Post.Id)
            throw new InvalidDataException("Deletion could not be confirmed. Refresh your uploads before retrying.");
        ownership.Remove(saved.Key);
    }

    internal async Task ImportCodeAsync(string code)
    {
        code = code.Trim();
        if (code.Length == 0 || code.Length > 8192) throw new ArgumentException($"Enter the {ContentName} management code provided by its owner or administrator.");
        ownership.Load();
        using var response = await SendAsync("/manage/status", new { }, code);
        if (!response.RootElement.TryGetProperty("exists", out var exists) || exists.ValueKind != JsonValueKind.True)
            throw new InvalidOperationException($"This code does not currently identify a posted {ContentName}.");
        var post = ReadPost(response.RootElement.GetProperty("item"));
        RequireCategory(post.Kind, post.Path, Kind);
        if (post.Id.Length == 0 || post.Path.Length == 0) throw new InvalidDataException($"The {ContentName} identity is missing.");
        var saved = ownership.Find(post.Path, post.Id);
        ownership.Save(new(saved?.Key ?? Guid.NewGuid().ToString("N"), code, post.Name, post.Path, post.Id));
    }
}
