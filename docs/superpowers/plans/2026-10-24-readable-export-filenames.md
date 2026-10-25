# Readable Export Filenames Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Give new Play Now exports and shared campaign downloads readable filenames without breaking existing save identities.

**Architecture:** Generate a bounded readable prefix followed by a full GUID when Studio allocates an export identity. Keep all current filename/embedded-ID contracts and update behavior. Add a download response filename in the community worker without moving stored ZIPs.

**Tech Stack:** Existing .NET 8/C# Studio and focused console checks; JavaScript worker and Node's built-in test runner. No new packages.

**Spec:** `docs/superpowers/specs/2026-10-24-readable-export-filenames-design.md`

## Global Constraints

- Readable names apply to new exports; existing exports retain their filenames and identities when updated.
- Keep the complete randomly generated GUID suffix; do not reduce uniqueness.
- Play Now IDs stay within the already accepted ASCII letters, digits, hyphen, and underscore character set.
- No stored campaign is renamed. Deployment and release publication are outside this change.
- Preserve unrelated canonical changes, especially the worker's existing migration aliases. Record baselines and compare before copying reviewed changes back.
- The installed game has advanced beyond the R26 compatibility baseline; builds and focused checks do not establish gameplay acceptance.

## Review Focus

- Same-name exports must remain independent, including two players sharing one name.
- Renaming an already exported entity must not orphan its existing files or create another team during Update.
- Unsafe, non-ASCII-only, blank, and long names must yield bounded, nonempty, filesystem-safe identities without changing display names.
- Download titles containing Unicode, control characters, quotes, device names, or repeated extensions must produce a safe usable ZIP filename.
- Missing or corrupt community metadata must preserve a working download; stored bytes, paths, revision, and management identity must remain intact.

### Task 1: Name new Play Now export identities

**Files:** Create `src/CampaignCreator/ExportFileNames.cs`; modify `src/CampaignCreator/ExportTranslation.cs` and `src/CampaignCreator/PlayerExport.cs`; add checks in `tests/PlayNowRosterChecks/Program.cs`, `tests/PlayNowUpdateChecks/Program.cs`, and `tests/PlayNowPersistenceChecks/Program.cs`.

**Interfaces:** `ExportFileNames.NewId(string? name, string fallback)` returns a stable-for-that-export identity with a name prefix of at most 48 ASCII characters and a full GUID in D format. Call it for the team, resolved team player, and resolved standalone player. To check standalone export without touching AppData, factor its existing pre-write construction into `PlayerExport.Build(CampaignDocument d, GameData game, string role, ExportReport? report)` returning `(string Id, Dictionary<string, object> Model, CampaignDocument Sidecar)` and keep `Export`'s existing path/write behavior.

- [x] Add failing checks through the production team builder and standalone builder: `Ice Wolves` starts `Ice-Wolves-`, `Jane Smith` starts `Jane-Smith-`, and the embedded IDs, filenames, supplemental documents, and roster references agree. Repeat exports and duplicate roster names must have distinct full GUID suffixes.
- [x] Exercise the helper through reflection for `Émile / Smith`, empty text, non-ASCII-only text, punctuation, and a 200-character name; assert safe characters, named fallback, prefix length <= 48, and a parseable D-format GUID suffix. Keep the full original name in exported content.
- [x] Run `dotnet run --project tests/PlayNowRosterChecks -c Release`; expect the new filename cases to fail before production changes.
- [x] Implement the helper, the three allocation sites, and the minimal standalone build extraction; preserve all existing validation, custom reward embedding, supplemental ownership, and writing order.
- [x] Add update checks using a real named-ID package: legacy updates preserve their GUID-only team/player files; renamed named exports retain their IDs; newly allocated slots have named IDs. Add a journal round-trip with named team/forward/goalie IDs.
- [x] Run `PlayNowRosterChecks`, `PlayNowUpdateChecks`, and `PlayNowPersistenceChecks`; expect all checks to pass. Commit only Task 1 code and checks.

### Task 2: Name campaign ZIP downloads

**Files:** Modify `worker/t2t-campaigns-worker.js` and `tests/CommunityWorkerChecks/worker.test.mjs`; create `docs/READABLE-EXPORT-FILENAMES-VALIDATION.md`.

**Interfaces:** Add `downloadFilename(name)` to return one safe `.zip` basename, and `downloadDisposition(name)` to produce an ASCII fallback plus UTF-8 encoded filename. `handleDownload` uses Dropbox's returned file metadata and existing presentation metadata when available; otherwise it uses the requested stored basename through the existing readable-name cleanup.

- [x] Add HTTP-route tests asserting `Content-Disposition` names from current campaign metadata and legacy stored paths, readable UTF-8 handling, safe punctuation/device/long-name fallbacks, and unchanged streamed bytes. Assert failed/missing metadata still downloads and no write/move requests occur.
- [x] Run `node --test tests/CommunityWorkerChecks/worker.test.mjs`; expect the new header assertions to fail before implementation.
- [x] Add helpers and response headers, preserving the existing download/status behavior. Avoid a new prerequisite metadata request: use the download response's Dropbox metadata when present and use path fallback otherwise. Expose `Content-Disposition` to browser clients.
- [x] Run the whole worker check file; expect all tests to pass. Build `src/CampaignCreator/CampaignCreator.csproj` in Release and run the existing Studio export checks needed by the standalone extraction. Commit Task 2 code and checks.
- [x] Write manual validation steps covering named team/standalone export, duplicate names, an older-export update, compatible-game load/save, and downloaded ZIP names after worker deployment. Record local checks and pending human validation.

### Finish

- [x] Review the branch against the approved design with a fresh reviewer; fix material findings and rerun affected checks.
- [x] Compare canonical files with their recorded baselines, then copy only this task's reviewed files back with backups. Preserve pre-existing worker aliases.
- [x] Record source commits, build/check receipts, and the fact that no installer/service was published. Give the user the validation guide and ask for observed results.
