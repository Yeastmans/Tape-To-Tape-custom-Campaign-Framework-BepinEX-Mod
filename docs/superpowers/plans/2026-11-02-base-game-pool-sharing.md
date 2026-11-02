# Base Game Pools Sharing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Upload and download Base Game Pools separately, replace them with confirmation, and restore dated backups.

**Architecture:** Reuse Studio's portable head/logo handling and managed chunked transport. Keep pool package validation and transactional profile replacement in a dedicated class; keep the server namespace separate from campaigns so old clients remain compatible.

**Tech Stack:** C#/.NET 8, Avalonia 11, JavaScript Cloudflare Worker, existing Dropbox storage.

**Spec:** `docs/superpowers/specs/2026-11-02-base-game-pool-sharing.md`

## Global constraints

- Three nonempty pools: draft_pool, free_agents, superstars.
- Replacement and restoration require UI confirmation and retain the preceding configuration as a backup.
- No shared credentials, user saves, unrelated libraries or unchecked candidates in uploads.
- Preserve campaign APIs, live cover editing, ownership codes, native identities and unlimited chunked uploads.
- Preserve R35 behavior and rebuild the runtime for the user-added Stu head and native helmet fixes. Existing native compatibility profile remains unchanged unless actual native API changes require an update.
- Work in the existing isolated R35 checkout; preserve unrelated tools/ffmpeg whitespace edits.

## Review focus

- Hostile ZIP paths, duplicate case-insensitive names, symlinks and missing candidates must leave current data untouched.
- Failed or cancelled imports/restores must not lose the existing pools or shared assets.
- Same-named custom heads/logos with different bytes must retain both user's and downloaded content.
- Legacy clients must never see pool sets as campaigns, including through management replacement.
- Restoring a disabled or previously absent profile must restore that original state and retain the selected backup.

### Task 1: Portable package and backup lifecycle

**Files:** `src/CampaignCreator/BaseGamePoolPackages.cs`, shared helpers in `CampaignSharing.cs` / `CampaignSharing.Heads.cs`, `tests/BaseGamePoolSharingChecks/`.

**Interfaces:** `BaseGamePoolPackages.Export(root, zip)`, `Inspect(zip)` returning title/counts; `Install(zip, root)` returning backup identity and counts; `Backups(root)`; `Restore(root, backupId)`. Operations are filesystem-only and safe to call in Task.Run; no UI or network calls.

- [ ] Write failing package round-trip, selected-only export, all-three replacement and backup/restore tests.
- [ ] Implement staging, validation, portable referenced assets, rollback and retained snapshots.
- [ ] Test unsafe archives, corrupt settings, head conflicts, absent/disabled original state and restore undo.

### Task 2: Community service category

**Files:** `worker/t2t-campaigns-worker.js`, `tests/CommunityWorkerChecks/worker.test.mjs`, worker protocol documentation.

**Interfaces:** `kind=base-game-pools` on `/list` and `/manage/prepare`; posts expose `kind`; missing kind means campaign. Pool paths are `/managed-pools/<uuid> - <name>.zip`. `/health` advertises `baseGamePools: true`. Shared download/manage routes accept both namespaces, with signed capabilities binding the exact path.

- [ ] Verify current live-source baseline including existing cover support before edits.
- [ ] Add failing tests proving separate listings and capability-bound replacement/download paths.
- [ ] Implement category with unchanged legacy campaign behavior; run full worker tests.

### Task 3: Studio sharing and restore controls

**Files:** `CommunityCampaignClient.cs`, `CampaignSharing.cs`, `HomeScreen.cs`, new `HomeScreen.BaseGamePoolSharing.cs`, help text, client/UI checks.

**Interfaces:** Client upload/owned operations accept a defaulted category without changing legacy requests; pool management uses separate saved ownership file. UI invokes Task 1 package functions and explicit confirmation before mutations.

- [ ] Add failing transport tests for category negotiation, request routing and ownership persistence.
- [ ] Add home card, upload form, pool list/import, preview/confirmation, management and dated restore selection.
- [ ] Verify cancel preservation and successful restore through the actual Avalonia controls.
- [ ] Run affected existing pool, sharing, ownership, packaging and worker checks.

### Task 4: Review and release

**Files:** release identity/notes, installer inputs, local evidence under `artifacts/base-game-pool-sharing-20261102`.

- [ ] Independent whole-change review, resolve material findings with regression tests.
- [ ] Verify Stu's real native head identity and preserve native no-helmet intent with regression checks and a muted campaign test.
- [ ] Build runtime and Studio; verify installer payloads against that exact tested candidate; preserve real user content.
- [ ] Deploy exact reviewed service source with original secret bindings, aliases and public catalog preserved.
- [ ] Publish new GitHub revision and verify all public asset downloads.
