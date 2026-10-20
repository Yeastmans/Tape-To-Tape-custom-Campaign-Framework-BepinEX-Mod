# Task 3 implementation report — jersey runtime candidate

Status: **DONE_WITH_CONCERNS** for the assigned source/test/build unit. Native installation, trampoline execution, final compatibility-profile generation, and visible gameplay remain unperformed. The current embedded compatibility baseline has no JerseyLogo raw contracts, so this candidate's new hooks remain disabled until final integration supplies the exact reviewed set. No gate was bypassed.

Base: `a0d842cefad5c4c6746f18bee042d68d0992b7a2`. Implementation commit: `7163d3a2df3ccce4a3f14e046e93108df213ce9d` (`Implement guarded per-kit jersey logo rendering`). The separate foundation report remains unchanged.

## Implemented

- `JerseyLogoRuntime.cs`: frozen Team/Player/Own documents, exact pointer-bound players, independent home/away resolution, frozen embedded payload validation, campaign-path plus SHA256 image cache, valid transparent Texture2D for explicit none, mixed compatible/premade kits, conservative missing/corrupt/unbound pass-through, bounded unique-id clone inheritance, snapshot/restore of both kits, context retirement and texture tracking.
- `JerseyLogoRequests.cs`: source-linked request policy. Every new renderer request advances ownership, including native/pass-through. Nested owner/world requests share the same generation and retain stale ownership instead of regaining it. Scoped saved requests reject late writes after native B, track exact specialized runner publication/completion, retire before callbacks can recycle runners, restore nested scopes, and distinguish fault/cancellation from audited synchronous forward success. Dictionary policy operations synchronize independently of original native execution.
- `JerseyLogoNativeHooks.cs`: raw pointer callbacks for 4 persistent UniTask MoveNext bodies, their 12 specialized runner helpers, 4 synchronous handoff MoveNext bodies, and the explicitly audited internal failure observer. Raw state memory is read only at entry; no state wrapper, boxing, reference-field write, or post-MoveNext state read occurs. SetStateMachine alone reads its proven runner-reference publication slot after original. Real player/owner/composite/renderer/runner wrappers are retained.
- Harmony boundaries bind fresh helper composites, apply textures after setup/material work, guard final world/UI SetLogo calls, and scope verified Forward/Goalie InitializeLogo calls. UI clear/identity calls without an owned scope remain native and supersede pending authored writes. The goalie owner's inline late write is identified by its saved owner scope.
- Existing campaign/export/head/decal bindings retain source documents and both metadata payloads, preserve per-kit premade decisions without clearing the compatible other kit, and copy/restore runtime metadata. The old player-aware shared-logo prefix yields when the new exact tracking is enabled. Team identity sprites are untouched.
- Native compatibility gets an explicit raw-contract model and baseline/selected-profile coverage equality. The runtime remains a single DLL via Core source links.

## Native target contract and integration handoff

The single source of raw targets is `src/CustomCampaignFramework/Assets/jersey-logo-targets.json`, embedded as `JerseyLogoTargets.json`. Startup consumes this exact document; final audit must consume the same document. **21 actual selected raw adapter contracts**, currently uninstalled:

| Family | MoveNext | SetStateMachine | SetResult | SetException |
|---|---|---|---|---|
| Forward d155 | 0x406F000 | 0x2FF8850 | 0x2FF9020 | 0x2FF90A0 |
| Goalie d91 | 0x3C1E3C0 | 0x3001D70 | 0x3002500 | 0x3002580 |
| World d20 | 0x40E0900 | 0x3075340 | 0x3075B10 | 0x3075B90 |
| UI d24 | 0x40E1F40 | 0x3076AC0 | 0x3077290 | 0x3077310 |

Synchronous handoffs: Bench d16 `0x376EB50`, penalty d39 `0x40872E0`, knockout d115 `0x3BBD1C0`, SkaterAndSkin d3 `0x44401A0`.

Internal failure observer: `0x9D38F0`, exact PE size `0xAC`, **zero metadata aliases**, identity `internal-native:26085704:0x9D38F0`, observed two-pointer ABI `(builder_fields_pointer, exception_object_pointer)`. This is not public builder SetException at `0x5F4B8C0`, has no invented MethodInfo, and is not metadata-resolved. It only marks the innermost exact saved builder-address match before forwarding native once. Only Forward d155 uses the separately proven fresh runner-null, normal-return, publication-observed/no-publication, no-fault inference for synchronous refresh. No such inference was generalized to other families or AsyncVoid.

Nine new exact managed patch boundaries:

- SkinHelper.GetSkinForForward(SkeletonData, ForwardData, bool, bool, bool, bool)
- SkinHelper.GetSkinForGoalie(SkeletonData, GoaltenderData, bool)
- T2TSkeletonExtensions.SetLogo(SkeletonRenderer, Texture2D, bool)
- T2TSkeletonExtensions.SetLogo(SkeletonGraphic, Texture, bool)
- T2TSkeletonExtensions.SetupSkeletonSlots(Skeleton, CustomizationFlags, bool)
- T2TSkeletonExtensions.SetupSkeletonSlotsMaterials(bool, SkeletonRenderer, Material, CustomizationFlags)
- T2TSkeletonExtensions.OverrideSlotWithCustomizationMaterial(SkeletonGraphic, Skin, TeamColorsData, string, Dictionary<Texture, Material>)
- Forward.InitializeLogo(TeamData)
- Goaltender.InitializeLogo(TeamData)

The existing GetSkins and GetSkinsForGoalie patches now capture exact list/player/home associations. Existing player-aware ExportAppearanceRuntime patches remain installed but yield logo selection when the new system is enabled. These changes must appear in the final managed-hook coverage review too.

Startup requires Windows x64, NativeCompatibility validation, the exact pinned GameAssembly and metadata identity, the complete approved raw set, exact loaded MainScriptsAssembly/spine-csharp/Interop.Runtime/HarmonySupport hashes, and full memory body plus prologue equality for every raw target before installing any callback. Partial creation rolls back created detours and this feature's Harmony patches. If removal fails, the disabled callback/trampoline delegates remain rooted and the receipt reports the remaining actual targets rather than claiming rollback succeeded.

At an actual future startup, the runtime writes `jersey-logo-installed-targets.json` beside its DLL with status/reason, the **actual installed raw contracts**, actual feature-owned managed patch methods, and `nativeExecutionValidated=false`. No runtime startup occurred in this task, so no actual installed-success receipt exists and none is claimed. `artifacts/four-features/jersey-native-contract-audit.json` is explicitly an offline audit with `installed=[]`; all 21 full bodies/prologues match the pinned input and the baseline remains unintegrated.

Integration must regenerate the paired baseline/profile with explicit `RawHooks`, retain internal-native identity semantics, include all new managed targets and native dependencies in the full audit, and verify the relocated provider trampolines before any installed-runtime claim. Existing profile comparison-site validation remains intact. Do not copy the 20-target research candidate list over the 21-target implementation manifest.

## TDD evidence and focused validation

All commands ran in `C:/Users/kiran/Desktop/tape to tape mods/artifacts/release-r23-26085704-20261020/publish-checkout` using `C:/Program Files/dotnet/dotnet.exe`.

Initial command:

```powershell
& 'C:/Program Files/dotnet/dotnet.exe' run --project tests/JerseyLogoRuntimeChecks -c Release
```

RED: initial source links reported missing JerseyLogoRuntime.cs/JerseyLogoRequests.cs (expected absent implementation). After creating an intentionally inert interface scaffold, the meaningful red run reported **2 passed; 8 failed**: ForwardHomeAwayTexturesDiffer, GoalieHomeAwayTexturesDiffer, ExplicitNoneUsesBlankTexture, CampaignSameNameIsolation, SnapshotRestoresBothKits, OwnPlayerUnaffectedByTeamChange, MixedCompatiblePremadeKits, ForeignAndAmbiguousCloneRejected. Native-pass-through and malformed-payload negative checks already passed under the inert scaffold. Snapshot/own tests were strengthened to reject vacuous null equality before that recorded red run.

GREEN: resolver implementation initially passed 10/10. Expanded final production-source-linked tests pass **29/29**, no warnings in final output. These execute the actual resolver, snapshot/clone/cache code, request generation/lifecycle/RunSlice/Publish/Finish policy, and exact compatibility-set validation with native/Unity doubles. They simulate suspend/resume and reentrancy; they do **not** execute native callbacks or IL2CPP tasks.

A later test-first self-review regression `CaptureLoggerFailureStillCallsOriginal` produced **27 passed; 1 failed** before protecting diagnostic reporting. After the fix it passed; subsequent strengthened embedded/per-kit checks yielded the final 29/29. This verifies that a logging exception cannot prevent the native-original delegate in the source-linked slice policy.

Other commands/results:

```powershell
& 'C:/Program Files/dotnet/dotnet.exe' run --project tests/AppearanceTranslationChecks -c Release
# PASS 125 appearance translation and compatibility checks
& 'C:/Program Files/dotnet/dotnet.exe' run --project tests/GoalieEquipmentChecks -c Release
# 9 PASS cases; exit 0
& 'C:/Program Files/dotnet/dotnet.exe' run --project tests/JerseyLogoChecks -c Release
# PASS 76 jersey logo assertions
& 'C:/Program Files/dotnet/dotnet.exe' run --project tests/CampaignLogoResolutionChecks -c Release
# Passed 34 campaign logo checks (22 are existing read-only live-logo path checks)
python artifacts/four-features/jersey-contract-audit.py
# PASS 21/21 exact native bodies/prologues; unique IDs/RVAs; unintegrated baseline; installed=[]
git diff --check
# exit 0, no output
```

Final fresh compile:

```powershell
& 'C:/Program Files/dotnet/dotnet.exe' build src/CustomCampaignFramework/CustomCampaignFramework.csproj -c Release '-p:InteropLive=C:/Users/kiran/Desktop/tape to tape mods/artifacts/game-update-26085704-20261020/interop-generation/interop' '-p:InteropDir=C:/Users/kiran/Desktop/tape to tape mods/artifacts/game-update-26085704-20261020/interop-generation/interop' -o artifacts/four-features/jersey-runtime-candidate --nologo
```

Result: exit 0, **0 errors**, one pre-existing `Plugin.cs:8430 CS0168` unused `ex` warning. Candidate DLL SHA256: `CF14FD515CC92DDC55496E8D030594C0158CDED90ED6E8CD37A73187FAC518CA`. This candidate was never copied over a shipped DLL or receipt.

## Self-review findings addressed

- Required assertions reject null/null or home/nonexistent-away false positives; embedded kits are independently checked.
- The actual current Spine API has separate SkeletonAnimation and SkeletonRenderer components. The adapter now follows the native `animation.Renderer` cast (40e0900.asm at 0x40E0BF8), rather than assuming inheritance or using the animation pointer as renderer identity.
- Setup application matches the exact target Skeleton pointer and refuses an absent composite. The focused harness and fresh compile were repeated after this final source tightening.
- New native B and stale nested A cannot regain ownership; native skin mismatch stays native rather than painting B's texture onto A's skin.
- Runner terminal mappings retire before original completion/reentry. Runner references use rooted genuine object wrappers; no raw state is rooted.
- Synchronous success is not inferred from finally/no-runner alone. Fault observer is exact-address scoped, publication read errors/zero pointers are indeterminate, and logging failures preserve exactly one original invocation.
- Snapshot Team/Player/Own and both per-kit payloads remain retained; cache keys include resolved path plus bytes rather than display name.
- Source/test allowlists are precise. No bin/obj, generated fixture, installed payload or shipped receipt is staged.

## Limits and conservative paths

- No detour creation, trampoline relocation/unwind, runtime field access, Unity image decode, or rendered image has been executed. Source-linked tests plus compile do not establish these. The final audit and human review remain required.
- Native SetSkin ordering is not repaired or claimed latest-wins. Unexpected current-composite mismatches retain native texture behavior. Old request SetLogo writes are suppressed only with saved scoped provenance.
- Unbound native players, head-only/referee content, missing/corrupt artwork, unknown resumed operations, absent/ambiguous clone provenance, and UI DTO paths without a verified player/kit binding do not receive authored overrides. UI clear/identity calls remain native and advance native ownership. Same-name/id global donor fallback is absent.
- AsyncVoid wrappers themselves are not observed for failure/success; the tracked inner UniTask families provide the binding. Only Forward's audited no-runner completion gets the texture-only inference.
- Helper metadata is retained within the campaign context; displayed mod textures are conservatively retained until the renderer is observed destroyed, because a SetLogo call alone does not prove every old native material released its texture. In-flight textures survive context changes. Collection is lazy at subsequent binding/render activity; no unsafe timeout destroys resources.
- Actual on-ice forward/goalie home/away, bench, penalty, knockout and menu images remain human gameplay checks. No game launch, install, real-save mutation, publication, push, source sync, original mirror edit, or policy-blocked cleanup was performed.

## Changed files

New: JerseyLogoRuntime.cs, JerseyLogoRequests.cs, JerseyLogoNativeHooks.cs, Assets/jersey-logo-targets.json; source-linked runtime test project/Program/NativeDoubles; this report. Existing: CampaignAppearanceRuntime.cs, ExportAppearanceRuntime.cs, AppearanceDecalRuntime.cs, HeadAppearanceRuntime.cs, runtime csproj, NativeCompatibility.cs, shared NativeCompatibilityProfile.cs, .gitignore, existing CampaignLogoResolutionChecks Program/project now precisely allowlisted. Foundation/evidence files were read, not rewritten.
