# Task 4a implementer report

Status: implemented and self-reviewed; ready for independent review. Base `7e6f42fdc9d1b45684bde89105dbe1a6361f92e4`. This completes only approved unit4a. Mandatory4b durable launch and4c actual-participation/post-boss completion integration are still pending. No native gameplay claims.

## Implementation

- New Core GoalieContractPolicy supplies validated positive-duration signing, exact armed-attempt authority, run-level processed-token deduplication (including delayed older callbacks after later completion and checkpoint reload), actual-participant debit and safe expiry reconciliation. All accepted states retain an Active. Policy preserves full goalie payload/appearance, signed duration, bench progress and the separate RecruitmentVisit ledger through detached clones. Protected expired contracts retain progress/occupants; unprotected expired reserve is removed, valid reserve promoted, otherwise active stays awaiting replacement.
- Existing Studio contract editor now appears in goalie Details & attributes, with goalie-specific off/help text. Existing skater controls and PlayerContractOptions parsing are unchanged. Goalie grid is retained inside the details ScrollViewer so the controls are actually visible. Guide and PLAYER-CONTRACTS describe signed duration, permanence, frozen offers and deferred replacement.
- Existing manually gated fresh-run callback freezes selected squad goalie fields only once for an already-owned native run. Hydration/Continue still reads only saved state. Existing confirmed TryRecruit receives a detached signed contract. Actual offer generation freezes configured duration before confirmation; the card and confirmation use that same snapshot even after authoring changes. Contract getter/setter clones prevent exposed snapshots from mutating the offer.
- Deferred reserve recruitment evaluates exact current native typed Handcuffs/custom retention protection. The confirmation explains both final slots. Known unprotected recruits become active, clearing reserve and departing the expired starter; protected signings retain active and fill/replace reserve. Unknown protection does not sign; a changed decision requires confirmation of the new result. Actual TryRecruit revalidates the expected decision before staging. Existing SaveChange/Mutation/scoped receipt logic remains the sole save transaction coordinator and saved recruitment consumption stays in the same checkpoint.
- Shared CustomRewardContractRetention.Prevents extracts precisely the existing custom KeepFreeAgents decision; existing prefix preserves default-mode short-circuit and otherwise negates that same result. GoalieContractRuntime.TryDepartureProtection returns unknown on scope or native/custom errors. No goalie registration in native forward-only freeAgents, new native detour, or match-accounting callback was added.

Interfaces and Ruling/cost details are in `task-4a-interfaces.md`; exact new native callable and modified existing gated/attribute hook bodies are in `task-4a-native-manifest.md`. Final profile is unchanged, so pending checkpoint/menu compatibility gates remain closed. All units must integrate before enabling complete contracts.

## RED/GREEN evidence

All logs below are in checkout `artifacts/four-features/goalie-task4a/`.

| Command suffix after explicit dotnet executable | RED evidence | GREEN evidence |
| --- | --- | --- |
| `run --project tests/GoalieRosterChecks -c Release` | `policy-red.log`: `SignedGoalieContractPolicyExists` failed on actual missing production API after48 existing store assertions. Reflection avoided a compile-only failure; final tests call the actual API directly. | `store-final.log`:76 checks, including full store/ledger roundtrip and policy cases. |
| `run --project tests/PlayerContractEditorChecks/Checks.csproj -c Release` | `editor-red.log`: `Goalies expose custom contract control` failed. An initial implementation still failed because the existing goalie grid bypassed the details container; corrected actual visual tree rather than weakening the test. | `editor-final.log`:24 actual Avalonia editor/cleanup/sharing checks, including enable/save default4, explicit9, frozen signed9 after author edit2, off behavior and unchanged skater assertions. |
| `run --project tests/GoalieRosterRuntimeChecks -c Release` | `signing-red.log`: `ActualFreshBoundarySignsAuthoredStarter` failed after132 prior checks. | `runtime-final.log`:153 checks, actual fresh hook, same-run repeat guard, Continue under changed authoring, confirmed recruit, malformed warnings and shared native/custom decision. |
| `run --project tests/GoalieRosterMenuChecks -c Release` | `offers-red.log`: `GeneratedOfferDisplaysActualFrozenSignedDuration` failed. `departure-red.log`: `DeferredReplacementResultShownBeforeConfirmation_none` failed. | `menu-final.log`:122 checks; actual generated6 offer survives author edit99 and exposed snapshot mutation; native/custom prevention, unknown rejection and changed-decision reconfirmation. |

Self-review regressions had their own RED/GREEN: `default-prefix-red.log` captured the refactor reading a null native manager in default mode; restoring the original short-circuit makes `DefaultModeRetentionPrefixDoesNotReadNativeManager` pass. `missing-scope-red.log` captured path ownership throwing outside the adapter's try block; moving the whole scope validation into the guarded decision makes `UnavailableCampaignFolderCannotBecomeUnprotectedDecision` pass. Both are included in the153 final runtime assertions.

Harness-only first compile failure for the extracted native prefix (missing HarmonyPrefix attribute double) was fixed before collecting meaningful departure RED. It is not native proof. Native storage/tasks/UI and custom ownership/native typed predicate are explicit doubles; production policy, bridge, roster, menu, signing and extracted actual custom prefix/offer lifecycle bodies execute in those tests. Candidate creation inside menu extraction remains a native boundary stub, as inTask3. No rendered in-game/controller evidence or native cache execution is claimed.

## Final scoped verification

Working directory for every command: `C:/Users/kiran/Desktop/tape to tape mods/artifacts/release-r23-26085704-20261020/publish-checkout`. Executable for every row: `C:/Program Files/dotnet/dotnet.exe`.

| Exact arguments | Result | Log |
| --- | --- | --- |
| `run --project tests/GoalieRosterChecks -c Release` |76 pass | store-final.log |
| `run --project tests/GoalieRosterRuntimeChecks -c Release` |153 pass | runtime-final.log |
| `run --project tests/GoalieRosterMenuChecks -c Release` |122 pass | menu-final.log |
| `run --project tests/PlayerContractEditorChecks/Checks.csproj -c Release` |24 pass | editor-final.log |
| `run --project tests/PlayerContractRuntimeChecks/Checks.csproj -c Release` |34 unchanged skater checks pass | skater-contract.log |
| `run --project tests/TwoLineContractChecks -c Release` |1063 unchanged roster/recruitment checks pass | twoline.log |
| `run --project tests/GoalieFreeAgentChecks -c Release` |40 goalie-pool/data checks pass | goalie-pool.log |

Fresh pinned interop build:

```powershell
& 'C:/Program Files/dotnet/dotnet.exe' build src/CustomCampaignFramework/CustomCampaignFramework.csproj -c Release '-p:InteropLive=C:/Users/kiran/Desktop/tape to tape mods/artifacts/game-update-26085704-20261020/interop-generation/interop' '-p:InteropDir=C:/Users/kiran/Desktop/tape to tape mods/artifacts/game-update-26085704-20261020/interop-generation/interop' '-p:OutputPath=../../artifacts/four-features/goalie-task4a-native-20261020/'
```

`wrapper-build.log`:0 errors; one existing `Plugin.cs(8431,34)` CS0168 warning. Final candidate SHA256 `4DAD13213EF37A548E28A7252DCB4066494D7A7A0103EA28B3A69C46A9169369`. Source-linked fixtures retain disclosed CS0649 warnings for production-extracted Away/Export and shared harness switches unused by a given subset; no broad suppression. Outputs are candidate-only and do not replace publicR23 runtime/receipts.

## Scope, self-review and limits

Self-review checked full production diff, accepted transition invariants, no forward collection mutation, exact default-mode preservation, original asynchronous save coordinator reuse, fresh-vs-Continue separation, cloning and saved recruitment ledger preservation. No unresolved functional issue found within4a. Direct generic call compilation and policy fixtures do not prove native Handcuffs execution, rendered menu behavior, native callbacks, Save/Continue gameplay or human acceptance.4b/4c and final native audit remain required.

Intended production changes: Core policy, goalie signing/protection adapter, existing Studio editor/view/guide, runtime csproj Core link, existing custom retention prefix extraction, fresh hook, offer generation, slot UI and roster signing integration; PLAYER-CONTRACTS documentation. Tests extend existing store/runtime/menu checks and their narrowly scoped native doubles/extraction. Existing PlayerContractEditorChecks source/project is now precisely allowlisted along with only ContractChecks.cs and ContractSigningChecks.cs. No bin/obj/generated fixture files are staged. Existing strong-selection lifetime and deferred CS0649/CS0168 cleanup remain untouched.

No canonical sync, native fixture, game launch, real save, install, push or publication occurred. All generated fixtures are within checkout artifacts/four-features or OS temp. EOF-only whitespace cleanup followed final passing builds/tests; no semantic code changes afterward. `git diff --check` passed. Source/build slot is released on final report delivery.

Implementation commit: `6f686a60020b9403d3d526a0ec289cffb3906c85` — Add signed goalie contract policy and protected replacement signing. Total final focused assertions:1512 (76+153+122+24+34+1063+40). Final `git diff --cached --check` passed before that commit. This report, interface handoff and native manifest are committed separately without additional source changes.
