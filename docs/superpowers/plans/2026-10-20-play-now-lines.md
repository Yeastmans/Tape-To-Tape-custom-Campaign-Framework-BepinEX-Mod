# Two-line Play Now implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Export ten skaters and one goalie, retain native line switching and authored stamina, and persist in-game lineup edits.

**Architecture:** Keep native Line 1 JSON and add a validated roster extension to the version-3 team sidecar. A runtime adapter hydrates both lines and companions native saves without replacing generated match IDs.

**Tech Stack:** CampaignCore netstandard2.1; Studio net8.0; Harmony/IL2CPP runtime.

**Spec:** [Approved design](../../FOUR-FEATURE-DESIGN-2026-10-20.md), Play Now roster section.

## Global constraints

The [umbrella plan](2026-10-20-four-features.md) constraints and verification rules apply. Two lines contain exactly five distinct skaters each and one goalie. Retain `Export Version = 3`; add `Export Roster Version = 1`. Disabled second-line behavior does not export unused Line 2 players. In-game edits persist; an explicit Studio update can replace them after a clear confirmation.

## Review focus

- Two exports share a player referenced only by Line 2: updates cannot overwrite that shared player.
- A clone generates new IDs: positions remap to the cloned players, never the original save IDs.
- Native cosmetic saves cannot revert Line 2 to an obsolete library snapshot.
- Unknown/malformed extensions preserve the valid native fallback and emit one useful diagnostic.
- Home/opponent stamina settings and roster-edit scopes remain independent even for the same exported team on both sides.

## Task 1: Shared roster contract and complete export

**Files:** create `src/CampaignCore/PlayNowRoster.cs`; modify `src/CampaignCreator/ExportTranslation.cs`; use existing `src/CampaignCore/RosterPosition.cs` and `src/CampaignCreator/RosterLines.cs`; create `tests/PlayNowRosterChecks/PlayNowRosterChecks.csproj` and `Program.cs` referencing Core and Creator.

**Interfaces:** `PlayNowRoster` exposes `string[] Line1`, `string[] Line2`, `string GoalieId`, `bool StaminaEnabled`. `PlayNowRosterCodec.TryRead(CampaignDocument document, out PlayNowRoster roster, out string error)` returns false with empty error for no extension, false with a diagnostic for invalid/unknown extension, true only after full validation. `Write(CampaignDocument document, PlayNowRoster roster)` validates then replaces only extension fields. `PlayerIds(PlayNowRoster roster)` returns all eleven IDs. `Remap(PlayNowRoster roster, Func<string,string> map)` returns a validated copy.

Storage keys: `Export Roster Version`, `Export Line 1`, `Export Line 2`, `Export Goalie`, `Export Line Stamina`. Each line stores comma-separated safe IDs in Left Wing, Center, Right Wing, Left Defense, Right Defense order, matching `RosterLines.Skaters`; stamina is `yes` or `no`. IDs permit ASCII letters, digits, hyphen and underscore only. Core owns this wire order without referencing Creator. No new JSON dependency in Core.

- [ ] Add failing checks named `CompleteTwoLineExport`, `DisabledLineTwoIsNotExported`, `IncompleteLineRejectedBeforeWrite`, `DuplicateOrWrongRoleRejected`, `UnknownRosterVersionUsesFallback`. Assert 11 unique player records plus one team record, corresponding sidecars, exact slot order, and native JSON containing Line 1 only. Round-trip both stamina values.
- [ ] Run `dotnet run --project tests/PlayNowRosterChecks -c Release`; confirm failures are the missing extension/export behavior, not environment failures.
- [ ] Implement the codec and change `BuildTeam` to use explicit `RosterPosition.Read` results and existing squad/opponent two-line settings. Validate declared duplicates and extra goalies before applying any legacy roster fallback; `RosterLines.Assign` can redistribute duplicate positions and must not conceal malformed export slots. Preserve role-aware appearance/reward export for every player. Resolve authored `line_stamina` using the same existing team behavior policy and store the effective value, not a mutable template reference. Reject enabled incomplete lines before any filesystem writes.
- [ ] Run the new checks and existing `tests/PlayNowUpdateChecks`. Require exit 0/all checks passing and unchanged single-line output semantics.
- [ ] Review/commit this contract and export unit in the implementation checkout; record exact source/test evidence in the task receipt.

## Task 2: Ownership-safe updates and current-lineup reimport

**Files:** modify `src/CampaignCreator/ExportTranslation.Update.cs`, `src/CampaignCreator/GameCustomContent.cs`, `src/CampaignCreator/MainWindow.PlayNowExport.cs` and its caller in `MainWindow.cs`; extend `tests/PlayNowUpdateChecks/Program.cs` and Task 1 checks.

**Interfaces:** consume Task 1 codec. Retain `ExistingTeams(...)`, `WriteUpdate(...)`, and `Package` APIs. Add `ExistingTeam.LineupChanged` to expose a current-sidecar versus saved-library-roster difference. Include current native JSON and sidecar bytes in `ExistingTeam.Fingerprint` and commit-time expected-state checks.

- [ ] Add failing checks `LineTwoSharedOwnership`, `TwoToOneRetainsSharedFiles`, `NativeEditInvalidatesChooser`, `UpdateRollbackRestoresBothLines`, `ImportUsesCurrentSavedLineup`. Assert exact preexisting bytes after injected failures and no writes outside selected export-owned paths.
- [ ] Run `dotnet run --project tests/PlayNowUpdateChecks -c Release`; observe the new failures.
- [ ] Enumerate both-line references when deciding reuse and remap every extension ID alongside native slots. Refuse unsafe reuse if another team's malformed extension leaves ownership uncertain. Preserve standalone/shared player files. Reimport the live sidecar lineup rather than the stale library snapshot. When `LineupChanged`, show that updating replaces saved in-game lineup edits with Studio's roster in the existing update confirmation.
- [ ] Re-run update and roster checks, including 1→2, 2→2 and 2→1 updates plus concurrent sidecar mutation. Require all assertions passing.
- [ ] Review/commit the update/reimport unit independently.

## Task 3: Native loading and cloned-team ownership

**Files:** create `src/CustomCampaignFramework/PlayNowRosterRuntime.cs`; modify `PlayNowTranslation.cs`, `Plugin.cs`; create `tests/PlayNowRosterRuntimeChecks/{PlayNowRosterRuntimeChecks.csproj,Program.cs,NativeDoubles.cs}` using the source-linked runtime fixture pattern. Update protected native inputs during final integration.

**Interfaces:** `PlayNowRosterRuntime.TryHydrate(TeamRepository repository, TeamDataModel model, TeamData team)` returns true only after applying a complete validated extension. `TryGet(TeamData team, out PlayNowRoster roster)` resolves only registered exported owners. `Clone(TeamData source, TeamData clone)` transfers eligibility and remaps cloned references without restoring original IDs. The registry retains object identity checks to prevent reused native pointers from inheriting stale ownership.

- [ ] Add failing source-linked tests `HydrateAllOrNothing`, `RepeatedHydrationIsIdempotent`, `CloneTwiceDoesNotShareLines`, `ForeignTeamUnchanged`, `MissingPlayerKeepsNativeFallback`.
- [ ] Run the new executable checks and verify the expected failures.
- [ ] Inspect native `TeamRepository.DeserializeTeam`, `DeserializePlayers`, `GetAllCustomTeams`, `InstantiateForwardWithId`, `DeserializeGoalie` and `TeamData.Clone` on the pinned build. Record completed-load ordering and ID mapping in `artifacts/followup-four-features-20261020/play-now-native-review.md` before installing hooks. Resolve every L2 player before assigning lines. Register eligibility only on success and clear ownership on repository/campaign lifecycle changes.
- [ ] Implement the adapter and narrowly bound hooks. Run source-linked checks plus existing export-appearance checks; compile the runtime to a candidate output. Native doubles prove orchestration only; annotate this limit.
- [ ] Review/commit after signature/body evidence agrees with the hooks and no partial failure mutates the native fallback.

## Task 4: Editable rosters, stamina and restart persistence

**Files:** create `src/CustomCampaignFramework/PlayNowRosterSave.cs`, `PlayNowRosterContext.cs`; modify `LineupPreviewRoster.cs`, `SquadStaminaRuntime.cs`, `PlayNowTranslation.cs`, `NativeSquadBehaviorBridge.cs` only at scoped consumers; extend runtime/update tests.

**Interfaces:** `PlayNowRosterContext.Enter(TeamData team)` returns an `IDisposable` scope; `Active` is true only for a registered exported team and unwinds on exceptions. `PlayNowRosterSave.Save(TeamRepository repository, TeamData team)` owns the native/sidecar save transaction for eligible exported teams; unrelated teams use the original native path. `PlayNowRosterRuntime.TryGet` supplies authored stamina.

- [ ] Add failing checks `RosterScopeUnwindsOnThrow`, `NativeSavePersistsEditedBothLines`, `SaveFailureRecoversPreviousPair`, `SameTeamBothSidesIndependent`, `ManualSwapWithStaminaOff`, `StaminaOnBeforeHudCreation`. Assert restart reads exactly the saved positions; a failed save cannot publish native Line 1 from a different revision than the sidecar.
- [ ] Run runtime and update check projects and verify expected failures.
- [ ] Inspect native `SaveCustomTeam`, `TeamRosterData.UpdateTemporaryTeamData`, `ApplyChanges`, `UpdateMultiLineTeam`, `TeamStaminaSystem.Initialize`, `StaminaHudWidget.TryCreate`, and switching/input callers. Limit multi-line query routing to the two reviewed roster operations with guaranteed scope cleanup. Initialize each exported team's stamina before its HUD; do not require campaign-home flags for Play Now.
- [ ] Implement a recoverable write journal containing prior/new native team JSON and sidecar bytes, validated stable IDs and revision fingerprints. Persist all required player records before committing roster references. On load, finish or roll back an interrupted pair deterministically; invalidate cached team documents after success. Preserve existing appearance/reward data. Verify native cosmetics and second-line player edits are retained.
- [ ] Run all Play Now checks, existing `tests/TwoLineContractChecks`, and targeted export-appearance checks. Review/commit; pass new native contracts to final integration. Human checks remain: both teams, both lines, input/faceoff switching, stamina HUD, editor save/restart and reimport.
