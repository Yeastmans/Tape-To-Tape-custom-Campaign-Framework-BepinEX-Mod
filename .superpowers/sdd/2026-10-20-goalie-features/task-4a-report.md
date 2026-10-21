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

Implementation commit: `6f686a60020b9403d3d526a0ec289cffb3906c85` â€” Add signed goalie contract policy and protected replacement signing. Total final focused assertions:1512 (76+153+122+24+34+1063+40). Final `git diff --cached --check` passed before that commit. This report, interface handoff and native manifest are committed separately without additional source changes.

## Review fix round 1 — Important I1 (2026-10-20)

Fix commit: `9f349a7a45a17a10bc2205ca2449b82e5bf3ce15` (reviewed base `3c5335e`). This section supersedes the earlier claim of no unresolved functional issue: review correctly identified that a swallowed definition-load failure could be mistaken for known unprotected and remove an expired active goalie. The focused fix is complete; independent rereview remains required. M1 warning cleanup stays deferred.

### Change and API handoff

The new strict `CustomRewardContractRetention.TryPrevents` first requires complete definitions for the current ModFolder/ModContentRoot and then rechecks readiness, scope and publication generation after actual Owned enumeration. It returns unknown if loading failed, any source was partial, ownership-time restoration invalidated the registry, or the snapshot was replaced. `GoalieContractRuntime.TryDepartureProtection` requires this known result before using the unchanged typed Handcuffs predicate. Existing legacy `Prevents`, native/skater Prefix and its default-mode short-circuit are unchanged.

EnsureAssets still publishes/logs partial definitions as before for existing callers, while recording availability for the new goalie query. Retained and per-export catches now contribute to source availability. LoadRetained returns bool and invalidates readiness on its existing swallowed catch, including its independently invoked restore path. A same-campaign incomplete snapshot retries only on the strict goalie readiness path; a loaded prior-campaign snapshot remains unknown instead of silently reloading global native assets. The helper checks availability again after Owned without retrying, so a newly loaded generation cannot retroactively certify an earlier evaluation. See the updated interfaces document for exact managed helper signatures.

Ruling/conservative boundary: completeness covers definition data, not native artwork/carrier creation. Failure after successful definition publication does not invalidate the managed classification; a fixture verifies the real outer catch logged the injected artwork failure while the known owned-protection result remains valid. Any retained/export parse failure conservatively blocks goalie departure even if another definition source already contains protection. The cost is that an unrelated malformed retained/export source can block replacement until repaired. A cross-campaign mismatch requires normal runtime rebinding/restart; the fixture Reset simulates a fresh binding and is not a new live reset API. No shared asset reload/lifecycle redesign was added. Optional absent retained/export sources preserve existing absence semantics.

No new native callable, patch, reflection target or manual gate. The previous manifest remains applicable with the appended managed-body delta. Frozen signed contracts, existing save receipts/Mutation/SaveChange, recruiting ledger and all other 4a interfaces are unchanged. Tasks 4b/4c remain pending: live attempt durability, participant provenance and successful native post-boss completion are not implemented or proven here. Compatibility gates remain unchanged/off pending final profile integration.

### Meaningful RED to GREEN

Logs are in checkout `artifacts/four-features/goalie-task4a/`. Tests now extract the actual production EnsureAssets definition-loading body and its nested/outer catch/finally, actual Owned enumeration, and actual LoadRetained. Only surrounding native carrier/artwork construction and native ownership/run services are boundary doubles; malformed on-disk campaign/retained/export documents flow through the real Core parser. The old optimistic Owned double was removed.

- `review-i1-red.log`: runtime failed `SwallowedDefinitionLoadFailureIsUnknownInsteadOfUnprotected` against the pre-fix loader behavior.
- `review-i1-menu-red.log`: menu failed `SwallowedLoaderFailureCannotConsumeSigningOrRemoveExpiredActive`. The GREEN assertion proves no offer consumption, no new save, same native active pointer, empty reserve and unchanged recruitment ledger on repeated clicks. Repair then requires ordinary confirmation and consumes exactly one signing.
- `review-i1-retained-red.log`: `LaterRetainedRestoreFailureInvalidatesEarlierCompleteSnapshot` exposed the independently swallowed retained-restore path; invalidation in its actual catch fixes it.
- `review-i1-during-owned-red.log`: after initial readiness was fixed, `RetainedFailureDuringOwnershipCannotAuthorizeEarlierReadySnapshot` still failed. The post-enumeration scope/readiness/generation check fixes it. Additional cases reject a replacement generation or scope change during Owned and accept a subsequent stable/repaired decision.
- Final runtime checks cover both known false and true, native and custom retention, default short-circuit, partial retained/export snapshots, unchanged legacy partial-registry behavior, stale prior-campaign scopes and recovery, and the artwork/definition distinction. A temporary authored fixture initially used an invalid duration; setting the real schema's supported `Seconds` value corrected that harness input before final verification. That setup failure is not counted as product RED.

### Exact final verification

Working directory: `C:/Users/kiran/Desktop/tape to tape mods/artifacts/release-r23-26085704-20261020/publish-checkout`. Each command uses `& 'C:/Program Files/dotnet/dotnet.exe'`; full output was redirected with `*> artifacts/four-features/goalie-task4a/<log>`.

| Exact command arguments | Result | Log |
| --- | --- | --- |
| `run --project tests/GoalieRosterRuntimeChecks -c Release` | `PASS 176 source-linked goalie runtime and contract signing checks` | review-i1-runtime-green.log |
| `run --project tests/GoalieRosterMenuChecks -c Release` | `PASS 130 source-linked goalie menu checks` | review-i1-menu-green.log |
| `run --project tests/PlayerContractRuntimeChecks/Checks.csproj -c Release` | `PASS: 34 source-linked contract policy/lifecycle checks. Native gameplay acceptance pending.` | review-i1-skater.log |
| `build src/CustomCampaignFramework/CustomCampaignFramework.csproj -c Release '-p:InteropLive=C:/Users/kiran/Desktop/tape to tape mods/artifacts/game-update-26085704-20261020/interop-generation/interop' '-p:InteropDir=C:/Users/kiran/Desktop/tape to tape mods/artifacts/game-update-26085704-20261020/interop-generation/interop' '-p:OutputPath=../../artifacts/four-features/goalie-task4a-native-20261020/'` | Build succeeded; 1 warning, 0 errors | review-i1-wrapper-build.log |

340 focused assertions passed in this fix round (176+130+34). Direct custom-retention legacy/default-prefix regression coverage executes the extracted actual helper within the runtime suite. Unchanged Core/editor/pool/two-line suites were not rerun. Prior pure policy/store76 and editor24 evidence remains the earlier unit's evidence, not fresh fix-round runs. Current candidate DLL SHA256: `177DF7D09D3F96C1213895BD12A2AD51B5DFCFF271D6C57583AD7DC3E56E2A72`. Existing CS0649 fixture warnings remain (runtime4/menu6); fresh production build retains the existing Plugin.cs(8431,34) CS0168 warning. No broad suppression or unrelated cleanup.

Self-review checked the complete fix diff, true/false/unknown paths, nested catches, post-lookup generation/scope revalidation, source-only readiness, exact native prefix preservation and zero-consumption menu recovery. `git diff --cached --check` passed before the fix commit. Only EOF whitespace cleanup followed the final test/build runs. New test file is precisely allowlisted as `tests/GoalieRosterRuntimeChecks/RetentionRegistryDoubles.cs`; no bin/obj/generated fixture/log output is staged. Updated source, extraction, native boundary doubles and focused tests are committed with the managed interface/native-manifest handoff. This report is committed separately.

No native/game execution, real-save access, canonical sync, install, push or release occurred. Builds and fixtures establish managed structural behavior only, not native execution or human gameplay acceptance. Sole source/build slot is released with final delivery; no downstream implementation has begun in this task.
