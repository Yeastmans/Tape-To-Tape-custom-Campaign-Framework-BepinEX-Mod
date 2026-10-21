# Task 2b implementation report

Status: native full-graph snapshot/checkpoint integration implemented in the isolated publish-checkout, targeted checks and fresh-wrapper build passed, ready for independent review. Runtime interception remains unavailable until final paired compatibility-profile integration. Native fixture execution and human gameplay are pending; no game was launched and no real saves were touched.

Base: `f7b89b38e0cf67c08403f79bb387e146032e6229` (reviewed Core 2a). Implementation commit: the commit containing this report; controller should record `git rev-parse HEAD` in the task ledger after receipt. No push, install, release, canonical synchronization, cleanup retry, or recruitment/contract transition implementation was performed.

## Result and file ownership

- `GoalieNativeGraph.cs` and `GoalieNativeSnapshot.cs`: native concrete goalie model plus fresh canonical goalie SkaterState using non-generic native ES3 full-graph serialization; explicit versioned profile, complete omitted-field supplements and both kit/logo bindings. Required property presence, identity/type/role/profile and native reconstructed model/earned-state equivalence are checked. Restore constructs both active and reserve detached before publication. No JsonUtility base-graph substitute or earned-state type whitelist. Separate native goalie/run statistics remain native-owned.
- `GoalieNativeAdmission.cs`: freeze exact native run bytes using the actual cache/default-derived File.Save serialization profile; retain original model only as identity/type carrier. Exact runtime-invoke ABI retains the actual native serialization exception before managed Il2CppException loses its pointer. Failed native serialization admission writes nothing and does not invoke the original. It completes its real pending proxy with that actual rooted exception.
- `GoalieOperationQueue.cs` and `GoalieNativeTask.cs`: one native builder/task per accepted request, exact typed bool/model factories, FIFO save/load execution, one original and one completion, real true/false/fault/cancel/null preservation. Reconcile before gate release, release before reentrant publication. No busy rejection, synthetic null/false/fault, finite queue cap or pooled UniTask consumption.
- `GoalieNativeCheckpoint.cs` and `GoalieCheckpointTransaction.cs`: copy exact native ES3Data type/bytes, fingerprint those bytes, final Core prepare before Store, inject/read back the full marker in the same cache, restore exact prior marker cache entry on injection failure. Independent physical-container read reconciles actual marker+fingerprint against the exact prepare even if native returned false/fault/cancel; no deletion of recovery prepares. Prepared-record recovery remains valid when commit-receipt publication failed. Local pair proof is never presented as cloud replication.
- `GoaliePersistenceBridge.cs`: actual save/load owner scopes, immutable Serialize substitution, exact store marker association, selected-byte-array/type/cache/model association, nested-deserialize exclusion, genuine null-load clearing, complete receipt events and diagnostics, external same-container replacement invalidation. Active snapshot admission must match supplied model's run/squad/goalie and full earned-state generation. Deserializer binding uses the actual injected V2 repository's three native repositories and null logo exactly as native construction does, plus actual hydration binding.
- `GoalieRosterRuntime.cs`: fresh-only authoring reserve policy, immutable selection/capture, validated Continue pair publication, preserved forward/relic owner references, detached mutation staging and exact prior-occupant conditional rollback for Task3. Continue preserves the saved capacity policy even if authoring changed. Missing/bad companion keeps native active+state together and reports unavailable editable reserve roster.
- `GoaliePersistenceContracts.cs`, `Plugin.cs`, `MatchRuleRunner.cs`: real wired startup call and persistent Unity tick; complete feature-scoped eight-hook manifest required before interception. Structural checks plus typed preflight replace incidental generated-wrapper whole-file hash pins. Native build/metadata and shipped interop ABI dependencies remain pinned. Partial hook install rollback is present.
- `GoalieRecruitment.cs`: old appearance-only deserializer postfix runs only when the new adapter is disabled, so it cannot overwrite accepted full restored state. Its candidate/recruitment transitions are otherwise unchanged.
- `CampaignSharing.cs`: `_goalie_rosters` excluded from authored campaign ZIPs, retaining local prepared/previous recovery records. Runtime csproj links reviewed Core files. `.gitignore` contains exact test-source allowlists only.
- `GoalieRosterStore.cs` and existing Core tests: narrowly addressed both Task2a followups: selected record must be an ordinary file before read (no symlink/reparse redirection); stronger full state, cross-campaign copied-record ownership, direct clone and mutable-isolation assertions retain all prior tests.

## Native manifest and installation handoff

Exact manifest: `.superpowers/sdd/2026-10-20-goalie-features/task-2b-native-manifest.md`.

It enumerates all eight hooks, exact signatures, runtime-invoke reference ABI/MethodInfo field, typed promise completion APIs including cancellation EDI, injected deserializer constructor, cache/settings/data APIs, existing native state-owner call order, hashes and static evidence. The machine-consumed manifest is `GoaliePersistenceContracts.Required` (feature `goalie-checkpoint`), reused for resolving and installing actual hook methods.

Startup already calls `GoaliePersistenceContracts.Install()` directly after existing `harmony.PatchAll()`. Complete reviewed NativeCompatibility NamedHooks, pinned binary/metadata, shipped Runtime/Harmony support hashes, wrapper structure, actual JSON/UTF8 defaults, exact Serialize pointer and typed factory/completion preflight must all pass before Enabled. Current profile lacks the new feature contracts, so the adapter logs unavailable. Final integration should generate the paired profile normally; there is no unwired manual-install step to discover later. Native smoke/fixture/gameplay validation is still required before presenting behavior as accepted.

## Targeted evidence

Executed from the isolated checkout with `C:/Program Files/dotnet/dotnet.exe`:

| Check | Result |
| --- | --- |
| `run --project tests/GoalieRosterChecks/GoalieRosterChecks.csproj -c Release` | 44 checks passed, including real fixture selected symlink rejection and target preservation, full state/isolation and all prior injected storage boundaries. |
| `run --project tests/GoalieRosterRuntimeChecks/GoalieRosterRuntimeChecks.csproj -c Release` | 88 source-linked checks passed. Actual production adapter/coordinator/bridge sources linked; native APIs, IL2CPP, Harmony, Unity and platform boundaries are doubles. |
| `python artifacts/four-features/check_candidates.py` | 19 actual Candidate/ApplyGoalieConfig checks passed; native Unity/rendering adapters are stand-ins. |
| `python artifacts/four-features/check_runtime.py` | 28 source-linked legacy orchestration checks passed; native adapters/UI not executed. |
| `run --project tests/GoalieSharingChecks/GoalieSharingChecks.csproj -c Release` | 3 checks against actual CampaignSharing.Zip: private records absent from ZIP, authored data retained, local recovery bytes unchanged. |

Total: 182 targeted assertions/checks, with the different evidence levels above. No unchanged unrelated broad suite was rerun. Parent separately triaged old optional map-test failures; no action taken here.

Meaningful observed RED -> GREEN evidence:

1. Full polymorphic earned-state test failed against retained incomplete snapshot scaffold (`PolymorphicEarnedStateFrozenAndRestored`), then passed with native graph model+state capture/restore. Later meaningful cases add version/identity rejection, supplement/kit restoration, detached independent restores and late-native-owner pairing.
2. Coordinator/typed-forwarding new-source API checks initially failed compilation with missing implementation source (CS2001); mutation staging checks likewise failed missing Stage (CS1061). Those are compile REDs, not misrepresented native behavior failures.
3. Selected linked Core record test initially loaded the redirected record; adding `Ordinary(token)` in Load rejects it and preserves the external fixture target.
4. `BridgeNativeNullLoadAdoptsNoStaleSelectedMarker` failed because the old selected roster survived real null result; explicit NoSelectedRun clears the binding and passes.
5. Self-review profile-drift regression initially failed because native true could still authorize a companion after queued profile mutation. Frozen immutable profile comparison and explicit external-drift pass-through invalidation now pass both authorization rejection and later-model native-pass-through assertions.

Additional bridge checks cover synchronous/asynchronous/reentrant overlap, exact task/attempt association, immutable admitted bytes despite live model mutation, same-native/different-generation marker behavior, actual model pointer association rather than semantic equality, nested marker calls, missing/corrupt/invalid reserve preserving exact native active/state, stored policy on Continue, forward/relic ownership, cancellation provenance, and prepare/inject/readback/store/platform/commit faults. Broader bridge assertions were added alongside/following implementation; not every one was individually demonstrated failing before implementation. No native behavior claim comes from the doubles.

The actual sharing fixture initially failed loading the incorrectly guessed assembly short name `CampaignCreator`; corrected to the actual csproj assembly name `T2T Campaign Studio`, then all three actual ZIP checks passed. This was a test harness issue, not a product regression. Early fresh-wrapper build found native Il2Cpp List has no LINQ Select; replaced it with an ordinary foreach, then compiled successfully. No warning was suppressed.

Final build command:

```powershell
& 'C:/Program Files/dotnet/dotnet.exe' build src/CustomCampaignFramework/CustomCampaignFramework.csproj -c Release '-p:InteropLive=C:/Users/kiran/Desktop/tape to tape mods/artifacts/game-update-26085704-20261020/interop-generation/interop' '-p:InteropDir=C:/Users/kiran/Desktop/tape to tape mods/artifacts/game-update-26085704-20261020/interop-generation/interop' '-p:OutputPath=../../artifacts/four-features/goalie-task2b-native-20261020/'
```

Result: 0 errors, only pre-existing Plugin CS0168 (now line8431). Scaffold CS0649 is gone through real deserializer binding. Candidate DLL SHA256 `B9C9AFC281181CFDC56D5476FAB61D32DF89CA0EDF52C7CB46736DF1A111301B`; build output lives in the unique checkout-local artifact folder and is not staged or installed.

## Next-task contracts and limits

- Task3 must Stage full detached proposed records before accepting mutation, retain prior occupants/offer, publish through the handle, associate its exact SaveAdmitted proxy/operation/attempt, and roll back only its owned still-current mutation on failed reconciliation. Ordinary forwarded autosave without companion is explicitly unavailable and cannot authorize mutation. The Stage handle is infrastructure, not recruitment policy/UI.
- Task4 must correlate SaveReceipt OperationId, AttemptToken, Proxy, Original, NativeResult/NativeError, Checkpoint and Reconciled; `Durable` requires true native result, no native error and matching persisted pair. False/fault/cancel may still have written; prepares are retained. Contract transitions/durable launch gating remain Task4, not implemented here.
- Supported immutable-save guarantee requires the reviewed profile. Every native settings field is conservatively frozen (including encoding pointer+CodePage and copied assembly-name values) and checked at exact Serialize. Unexpected external drift logs `profile drift`, allows native original serialization, and invalidates companion/Task3/Task4 authorization. That outside-profile native pass-through can serialize later live model state. No shared default/cache settings are changed.
- Unexpected same-container external CacheFile remains a native synchronous operation. The bridge detects and invalidates its active association/reconciliation instead of blocking or inventing its return. Other container paths remain unaffected. This is not a claim to serialize arbitrary external mod writes.
- True process-wide allocation/GC/handle exhaustion or native/interop corruption is outside recoverable guarantee; finite pools cannot solve unbounded admission. Supported ordinary overlap and storage failure paths retain real promise/result provenance. No intentional busy/fabricated fallback exists.
- **Deferred minor retention concern for independent/final review:** Selections strongly roots successfully loaded models until hydration consumes them. LoadSquadId-only loads that never hydrate can accumulate model/cache snapshots for the process lifetime. No unsafe bounded eviction or unaudited native weak handles were invented; delayed legitimate model association is preserved. Controller explicitly authorized documenting this concern for review.
- Legacy/missing/corrupt companion Continue keeps valid native active and empty/unavailable editable reserve state with diagnostic. It does not infer saved reserve capacity or fresh contracts from current authoring; Task3/4 must not treat it as a new run. Any migration is separate explicit policy.
- Native-supported ES3 serializable graphs are the boundary, not arbitrary object memory/shared reference topology/cycles/mod-defined stripped types. Restore validates reconstructed model and earned state before publication; native runtime fixtures must still demonstrate supported concrete classes and supplements in the real game.

## Self-review and release

Reviewed full new graph/ownership/bridge sources, touched legacy hook behavior, exact target manifest and startup flow; retained Core recovery rules. Fixed stale null selection, native collection compile mismatch, generated-wrapper pin reproducibility risk, fresh deserializer binding and external profile-drift authorization during this unit. Test allowlists expose source/project files only; no bin/obj, real saves, compiled candidate, parent ledger/brief edits or canonical evidence are committed. Source/build slot is released to controller after commit for independent full `f7b89b3..HEAD` review.
