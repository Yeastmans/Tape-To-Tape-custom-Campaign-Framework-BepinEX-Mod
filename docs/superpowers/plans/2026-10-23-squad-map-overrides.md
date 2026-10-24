# Player squad map overrides Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan inline, with one fresh whole-branch review at the end.

**Goal:** Give campaign squads optional, independent map edits while untouched stages keep following the campaign.
**Architecture:** Full per-stage snapshots in each squad's `map_overrides` directory, gated by `Use Own Maps` in `team.txt`. A shared resolver selects campaign or squad files; the existing map editor gains a squad mode with deliberate-edit tracking, undoable resets, stale-save protection, and no sequence controls. Runtime receipts retain the actually applied source across Continue.
**Tech Stack:** C#, CampaignCore netstandard2.1, Avalonia desktop creator, Unity IL2CPP mod net6.0, console regression projects and Avalonia headless editor checks.
**Spec:** `docs/superpowers/specs/2026-10-23-squad-map-overrides-design.md` (approved).

## Global Constraints

- Preserve custom Coach work, unrelated source, installed files, and real campaign/save data. Source and a local candidate only; no release or installation.
- Canonical workspace is not Git. Seed an isolated local clone from canonical source, record hashes, and promote reviewed changes only when canonical originals still match.
- Execute continuously under the user's implementation approval. Keep a task ledger, RED/GREEN evidence and backups; use local commits. Keep evidence after completion.
- Inherited previews must never become persisted overrides from opening, navigation, normalization, or unchanged saves. Existing saved overrides remain independent until explicit reset.
- All squads share campaign count/order; structural changes remap overrides by surviving original stage index. Reject stale squad saves without losing drafts.
- Builds and automated checks are technical diagnostics; human gameplay acceptance stays pending.

## Review Focus

1. No-op, first-edit, undo/redo, reset and disabled retention: core persistence checks in Task 1 and real editor checks in Task 2.
2. Stage deletion/renumbering and stale concurrent editors: core remapping/revision tests in Task 1 plus editor integration in Task 2.
3. Selected squad isolation, malformed overrides and Continue snapshots: runtime source/receipt checks in Task 3.
4. Effective per-squad validation, including draft campaign edits: budget checks in Task 3.
5. Whole-campaign portability and standalone-team exclusion, including disabled Coach dependencies/collisions: real archive/copy checks in Task 3.

## Task 1: Core inheritance and persistence

**Files:** add `src/CampaignCore/SquadMapOverrides.cs`; add `tests/SquadMapChecks/{SquadMapChecks.csproj,Program.cs}`.
**Interfaces:** `ModeKey`, `Folder`, `Own(CampaignDocument)`, `OverridePath(squad,stage)`, `ResolvePath(campaign,squad,stage)`, `Squads(campaign)`, `Revision(campaign,squad)`, `Save(campaign,squad,expectedRevision,changes)` where null values mean reset, and `Remap(campaign,originalStages,backup)`.

- [x] Create isolated checkout/baseline, ledger and preservation manifests.
- [x] Write a reflection-based failing assertion for the missing resolver, then tests for inherited/own/disabled selection, malformed-file selection, per-map persistence, no-op saving, reset/backups, revision rejection and remapping swaps/removals/additions. Run the core check and record RED.
- [x] Implement core storage with positive stage checks, campaign-owned squad paths, atomic document saves, backups before changes, content-based revision fingerprints and snapshot-first remapping. Run `dotnet run --project tests/SquadMapChecks -c Release`; expected PASS with fixtures confined to a task temp directory.
- [x] Commit core implementation and ledger the result.

## Task 2: Squad editor and deliberate map overrides

**Files:** `TeamEditorView.cs`, new `SquadMapsEditor.cs`, `MapEditorView.cs`, new `MapEditorView.SquadMaps.cs`, `MapEditorWindow.cs`, `CampaignWorkspace.cs`, `CampaignWorkspace.Squads.cs`; add `tests/SquadMapEditorChecks`.
**Interfaces:** optional squad directory on map editor/window; squad-aware save/reset/status; original stage indices exposed for shared draft validation. Team map launch uses the existing successful team save path before opening.

- [x] Write and run failing real-editor checks for untouched save, first edit of one map, undo/redo, reset, disabled retention, shared-file isolation, stale saves and stage remapping. Expected RED on missing squad-mode behavior.
- [x] Add Maps settings and persist the flag through the existing team editor registration. Reuse the map editor in a squad-labelled window. Hide/guard sequence operations in squad mode.
- [x] Keep normalized opening baselines and squad override state in undo snapshots. Save only changed overrides or resets. Existing overrides stay custom; undo of the first edit restores inheritance. Preserve drafts on rejected saves.
- [x] Route shared campaign sequence saves through override remapping and update the captured revision after successful squad saves. Run core/editor checks; expected PASS.
- [x] Commit editor implementation and ledger results.

## Task 3: Runtime, validation and portable content

**Files:** new `CustomCampaignFramework/SquadMapRuntime.cs`, `CustomMapLayout.cs`, `CustomMapLayout.Receipt.cs`; `CampaignRewardBudget.cs`, `OpponentAssignments.cs`, `CustomCoachCatalog.cs`, `SquadCopy.cs` and actual team export/import paths; add runtime/portability regressions to appropriate test projects.
**Interfaces:** runtime stable team identity resolves saved flag on each new stage; receipt metadata stores squad/source identity and validates against the saved run's squad during pre-hydration. Legacy receipt metadata remains supported. Budget APIs accept squad draft context and original stage mapping.

- [x] Write/run failing regressions for two squads with distinct graphs, live toggle changes, malformed override rejection, saved receipt squad mismatch/legacy compatibility, budget overlays, disabled Coach dependencies and standalone-team transfer. Expected RED for missing behavior.
- [x] Resolve stage source before the existing map cloning/apply boundary; retain native failure fallback and report invalid selected source. Add receipt source/squad identity without changing shared sequence or native hooks.
- [x] Compute reward budgets per effective squad graph; combine shared drafts and original-stage override mapping correctly. Include retained override Coaches in validation/export and independent dependency collision checks.
- [x] Preserve overrides in whole-campaign copy/ZIP; strip map flag/files from standalone squad/library transfer. Run targeted suites and relevant existing Coach/reward/goalie/runtime compatibility checks; expected PASS, with unrelated historical failures documented separately.
- [x] Commit integration and ledger results.

## Task 4: Candidate, review and guarded delivery

**Files:** task evidence under `artifacts/squad-map-overrides-20261023`; feature handoff under `docs/superpowers`; only reviewed product/test/docs files promoted.

- [x] Build runtime against installed interop and publish a self-contained creator candidate with that exact runtime and matching hash. Verify bundle hash and unchanged native compatibility baseline. Expected successful build/package diagnostics.
- [x] Dispatch one fresh whole-branch review as required by executing-plans. Fix Critical/Important findings in one regression-led pass; record Minor findings. Re-run affected checks and final build after changes.
- [x] Preview guarded promotion against initial canonical hashes, back up overwritten files, copy reviewed changes and verify exact file hashes. Leave live installation and publishing mirror untouched.
- [x] Deliver source/candidate links and detailed human validation steps covering setup, actions, expected behavior and edge cases. Ask for observed results; gameplay acceptance remains pending.
