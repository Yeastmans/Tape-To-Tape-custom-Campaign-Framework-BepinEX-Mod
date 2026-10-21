# Task 4c implementation report

Status: ready for independent scoped review. Base `73be84e6b3c1ba99f7e4b576701af6575695d97d`, branch `feature/four-approved-features-20261020`. Implementation remains gated. This report does not claim final Task 4 integration or native gameplay acceptance.

## Delivered behavior

`GoalieMatchCompletion` provides resolve-only launch, clone, proven-play, restart/overtime, departure and enclosing-update hooks. An origin must first be durably armed and committed by the reviewed 4b permission registry. Native node launch freezes its Blueprint identity. Successful native clone observations bind canonical home goalie through both clone stages to the live receiver/data; initialization alone never marks participation. Successful normal faceoff/puck-drop or shootout InRound entry freezes the originating participant. Bench presence never charges a reserve.

End-of-game contexts are bound at successful native flow initialization, before asynchronous execution. Old contexts cannot borrow a newer token; rematch/overtime retain the original session. Canceled previews, recovered Armed state and retired ProcessedAttemptTokens do not establish play or a debit. An obsolete manager callback is ignored; a changed current live identity invalidates exact completion.

The scoped departure observation captures the actual owned custom-retention prefix decision and typed native Handcuffs predicate. Reviewed 4a strict current definition readiness is retained. Missing/foreign skip, predicate failure, changed readiness, duplicate departure or unknown order cannot become false protection. No shared generic detour, forward ParticipationTracker or freeAgents insertion was added.

Only a successful enclosing CampaignUpdateStep.Execute return with Succeeded UniTask status, verified participant and known departure decision can enter completion. Native earned-state copying and boss-specific bonuses finish first. `GoalieRosterRuntime.TryCompleteMatch` then captures the final canonical graph, calls existing pure completion/reconciliation, and uses existing SaveChange/Stage/Mutation plus the exact scoped save receipt to publish the roster and consumed attempt in one paired checkpoint. No transaction implementation or ledger was duplicated. Platform false/fault/cancel versus local commit remains visible; failed/no-write/uncertain transitions do not fabricate durable success. Existing CanEdit stays closed in matches.

Player contract and Studio guidance now explain played completion, canceled previews, rematch/overtime, post-boss bonus ownership, recovery and pending human acceptance.

## Native evidence and integration ownership

Canonical `artifacts/followup-four-features-20261020/goalie-contract-native-research.md` and `goalie-match-start-proof.md` plus their referenced bodies were reused. No solved native research was repeated. Pinned GameAssembly SHA256 `92E73E6D0BE549DD8C695302B7AAE6A77CBBF4DE27F27D26ED01F4B3DABC5B8D`; metadata SHA256 `1009F63178E260153CBBF224338BDC9124F9468D0F462F7A58327F638188914A`.

Exact 14 targets, their RVAs/signatures, body/field/callable dependencies and ordering are in `task-4c-native-manifest.md`. Startup APIs, transaction/rollback ownership and provenance details are in `task-4c-interfaces.md`. Resolve supplies 8 prefixes and 14 finalizers; these are not installed by attributes or a startup caller. Final integration owns atomic activation of all reviewed 4a/4b/4c, persistence and menus, full native/profile audit, Priority.First installation and foreign patch-order checks, and unpatch plus Unsubscribe on partial failure. The new overtime wrapper uses its pinned signature and existing state-machine proof; final audit must cover both, and this unit did not capture a new wrapper body.

## Meaningful RED / GREEN

Logs are retained locally under `artifacts/four-features/goalie-task4c/` (generated artifacts are not source commits).

| RED log | Source-linked failure demonstrated | GREEN correction |
| --- | --- | --- |
| completion-red.log | Existing 285 checks pass, proven played post-boss completion does not debit | Added owned completion boundary and existing paired save transition |
| blueprint-red.log | Native-shaped MatchBlueprint context fails mistaken node-pointer matching | Compare the exact originating Blueprint |
| overtime-red.log | Reinitialized shootout loses originating session | Explicit SwitchToShootout provenance handoff |
| overtime-reuse-red.log | Native retained-live-goalie overtime path cannot complete | Transfer only the explicitly carried exact live binding |
| stale-manager-red.log | Old scene manager callback invalidates current rematch | Ignore obsolete manager callbacks |
| blueprint-identity-red.log | Mutable node Blueprint can rewrite old context identity | Freeze Blueprint pointer at accepted launch |
| missing-scope-red.log | Missing finalizer scope can throw during cleanup instead of preserving native exception | Null-safe scope cleanup and three exception-preservation checks |

All these cases pass in the final source-linked suite. Fixture/build corrections (native `_skaterData`, interface TryCast, reflection argument counts and native-shaped context representation) are not presented as behavioral RED evidence.

## Exact final checks

All commands ran in this checkout using `C:/Program Files/dotnet/dotnet.exe`. Each completed with exit code 0. No unchanged broad suite was rerun.

```powershell
& 'C:/Program Files/dotnet/dotnet.exe' run --project tests/GoalieRosterRuntimeChecks -c Release
& 'C:/Program Files/dotnet/dotnet.exe' run --project tests/GoalieRosterMenuChecks -c Release
& 'C:/Program Files/dotnet/dotnet.exe' run --project tests/PlayerContractRuntimeChecks/Checks.csproj -c Release
& 'C:/Program Files/dotnet/dotnet.exe' run --project tests/TwoLineContractChecks -c Release
```

| Final log | Assertions | Scope |
| --- | ---: | --- |
| runtime-final.log | 352 | Prior 285 plus 67 completion/provenance/retention/recovery assertions |
| menu-final.log | 130 | Directly shared boundary doubles and edit/signing guards |
| skater-final.log | 34 | Affected shared retention and skater policy/lifecycle |
| twoline-final.log | 1063 | Affected existing roster/recruitment dependencies |

Total 1579 passing checks. Existing 4a/4b Core/editor/pool evidence was reused. Runtime/menu fixture warnings are existing unused fake fields/switches, not suppressed. The build below had only existing Plugin.cs:8431 CS0168, zero errors.

```powershell
& 'C:/Program Files/dotnet/dotnet.exe' build src/CustomCampaignFramework/CustomCampaignFramework.csproj -c Release '-p:InteropLive=C:/Users/kiran/Desktop/tape to tape mods/artifacts/game-update-26085704-20261020/interop-generation/interop' '-p:InteropDir=C:/Users/kiran/Desktop/tape to tape mods/artifacts/game-update-26085704-20261020/interop-generation/interop' '-p:OutputPath=../../artifacts/four-features/goalie-task4c-native-20261020/'
```

`wrapper-final.log`: build succeeded, 1 warning, 0 errors. Candidate `artifacts/four-features/goalie-task4c-native-20261020/CustomCampaignFramework.dll`, SHA256 `C3798406BC20550B943DE5ABD3861FB0A18253726EA50AB685853C905D49C2C7`. Explicit InteropLive and InteropDir both point to fresh build-26085704 wrappers. This is a local diagnostic candidate, not an installed or released payload. `git diff --check` passed before commit.

## Limits and remaining acceptance

Checks invoke production source with native-shaped doubles. They do not prove Harmony injection/order, native scene/controller lifetime, native callback timing, IL2CPP cache behavior, actual native graph serialization or human UI/gameplay. Combined installer/profile/installation audit and independent 4c review remain required. Human validation must include normal/shootout/rematch/overtime, canceled-preview replacement and recovered Armed invalid origins, bench-only, protected expiry then removal, post-boss bonus recipient, reserve promotion/deferred empty reserve, exact Save/Continue recovery, and platform/local save distinctions.

No game launch, native gameplay fixture execution, install, real save edits, canonical sync, push, release, schema change, hook activation or current profile alteration occurred. Sole source/build slot is released with this implementation for independent review; final integration owns activation after the complete audit.
