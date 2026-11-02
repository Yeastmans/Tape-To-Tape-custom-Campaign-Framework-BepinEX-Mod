using System.Net;
using System.Text;
using System.Text.Json;
using CampaignCreator;

internal static class PoolSharingChecks
{
    private const string PoolPath = "/managed-pools/12345678-abcd-1234-abcd-123456789abc - Weekend pools.zip";
    private const string CampaignPath = "/managed/12345678-abcd-1234-abcd-123456789abc - Weekend campaign.zip";

    internal static async Task Run(Action<bool, string> check, string folder)
    {
        var failures = new List<Exception>();
        async Task RunCase(string name, Func<Task> action)
        {
            try { await action(); }
            catch (Exception ex) { failures.Add(new Exception(name + ": " + ex.Message, ex)); }
        }
        await RunCase(nameof(ImportedCodesStayInTheirCategory), () => ImportedCodesStayInTheirCategory(check, folder));
        await RunCase(nameof(PoolImportsUseTheirOwnStore), () => PoolImportsUseTheirOwnStore(check, folder));
        await RunCase(nameof(PoolsRequireServiceCapability), () => PoolsRequireServiceCapability(check, folder));
        await RunCase(nameof(PoolsCreateAndReplaceWithDurableCodes), () => PoolsCreateAndReplaceWithDurableCodes(check, folder));
        await RunCase(nameof(ReservationsCannotCrossCategories), () => ReservationsCannotCrossCategories(check, folder));
        await RunCase(nameof(SessionsCannotChangeTheReservedPath), () => SessionsCannotChangeTheReservedPath(check, folder));
        await RunCase(nameof(FinishCannotChangeCategory), () => FinishCannotChangeCategory(check, folder));
        await RunCase(nameof(MetadataFailureRetainsPostedPoolIdentity), () => MetadataFailureRetainsPostedPoolIdentity(check, folder));
        await RunCase(nameof(OwnedPostsStayInTheirCategory), () => OwnedPostsStayInTheirCategory(check, folder));
        await RunCase(nameof(MutationsRejectOtherCategorySelections), () => MutationsRejectOtherCategorySelections(check, folder));
        await RunCase(nameof(VerifiedPoolDeletionRemovesOnlyItsOwnCode), () => VerifiedPoolDeletionRemovesOnlyItsOwnCode(check, folder));
        await RunCase(nameof(LegacyCampaignWireFormatIsPreserved), () => LegacyCampaignWireFormatIsPreserved(check, folder));
        await RunCase(nameof(UnknownClientCategoriesFailBeforeNetwork), () => UnknownClientCategoriesFailBeforeNetwork(check, folder));
        await RunCase(nameof(PoolFallbackNamesHideStorageIdentity), () => PoolFallbackNamesHideStorageIdentity(check));
        if (failures.Count > 0) throw new AggregateException(failures);
    }

    private static async Task PoolImportsUseTheirOwnStore(Action<bool, string> check, string folder)
    {
        using var fixture = new ClientFixture(folder, request => Task.FromResult(Json(new { ok = true, exists = true, item = Post(PoolPath, "base-game-pools") })), "base-game-pools");
        await fixture.Client.ImportCodeAsync(" pool-code ");
        check(fixture.Store.Load().Single().Path == PoolPath, "A pools client must import pool management access into its supplied store");
        foreach (var item in new[] { Post(CampaignPath, "campaign"), Post("/Legacy.zip"), Post(PoolPath), Post(CampaignPath, "base-game-pools") })
        {
            using var rejected = new ClientFixture(folder, request => Task.FromResult(Json(new { ok = true, exists = true, item })), "base-game-pools");
            check(await Failure(() => rejected.Client.ImportCodeAsync("wrong-code")) != null && rejected.Store.Load().Count == 0,
                "Pool imports must reject legacy, campaign and inconsistent category responses without saving codes");
        }
    }

    private static async Task PoolsRequireServiceCapability(Action<bool, string> check, string folder)
    {
        foreach (object? capability in new object?[] { null, false, "true", 1 })
        {
            using var fixture = UploadFixture(folder, request => request.Route == "/health"
                ? Json(new { ok = true, campaignManagement = true, campaignMetadata = true, baseGamePools = capability }) : null);
            var failure = await Failure(() => fixture.Client.UploadAsync(fixture.ZipPath, "Weekend pools"));
            check(failure != null && failure.Message.Contains("pool", StringComparison.OrdinalIgnoreCase), "Pool uploads must report missing pool capability clearly");
            check(fixture.Requests.Count == 1 && fixture.Requests.Single().Route == "/health" && fixture.Store.Load().Count == 0,
                "Pool capability must be verified before reserving ownership or publishing bytes");
        }
    }

    private static async Task PoolsCreateAndReplaceWithDurableCodes(Action<bool, string> check, string folder)
    {
        ClientFixture? fixture = null;
        int completed = 0;
        using (fixture = UploadFixture(folder, request =>
        {
            if (request.Route == "/upload/start")
                check(fixture!.Store.Load().Single().Code == "private-code", "Pool management codes must be durably saved before upload start");
            if (request.Route == "/upload/finish") return Json(Post(PoolPath, "base-game-pools", "r" + ++completed));
            return null;
        }))
        {
            var metadata = new CommunityPresentation("Weekend pools", "Pool author", "Three selected pools", DisplayName: "Weekend pools by Pool author");
            var posted = await fixture.Client.UploadAsync(fixture.ZipPath, "Weekend pools", metadata: metadata);
            check(posted.Kind == "base-game-pools" && posted.Path == PoolPath, "Pool upload results must expose their category");
            var prepared = fixture.Requests.Single(x => x.Route == "/manage/prepare");
            using var prepare = JsonDocument.Parse(prepared.Body);
            check(prepare.RootElement.TryGetProperty("kind", out var preparedKind) && preparedKind.GetString() == "base-game-pools" && prepare.RootElement.GetProperty("name").GetString() == "Weekend pools",
                "Pool prepare must explicitly select the pool namespace");
            var started = fixture.Requests.Single(x => x.Route == "/upload/start");
            using var start = JsonDocument.Parse(started.Body);
            check(start.RootElement.GetProperty("operation").GetString() == "create" && start.RootElement.GetProperty("size").GetInt64() == 7 && started.ManageCode == "private-code",
                "Pool creation must use the managed chunked upload protocol");
            check(fixture.Requests.Where(x => x.Route.StartsWith("/upload/chunk?", StringComparison.Ordinal)).SelectMany(x => x.Bytes).SequenceEqual(File.ReadAllBytes(fixture.ZipPath)),
                "Pool uploads must transmit the unchanged source archive bytes");
            var finishRequest = fixture.Requests.Single(x => x.Route == "/upload/finish");
            using var finish = JsonDocument.Parse(finishRequest.Body);
            check(finish.RootElement.GetProperty("metadata").GetProperty("description").GetString() == "Three selected pools"
                && finishRequest.ManageCode == "private-code" && finishRequest.UploadCode == "upload-ticket", "Pool metadata must finish with both managed credentials");
            var saved = fixture.Store.Load().Single();
            check(saved.Id == posted.Id && saved.Path == PoolPath, "Successful pool upload must retain its posted identity with its private code");
            var replaced = await fixture.Client.UploadAsync(fixture.ZipPath, "Weekend pools", replacement: new(saved, posted));
            check(replaced.Id == posted.Id && replaced.Revision == "r2" && fixture.Store.Load().Count == 1,
                "Pool replacement must preserve the original management identity");
            check(fixture.Requests.Count(x => x.Route == "/manage/prepare") == 1, "Pool replacement must reuse the saved management code");
            using var replacementStart = JsonDocument.Parse(fixture.Requests.Last(x => x.Route == "/upload/start").Body);
            check(replacementStart.RootElement.GetProperty("operation").GetString() == "replace" && replacementStart.RootElement.GetProperty("rev").GetString() == "r1",
                "Pool replacement must bind the selected revision");
        }
    }

    private static async Task ReservationsCannotCrossCategories(Action<bool, string> check, string folder)
    {
        foreach (var reservation in new[]
        {
            new { path = CampaignPath, kind = (object?)"campaign" },
            new { path = PoolPath, kind = (object?)"campaign" },
            new { path = CampaignPath, kind = (object?)"base-game-pools" },
            new { path = PoolPath, kind = (object?)"unknown" },
            new { path = PoolPath, kind = (object?)null },
            new { path = "/managed-pools/../campaign.zip", kind = (object?)"base-game-pools" },
            new { path = PoolPath + "\n", kind = (object?)"base-game-pools" }
        })
        {
            using var fixture = UploadFixture(folder, request => request.Route == "/manage/prepare"
                ? Json(new { ok = true, managementCode = "private-code", reservation.path, reservation.kind }) : null);
            check(await Failure(() => fixture.Client.UploadAsync(fixture.ZipPath, "Weekend pools")) != null,
                "Pool reservations must reject unknown, missing, mismatched or unsafe category paths");
            check(fixture.Requests.All(x => !x.Route.StartsWith("/upload/", StringComparison.Ordinal)) && fixture.Store.Load().Count == 0,
                "Invalid reservations must not save access or start publishing");
        }
    }

    private static async Task SessionsCannotChangeTheReservedPath(Action<bool, string> check, string folder)
    {
        foreach (string path in new[] { CampaignPath, PoolPath.Replace("Weekend", "Different", StringComparison.Ordinal) })
        {
            using var fixture = UploadFixture(folder, request => request.Route == "/upload/start"
                ? Json(new { ok = true, ticket = "wrong-target-ticket", chunkBytes = 3, path }) : null);
            check(await Failure(() => fixture.Client.UploadAsync(fixture.ZipPath, "Weekend pools")) != null,
                "A pool upload session must target exactly its reserved path");
            check(fixture.Store.Load().Single().Code == "private-code" && fixture.Requests.All(x => !x.Route.StartsWith("/upload/chunk", StringComparison.Ordinal) && x.Route != "/upload/finish"),
                "Invalid sessions must retain the recovery code without sending archive bytes");
        }
    }

    private static async Task FinishCannotChangeCategory(Action<bool, string> check, string folder)
    {
        foreach (var post in new[] { Post(PoolPath, "campaign"), Post(CampaignPath, "base-game-pools"), Post(PoolPath), Post(PoolPath, "unknown") })
        {
            using var fixture = UploadFixture(folder, request => request.Route == "/upload/finish" ? Json(post) : null);
            check(await Failure(() => fixture.Client.UploadAsync(fixture.ZipPath, "Weekend pools")) != null,
                "The pool upload finish must reject a changed, missing or unknown category");
            var saved = fixture.Store.Load().Single();
            check(saved.Code == "private-code" && saved.Path == PoolPath && saved.Id == "", "A mismatched finish must preserve the initial recovery record");
        }
    }

    private static async Task MetadataFailureRetainsPostedPoolIdentity(Action<bool, string> check, string folder)
    {
        var result = Post(PoolPath, "base-game-pools");
        result["metadataSaved"] = false;
        using var fixture = UploadFixture(folder, request => request.Route == "/upload/finish" ? Json(result) : null);
        var posted = await fixture.Client.UploadAsync(fixture.ZipPath, "Weekend pools", metadata: new("Weekend pools"));
        check(posted.MetadataWarning.Contains("pool", StringComparison.OrdinalIgnoreCase), "Pool metadata fallback warnings must identify the posted pool set");
        check(posted.Id == "id:shared" && fixture.Store.Load().Single().Id == "id:shared", "Metadata warnings must not discard successful pool ownership");
    }

    private static async Task OwnedPostsStayInTheirCategory(Action<bool, string> check, string folder)
    {
        foreach (string kind in new[] { "campaign", "base-game-pools" })
        {
            string expectedPath = kind == "campaign" ? CampaignPath : PoolPath;
            string otherPath = kind == "campaign" ? PoolPath : CampaignPath;
            string otherKind = kind == "campaign" ? "base-game-pools" : "campaign";
            foreach (var item in new[] { Post(otherPath, otherKind), Post(expectedPath, "unknown"), Post(otherPath, kind), Post(expectedPath, otherKind) })
            {
                using var fixture = new ClientFixture(folder, request => Task.FromResult(request.Route == "/health"
                    ? Json(new { ok = true, campaignManagement = true, baseGamePools = true })
                    : Json(new { ok = true, exists = true, item })), kind);
                var saved = new CommunityOwnership("owned", "private-code", "Saved selection", expectedPath, "id:shared");
                fixture.Store.Save(saved);
                var owned = await fixture.Client.OwnedAsync();
                check(owned.Count == 1 && owned[0].Post == null && owned[0].AccessError.Length > 0,
                    "Ownership status must not enable management for another or unknown category even when its file ID matches");
                check(fixture.Store.Load().Single() == saved, "Rejected ownership status must leave the original credentials and identity unchanged");
            }
            using var mismatchedSaved = new ClientFixture(folder, request => Task.FromResult(request.Route == "/health"
                ? Json(new { ok = true, campaignManagement = true, baseGamePools = true })
                : Json(new { ok = true, exists = true, item = Post(expectedPath, kind) })), kind);
            mismatchedSaved.Store.Save(new("wrong-store", "private-code", "Saved selection", otherPath, "id:shared"));
            var rejectedSaved = await mismatchedSaved.Client.OwnedAsync();
            check(rejectedSaved.Single().Post == null && rejectedSaved.Single().AccessError.Length > 0,
                "Ownership from another namespace must remain disabled even if the service returns a matching ID");
        }

        using var valid = new ClientFixture(folder, request => Task.FromResult(request.Route == "/health"
            ? Json(new { ok = true, campaignManagement = true, baseGamePools = true })
            : Json(new { ok = true, exists = true, item = Post(PoolPath, "base-game-pools") })), "base-game-pools");
        valid.Store.Save(new("owned", "private-code", "Saved selection", PoolPath.Replace("Weekend", "Original", StringComparison.Ordinal), "id:shared"));
        var verified = (await valid.Client.OwnedAsync()).Single();
        check(verified.Post?.Kind == "base-game-pools" && verified.AccessError.Length == 0 && verified.Ownership.Path == PoolPath,
            "Same-category ownership must retain stable public IDs while updating a verified current path");
        check(valid.Store.Load().Single().Path == PoolPath, "A verified same-category current path must be persisted");
    }

    private static async Task MutationsRejectOtherCategorySelections(Action<bool, string> check, string folder)
    {
        foreach (string kind in new[] { "campaign", "base-game-pools" })
        {
            string expectedPath = kind == "campaign" ? CampaignPath : PoolPath;
            string otherPath = kind == "campaign" ? PoolPath : CampaignPath;
            string otherKind = kind == "campaign" ? "base-game-pools" : "campaign";
            foreach (var selection in new[]
            {
                (savedPath: expectedPath, postPath: otherPath, postKind: otherKind),
                (savedPath: otherPath, postPath: expectedPath, postKind: kind),
                (savedPath: expectedPath, postPath: expectedPath, postKind: "unknown"),
                (savedPath: expectedPath, postPath: otherPath, postKind: kind)
            })
            {
                using var fixture = UploadFixture(folder, request => request.Route == "/manage/delete"
                    ? Json(new { ok = true, deleted = true, id = "id:shared" }) : null, kind);
                var saved = new CommunityOwnership("owned", "private-code", "Saved selection", selection.savedPath, "id:shared");
                fixture.Store.Save(saved);
                var post = new CommunityCampaignPost("Selected", selection.postPath, 7, "2026-11-02", "id:shared", "r1", "hash", Kind: selection.postKind);
                var selected = new ManagedCampaign(saved, post);
                check(await Failure(() => fixture.Client.UploadAsync(fixture.ZipPath, "Selection", replacement: selected)) != null,
                    "Replacement must reject cross-category ownership or selected posts");
                check(fixture.Requests.All(x => !x.Route.StartsWith("/upload/", StringComparison.Ordinal)),
                    "Cross-category replacement must fail before starting any upload session");
                check(await Failure(() => fixture.Client.DeleteAsync(selected)) != null,
                    "Deletion must reject cross-category ownership or selected posts");
                check(fixture.Requests.All(x => x.Route != "/manage/delete") && fixture.Store.Load().Single() == saved,
                    "Rejected management must send no delete request and preserve saved access");
            }
        }
    }

    private static async Task VerifiedPoolDeletionRemovesOnlyItsOwnCode(Action<bool, string> check, string folder)
    {
        using var fixture = UploadFixture(folder, request => request.Route == "/manage/delete"
            ? Json(new { ok = true, deleted = true, id = "id:shared" }) : null);
        var saved = new CommunityOwnership("delete-me", "private-code", "Weekend", PoolPath, "id:shared");
        fixture.Store.Save(saved);
        fixture.Store.Save(new("keep-me", "other-code", "Other pools", PoolPath.Replace("Weekend", "Other", StringComparison.Ordinal), "id:other"));
        await fixture.Client.DeleteAsync(new(saved, new("Weekend", PoolPath, 7, "2026-11-02", "id:shared", "r1", "hash", Kind: "base-game-pools")));
        check(fixture.Store.Load().Single().Key == "keep-me", "Confirmed pool deletion must remove only its own local management record");
        var deleted = fixture.Requests.Single();
        using var body = JsonDocument.Parse(deleted.Body);
        check(deleted.Route == "/manage/delete" && deleted.ManageCode == "private-code" && body.RootElement.GetProperty("rev").GetString() == "r1",
            "Pool deletion must authenticate and bind the selected revision");
    }

    private static async Task LegacyCampaignWireFormatIsPreserved(Action<bool, string> check, string folder)
    {
        using var fixture = UploadFixture(folder, request => request.Route switch
        {
            "/health" => Json(new { ok = true, campaignManagement = true }),
            "/manage/prepare" => Json(new { ok = true, managementCode = "private-code", path = CampaignPath }),
            "/upload/start" => Json(new { ok = true, ticket = "upload-ticket", chunkBytes = 3 }),
            "/upload/finish" => Json(Post(CampaignPath)),
            _ => null
        }, "campaign");
        var posted = await fixture.Client.UploadAsync(fixture.ZipPath, "Legacy-compatible campaign");
        check(posted.Kind == "campaign" && fixture.Store.Load().Single().Id == "id:shared", "Missing response kind must continue to mean campaign");
        using var prepare = JsonDocument.Parse(fixture.Requests.Single(x => x.Route == "/manage/prepare").Body);
        check(prepare.RootElement.EnumerateObject().Count() == 1 && prepare.RootElement.GetProperty("name").GetString() == "Legacy-compatible campaign",
            "Legacy campaign reservations must keep their existing name-only request body");
        using var start = JsonDocument.Parse(fixture.Requests.Single(x => x.Route == "/upload/start").Body);
        check(start.RootElement.EnumerateObject().Select(x => x.Name).Order().SequenceEqual(new[] { "name", "operation", "rev", "size" }),
            "Legacy campaign upload starts must retain their existing wire fields");
        check(fixture.Requests.Single(x => x.Route == "/upload/finish").Body == "", "Campaign uploads without metadata must keep their empty finish body");
    }

    private static async Task UnknownClientCategoriesFailBeforeNetwork(Action<bool, string> check, string folder)
    {
        check(await Failure(() =>
        {
            using var fixture = UploadFixture(folder, kind: "unknown-category");
            return fixture.Client.UploadAsync(fixture.ZipPath, "Unknown");
        }) != null, "Unsupported client categories must fail before a request can select an unintended namespace");
    }

    private static Task PoolFallbackNamesHideStorageIdentity(Action<bool, string> check)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            name = "12345678-abcd-1234-abcd-123456789abc - Weekend pools.zip", path = PoolPath, kind = "base-game-pools"
        }));
        var presentation = CommunityPresentation.Parse(document.RootElement, "");
        check(presentation.DisplayName == "Weekend pools", "Pool fallback display names must hide the managed storage UUID");
        return Task.CompletedTask;
    }

    private static ClientFixture UploadFixture(string folder, Func<WireRequest, HttpResponseMessage?>? overrideResponse = null, string kind = "base-game-pools")
    {
        string path = kind == "base-game-pools" ? PoolPath : CampaignPath;
        return new(folder, request =>
        {
            var custom = overrideResponse?.Invoke(request);
            if (custom != null) return Task.FromResult(custom);
            if (request.Route == "/health") return Task.FromResult(Json(new { ok = true, campaignManagement = true, campaignMetadata = true, baseGamePools = true }));
            if (request.Route == "/manage/prepare") return Task.FromResult(Json(new { ok = true, managementCode = "private-code", path, kind }));
            if (request.Route == "/upload/start") return Task.FromResult(Json(new { ok = true, ticket = "upload-ticket", chunkBytes = 3, path }));
            if (request.Route.StartsWith("/upload/chunk?offset=", StringComparison.Ordinal))
                return Task.FromResult(Json(new { ok = true, offset = long.Parse(request.Route["/upload/chunk?offset=".Length..]) + request.Bytes.Length }));
            if (request.Route == "/upload/finish") return Task.FromResult(Json(Post(path, kind)));
            throw new Exception("Unexpected upload route: " + request.Route);
        }, kind);
    }

    private static async Task ImportedCodesStayInTheirCategory(Action<bool, string> check, string folder)
    {
        foreach (var item in new[]
        {
            Post(PoolPath, "base-game-pools"),
            Post(CampaignPath, "unknown-future-category"),
            Post(PoolPath, "campaign"),
            Post(CampaignPath, 42)
        })
        {
            using var fixture = new ClientFixture(folder, request => Task.FromResult(Json(new { ok = true, exists = true, item })));
            var failure = await Failure(() => fixture.Client.ImportCodeAsync(" private-code "));
            check(failure != null, "Campaign code import must reject pool, unknown and inconsistent categories");
            check(fixture.Store.Load().Count == 0, "Rejected categories must not write imported ownership");
            check(fixture.Requests.Single().Route == "/manage/status" && fixture.Requests.Single().ManageCode == "private-code",
                "Import must verify the supplied trimmed code at the service boundary");
        }

        using var legacy = new ClientFixture(folder, request => Task.FromResult(Json(new { ok = true, exists = true, item = Post("/Legacy.zip") })));
        await legacy.Client.ImportCodeAsync(" legacy-code ");
        var saved = legacy.Store.Load().Single();
        check(saved.Code == "legacy-code" && saved.Path == "/Legacy.zip" && saved.Id == "id:shared",
            "A legacy post without kind must still import as a campaign");
    }

    private static Dictionary<string, object?> Post(string path, object? kind = null, string revision = "r1", bool includeKind = true)
    {
        var item = new Dictionary<string, object?>
        {
            ["ok"] = true, ["name"] = "Weekend selection", ["path"] = path, ["id"] = "id:shared", ["rev"] = revision,
            ["size"] = 7L, ["modified"] = "2026-11-02T00:00:00Z", ["contentHash"] = "stored-hash"
        };
        if (includeKind && kind != null) item["kind"] = kind;
        return item;
    }

    private static HttpResponseMessage Json(object data) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json")
    };

    private static async Task<Exception?> Failure(Func<Task> action)
    {
        try { await action(); return null; }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or ArgumentException) { return ex; }
    }

    private sealed record WireRequest(string Route, HttpMethod Method, byte[] Bytes, string ManageCode, string UploadCode)
    {
        internal string Body => Encoding.UTF8.GetString(Bytes);
    }

    private sealed class ClientFixture : IDisposable
    {
        private readonly HttpClient http;
        internal CommunityOwnershipStore Store { get; }
        internal CommunityCampaignClient Client { get; }
        internal string ZipPath { get; }
        internal List<WireRequest> Requests { get; } = [];

        internal ClientFixture(string folder, Func<WireRequest, Task<HttpResponseMessage>> respond, string kind = "campaign")
        {
            Store = new(Path.Combine(folder, Guid.NewGuid().ToString("N") + "-ownership.json"));
            ZipPath = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".zip");
            File.WriteAllBytes(ZipPath, [80, 75, 3, 4, 173, 0, 255]);
            http = new(new WireHandler(Requests, respond));
            Client = new(http, "https://community-client.invalid", Store, kind);
        }

        public void Dispose() => http.Dispose();
    }

    private sealed class WireHandler(List<WireRequest> requests, Func<WireRequest, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string Header(string name) => request.Headers.TryGetValues(name, out var values) ? values.Single() : "";
            var wire = new WireRequest(request.RequestUri!.PathAndQuery, request.Method,
                request.Content == null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken),
                Header("X-Campaign-Manage"), Header("X-Campaign-Upload"));
            requests.Add(wire);
            return await respond(wire);
        }
    }
}
