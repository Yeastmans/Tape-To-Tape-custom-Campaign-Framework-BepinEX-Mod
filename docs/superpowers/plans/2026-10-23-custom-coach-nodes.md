# Custom Coach Nodes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Add reusable custom Coaches with world-map identity and strict per-node relic pools.

**Architecture:** Keep native Coach graph types and add self-contained optional node snapshots. The editor owns library/draft operations; the runtime binds a snapshot to the actual Coach event and uses scoped reward selection and per-instance map presentation.

**Tech Stack:** C#, netstandard2.1 CampaignCore, .NET 8 Avalonia Studio, .NET 6 Unity IL2CPP/Harmony mod.

**Spec:** `docs/superpowers/specs/2026-10-23-custom-coach-nodes-design.md`, explicitly approved with "Approve and implement".

## Global Constraints

- Coach only; artwork and custom name on editor/world map only.
- Custom -> Create new / Load saved. Placed copies are independent.
- Name: required, trimmed, single-line, maximum 80 characters.
- Optional PNG: maximum 4 MiB, maximum 2048 pixels per side, fully decoded by the editor.
- At least one selected relic; never offer unselected IDs/upgrades or restore outside candidates.
- Preserve native ownership/mode eligibility and Coach pick/reroll mechanics.
- Empty eligible pools provide a safe leave action and explanatory text.
- Preserve existing maps, current runs, unrelated work, and published/installed binaries.
- Technical diagnostics do not establish human gameplay acceptance.

## Review Focus

- Multiple different custom Coaches and a normal Coach in one map: no shared art or pool contamination.
- A pool containing only owned/unavailable relics: progression remains possible without fallback rewards.
- Cancel, undo, type conversion, and library edits: no partial snapshot/dependency writes or lost graph state.
- Imported custom relic ID collisions: reject conflicting mechanics without overwriting existing content.
- Continue after author edits: restored applied snapshot governs both artwork and reward choices.

## Task 1: Versioned snapshots and core contracts

**Files:** Create `src/CampaignCore/CustomCoachDefinition.cs`, `CustomCoachPool.cs`; modify `MapLayout.cs`; create `tests/CustomCoachChecks/CustomCoachChecks.csproj`, `Program.cs`.

**Interfaces:** `MapLayoutNode.CustomCoach : CustomCoachDefinition`; definition `Id`, `Name`, `ImagePng`, `Relics`, `Rewards`; `ToDocument()`, `Parse(string)`, `Copy()`, `Validate()`, `SaveNew(string library)`; `CustomCoachPool` exposes pure exact-selection and dependency conflict contracts used by authoring/runtime.

- [ ] Write and run failing checks through actual MapLayout parsing for optional snapshot preservation; valid legacy maps still roundtrip. Expect failure because new metadata is lost.
- [ ] Implement compatible optional `Custom coach <node ID>` metadata and self-contained versioned definitions with deep copies, bounded input, image header validation, valid relic definitions, and exact-ID selection.
- [ ] Add meaningful tests for one-relic pools, distinct placements, malformed snapshots, conflicting custom definitions, unavailable IDs, delimiter-heavy names, image bounds, and conversion to normal nodes.
- [ ] Run `dotnet run --project tests/CustomCoachChecks -c Release`; expect all checks pass. Commit source/tests only in the isolated checkout.

## Task 2: Library, editor workflow, and portable campaign save

**Files:** Create `src/CampaignCreator/CustomCoachEditor.cs`, `CustomCoachCatalog.cs`; modify `MapEditorView.cs` (partial split `MapEditorView.CustomCoaches.cs`), `MapNodeArtwork.cs`, `MapGamePreview.cs`, `CustomRewardCatalog.cs`, `CampaignWorkspace.cs`, and `CampaignSharing.cs` as required; create `tests/CustomCoachEditorChecks`.

**Interfaces:** `CustomCoachCatalog` lists/saves immutable library entries and validates/materializes required campaign rewards; editor returns a detached `CustomCoachDefinition` or null on cancel. `MapNodeArtwork.Get` resolves embedded PNG before native art. Node picker inserts only after successful completion.

- [ ] Add failing editor integration checks for the Custom picker and Create new/Load saved, cancel without mutation, detached placement copies, and snapshot sharing. Use isolated test content roots.
- [ ] Implement create/load/edit/save-as-new dialogs with searchable relic entries, unavailable selection retention, decoded PNG preview, and required validation. Keep every placement change undoable.
- [ ] Integrate custom names/art into builder and game preview; preserve empty/fallback art and native graph constraints. Validate conflicting/unavailable dependencies before campaign save/sharing; include node-only custom relic definitions without adding them to global pools.
- [ ] Run new editor checks plus existing map/reward/sharing suites affected by the change. Build Studio. Expect no new failures; commit.

## Task 3: Native Coach event ownership and world-map rendering

**Files:** Create `src/CustomCampaignFramework/CustomCoachRuntime.cs`, `CustomCoachPresentation.cs`; modify `CustomRewardPools.cs`, `CustomMapLayout.cs`, runtime project and compatibility inputs; extend runtime source-linked tests and add compiled hook/native-boundary evidence under this task's artifact directory.

**Interfaces:** runtime resolves `CustomCoachDefinition` only from `CustomMapLayout.LayoutFor` and the exact node; event-bound selection scope provides the current custom pool only during its Coach initialization/selection. Presentation owns cloned blueprints/event objects and imported textures per map lifecycle.

- [ ] Inspect current native LaunchEvent/showEventPanel/Initialize/NextChoices and modern/legacy menu boundaries before choosing hooks; record direct call order and shared/inlined method constraints.
- [ ] Add failing reward adapter checks showing a node override supersedes the campaign pool and never escapes selected IDs on stat/starter/act-speedrun, reroll, upgrades, or exhaustion.
- [ ] Implement event ownership, scope restoration, safe empty-offer exit/message, dependency registration, and leak prevention across standard/custom events and Continue.
- [ ] Implement map instance art and tooltip title with preserved click/controller/state/bounds behavior; no event-screen reskin or shared blueprint edits.
- [ ] Build against actual installed interop; extend/review compatibility contract inputs for every new native hook, and run applicable source-linked and compiled compatibility checks. Commit.

## Task 4: Integrated candidate, review, and guarded promotion

**Files:** task artifact receipts, `docs/CUSTOM-COACH-VALIDATION.md`, approved spec/plan; feature source/tests only.

- [ ] Build the combined runtime and Studio with an explicitly isolated bundled runtime/hash; verify package identity and retain all logs.
- [ ] Run the changed behavior suites and affected existing map/reward/creator/compatibility suites. Do not repeat unrelated gameplay or overwrite the active test run.
- [ ] Request one fresh whole-change review using the spec, plan, diff, and evidence. Repair important findings with regression checks.
- [ ] Compare canonical source against the initial snapshot; promote only this feature's reviewed files with backups, aborting any conflicting overwrite.
- [ ] Write detailed manual acceptance steps for two disjoint Coaches, normal-node isolation, small/depleted pools, save/Continue, sharing, images, and controller navigation. Mark human results pending.
- [ ] Deliver candidate path and concise diagnostics/limitations. No install, release, or live save changes.

## Execution decisions

The user's explicit approval includes implementation; proceed in this chat with a final independent review without another approval round. The canonical source folder is not a Git repository and the Git mirror has unrelated edits, so use a fresh local clone seeded from the canonical source and guard promotion. Ledger stays in the task's artifact directory for recovery; do not delete evidence at completion.
