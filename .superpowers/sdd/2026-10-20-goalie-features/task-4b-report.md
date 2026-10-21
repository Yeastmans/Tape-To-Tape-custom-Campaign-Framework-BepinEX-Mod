# Task4b implementer report

Status: **DONE — ready for independent full4b review**, with native/human validation and final combined activation still pending. Full review base `2bf85d2430ab66ae52b48b47e4e37661db5f127f`, branch `feature/four-approved-features-20261020`. Earlier partial checkpoint commits `741ba73051635ca7858688c2b58308777ca860d5` and `67cbce07f6e0bb0838c2f9239996ac0f01079669` are included in the review scope; this final report replaces their NEEDS_CONTEXT status.

## Approved policy and implementation

The user answered YES to replacing a canceled preview attempt when choosing a different match or swapping the starting goalie, with no contract debit for the canceled attempt. That exact scope is implemented. Actual started-match abandonment is not inferred or added. The controller confirmed the conservative process-loss provenance boundary below.

- Immutable GoalieMatchAttempt extends existing version1 checkpoint with token, exact campaign/run/squad, campaign-home team and goalie stable IDs, source revision/checkpoint, prelaunch route witness and intended act/native type/index/layer. RecruitmentVisit, signed contracts, reserve progress and run replay ledger survive each transition. No pointer/timestamp/completion-created identity persists.
- Both native StartMatch and inlined OnStartInput gate before native UI effects, capture exact origin/generation/input/node/run/team/goalie, join repeated clicks and require the exact strict Durable save receipt/admission/token/checkpoint before one original wrapper reentry. Cancel remains usable; no early m_IsExiting or loading block. Native save task has only its existing consumer. Reentry revalidates after origin subscribers, including synchronous cancellation.
- Same-context retry uses its saved token. Native false/fault/cancel after local write retains saved context and never launches. Unknown persistence makes the roster unavailable and explains Continue recovery. Proven no-write rolls back the exact mutation.
- Explicit Cancel/BackToMap with an owned never-permitted runtime witness saves CanceledBeforeLaunch via existing Stage/SaveChange. A pending arm save reconciles first, then schedules that cancellation. The native menu can close while the persistent bridge completes it. New input waits for cancellation reconciliation. Same-context retry rearms the same token; a changed-context start atomically appends the canceled token to ProcessedAttemptTokens and saves a distinct new token, without contract progress changes. Local confirmed cancellation/replacement with platform failure remains saved; replacement still needs its own successful Durable receipt before launch. No-write rollback restores both old context and retirement ledger.
- Pure CompleteMatch clears token/context together, rejects an unconsumed CanceledBeforeLaunch attempt, and treats retired canceled tokens as run-level replay no-ops. No actual participation/completion/debit caller is added. ProcessedAttemptTokens now contains completed **and retired canceled** keys; membership is not proof of a charge.
- LaunchAuthorized exposes a detached immutable attempt and exact runtime origin for4c. TryReuseOrigin/MatchesDestination require that explicit unconsumed Armed origin and expected native destination after SetCurrentNode. Continue returns to map, rehydrates the context and gets a new runtime origin only from a successful gated preview. No initialization-generated identity or mutable-current-token binding. Direct node/scene starts without this origin are unowned for4c.

The final interface/lifecycle/activation contract is `task-4b-interfaces.md`; every new native method/property/UI dependency and limit is in `task-4b-native-manifest.md`. Six native method contracts/seven patches are declared resolve-only, with no installer/startup invocation or HarmonyPatch attributes. Compatibility profile remains unchanged. Do not activate4a signing,4b launch or4c completion independently.

## Provenance limitation

After startup permission is committed, the runtime never-launched witness is removed. Later preview Cancel cannot turn that origin into a canceled unlaunched attempt. After process loss, an Armed record is also ambiguous: same-context retry remains available, but changed-context launch shows "This saved match may already have started. Resume its original match and goalie; a changed attempt cannot be verified." It never invents cancellation or charges abandonment. Saved CanceledBeforeLaunch survives Continue and supports approved replacement. This is an explicit provenance boundary accepted by the controller, not an unresolved product question. Actual native callbacks/UI/save timing remain human validation.

## RED/GREEN evidence

Logs are in checkout `artifacts/four-features/goalie-task4b/`. Tests execute linked production gate, roster, bridge, checkpoint store and policy; native methods/tasks/storage/UI are boundary doubles. No Harmony/native/rendered/gameplay claims.

- `launch-red.log`: missing production gate allowed startup before save after176 prior assertions; initial gate GREEN.
- `boundaries-red.log`: live squad change could still launch; exact live run/squad check fixed it.
- `recovery-red.log`: unknown persistence blocked launch but left saving text; surviving exact preview now explains Continue.
- `proof-red.log`: mismatched proof blocked launch but incorrectly blamed platform failure; text now distinguishes durable proof mismatch from actual platform failure.
- `attempt-store-red.log`: temporarily removing DataMember14 made exact attempt roundtrip fail; restored annotation passed.
- `rematch-red.log`: explicit-origin reuse accepted another native destination; now requires original act/index/layer after native selection.
- `cancellation-red.log`: pending Cancel did not persist unlaunched lifecycle; new observed-cancellation save path fixes it, preserving zero debit.
- `handoff-cancel-red.log`: cancellation during synchronous origin handoff could reach native startup; post-subscriber revalidation now withholds it and saves cancellation.
- `cancellation-store-red.log`: temporarily removing the canceled completion guard allowed an unconsumed canceled token to complete; restored guard passes.

Focused boundaries additionally cover exact input/method/argument and one startup, duplicate and nested wrong-entry requests, one repository task result consumption, cancellation/reopen/destruction/route/node/run/squad/goalie/visibility changes, foreign/mismatched receipts and missing companion, native false/fault/cancel after local commit, unknown readback, pending cancellation, changed destination, actual between-match goalie swap, cancellation/replacement no-write rollback, canceled Continue, ambiguous Armed Continue, actual permitted origins, later attempts and stale/retired replay, base/permanent/PlayNow/zoom pass-through. Recruitment consumption and both goalie contracts survive replacement.

Development-only compile corrections: nested private-field accessibility in initial implementation, shared TeamData.id native double placement for menu fixtures. The initial missing-companion fixture reused identical storage and correctly proved NoLocalWrite rather than unknown; independent filenames made its intended unknown case meaningful. These are documented development issues, not native proof. No unrelated suites were repeated.

## Final verification

Working directory: `C:/Users/kiran/Desktop/tape to tape mods/artifacts/release-r23-26085704-20261020/publish-checkout`. Executable: `C:/Program Files/dotnet/dotnet.exe`.

| Exact arguments | Result | Log |
| --- | --- | --- |
| run --project tests/GoalieRosterChecks -c Release |92 pass | store-final.log |
| run --project tests/GoalieRosterRuntimeChecks -c Release |275 pass | cancellation-runtime-green.log |
| run --project tests/GoalieRosterMenuChecks -c Release |130 pass | menu-final.log |

Total497 focused passing assertions. Source-linked fixture warnings remain existing extracted Away/Export and subset-unused boundary switches (CS0649); no broad suppression.

Fresh pinned wrapper build:

```powershell
& 'C:/Program Files/dotnet/dotnet.exe' build src/CustomCampaignFramework/CustomCampaignFramework.csproj -c Release '-p:InteropLive=C:/Users/kiran/Desktop/tape to tape mods/artifacts/game-update-26085704-20261020/interop-generation/interop' '-p:InteropDir=C:/Users/kiran/Desktop/tape to tape mods/artifacts/game-update-26085704-20261020/interop-generation/interop' '-p:OutputPath=../../artifacts/four-features/goalie-task4b-native-20261020/'
```

`wrapper-final.log`:0 errors, one existing Plugin.cs(8431,34) CS0168 warning. Candidate `artifacts/four-features/goalie-task4b-native-20261020/CustomCampaignFramework.dll` SHA256 `CDC869FC64C7996F9A68E5B6F33F31D66705455B0AFC6FDB3EA057358935B42D`. This replaces the earlier partial candidate in the same candidate-only folder; publicR23 artifacts/receipts untouched. No semantic source changes followed these checks/build.

Self-review covered the full changed production implementation and approved cancellation transitions, exact save ownership and reconciliation, replay and roster invariants, pending/reentrant cancellation, lost-runtime-witness limits, no native forward collection changes, no actual play/debit inference and no partial activation. Final exact-set native audit must inspect OnDestroy body/aliases, callable structure, Harmony ordering and wrapper reentry; then human validation must cover rendered Cancel/recovery UI, Save/Continue/rematch and real4c lifecycle. No game, install, real-save mutation, canonical sync, push or release occurred. Independent full4b review is next;4c has not been implemented. Source/build slot is released on report delivery.
`47d00e52db6f14fd137950c30310abbebbc7fd1f` completes the approved cancellation implementation/tests/final interface and manifest on top of the two partial checkpoint commits. Final staged diff check passed and the working tree was clean after this implementation commit. Full review remains BASE2bf85d2430ab66ae52b48b47e4e37661db5f127f through the final report receipt commit.
