# Campaign goalie feature implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add campaign recruitment controls, an optional squad reserve goalie, between-match swaps and saved played-match contracts.

**Architecture:** Keep the active goalie in native TeamData and the reserve in a run-scoped companion checkpoint. Pure policy/state transitions sit in Core; native adapters capture complete goalie state and coordinate saves, roster UI and match/departure callbacks.

**Tech Stack:** CampaignCore netstandard2.1, Avalonia Studio net8.0, Harmony/IL2CPP runtime, native V2 run serializer.

**Spec:** [Approved design](../../FOUR-FEATURE-DESIGN-2026-10-20.md), confirmed toggle rules and Reserve goalie/contracts section.

## Global constraints

The [umbrella plan](2026-10-20-four-features.md) constraints apply. Recruitment is per campaign; reserve slot is per playable squad. Both default off, including absent fields. Node role defaults Skaters. Goalies is hidden/unselectable until campaign recruitment is enabled; a visible reminder tooltip explains why. Preserve authoring data rather than silently changing existing nodes. One active plus at most one reserve; no Play Now reserve and no in-match swaps. Default custom duration is four played matches, off retains permanent-until-replaced goalie behavior. Expiry promotes the reserve or retains the active goalie until replacement.

## Review focus

- Unsaved campaign settings must drive map controls immediately, while invalid imported/stale nodes remain visible as validation errors.
- Native save and companion writes can fail independently; never load a reserve from a different checkpoint/run.
- Earned rewards/stats, independent appearance and both kit logos survive repeated swaps and Continue.
- Duplicate callbacks, canceled cards and reopened menus cannot consume a signing or count a match twice.
- Departure prevention and an empty reserve defer expiry without resetting contract progress or creating an empty active slot.

## Task 1: Authoring gates and scoped settings

**Files:** create `src/CampaignCore/GoalieFeatureOptions.cs`; modify `src/CampaignCreator/CampaignWorkspace.cs`, `CampaignWorkspace.Goalies.cs`, `MapEditorView.cs`, `TeamEditorBehaviors.cs`, `UpgradeCleanup.cs`, `src/CampaignCore/SquadBehaviors.cs`, `src/CustomCampaignFramework/GoalieRecruitment.cs`; verify and reuse existing `src/CampaignCore/MapLayout.cs` role serialization and recognized-role validation; extend `tests/GoalieFreeAgentChecks/Program.cs`; add `tests/GoalieFeatureEditorChecks/{GoalieFeatureEditorChecks.csproj,Program.cs}`.

**Interfaces:** `GoalieFeatureOptions.Recruitment(CampaignDocument campaign)` reads `Goalie Recruitment` from `campaign.txt`; `Reserve(CampaignDocument squad)` reads `Squad Behavior reserve_goalie` from that squad's `team.txt`. Missing/invalid values return false; accept established on/yes/true/1 spelling. Add behavior `reserve_goalie` with no native template defaults. `ValidateNodes(bool recruitmentEnabled, IEnumerable<MapLayout> layouts)` returns node-specific messages for Goalies roles with recruitment disabled. `MapEditorView.GoalieRecruitmentEnabled` is a `Func<bool>` supplied by CampaignWorkspace's current draft, with standalone-editor fallback to saved campaign settings.

- [ ] Add failing tests for defaults off, campaign/squad isolation, all four combinations, unsaved toggle updates, missing controls in non-playable team editors, Save/reopen, hidden Goalies option and available reminder tooltip. Imported Goalies nodes with feature off must remain unchanged and generate a named validation error.
- [ ] Run `dotnet run --project tests/GoalieFeatureEditorChecks -c Release` and `tests/GoalieFreeAgentChecks`; observe expected new-policy failures, updating old fixture inputs to opt in explicitly where required.
- [ ] Add the campaign checkbox on Free Agents/Goalies and reserve control in playable-squad behavior settings. Tooltip text: `Enable Goalie recruitment in campaign settings to choose Goalies for this node.` Refresh node choices when the draft changes. Validation prevents saving an incompatible node/configuration pair and tells the author to enable recruitment or change the node; no automatic conversion.
- [ ] Preserve settings through import/share/cleanup. Add the same campaign gate to runtime selection, with no accidental skater/native fallback for malformed externally edited Goalies nodes. On that invalid runtime path keep the event escapable using the existing empty-offer continuation and log the precise configuration error; do not recruit anything.
- [ ] Run focused editor/Core/runtime recruitment checks. Review/commit this gating unit before reserve work; verify ordinary Skaters nodes are unchanged.

## Task 2: Full goalie state and recoverable checkpoint store

**Files:** create `src/CampaignCore/GoalieRosterState.cs`, `GoalieRosterStore.cs`; create `src/CustomCampaignFramework/GoalieRosterRuntime.cs`, `GoalieNativeSnapshot.cs`; modify `GoalieRecruitment.cs`, `Plugin.cs`, existing save/Continue hooks; create `tests/GoalieRosterChecks/{GoalieRosterChecks.csproj,Program.cs}` and `tests/GoalieRosterRuntimeChecks/{GoalieRosterRuntimeChecks.csproj,Program.cs,NativeDoubles.cs}`. Extend protected native inputs during integration.

**Interfaces:**

- `GoalieSlot { Active, Reserve }`; `GoalieRecord` stores `PlayerId`, complete serialized native goalie payload, supplemental `CampaignDocument` text, and optional contract state.
- `GoalieContractState` stores signed `Duration`, `PlayedMatches`, `LastMatchKey`, and `AwaitingReplacement`.
- `GoalieRosterState` stores version 1, campaign/run/squad identity, saved reserve-enabled policy, native checkpoint identity, monotonic revision, active record and optional reserve record. Clone before transitions; do not expose mutable shared lists.
- `GoalieRosterStore.Prepare(string campaignFolder, GoalieRosterState state)` returns a prepared checkpoint token; `Commit(string token, string nativeCheckpointHash)` marks its matching native save committed; `Load(string campaignFolder, string runId, string nativeCheckpointHash)` returns only a matching validated checkpoint. Store under `_goalie_rosters` with hashed safe filenames; journal writes use atomic replacement and retain the previous recoverable revision.
- `GoalieNativeSnapshot.Capture(GoaltenderData goalie)` returns a `GoalieRecord`; `Restore(GoalieRecord record)` returns a fully restored native goalie or throws before modifying the roster. Reuse `GoalieRecruitment.Snapshot`/existing supplemental restoration, extended for both jersey slots.

- [ ] Add failing checks `FullStateRoundTrip`, `NoCrossRunRestore`, `NativeSaveFailedKeepsPreviousCheckpoint`, `CompanionCommitFailedRecoversPreparedMatch`, `CorruptNewestUsesMatchingPrevious`, `UnknownVersionKeepsNativeActive`, `OldRunStartsWithoutReserve`. Cover every write boundary with fault injection and compare exact prior native/companion bytes.
- [ ] Run both new test projects; confirm expected failures.
- [ ] Inspect native SerializerV2 goalie serialization, DeserializerV2 conversion, RunDataRepositoryV2 Save/Load/SaveToPersistentStorage and actual async completion. Record which serialized native bytes uniquely identify the saved checkpoint, including active goalie state and run progress. Use a durable prepared record before native save and promote only after actual successful completion; a postfix on the async method's initial return is insufficient.
- [ ] Implement the store and native adapter. Use the native V2 model for earned fields and supplemental data for omitted appearance/number/pass-read fields. On Continue match the exact native checkpoint, validate both slot identities and deserialize before roster mutation. No matching companion means keep the valid native active goalie and report the missing reserve; do not restore a guessed latest record. New native usage requires audited signatures and protected methods.
- [ ] Run store/runtime tests and the existing actual candidate/supplemental goalie checks. Test that disabled reserve policy cannot delete an already saved goalie during Continue: saved run policy governs existing roster capacity; authoring changes apply on a fresh run. Review/commit with the checkpoint recovery evidence.

## Task 3: Recruitment slot choice and between-match swap UI

**Files:** create `src/CustomCampaignFramework/GoalieRosterMenu.cs`; modify `GoalieRecruitment.cs`, `GoalieRosterRuntime.cs`, `LineupPreviewRoster.cs` only for the explicit campaign entry point; extend Task 2 runtime tests and add `tests/GoalieRosterMenuChecks/{GoalieRosterMenuChecks.csproj,Program.cs,NativeUiDoubles.cs}` using the existing TwoLineContractRoster fixture pattern.

**Interfaces:** `GoalieRosterRuntime.TryRecruit(CampaignState state, GoaltenderData candidate, GoalieSlot slot, int expectedRevision)` and `TrySwap(CampaignState state, int expectedRevision)` return false without mutation if scope/revision/capacity is invalid. `GoalieRosterMenu.Open(CampaignState state)` opens between-match controls. Recruitment selection binds the existing offer revision and campaign/run/node identities; cancel commits nothing.

- [ ] Add failing tests `ReserveDisabledReplacesOnlyStarter`, `ChooseEmptyReserve`, `ChooseFilledSlotShowsReplacement`, `CancelDoesNotConsumeOffer`, `DoubleClickCommitsOnce`, `StaleVisitCannotRecruit`, `SwapPreservesEarnedState`, `InMatchAndPlayNowCannotSwap`, `ControllerNavigationReachesBothSlots`.
- [ ] Run menu/runtime check projects; verify expected failures.
- [ ] Implement the native-menu integration after reviewing the existing LineupMenu/card/navigation lifecycle. Display exactly enabled slots, occupant names and contract status. Prepare a detached preview and complete snapshots before commit. On confirmed recruitment change only the selected slot; on swap exchange both full records and the native active reference. A failed save/preparation retains both prior occupants and the signing offer.
- [ ] Refresh native roster previews and appearance bindings without applying the authored starter over a recruited/swapped goalie. Keep game-state ownership separate from preview clones. On Close/Cancel unwind menu listeners and re-enable the GM continuation exactly once.
- [ ] Run menu/runtime/store checks and existing `tests/TwoLineContractChecks` as the directly affected UI regression. Review/commit. Rendered mouse/controller behavior remains pending gameplay evidence.

## Task 4: Signed goalie contracts and safe expiry

**Files:** create `src/CampaignCore/GoalieContractPolicy.cs`, `src/CustomCampaignFramework/GoalieContractRuntime.cs`; modify `src/CampaignCreator/PlayerContractEditor.cs`, `PlayerEditorView.cs`, `GoalieRecruitment.cs`, `GoalieRosterRuntime.cs`; extend Task 2 fixtures and `tests/PlayerContractEditorChecks/Checks.csproj`; update `docs/PLAYER-CONTRACTS.md` and Studio guide.

**Interfaces:** reuse `PlayerContractOptions.Duration(CampaignDocument)` and default 4. `GoalieContractPolicy.Sign(int duration)` creates zero-played state. `CompleteMatch(GoalieRosterState state, string matchKey, IReadOnlyCollection<string> participantIds, bool departurePrevented)` returns a new validated state, debiting each participating goalie once. `ReconcileDeparture(GoalieRosterState state, bool departurePrevented)` promotes a valid reserve or marks an expired active as awaiting replacement. `GoalieContractRuntime` obtains real native match identity, participation and departure protection before calling policy; it never registers goalies in forward-only free-agent collections.

- [ ] Add failing checks for enabled default four, explicit durations, invalid values, initial starters versus new recruits, signed duration frozen after author edits, exact participation, bench-only matches, duplicate completion after reload, Handcuffs/departure prevention, expiry promotion, empty-reserve deferral, expired reserve removal, and replacement of a deferred-expiry active. Assert `Active != null` for every accepted transition and that swap never resets progress.
- [ ] Run goalie roster/runtime and `dotnet run --project tests/PlayerContractEditorChecks/Checks.csproj -c Release`; confirm expected failures without changing existing skater policy expectations.
- [ ] Add goalie support to the existing contract controls with goalie-appropriate off/help text. Sign only at fresh-run creation or confirmed recruitment; Continue consumes saved state. Display actual signed duration on offers. Reuse the reviewed native departure-prevention outcome rather than a guessed relic-name test.
- [ ] Review native match start/end/participation and departure ordering on the pinned build; record the persistent match identity and replay behavior. Snapshot actual goalie participation, debit and reconcile once, then checkpoint the transition through Task 2. A contract expiring without backup remains expired but active; signing an available replacement completes the pending departure with a clear pre-confirmation result.
- [ ] Run the complete focused goalie checks plus unchanged `tests/PlayerContractRuntimeChecks/Checks.csproj` and affected skater recruitment tests. Review/commit. Integrate the native contracts and exact candidate audit through the umbrella plan; do not call builds proof of Save/Continue gameplay.
