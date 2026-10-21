# Task 3 implementer report

Status: implemented and self-reviewed, ready for controller's independent review. Base `81ea5ab92ace00dcfd501a6e28d17132a0c4492a`. Work is isolated to `artifacts/release-r23-26085704-20261020/publish-checkout`; no canonical sync, game launch, native fixture, real save, install, push or publication. Implementation commit `cc1d656e875d2bbc41456d8b82fd9e1b4f840ac7` — Add gated goalie slot menus and durable recruitment transitions. Initial report/native manifest commit `7121a34`; this documentation follow-up adds the required literal Ruling labels.

## Result and file scope

- `GoalieRosterMenu.cs` adds real native LineupMenu cards, GM/legacy recruitment takeover, native mouse/controller callbacks and existing native navigation. Enabled capacity shows one Active or Active+Reserve, including a selectable empty reserve. Occupant names and actual Permanent / signed remaining count / expired-awaiting-replacement status appear. First activation previews replace/fill/swap in the header; second activation of the same slot confirms. Back cancels before confirmation, blocks while saving, and restores the owning GM/roster once.
- `LineupPreviewRoster.cs` has one purposeful entry change: its existing `LineupInitialRoster.Postfix` continues `FillInactiveLine` and then calls `GoalieRosterMenu.EntryLabel`. The latter lazily attempts the exact gated hooks and labels/unlocks the canonical campaign goalie card as Manage goalies. The manual LoadCurrentTeam postfix reapplies this entry after native refresh. Current roster menu is reused for swaps because native UIModule may retain a menu by type; parent forward edits stay in TeamRosterData. On exit, only m_G is refreshed from canonical active, then UpdateTemporaryTeamData/LoadCurrentTeam rebuild its preview. No authored starter is reapplied.
- `GoalieRecruitment.cs` replaces direct active assignment and pre-offer supplemental writes with detached complete candidate offers, current native event/run/node identity, revision, pending selection, cancellation and exact one-time consumption. Actual selected goalie pool remains required; short/missing pool never gets skater/native substitutes. Both roster occupants and already offered templates are excluded. Preparation captures the full candidate before exposing it.
- `GoalieRosterRuntime.cs` adds `TryRecruit`/`TrySwap` with **long** revisions, exact run/campaign/capacity checks and between-match Rogue-mode gating. It stages complete active/reserve graph+supplement+contract records, publishes the canonical active and earned-state owner together, invokes normal CampaignState.SaveRunData, and finalizes only its exact receipt. Reserve-only recruitment retains the exact valid unchanged active native reference/earned-state owner rather than recreating it. No unrelated skater policy changes.
- Authorized integration additions: `GoaliePersistenceBridge.cs` captures the scoped caller token, exact repository/run pointer, admitted frozen roster identity/revision and prior physical checkpoint proof; `CampaignCore/GoalieRosterState.cs` adds an optional separate persisted recruitment visit record. These are required to make offer consumption correct under real SaveRunData, reentry, uncertain disk writes and Continue.
- Tests extend existing runtime doubles and actual production helper extraction, add TransitionChecks and a source-linked GoalieRosterMenuChecks project, extend Core store checks, and add one no-op entry boundary to the existing TwoLine regression fixture. `.gitignore` explicitly allows only the four new test source/project files; bin/obj/fixtures remain ignored.

## Persistence and offer outcomes

| Evidence | Runtime/menu behavior | Offer / continuation |
| --- | --- | --- |
| Full candidate preparation fails, save never admitted, or exact prior physical fingerprint+marker confirmed unchanged (or no Store attempt), with still-owned rollback | Restore both prior records and exact old active/earned owner; allow retry/back | No consumption, no GM advancement |
| Reconciled local native+companion checkpoint, including native false/fault/cancel | Adopt checkpoint/revision and consume once; preserve actual native result/error, report local-save platform failure where applicable | Ledger and offer consumed; close and advance GM once; no memory-only rollback/refund |
| Physical readback unknown, unpaired changed write, unexpected replacement or ownership lost | Keep roster unavailable/locked; no invented success and no rollback authority | Offer marked unresolved/unusable (or consumed if receipt proves local commit), no duplicate retry; explicit main-menu + Continue recovery explanation |

Completion identifies the scoped caller receipt even when another SaveAdmitted subscriber reenters/drains the queue before this consumer's admission callback. Unrelated repository/run saves cannot steal the transition. NoReceipt rollback applies to failures before immutable admission; an admitted missing-companion receipt is still a possibly writing native operation and must be classified on completion. The ordinary SaveRunData path replaces TeamState before repository invocation, covered by the doubles and native body evidence. `Durable` retains Task2b's native true + no error + reconciled definition for Task4; Task3 consumption uses confirmed local commitment, not platform outcome.

Destroy/close unregister exact delegates once. If destruction happens while a save is pending, the pending offer remains unavailable until its actual callback. A late completion still consumes proven signing but cannot disable/unlock a newly loaded run; change ownership token prevents that. Unresolved Back may close the slot view, but the roster and offer remain locked. Reopening alone does not clear the lock; actual Fresh/Load/NoSelectedRun rebinding does, and Continue only enables editing if the persisted pair is reconciled. Missing/corrupt/legacy companion remains unavailable editable roster, with existing native active/legacy appearance fallback preserved.

## Technical rulings and cost if wrong

1. Ruling: **Scoped receipt identity addition, authorized by controller.** Normal SaveRunData returns void and can synchronously admit unrelated/reentrant saves. Bind one exact injected repository/run/caller operation; keep frozen full Admission scope+revision in receipt. Cost if wrong: the menu could consume or roll back against another save. Exact proxy/operation/reference checks, pointer scope and reentrant callback-order test defend this.
2. Ruling: **Three persistence outcomes, authorized by controller.** Only exact unchanged previous checkpoint or no native Store authorizes rollback/reuse; confirmed local pairing consumes despite platform failure; uncertain physical state locks. Cost if wrong: duplicate recruitment or loss of prior active through memory-only rollback. Readback unknown and confirmed unchanged prior checkpoint are separate checks; actual native result/error is retained unchanged.
3. Ruling: **Separate durable recruitment visit ledger, authorized by controller.** Persist Witness, GUID Generation, native Quota, unique ConsumedOfferIds and OfferedPoolIds in the same roster checkpoint as the changed occupant. Cost if wrong: extra schema/lifecycle complexity or suppressing a legitimate later visit. Native completed-route evidence binds a later generation to changed persisted path; tests cover same pre-exit checkpoint reload, replaced-away consumed goalie, genuinely changed route, unrelated map bytes/formatting, missing pool and remaining choices. Match ProcessedAttemptTokens is untouched.

The witness is a hash of canonical persisted map.act, run.CurrentAct, current node coordinates and ordered saved path points; containing checkpoint already binds folder/run/squad. It does not hash full currentMap bytes: formatting, nodes/reward fields and unrelated edits cannot regenerate signing quota. Native Exit appends the completed path after recruitment; the checkpoint before GM continuation therefore resumes the same visit. Reentry after actual later traversal produces a new generation. Offers use generation + consumed count + pool template to form stable IDs. Consumed quota survives replacement of a prior hire, and missing/stale pool candidates never replenish quota. A new visit is saved on first confirmed hire, atomically with its consumed offer; merely opening/cancelling never writes or consumes.

Backward defaults: `RecruitmentVisit` absent in a valid V1 checkpoint is null (no known goalie signing in that older schema). New arrays are optional only by omission of the whole visit; malformed present records (null/duplicate IDs, invalid generation/quota) fail validation. Old Task2-only checkpoints had no Task3 hiring path. This does not migrate legacy/missing companion files into an editable roster and does not infer reserve capacity from current authoring.

## RED / GREEN evidence

- Initial runtime tests failed compile because TryRecruit/TrySwap did not exist; initial menu project failed missing GoalieRosterMenu source. Harness-only missing native shapes were then completed, not misrepresented as behavioral proof.
- Meaningful menu RED: `ChooseEmptyReserveChangesOnlyReserve` failed because reserve-only recruitment restored a new active object. Production Mutation now preserves the exact unchanged active plus earned owner; check passed with the full menu path.
- Ledger tests added before schema/VisitLedger implementation failed compile for missing ledger APIs/types. Retained `artifacts/four-features/goalie-task3-research/ledger-red.log`; green checks exercise actual Choices and actual checkpoint store roundtrip.
- Runtime reentry RED: `ForeignReentrantAdmissionCannotStealExactSaveReceipt` failed after accepted transition because a subscriber drained completion before the initiating consumer's admission callback. Completion now claims the same exact caller-scoped receipt if admission has not arrived; focused runtime check then passes. No queue/proxy failure behavior changed.

## Fresh verification (2026-10-20)

Commands run from isolated checkout, executable `C:/Program Files/dotnet/dotnet.exe`:

| Command (Release) | Result |
| --- | --- |
| `dotnet run --project tests/GoalieRosterMenuChecks -c Release` | 76 source-linked menu checks pass |
| `dotnet run --project tests/GoalieRosterRuntimeChecks -c Release` | 132 source-linked runtime/persistence checks pass |
| `dotnet run --project tests/GoalieRosterChecks -c Release` | 48 store/ledger checks pass, including selected-path link guard |
| `dotnet run --project tests/TwoLineContractChecks -c Release` | 1063 directly affected source-linked roster/recruitment regression checks pass |

Final wrapper build:

```powershell
& 'C:/Program Files/dotnet/dotnet.exe' build src/CustomCampaignFramework/CustomCampaignFramework.csproj -c Release '-p:InteropLive=C:/Users/kiran/Desktop/tape to tape mods/artifacts/game-update-26085704-20261020/interop-generation/interop' '-p:InteropDir=C:/Users/kiran/Desktop/tape to tape mods/artifacts/game-update-26085704-20261020/interop-generation/interop' '-p:OutputPath=../../artifacts/four-features/goalie-task3-native-20261020/'
```

Candidate DLL SHA256: `27765BAE6B0E36988732EC4787516417CAE7620657E728EF4C72AC1CCDA7B49A`.

Result: 0 errors, one existing `Plugin.cs(8431,34)` CS0168 warning. Test fixture compiles also report CS0649 for intentionally unassigned native boundary/production-extraction subset fields (Away/Export, and deliberately unused shared harness switches in each subset); not suppressed. Logs are `artifacts/four-features/goalie-task3-research/{menu-checks,runtime-checks,store-checks,twoline-checks,wrapper-build}.log`. No unrelated suites rerun.

Menu fixtures source-link the actual runtime, bridge, store, menu and extract actual recruitment lifecycle/Choices plus actual existing appearance helpers. Native menu/card navigation, Unity objects/tasks/storage boundaries are doubles. Candidate construction within the menu-only extraction is a native boundary stub; full Candidate behavior was covered by earlier task checks, not claimed rerun here. Assertions cover enabled/disabled reserve, replacement confirmation, full-state swap, signed-status display, forbidden modes, cancel, double-click, stale visit, native menu takeover, parent preview/forward preservation, exact listener cleanup, failure/unknown/late completion, and persisted quota recovery.

## Self-review / integration handoff

Reviewed full source/test diff against base, brief checklist and three persistence outcomes; checked no per-offer supplement write/direct goalie replacement remains. New manually gated hooks and semantic native calls/RVAs/evidence are enumerated in `task-3-native-manifest.md`; attribute-only audit is insufficient. All exact hook signatures compile against the reviewed fresh wrappers. Parent roster reuse is intentional rather than showing a second potentially retained same-type menu. Current report provides Task4 optional signed-contract input and correct display; contract expiration/match accounting policy is not implemented here.

Remaining limits: all real IL2CPP/Unity/ES3 execution, rendered mouse/controller interaction, empty-card rendering, retained native menu timing, actual event/async callbacks, physical map/save lifecycle and human acceptance remain pending final native fixtures/gameplay. The menu hooks remain off under the unchanged final profile. These are explicit unvalidated boundaries, not passing native claims. No unresolved technical blocker identified in focused self-review.

Post-verification edits were documentation and removal of trailing whitespace in the menu test project only. `git diff --cached --check` passed before the implementation commit. Source/build slot released to controller after documentation commit; Task3 remains pending independent review.


## Review fix round 1/5 — I1 parent presentation restoration

Reviewed base `2307d79`; source fix commit `25162f1bde3fb42bc93f76572d049fd56613558e` — Restore parent lineup presentation after goalie management. Addressed Important I1; also improved M1's fixture boundary in the same authorized files. M2 remains documented below. Scoped self-review found no further production changes needed; independent rereview remains pending.

Exact production change is confined to `GoalieRosterMenu.Selection`, `Open`, and `Finish`: capture the original parent's `m_HideBench` and `m_BenchPlayersList.gameObject.activeSelf` before any takeover/preparation; restore both before `Team.UpdateTemporaryTeamData` and `LoadCurrentTeam`. `ParentPolicyCaptured` avoids using an uncaptured default on early failure. The retained `TeamRosterData` and `m_AreSkatersLocked` are unchanged; normal native refresh rebuilds active/bench cards, names, locks, header, contract-duration containers and selection presentation. The existing campaign goalie entry label is reapplied. Restoring visibility explicitly matters because a native refresh while in the goalie subview can deactivate the bench object; the native visible-policy branch repopulates it without reactivating it.

Native evidence checked against supplied LoadCurrentTeam capture: policy branch at `0x43894AD` skips bench population; visible branch calls SkaterCardList.Refresh at `0x4389DE0`; hidden branch calls Component.get_gameObject at `0x4389DF9` and GameObject.SetActive(false) at `0x4389E0F`. The prior profile/hook gate remains unchanged. The manifest addendum records the additional accessor dependency.

Meaningful RED before production edit: the strengthened native-shaped LoadCurrentTeam double respects hide policy, skips bench population and deactivates its object; a subsequent visible refresh does not automatically reactivate it. The new test failed **ParentBenchPolicyPopulationAndVisibilityRestored_cancel** on reviewed production code. Retained output: `artifacts/four-features/goalie-task3-research/review-i1-red.log`. After the source fix, all five exit scenarios pass: Cancel, confirmed swap, preparation failure after Configure (injected initial-focus failure), native refresh while the subview is active then Cancel, and an originally hidden bench. Checks include bench/second-line UI data+card population, original visibility/policy, normal header, position/name labels, lock policy, removal of goalie contract-duration presentation, unselected toggles, exact pending forward reference/HasChanged retention, and campaign goalie entry. The refresh case also verifies retired cards no longer dispatch to the list.

M1 fixture refinement: test activation now flows through `AbstractNavigable.Click -> Toggle.OnNavigableClicked/SetSelected -> SkaterCardList.OnSkaterCardToggled -> LineupMenu callback -> actual GoalieRosterMenu.Pick`. Refresh/Clear detach the old card subscription; locked toggles reject activation; suppressing toggle selection events during the production reset is respected. Focus changes are a separate boundary and never activate a toggle. The tests verify initial focus and empty-reserve focus produce no callback/confirmation, then require two explicit activation events for the actual slot action. Evidence is the review's pinned Toggle 0x9EFD30/0x9EFDC0, list callback 0x44D3670 and LineupMenu.BehaviourEnable 0x43864C0 bodies. This is a better behavioral double, **not** rendered controller-navigation proof; the prior two-count reachability test was renamed/replaced to avoid that claim.

Fresh focused verification from this checkout:

- `C:/Program Files/dotnet/dotnet.exe run --project tests/GoalieRosterMenuChecks -c Release`: **90 checks pass**, final output `review-i1-green.log` (run again after strengthening only the presentation assertions).
- `C:/Program Files/dotnet/dotnet.exe run --project tests/TwoLineContractChecks -c Release`: **1063 checks pass**, output `review-i1-twoline.log`.
- Fresh-wrapper build using the exact full command in the initial report, same reviewed `InteropLive`/`InteropDir` and candidate-only output: **0 errors**, one pre-existing Plugin.cs(8431,34) CS0168 warning; output `review-i1-wrapper-build.log`.
- Candidate DLL SHA256 after this fix: `BF2068133E4C8F1F93F2B0721B535CD5D198F3D66BE32CC9A674E87283C19E45`.
- `git diff --cached --check` passed before source commit. No persistence/runtime/store source changes and no unchanged runtime/store suites rerun.

All logs are under checkout `artifacts/four-features/goalie-task3-research/`. M2: existing CS0168 remains; menu subset still reports CS0649 for shared harness switches and extracted Away/Export bindings that this project intentionally does not assign. These are unchanged diagnostics, disclosed rather than hidden with broad suppression. No unrelated cleanup.

Limitations remain unchanged: no game/native fixtures or real saves executed; actual Unity card rendering, physical layout/controller focus traversal, native event timing and gameplay acceptance remain pending. Source/build slot released after documentation commit. Scoped rereview should assess the fix range from `2307d79`, not repeat unrelated Task2/3 review.
