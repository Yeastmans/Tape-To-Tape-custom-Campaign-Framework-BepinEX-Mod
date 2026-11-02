# Base Game Pools Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Edit the three shared player candidate pools used with Base Game while retaining native squad behavior.

**Architecture:** A portable settings/candidate-file contract is shared by an Avalonia editor and a dedicated default-mode runtime adapter. Keep consumer-specific temporary native lists isolated and restore them on every exit. Build from the clean R33 checkout, preserving unrelated canonical work.

**Tech Stack:** C# netstandard2.1 core, .NET 8/Avalonia Studio, .NET 6 BepInEx IL2CPP/Harmony mod.

**Spec:** `docs/superpowers/specs/2026-11-01-base-game-pools-design.md`

## Global constraints

- Three pools: `draft_pool`, `free_agents`, `superstars`; every saved pool has at least one selected file.
- Base Game only; native no-bench/no-superstar flows and capacities remain unchanged.
- Shared standard bench for all native teams when enabled; no nine/three candidate cap.
- Missing/disabled settings are inert; source assets and unrelated campaigns remain unchanged.
- Current game 26289478; no weakened native validation, no user-save schema changes.
- Live tests muted; preserve and verify restoration of user state.

## Shared interfaces

Task 1 owns `CampaignCore.BaseGamePoolSelection` with `bool Enabled { get; set; }` and `Dictionary<string,string[]> Pools { get; }`, initialized with all three known keys and empty arrays. Missing settings load as disabled. `CampaignCore.BaseGamePlayerPools` exposes `Kinds`, `Folder(string root)`, `PoolDirectory(string root,string kind)`, `Load(string root)`, `Save(string root,BaseGamePoolSelection selection)`, `SelectedFiles(string root,string kind,BaseGamePoolSelection selection)`, `CustomPlayerId(string kind,string fileName)`, and `CustomIdPrefix`. Persist schema 1, enabled yes/no and comma-separated safe generated filenames in `settings.txt`; reject unsafe paths, commas in filenames, duplicate selections and missing referenced files. Save requires all three lists nonempty even if disabled. Stable IDs derive from pool kind and filename, never root or index.

## Task 1: Storage and editor

**Files:** Create `src/CampaignCore/BaseGamePlayerPools.cs`, `src/CampaignCreator/BaseGamePoolsContent.cs`, `BaseGamePoolsView.cs`, `HomeScreen.BaseGamePools.cs`; narrowly modify HomeScreen.cs and player-editor navigation as needed. Add focused tests under `tests/BaseGamePoolChecks/`.

- [ ] Write failing core tests for missing/disabled settings, three-pool atomic round-trip, add/replace/reorder, empty/missing/duplicate/traversal rejection and stable IDs.
- [ ] Implement the shared interfaces, independent candidate copies and native standard-pool initialization from the existing extracted catalog. Existing files/settings must not be overwritten merely by opening the editor.
- [ ] Add the Home button and three scrollable sections, enabled toggle, add/import/replace/edit, save/back, and restore defaults. Reject empty selections before writes. Reuse normal player editing and source pickers.
- [ ] Test native default counts from a small controlled catalog, custom addition beyond nine/three, replacing only the chosen entry, cancelled picker/navigation, save failure retention and return from player editing. Run headless UI checks and capture a real or rendered UI for review.

## Task 2: Native runtime adapter

**Files:** Create `src/CustomCampaignFramework/BaseGamePoolsRuntime.cs`; add linked core include to its csproj and one default-mode installation call in Plugin.cs. Add `tests/BaseGamePoolRuntimeChecks/` and narrowly justified compatibility coverage if needed.

- [ ] Read `../compatibility/NATIVE-POOL-FLOW.md` in the task artifacts and current native signatures/disassembly before implementation. Confirm all hook targets against actual current interop.
- [ ] Write failing source-linked tests around real selection/scoping logic with only native/Unity dependencies substituted. Cover inert settings, three independent lists, no-bench squads, exception restoration, one/two/zero eligible offers, stable/custom/native identities, and unmodified custom campaigns.
- [ ] Implement the dedicated default-mode adapter using Task 1 interfaces. Preserve native selection/unlock/roster rules and fixed offer semantics without unsafe indexing; resolve exhausted eligible pools explicitly. Never enable general campaign patches or leave a bench source installed for GM.
- [ ] Compile against current live interop; run relevant native coverage checks. Report unresolved native questions rather than guessing.

## Task 3: Integration, review, publication

- [ ] Integrate both task diffs; add narrow test-source ignore exceptions and review complete changed-file scope.
- [ ] Run affected core/UI/runtime tests and native compatibility checks; render/inspect the editor and perform authorized muted gameplay checks with verified state restoration.
- [ ] Obtain fresh independent review; fix actionable findings with reproducing tests.
- [ ] Build current runtime and Studio, verify packaged payloads, then publish the requested finished update to GitHub. Keep FC issue evidence and any justified fix clearly identified.

## Review focus

- A settings save must not leave one pool changed when another is invalid: Task 1 atomic profile tests.
- Native and imported player identities must survive list reordering and relocation: Tasks 1 and 2 identity tests.
- Native eligibility can exhaust a nonempty saved pool: Task 2 bounded offer tests and native audit.
- Scoped temporary lists must restore even on exceptions and nested calls: Task 2 scope tests.
- Editor open/cancel must not activate runtime overrides or modify source-library players: Task 1 navigation/source preservation tests.
