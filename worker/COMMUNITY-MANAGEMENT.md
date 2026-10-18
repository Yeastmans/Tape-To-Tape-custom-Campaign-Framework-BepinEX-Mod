# Private campaign management codes

Studio saves a private code in the user's AppData before publishing a managed
campaign. Possession of that code permits replacing or deleting only its posted
Dropbox copy. A campaign's name, author text, local files, and downloaded ZIP do
not prove ownership. Codes must never be included in ZIP exports, public lists,
URLs, logs or screenshots. Deleting the Dropbox copy does not delete local files.

## Production deployment — 2026-10-17

The worker at `https://t2tcampaings.shaespring14.workers.dev` is deployed as version
`52aa7134-c402-4ab9-96b7-477cf22e646d` with management enabled. Downloaded production
source exactly matches `t2t-campaigns-worker.js`, SHA-256
`6A1ACEE550F0AAF2ADA6D6D97C7B8049AA09237AB1888D91597D1E129EBBBB23`.
The existing settings and three Dropbox secrets were preserved. Separate management
and administrator secrets are encrypted in Cloudflare, with a Windows DPAPI backup
outside the workspace. Preserve the management secret across future deployments.

Sixteen non-destructive live checks passed. The original Yeast-the-man NHL post's
management code is saved and reverified in the owner's Studio AppData. No ZIP was
uploaded, replaced or deleted by deployment verification. The matching Studio
interface is prepared for revision 12; human feature acceptance remains pending.
See `../HANDOFF_COMMUNITY_MANAGEMENT_2026-10-17.md` and the local evidence under
`artifacts/community-management-20261017/deployment/`.

## Worker setup

Keep the existing encrypted `DROPBOX_APP_KEY`, `DROPBOX_APP_SECRET`, and
`DROPBOX_REFRESH_TOKEN`. Add an encrypted `CAMPAIGN_MANAGEMENT_SECRET` with at
least 32 random characters. This is a stable signing secret: back it up securely
and preserve it across deployments. Changing it invalidates every issued code.
Its purpose is separate from the Dropbox credentials, which can rotate normally.

Optionally add a different encrypted `CAMPAIGN_ADMIN_SECRET` for manual legacy
assignment. There is no default admin secret and no admin secret in Studio.
Remove this optional variable to disable future administrator assignments.
Previously issued user codes continue to work.

Deploy `t2t-campaigns-worker.js` using the existing Cloudflare Worker deployment
workflow. `/health` advertises `campaignManagement: true` only when the stable
signing secret is configured. This source update does not itself deploy anything.
Existing anonymous upload, list and download clients remain compatible.

## Protocol

All management calls are POST. JSON bodies and responses use UTF-8. Private codes
are sent in the `X-Campaign-Manage` header, never a URL. JSON responses use
`Cache-Control: no-store`. Error responses are `{ "error": "public message" }`.

1. `/manage/prepare`, body `{ "name": "Campaign - Author" }`, returns
   `{ ok, managementCode, path, name }`. This reserves a unique capability for
   `/managed/<uuid> - <timestamp> - <safe name>.zip` without calling Dropbox.
   **Persist the code and path successfully before starting any upload.** The
   reserved namespace cannot be targeted by the legacy upload endpoints.
2. `/upload/start`, management header plus `{ "size": 123, "operation": "create" }`,
   returns `{ ok, ticket, chunkBytes, path }`. Use this chunked route for all managed
   ZIPs, including small ones. The current limit is 512 MiB; advertised chunk size
   remains 1 MiB. Prior anonymous upload sessions may still send 8 MiB chunks.
3. `/upload/chunk?offset=0`, with `X-Campaign-Upload: <ticket>` and raw bytes,
   returns the acknowledged offset. No management code is needed for a chunk.
4. `/upload/finish` requires both the upload ticket and management header for
   managed uploads. The response is `{ ok, id, name, path, rev, size, modified,
   contentHash }`. Only successful commit makes the ZIP public.
5. `/manage/status`, with management header, returns
   `{ ok, exists: true, path, item }`, where item contains the public fields above
   without `ok`. Before first upload or after deletion it returns
   `{ ok, exists: false, path }`; deleted legacy codes have an empty path.
6. Replacement uses `/upload/start` with `{ "size": 123, "operation": "replace",
   "rev": "CURRENT_REVISION" }` and the saved code. The original ZIP remains
   available while uploading. Finish verifies ownership and revision again, then
   commits a conditional update to the same Dropbox file ID. Concurrent updates
   return 409. Deletion during replacement prevents that replacement's finish.
7. `/manage/delete`, with saved code and `{ "rev": "CURRENT_REVISION" }`, deletes
   only that exact file and revision; response `{ ok, deleted: true, id, path }`.
   Missing files return 404; changed revisions return 409. Refresh status to
   reconcile a lost deletion response.

`/list` returns all pages of public ZIPs in the root and reserved managed namespace,
including `id`, `rev`, `contentHash` and existing metadata fields. It never returns
management codes. Downloads accept only those public ZIP paths; folders, private
subdirectories, arbitrary IDs and traversal paths are rejected.

Authorization errors return 403, malformed inputs 400, missing posts 404,
revision conflicts 409, missing management configuration 503, and upstream
failures 502. A stale or failed operation never silently creates another post.

## Lost responses and saved-code recovery

The code exists and is saved before the first Dropbox mutation, so an interrupted
upload or lost finish response cannot leave a successfully posted campaign without
its private credential. Refresh `/manage/status` using the already saved code.
For initial creation, `exists: true` confirms the reserved post was committed.
For replacement, refresh its revision and content hash; a changed revision alone
does not prove which concurrent replacement won. Do not automatically issue a new
code or upload another public copy after an uncertain result. Codes can be imported
on another installation and resolved through the same status call.

Paths authorized by new codes belong to their holder permanently, including after
deletion; they may deliberately create another ZIP at that reserved path. In-flight
replacement tickets cannot recreate a deleted file. Legacy codes bind the immutable
Dropbox file ID instead, so reusing an old filename never transfers authority.

## Manual legacy assignment

Old uploads contain no verifiable ownership record. An administrator must decide
who owns a post using evidence outside the public campaign name or downloaded ZIP.
There is no automatic claim endpoint available to ordinary users.

Once ownership is confirmed, run `assign-legacy-code.mjs` locally with Node 22+.
Set `CAMPAIGN_ADMIN_SECRET` securely in that process environment (do not place it
in command arguments or commit it). Supply the exact public root ZIP path and a
new private output file outside the workspace, campaign and export folders:

```powershell
node .\worker\assign-legacy-code.mjs --worker https://YOUR-WORKER.workers.dev --path "/Existing campaign.zip" --output "$env:LOCALAPPDATA\T2T-legacy-assignment.json"
```

The helper calls `/manage/claim` with the separate `X-Campaign-Admin` header.
It reads metadata and issues a code; it does not replace or delete the ZIP.
It refuses redirects and refuses to overwrite an output file, and prints only the
saved file location. The result contains the private `managementCode`, file ID,
name and path. Deliver the private code to the confirmed owner through a private
channel, then remove temporary transfer copies according to your normal process.
The owner can import the code into Studio. Never include this helper's output in a
campaign package or public support attachment.

## Local verification

```powershell
node --test .\tests\CommunityWorkerChecks\worker.test.mjs .\tests\CommunityWorkerChecks\admin-helper.test.mjs
```

The test harness runs the actual Worker and HMAC code with only Dropbox HTTP
replaced by an in-memory fixture. It never accesses real Dropbox. It covers
ownership, lost responses, revision races, replacement preservation, deletion,
legacy assignment, privacy filtering, pagination and old-client compatibility.
The helper tests write only temporary files and use a local request substitute.
The preserved pre-change Worker and initial failing test output are in
`tests/CommunityWorkerChecks/evidence/`. Live deployment and real ZIP replacement
or deletion require a separate authorized operation; none is performed by tests.
