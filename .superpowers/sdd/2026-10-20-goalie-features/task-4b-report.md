# Task4b partial implementer checkpoint

Status: **NEEDS_CONTEXT — unfinished4b, not ready for independent review**. Base `2bf85d2430ab66ae52b48b47e4e37661db5f127f`, branch `feature/four-approved-features-20261020`. All independent implementation and focused checks are complete; the controller requested a coherent checkpoint while the human clarification remains pending. No activation/profile change.

## Exact outstanding decision and resume instructions

The controller asked the human whether a saved unconsumed attempt from a canceled/unlaunched preview may be durably superseded when selecting another destination or active goalie, or must retain its original context. No answer has arrived. The gate therefore has an explicit pending-policy guard for mismatched saved context, with neutral unavailable text; this is **not** an approved reject-until-restored product policy. TryArmMatch currently supports first arming and exact context reuse only. No retirement/supersession/abandonment debit is implemented. `Lifecycle == Armed` is intent only and does not distinguish an actually started unfinished match from an unlaunched intent after process loss.

Resume this same implementer after the ruling: read the answer, finish the changed-context transition and its save/recovery tests, explicitly distinguish any canceled/unlaunched scope from actual started match/rematch policy, update interfaces/manifest/report, run only changed relevant checks and fresh pinned build, then request the planned independent review. Do not call4b complete, enable contracts, start4c or request review before that work. If the ruling needs durable additional lifecycle state, design it explicitly rather than inferring started status from the armed token.

## Implemented independent work

- Optional immutable versioned GoalieMatchAttempt extends the existing version1 checkpoint: token; campaign/run/squad; stable campaign-home team/goalie IDs; originating revision/checkpoint; intended act/type/index/layer; prelaunch route witness; Armed lifecycle. Context round-trips with recruitment consumption, contracts and run replay ledger. Task4a pure completion clears token/context together; no new completion caller.
- Roster.TryArmMatch reuses reviewed Stage/SaveChange and exact native receipt transaction. Mutation.Complete additionally checks matching token/context. Same-context retries save the same token. Reconciled local commit with native false/fault/cancel remains saved and cannot launch. Unknown readback uses existing unavailable/reload recovery and truthful preview UI.
- Paired manual StartMatch/OnStartInput gates precede native UI effects and retain method/menu/input/generation/run/team/goalie/node context. Duplicate clicks suppress into one pending save. ShowMenu replacement, Cancel, BackToMap, destruction, node/route/run/squad/goalie/visibility changes prevent stale reentry. Exact Durable receipt/admission/checkpoint/context permits one original wrapper invocation only. No early m_IsExiting assignment, loading block or second native task consumption.
- LaunchAuthorized emits an immutable runtime origin with exact ownership and saved operation/checkpoint. TryReuseOrigin requires that explicit unconsumed origin and currently selected destination for restart/rematch. MatchesDestination adds original node reference/type after native selection. Continue rehydrates saved context, returns to map and obtains a new runtime origin only from a new successful preview gate. Old/processed origins never rebind to later token. These helpers do not certify play or debit.
- New six-method/seven-patch manual contract set is resolve-only. No installer/startup invocation or compatibility profile edits; final coherent4a/4b/4c activation remains mandatory. Direct node/scene paths without a captured origin cannot authorize4c debit. All new callable/native/UI dependencies and limits are in task-4b-native-manifest.md; detailed4c API/lifecycle contract is in task-4b-interfaces.md.

## Focused RED/GREEN evidence

Logs: checkout `artifacts/four-features/goalie-task4b/`. Actual linked production gate, roster, bridge, checkpoint store and policy run in these checks. Native wrappers, native storage/tasks/UI remain boundary doubles; no Harmony installation/native execution/rendered or gameplay proof.

- `launch-red.log`: after176 pre-existing runtime assertions, ContractedPreviewWaitsBeforeNativeStartSideEffects failed against absent production gate. The native-boundary double entered original startup without a save. First implementation passes this check.
- `boundaries-red.log`: LateSaveCannotLaunchAfter_squad failed, revealing missing live squad revalidation. Added exact run/squad checks; GREEN.
- `recovery-red.log`: UnknownPersistenceExplainsContinueRecoveryInPreview failed because CanEdit false suppressed the recovery text. Kept launch blocked while explaining Continue on the surviving exact preview; GREEN. An initial missing-companion fixture reused identical storage across cases and correctly proved NoLocalWrite instead of unknown; gave each setup an independent filename before collecting the meaningful recovery RED.
- `proof-red.log`: ProofMismatchDoesNotClaimPlatformFailure_receipt-token failed. Strict mismatch already blocked launch, but text incorrectly blamed native platform failure. Durable mismatched proof now explains reload, while actual false/fault/cancel explains saved-local platform failure; GREEN.
- `attempt-store-red.log`: mutation test temporarily removed DataMember14 serialization, causing ExactImmutableAttemptRoundTripsWithDestinationAndOwnership to fail after76 checks; restored production annotation and87 pass. Mutation backup stayed ignored in evidence folder.
- `rematch-red.log`: RematchCannotReuseOriginAtDifferentNativeDestination failed; explicit-origin reuse now also requires original act/index/layer after native SetCurrentNode. GREEN.

Extra boundaries cover exact input argument/method, same-instance nested wrong entry, consumed permit reuse, one native task result consumption, foreign operation receipts, invalid receipt/admission/checkpoint token/id/caller, successful native save without companion, unknown physical readback, local commit with native false/fault/cancel, Cancel/reopen same-token retry, Continue, consumed origin, legitimate later identity, and base/permanent/PlayNow/zoom pass-through. No generalized suite expansion.

## Final verification and candidate

All commands ran in `C:/Users/kiran/Desktop/tape to tape mods/artifacts/release-r23-26085704-20261020/publish-checkout`, executable `C:/Program Files/dotnet/dotnet.exe`.

| Exact arguments | Result | Evidence |
| --- | --- | --- |
| run --project tests/GoalieRosterChecks -c Release |87 pass | store-final.log |
| run --project tests/GoalieRosterRuntimeChecks -c Release |238 pass (176 existing +62 launch/boundary) | runtime-final.log |
| run --project tests/GoalieRosterMenuChecks -c Release |130 pass | menu-current.log |

Total455 focused passing assertions. Menu initially failed compilation because the new source TeamData.id read was represented only in the launch fixture; moved that boundary property to shared NativeDoubles, then130 passed. Initial gate compilation also caught inner private-field access, corrected before meaningful gate GREEN. Those compile failures are harness/implementation development evidence, not native proof. Final source-linked fixture warnings are the existing extracted Away/Export and subset-unused boundary switches (CS0649); no broad suppression. No skater behavior was modified and no unrelated suites were repeated.

Pinned fresh runtime build:

```powershell
& 'C:/Program Files/dotnet/dotnet.exe' build src/CustomCampaignFramework/CustomCampaignFramework.csproj -c Release '-p:InteropLive=C:/Users/kiran/Desktop/tape to tape mods/artifacts/game-update-26085704-20261020/interop-generation/interop' '-p:InteropDir=C:/Users/kiran/Desktop/tape to tape mods/artifacts/game-update-26085704-20261020/interop-generation/interop' '-p:OutputPath=../../artifacts/four-features/goalie-task4b-native-20261020/'
```

`wrapper-final.log`:0 errors, one existing Plugin.cs(8431,34) CS0168 warning. Candidate DLL `artifacts/four-features/goalie-task4b-native-20261020/CustomCampaignFramework.dll`, SHA256 `08A539602D6F7999BBEBCCFA5D7BF630D888AEFA5972E65294B51CCB177D13DC`. Build is structural wrapper evidence only, not installed/runtime gameplay proof. PublicR23 outputs/receipts untouched.

Self-review checked changed production files, exact save ownership/reconciliation reuse, recruitment ledger preservation, no pointer/timestamp identity persistence, immutable captured origin, cleanup of one-use permits and pending handles, absence of actual completion/debit/startup activation, and explicit unsupported/pending boundaries. Final stage includes only owned source/tests/docs plus the exact test allowlist. No game, install, real-save mutation, canonical sync, push or release occurred. Source/build slot released after checkpoint delivery.

EOF-only trailing blank cleanup followed final tests/build; no semantic source change. Final cached diff check passed before checkpoint commit.
`741ba73051635ca7858688c2b58308777ca860d5` checkpoints the owned source/tests/interfaces/native manifest and this partial report. Working tree was clean after that commit; resume from it for the pending policy ruling.
