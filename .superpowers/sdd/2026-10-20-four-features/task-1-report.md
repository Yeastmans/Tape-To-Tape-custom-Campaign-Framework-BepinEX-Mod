# Final integration task report

Status: DONE_WITH_CONCERNS for implementation/static/managed/package scope. Independent integration and whole-branch reviews remain pending. Native/human acceptance is not completed. Base ba8f236ba9d5aded6522ec20c4e8fc98cee24f3a; intended commit is reported in the handoff rather than self-referenced here.

## Candidate and activation

- Runtime: build/FourFeatureCandidate/runtime/CustomCampaignFramework.dll, SHA256 **3E480DC35C81FF4E68640EEEEFBCD0E4D3FAC5DA812FEF4B34924D8A3DB63E77**.
- Self-contained Windows x64 Studio: build/FourFeatureCandidate/studio-publish. Its bundled runtime is byte-identical to that runtime; bundle hash file and native baseline agree.
- Baseline: **0D16BE041548C631DD4B32BC2C9339604D30BBBED2B89225FDE596323C70F060**, game 26085704. GameAssembly and metadata still match the reviewed 92E73E...C5B8D / 1009F6...88914A identities in the exact receipt.
- Evidence root: canonical artifacts/followup-four-features-20261020/final-integration. FINAL-RECEIPT.json and source-inputs.json/payload-files.json identify every final input/payload. Earlier preprofile/studio-build outputs are superseded diagnostics, not final payloads.

Plugin.Load now invokes one GoalieLifecycleActivation owner after PatchAll. Persistence preflight is separate from enablement; roster menus no longer install lazily. The complete 8 checkpoint +7 menu +6 launch +14 completion plan resolves before installation, alongside exact feature profile sets, platform/native/provider checks, graph/typed promise preflight, and the already installed actual custom-retention prefix. The coordinator subscribes both completion dependencies, installs the plan, checks every actual owned patch and observation priority, and only then sets both capability flags. Completion prefixes/finalizers use Priority.First; launch ShowPostfix uses Priority.Last after custom preview display. Partial failure disables, unsubscribes and UnpatchSelf removes even a Patch call that mutated then threw; cleanup errors retain the original failure and remain disabled.

The transaction tests first failed because the implementation source was absent. The actual coordinator's missing-retention regression later failed (it incorrectly enabled without that observer); adding the explicit prerequisite made the final checks pass. Boundary doubles exercise all 35 partial failure positions, missing feature sets, subscription cleanup, flag publication, finalizer placement, repeat startup, foreign provenance patches and missing/misordered retention. Actual Harmony/provider execution remains unexecuted.

A compiled audit also found PlayNowTeamInitialization's Team.Initialize target ambiguous between zero-argument and (TeamData,bool,bool) overloads. The attribute now selects the reviewed three-argument overload. Only the directly affected fake HarmonyPatch constructor was expanded for that signature. The resulting compiled binding audit has zero issues.

## Paired compatibility and native audit

CompatibilityTool generated separate staged baseline/profile files, then verified the staged baseline and required generated profile equality before paired source promotion in this isolated checkout. Prior source assets were preserved under previous-native-*.json; prior release receipts were never overwritten. The new embedded FourFeatureContracts inventory includes raw and named sets that Verify now preserves and validates, instead of silently dropping them from generated profiles. Exact raw callbacks require the pinned full binary/metadata identity and complete body/prologue matches.

Final counts are coverage descriptions, not standalone acceptance:

| Surface | Final result |
|---|---:|
| Protected native methods | 1,448, including all prior protected contracts |
| Comparison sites / squad queries | unchanged 137 / 109 |
| Source named contracts | jersey11 / checkpoint8 / menu7 / launch6 / completion14 =46 |
| Actual manual target registrations | 80 =46 named +34 Play Now (including constructors) |
| Launch / completion patches | launch6 methods7 patches; completion14 targets8 prefixes14 finalizers |
| Raw jersey callbacks | 21 exact IDs/RVAs, full bodies/prologues/aliases verified |
| Attributed compiled target references | 335 across239 attributed classes, zero binding issues |
| Direct source references | 532 references/463 distinct native overloads, zero unresolved/unexpected aliases |
| Combined manual/source inventory | 618 references/520 distinct native overloads;42 aggregate-bearing signatures checked |
| Compiled callable references | 2,062 resolved fresh interop/provider references, zero issues |

manual-hook-audit.json maps exact generated signatures, callback/state injection and selected native overloads. Raw state-machine/specialized runner contracts consume the same embedded manifest as InstallRaw. The unnamed 0x9D38F0 helper keeps its reviewed two-pointer ABI and zero aliases; no fake metadata identity or public SetException substitution was introduced. The known CancellationToken small-wrapper overload remains excluded by all actual appearance installers. Generated wrappers are provenance for the goalie adapter, not a new whole-DLL goalie pin; existing reviewed jersey component gates are retained unchanged.

New native evidence captures OnDestroy and its unregister/dispose chain, the overtime async-void wrapper's exact d52 builder Start and reviewed state machine, initialization/end-context bodies, and JsonUtility wrapper/icall. A direct-call scan identifies EndOfGameFlowController.Initialize in HockeyGameplayDependenciesInstaller.InitializeAll after an interface installer call; it does not prove full virtual/interface chronology or nesting in InitializeGoaltenders. JsonUtility resolves ToJsonInternal_Injected and schema evidence confirms Single fields, but actual emitted Unity float spelling is unexecuted. The approved schema-aware Single comparison and exact ID/integer matching remain unchanged.

## Commands and checks

All .NET commands used C:/Program Files/dotnet/dotnet.exe from the isolated checkout. Commands are logged under the evidence root. No builds overlapped; a Windows exec call that returned while its child retained a log was detected and allowed to finish before further builds.

- CompatibilityTool baseline/verify used game C:/Steam/steamapps/common/Tape to Tape, build26085704, source squad TSV, flow4404340 and exact TitleScreen.<<NewRun>g__StartNewRun|#>d$$MoveNext identity. Staged/verified v2 profiles agree exactly with embedded/source/baseline profile.
- Runtime build: build src/CustomCampaignFramework/CustomCampaignFramework.csproj -c Release -p:InteropLive=<fresh26085704interop> -p:InteropDir=<same> -o build/FourFeatureCandidate/runtime --no-restore. Final0errors, one existing Plugin.cs8431 CS0168 warning.
- Studio build: build src/CampaignCreator/CampaignCreator.csproj -c Release with StudioRuntimeBinary and StudioRuntimeHash set to the exact candidate paths, -o build/FourFeatureCandidate/studio-build. 0warnings/0errors. Final publish repeats the same explicit bundle inputs with -r win-x64 --self-contained true -o build/FourFeatureCandidate/studio-publish; exit0.
- Copied/rebound audit Program.cs and scripts run only in the new evidence root. Compiled module, source/static, manual, raw, provider and aggregate ABI results are bound to the final runtime hash. Original R23 audit outputs remain historical.

| Focused project (run -c Release) | Observed final result |
|---|---:|
| GoalieActivationChecks |19 transaction +82 actual coordinator checks |
| GoalieRosterRuntimeChecks |352 |
| GoalieRosterMenuChecks |130 |
| PlayNowRosterRuntimeChecks |23 |
| PlayNowPersistenceChecks |28 |
| PlayerContractRuntimeChecks/Checks.csproj |34 |
| TwoLineContractChecks |1,063 |
| AppearanceTranslationChecks |125 |
| ExportTeamAppearanceChecks |31 |
| JerseyLogoRuntimeChecks |33 |
| ModCompatibilityChecks -- . <game> |128 |
| GameArtPackageChecks -- <actual studio-publish> <actual runtime DLL> |passed |

Total focused assertions2048, plus actual publish package checks. Runtime/menu fixture warnings are unused boundary-double fields; they are not suppressed. Production runtime's sole existing unused ex warning remains. Package test now locates MapSeams beside the supplied runtime instead of silently inspecting the old bin/Release output. Its source/project are precisely allowlisted along with previously ignored compatibility-check sources. Existing component Core/editor/import/sharing evidence was reused; no unrelated whole suite was rerun. The documented optional map-tab/name assertions remain unrelated historical/stale-contract issues and were not weakened or called passing.

## Limits, lifecycle triage and handoff

No game, IL2CPP fixture, installed Harmony trampoline, native task/graph/cache execution, real save, installation, push, new release, canonical product sync or original dirty Git mirror access occurred. Final review must inspect persistence failure cases, actual hook scope/order and appearance precedence. Controller owns independent review and selective canonical promotion after collision checks.

Play Now modern editor entry ownership/Cancel/No and legacy clone/draft behavior reuse the approved native evidence. Outer serialized legacy close/discard remains unproven, so no invented discard/reset was added. Strong player/editor/raw-model registries were triaged and retained: no verified native destruction/eviction callback justifies clearing revision authority while warm caches remain. Long-session memory retention is a bounded concern, not an established safe cleanup opportunity. Native saved goalie head and reimport Face metadata retention do not establish the separate later Studio exporter roundtrip, which still deliberately empties headSkin.

Human acceptance remains exact-payload Save/Continue/recovery, native serialization spelling and full earned-state graph, real typed promise faults/cancellation, preview cancel/reopen, normal/shootout/rematch/overtime chronology, actual goalie participation/protected expiry/post-boss promotion, native two-line switching/stamina and editor save/cancel, visible world/UI per-kit/personal artwork, raw callback relocation/unwind and runtime partial-install behavior. See docs/FOUR-FEATURE-CANDIDATE.md for concrete manual steps. These limitations are explicit; source-linked tests and compilation are not gameplay proof.

The new candidate guide, PLAYER-CONTRACTS and native-review docs reflect integrated candidate status. Approved spec/umbrella planning-era status was updated without changing requirements. SESSION_HANDOFF imports the newest canonical handoff intact beneath a new candidate summary; its input hash is recorded. Canonical handoff/product promotion remains controller-owned. Policy-blocked temporary copies were not touched, and no speculative cleanup was attempted.
