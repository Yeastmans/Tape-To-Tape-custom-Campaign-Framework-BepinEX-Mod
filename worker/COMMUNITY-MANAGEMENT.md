# Private campaign management codes

Studio saves a private code in the user's AppData before publishing a managed
campaign. Possession of that code permits replacing or deleting only its posted
Dropbox copy. A campaign's name, author text, local files, and downloaded ZIP do
not prove ownership. Codes must never be included in ZIP exports, public lists,
URLs, logs or screenshots. Deleting the Dropbox copy does not delete local files.

## Production deployment — 2026-10-17

The worker at `https://t2tcampaings.shaespring14.workers.dev` is deployed as version
`52aa7134-c402-4ab9-96b7-477cf22e646d` with management enabled. Downloaded production
source matched the then-current `t2t-campaigns-worker.js`, SHA-256
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

## Campaign details deployment — 2026-10-18

Version `df46b418-7ae7-4a5a-84cb-1684db46e6c5` adds readable campaign titles,
descriptions and icons. Downloaded production source is byte-identical to the
reviewed source, SHA-256
`8B485F5AA09E8A94D0BD8FE3F6447DD8AB41E55C01BE9741843CD76757A91F32`.
Health advertises `campaignMetadata: true`. Existing runtime settings, routes and
all five encrypted secret bindings were preserved. The three existing public
ZIPs retained their IDs, paths, revisions, sizes and content hashes. Verification
sent no upload, replacement or deletion requests. Receipts are under
`artifacts/referee-community-20261018/deployment/` in the development workspace.

Studio revision 16 accepts uploader-selected **PNG files only**, shows a preview
and remove control, and sends a bounded thumbnail with the description. It
preserves the original PNG, its proportions and transparency. Icons are optional;
Studio does not automatically select a team logo. Local client/browser and
simulated-storage checks cover this flow; a human end-to-end upload remains pending.

## Readable ZIP download deployment — 2026-10-24

Worker version `0a4dd948-5c93-40c5-95bd-4de57d33e7c9` serves campaign-title ZIP
filenames through `Content-Disposition`, including a Unicode filename and an
ASCII fallback. Download names are bounded and filesystem-safe; display titles
and ZIP contents remain unchanged. Older uploads fall back to their readable
stored filename. Source SHA-256:
`736A101DAAF7BC94218C41D71EA855C04CCC079BFD0C4D61F402BA401FB1B4B5`.

Both existing public ZIPs were downloaded and matched their preserved original
SHA-256 hashes. Catalog IDs, paths, revisions, sizes and metadata, existing
management access, encrypted secret bindings, routes and runtime settings were
verified unchanged. Keep the `CAMPAIGN_FILE_ID_ALIASES` variable and management
signing secret on later deployments; the aliases preserve the public IDs used
by existing Studio installations after the storage-account migration. No
campaign was uploaded, replaced or deleted by these checks. Browser acceptance
remains pending. Local receipts: `artifacts/release-r27-readable-filenames-20261024/worker-deployment/`.

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
   `{ ok, managementCode, kind, path, name }`. This reserves a unique capability for
   `/managed/<uuid> - <timestamp> - <safe name>.zip` without calling Dropbox.
   **Persist the code and path successfully before starting any upload.** The
   reserved namespace cannot be targeted by the legacy upload endpoints.
2. `/upload/start`, management header plus `{ "size": 123, "operation": "create" }`,
   returns `{ ok, ticket, chunkBytes, path }`. Use this chunked route for all managed
   ZIPs, including small ones. Studio and the chunked Worker routes impose no
   application limit on total ZIP size; `/health` reports `maxCampaignBytes: null`
   for this contract. Byte counts must be safe integers: sizes must be positive
   and offsets nonnegative. Advertised chunk size remains 1 MiB; chunks up to
   8 MiB remain accepted for older sessions. New upload tickets expire after
   seven days; previously issued tickets retain their original expiry.
3. `/upload/chunk?offset=0`, with `X-Campaign-Upload: <ticket>` and raw bytes,
   returns the acknowledged offset. No management code is needed for a chunk.
4. `/upload/finish` requires both the upload ticket and management header for
   managed uploads. An absent or zero-byte request body remains supported. A new
   client may send `{ "metadata": { "displayName": "Campaign - Author",
   "campaignName": "Campaign", "author": "Author", "description": "About this
   campaign", "icon": "data:image/png;base64,..." } }`. The public response is
   `{ ok, id, kind, name, campaignName, author, description, icon, path, rev, size,
   modified, contentHash }`, with metadata status fields described below. Only a
   successful ZIP commit makes the ZIP public.
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
8. `/manage/icon`, with saved code and `{ "id": "PUBLIC_FILE_ID",
   "rev": "CURRENT_REVISION", "icon": "data:image/png;base64,..." }`, replaces
   only the cover. `/health` advertises `campaignCoverEdit: true`. The response
   contains the public post plus `ok: true, iconSaved: true`. ZIP identity, bytes,
   revision, download URL and other presentation fields are preserved. Extra
   body fields, invalid ownership, stale revisions and incomplete or corrupt
   PNGs are rejected before writing. Sidecar updates are conditional on the exact
   read revision; concurrent edits return a refreshable conflict.

Chunking streams a ZIP from disk with bounded request memory; removing the total
application cap does not provide unlimited provider storage. Dropbox currently
limits an upload session to **2,199,019,061,248 bytes** (about 2 TB) and seven days,
and the storage account must have enough free quota. See the official
[Dropbox upload-session documentation](https://docs.dropboxapi.com/dropbox-api/api-reference/user-endpoints/files/upload-session-start).
Provider failures are returned through the existing upload error paths.

The old single-request multipart `/upload` endpoint retains its **20 MiB file
limit** and buffered request behavior for legacy clients. It is separate from
the chunked routes and is not the upload route used by current Studio.

`/list` returns all pages of campaign ZIPs in the root and `/managed/` namespace,
including `id`, `rev`, `contentHash` and existing metadata fields. It never returns
management codes or Base Game Pools posts. Downloads accept public ZIP paths in
either category; folders, private
subdirectories, arbitrary IDs and traversal paths are rejected.

## Base Game Pools category

Clients require `/health` to advertise `baseGamePools: true` before sharing pool
sets. The category is selected by `GET /list?kind=base-game-pools` and by
`POST /manage/prepare` with `{ "name": "Pool set - Author",
"kind": "base-game-pools" }`. Missing `kind` on these two routes means
`campaign`; explicit `kind=campaign` is also accepted. Unknown values, empty
strings and non-string JSON values return HTTP 400. A pool list never includes
campaigns, and the old `/list` response never includes pool sets.

Pool codes reserve `/managed-pools/<uuid> - <safe name>.zip`; the safe name uses
the same timestamped filename format as campaign uploads. The signed capability
binds that exact path, using the unchanged management signing secret and format.
`/upload/start`, `/upload/chunk`, `/upload/finish`, `/manage/status`,
`/manage/delete`, `/manage/icon` and `/download?path=...` are shared. No extra
category field is required for these operations: the reserved path determines
the category. If `kind` is supplied to upload start or finish it must match the
authorized category; it cannot move or reinterpret a post. Anonymous and legacy
uploads remain campaigns. Administrator legacy claims accept only campaign root
ZIPs, and a legacy file-ID capability cannot manage a file moved into the pool
namespace.

Prepare responses and public post objects from list, finish, status, claim and
cover editing include `kind: "campaign"` or `kind: "base-game-pools"`. This value
comes from the trusted path, never caller presentation metadata. Missing,
corrupt or unavailable metadata preserves that category and falls back to the
readable filename. Pool sets use the same optional presentation fields and
ID-keyed private sidecars as campaigns. The wire field `campaignName` remains
the short display title for both categories. Store pool ownership codes in a
separate local management list; do not include them in exported pool packages.

The server separates transport and ownership; Studio validates the versioned
pool package and all three pool selections before replacing local data.

## Public campaign presentation

Clients check `/health` for `campaignMetadata: true` before uploading presentation
metadata. The field is independent of `campaignManagement`, which must also be
enabled for managed uploads. Metadata is supplied only to `/upload/finish`; icons
and descriptions are never embedded in management codes or upload tickets.

The supported text limits are 240 characters for `displayName`, 160 for
`campaignName`, 60 for `author`, and 2,000 for `description`. Newlines are allowed
in descriptions. An icon is empty or a strict `data:image/png;base64,` string of
at most **350,000 characters**, with PNG signature and IHDR dimensions from 1 to
512 pixels in each direction. Input is bounded before parsing and validated
before the ZIP is committed. The request cap accommodates JSON unicode escapes;
stored sidecar reads have a smaller 384 KiB bound.

Omitting the metadata object preserves existing metadata during replacement.
Providing it replaces the complete presentation: missing string fields become
empty. Empty display names fall back to the readable storage name; empty campaign
names fall back to the display name. Old public entries get readable names by
removing only the recognized managed UUID, storage timestamp and `.zip` suffix.
Their exact download paths, IDs, revisions, sizes and content hashes are retained.
Descriptions previously discarded by older clients are not recoverable from the
service. Existing posts have no icon or description until a metadata upload.

Presentation is stored in Dropbox at `/.campaign-metadata/<SHA-256(file.id)>.json`.
This namespace is excluded from public listing and direct download; only validated,
whitelisted presentation fields are included in public campaign records. No
management credential enters a sidecar. Metadata follows the stable ZIP file ID,
so replacing a ZIP keeps its presentation and a different file at an old path
cannot inherit it. Sidecars for deleted ZIPs are inert and are not automatically
deleted in this update. Optional metadata read failures fall back to a readable
title rather than failing an entire listing. Preview reads run at most four at a
time and share one OAuth token within that listing.

ZIP and sidecar are separate atomic Dropbox writes. After committing the ZIP,
the worker snapshots the sidecar revision, verifies that the ZIP still has the
just-committed revision, and conditionally adds or updates the sidecar. A stale
finish cannot overwrite newer metadata. Failed conditional writes are not retried
blindly. Successful persistence returns `metadataSaved: true`. A post-commit
failure returns **`ok: true, metadataSaved: false, metadataWarning: "..."`**, along
with the exact identity of the ZIP that committed. Clients must display this
warning rather than report the ZIP upload as failed or automatically publish a
second copy. A rejected write leaves the preceding sidecar intact; a lost write
response can mean the old or the complete new sidecar exists. Refresh to resolve
that uncertainty. Metadata is validated again when read from storage.

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
node --test .\tests\CommunityWorkerChecks\worker.test.mjs
dotnet run --project .\tests\CommunityClientChecks\CommunityClientChecks.csproj
```

The test harness runs the actual Worker and HMAC code with only Dropbox HTTP
replaced by an in-memory fixture. It never accesses real Dropbox. It covers
ownership, lost responses, revision races, replacement preservation, deletion,
legacy assignment, privacy filtering, pagination and old-client compatibility.
The Worker suite contains 64 checks, including readable legacy
names, presentation round trips, exact field/icon limits, metadata preservation,
partial success, delayed writes, lost metadata responses, poisoned metadata,
bounded concurrent reads, bounded error bodies and JSON escaping at the limit.
It also covers the existing live cover-editing contract and the separate pool
namespace, category negotiation, cross-owner finish rejection, tampered signed
paths, legacy claim boundaries, pool download/cover operations and metadata
fallback. All remote-storage operations in these checks are simulated.
The helper tests write only temporary files and use a local request substitute.
The preserved pre-change Worker and initial failing test output are in
`tests/CommunityWorkerChecks/evidence/`. Live deployment and real ZIP replacement
or deletion require a separate authorized operation; none is performed by tests.

The upload-size regressions accept ZIP sizes above 512 MiB, preserve cursor values
above 4 GiB, reject invalid sizes/offsets, and verify that seven-day tickets outlive
one hour while still expiring. They retain chunk and legacy multipart bounds.
The upload-only client project streams a real 512 MiB plus 17-byte file through
create and replacement requests, checking sizes, offsets, bounded requests,
saved ownership and selected revision, as well as rejecting empty files.

## Upload cap removal deployment — 2026-11-01

Live Worker version `f75ed607-3d9d-4c98-81a6-a4f6ce4e539e` serves this upload
change. Its `/health` reports `maxCampaignBytes: null`. The deployment applies
only the upload delta to the live source and preserves its existing cover editing,
runtime settings, secret bindings and migration aliases. Downloaded deployed
source SHA-256 is `53c1caf1be566e4bb87aa450a011b11bae8030929c9db2f476f2d092d7043554`.

The public campaign catalog was unchanged. A session declared at 536,870,913
bytes accepted a four-byte chunk; neither that session nor an earlier unfinished
probe was committed. This confirms start/chunk acceptance beyond the former cap,
not a complete large transfer. Users need Studio revision 33 or newer to remove
the client-side limit. Local receipts are under
`artifacts/unlimited-campaign-uploads-20261101/deployment/`.

## Base Game Pools source baseline — 2026-11-02

Before this category change, a fresh read-only snapshot of production matched
the deployed upload-cap source above exactly. The published R35 checkout lacked
the already-live cover editing code; this update restores that exact capability
and its regression tests before adding the category delta. Do not deploy the
older checkout Worker over production. Preserve the existing five encrypted
bindings, `CAMPAIGN_FILE_ID_ALIASES`, module name, routes and runtime settings.

Local evidence is under `artifacts/base-game-pool-sharing-20261102/worker/`,
including the current production source, settings/catalog snapshots, failing-first
and passing test outputs, and the candidate-to-live diff. This source update has
not itself deployed the category or posted any public test content.
