# Campaign map and fresh-run lifecycle repair

Parent source: `8bbb84d1b4a4444ba9ead806b5fcbc99bc7d7b85`. Installed predecessor: `E665E9C0AC7CFB565F0DECB81BADFD00FB1CE5A5753346D97D91C91E6D70D435`.

## Proven failures

The retest log records `GameManager.get_GameType` throwing from match Start and cancellation on the world map. Native build 26141904 dereferences the match manager, in-game data, and team selection in that property. The map has no obligation to retain those objects.

Normal native `RunManager.Start` inlines `ApplyNewRunStartEffects`; neither goalie baseline capture nor existing custom starting reward/skater-contract completion observed that method. `ResetRunData` creates a new run object and GUID. Both actual `CreateMapFromScratch` callers are in Start, after native starting effects or a completed-run reset, and before first map save.

GM event selection does not call `CampaignState.SetCurrentNode`. Native `EventChoiceMenu.InitData` assigns the event and map node, then Show invokes Initialize before visibility. GeneralManagerEvent.Initialize dispatches NextChoices through its actual virtual slot. Legacy UI calls EventChoicePanel.ShowMenu before Initialize. The authored fixture is valid and unchanged.

## Repair and authority

Map edits require the positive world-map state, no active match, and the exact selected campaign/run. Launch-origin reuse retains the existing committed permission and exact attempt identities through scene teardown. Match completion uses the concrete Rogue end-game context and exact home-team owner. Unknown or unassociated rosters cannot bypass contract authority. Existing positively associated marker-free R23 migration remains intact: permanent native active goalie, reserve disabled, no authored contract reset.

The independent FreshRun owner checks three named contracts against the pinned native profile. Reset prefix discards stale witnesses; successful reset postfix records only a changed native run pointer and GUID. The map prefix consumes that exact state/run/folder witness once and obtains the final selected squad after native starting effects. Hydration invalidates a pending witness. Empty maps, failed saves, Continue, missing companions, and disabled goalie options do not create fresh authority. The existing custom starting rewards and skater CompleteStarting run first, then the enabled goalie baseline captures the result. Shared starting behavior remains available when goalie activation fails; goalie activation requires the verified shared owner. Boundary logs report accepted reset, consumed/rejected map witness, and successful goalie binding.

Actual GM InitData and legacy ShowMenu bind the exact event, map node, selected coordinates, campaign/run/team, and route witness. The saved recruitment ledger hashes the selected GM coordinates rather than the previous match point. Event identity is checked before suppressing native choices. Actual closure/destroy invalidates only its own menu; temporary lineup takeover hiding preserves the visit. No synthetic native current-node mutation occurs. Selection logs report the resolved role and resulting offers.

## Diagnostics and limits

The red source-linked diagnostic reproduced the absent match-manager exception. Focused runtime diagnostics exercise absent-manager launch/cancel/completion, new/loaded run authority, rejected same identity/skipped reset/hydration/foreign owner/folder, final squad replacement, shared consumer independence, and exactly-once behavior. GM diagnostics exercise actual pre-visibility InitData order, permanent and finite offers, sibling nodes sharing an event, stale closure, legacy entry, failed saves, and canceled takeover. These use managed native-boundary doubles, not native gameplay.

The actual shipped Harmony provider compiles all 40 production plans (37 goalie plus 3 shared fresh plans), verifies all five embedded named contract sets, and checks pass/skip/throw provenance: 63 checks. This generates methods without executing native wrappers or installing detours. Production also compiles against installed interop. Existing startup/cache/card and published-package diagnostics run against the final staged DLL.

Native baseline changes retain every existing method body, callback address, and site. Three audited unique bodies are added: CreateMapFromScratch, EventChoiceMenu.OnClosed, and BehaviourDestroy. The named contract delta removes the dead goalie fresh callback, moves legacy binding to prefix, and adds actual GM lifecycle and independent fresh hooks. Derived baseline: `A1C673BA9F16617C31A7274F444D57F880C143200861ED0ADC5FEB08561DEBA6`. Installed predecessor is allowlisted for the updater.

Evidence remains under `artifacts/feature-test-crash-20261022/menu-context-failure`: original LogOutput/Player logs, context-red.log, native-lifecycle-review.md, native lifecycle traces, final-target-ownership-audit.json, new-boundary-bodyhashes.json, and final diagnostic logs/receipt. No game execution, live-save change, installation, canonical promotion, or release is performed by this repair task.

## Human acceptance pending

Using the separately preserved fresh test campaign, start New Run. Confirm the reset/map/fresh-binding log sequence, active goalie and empty reserve, and initial save. Enter the authored Goalies GM: permanent/finite labels and candidate cards should appear; canceling selection should leave the offer and roster intact; confirm a reserve hire and reopen lineup. Start a match, cancel once and reopen, then start and play to completion. Confirm contract decrement exactly once, active/reserve continuity, and Continue restoration. Retain failed logs if any step fails. Compilation and diagnostics do not establish this result.
