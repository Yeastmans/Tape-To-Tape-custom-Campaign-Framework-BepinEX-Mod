# Consolidated final-review repair report

All five findings in final-review.md are addressed in one coordinated source/build wave from 7d8b0f1030c4395412e86879a1321b107a9dff32. Corrected-range independent review remains pending. This report records managed/static/package evidence, not native execution or gameplay acceptance. The exact source commit is the gitCommit in the new FINAL-RECEIPT.json, generated after this report is committed.

Evidence root: C:/Users/kiran/Desktop/tape to tape mods/artifacts/followup-four-features-20261020/final-review-fixes. Original final-integration receipts and payloads are preserved.

## Findings and regressions

- **I1:** Shared Core PlayNowPositions defines the existing Studio v1 wire order LW, C, RW, LD, RD. Studio export/update/import, runtime hydration/snapshots/editor/native-save fields and journal validation consume that contract. Native bench presentation deliberately remains C, LW, RW, LD, RD. The new StudioBoundaryChecks uses actual ExportTranslation.BuildTeam and Write, then source-linked runtime hydration, absolute editor lines and native bench projection; it rearranges and saves through the real journal, reloads the native/extension pair, imports through GameCustomContent and updates through actual ExportTranslation.WriteUpdate. Distinct Center/Right Wing identities and both stamina settings cross the production boundary. Native objects are managed doubles; the producer payload is not separately fabricated. The previously unpublished incompatible v1 runtime-candidate interpretation is rejected on Studio import/update with instructions to re-export from authored data; no ambiguous positional migration is invented.
- **I2:** The selected checkpoint now carries HasMarker independently of its nullable string. Only a positively associated selected model/run/squad with an actually absent marker key can migrate. Exactly one hydrated native goalie and matching late earned-state ownership are required; the identity-checked R23 omitted-appearance supplement is restored before baseline capture. The baseline disables reserve, has no reserve occupant, signed contract, progress, visit or attempt inferred from current authoring. It is committed by the existing checkpoint protocol. The genuine marker-free R23 fixture continues, checkpoints, explicitly recruits Active with reserve disabled, and continues again. Present null/wrong-type/invalid markers, missing/corrupt/unaccepted companion, mismatched run and failed selected association remain unavailable with native Active retained. Existing rollback, receipt and appearance-precedence coverage remains green.
- **M1:** Both runtime hydration and native model-load handling share codec rejection reporting. Malformed/unsupported extensions log the team ID, codec reason, fallback and re-export action. Absent extensions remain quiet. Revision digest deduplication survives the cache-completion reset so a single relevant model load is not reported twice; a changed invalid revision can report again. Actual runtime warning-sink assertions cover both error kinds and this lifecycle boundary.
- **M2:** Mode changes reset the nested picker presentation without emitting an explicit image authoring change. Actual controls cover image to Inherit/None in both kit tabs and after save/reopen. This test also reproduced a second correctness defect: generic Team settings controls registered duplicate readers for jersey source/metadata and overrode the dedicated picker values during save. Those fields are now reserved for their dedicated editor. The controller explicitly included that repair in M2 scope. Captions, saved mode/source and cleared embedded metadata now agree.
- **M3:** Crest previews pass the selected kit's full embedded metadata and inherited team-identity metadata through the same validated snapshot precedence as export. The cache includes embedded content. Actual rendered-image tests show the retained embedded pixels after source removal/replacement, and refresh when embedded pixels change under the same source; identity and the opposite kit remain independent. Adjacent default-new-team coverage persists a valid PNG plus ID/hash/mode/source for both kits and verifies ApplyTeamLook preserves all fields without copying players' personal appearance.

## RED and GREEN

Use C:/Program Files/dotnet/dotnet.exe. Ordinary focused commands are `dotnet run --project tests/<Project> -c Release`; the actual control route is `dotnet run --project tests/CampaignStudioChecks -c Release -- --logo-picker <evidence-output-directory>`.

| Regression | RED receipt | Observed failure | GREEN receipt |
|---|---|---|---|
| I1 actual export boundary | red-i1.log | Studio export fails runtime two-line hydration | PlayNowPersistenceChecks.log |
| I1 old candidate compatibility | red-old-v1.log | incompatible v1 import/update accepted silently | PlayNowUpdateChecks.log |
| I2 genuine legacy Continue | red-i2.log | owned legacy baseline unavailable | GoalieRosterRuntimeChecks.log |
| M1 runtime diagnostics | red-m1.log | malformed/unsupported diagnostics absent | PlayNowRosterRuntimeChecks.log |
| M1 load lifecycle | red-m1-load-boundary.log | same rejection logged twice across cache completion | PlayNowRosterRuntimeChecks.log |
| M2 actual caption/reopen | red-m2.log | explicit image caption persists after mode change | LogoPickerChecks.log |
| M3 retained preview/cache | red-m3.log | embedded pixels under unchanged source do not refresh | LogoPickerChecks.log |

The first boundary fixture attempt lacked a required face catalog entry; that fixture error was corrected before the behavioral RED capture. Canonical position changes also required updating old fixture expectations. Intermediate attempts are retained as diagnostic history; only the final logs listed below are claimed green. The additional M2 generic-reader defect was observed in the actual save/reopen assertion during this progression (saved expected empty source became `none`); it was fixed with the dedicated-field reservation, not a weaker assertion.

| Final focused check | Passed cases/assertions |
|---|---:|
| GoalieRosterRuntimeChecks | 404 |
| GoalieRosterMenuChecks | 130 |
| GoalieRosterChecks | 92 |
| PlayNowRosterChecks | 33 |
| PlayNowRosterRuntimeChecks | 24 |
| PlayNowPersistenceChecks | 29 |
| PlayNowUpdateChecks | 56 |
| AppearanceTranslationChecks | 125 |
| ExportTeamAppearanceChecks | 31 |
| JerseyLogoChecks | 76 |
| TeamLookFillChecks | 40 |
| CampaignStudioChecks --logo-picker | 47 |
| **Total** | **1087** |

The last M1 lifecycle repair reran the affected runtime and persistence checks. Unchanged full suites were not repeated. Historical TwoLineContractChecks, PlayerContractChecks, activation and raw-provider results remain prior evidence only; they are not included in 1087. Previously reported optional map-test failures are not called passed.

## Exact candidate and audits

Output: build/FourFeatureCandidate-FinalReview/runtime and build/FourFeatureCandidate-FinalReview/studio-publish (self-contained win-x64). Runtime SHA256: F646E8665FAEEFAE0775DCFAC7C9B53A24141BD25CA3EAC8CA6ECB8B4379DB1C. Studio bundles that exact DLL and runtime hash. Both outputs are new; earlier candidates remain intact.

Build commands:

```
dotnet build src/CustomCampaignFramework/CustomCampaignFramework.csproj -c Release -p:InteropLive=<fresh-interop> -p:InteropDir=<fresh-interop> -o build/FourFeatureCandidate-FinalReview/runtime --no-restore
dotnet publish src/CampaignCreator/CampaignCreator.csproj -c Release -r win-x64 --self-contained true -p:StudioRuntimeBinary=<new-runtime-dll> -p:StudioRuntimeHash=<new-runtime-hash-file> -o build/FourFeatureCandidate-FinalReview/studio-publish
dotnet run --project tests/GameArtPackageChecks -c Release -- build/FourFeatureCandidate-FinalReview/studio-publish build/FourFeatureCandidate-FinalReview/runtime/CustomCampaignFramework.dll
```

Both interop properties point to C:/Users/kiran/Desktop/tape to tape mods/artifacts/game-update-26085704-20261020/interop-generation/interop. Runtime build and Studio publish pass; actual GameArtPackageChecks passes. Runtime build retains one existing Plugin.cs CS0168 warning. Warning hygiene is outside this fix wave.

Fresh compiled/source/manual/raw/ABI evidence is under compatibility-audit in the new evidence root: 2103 callable references, 335 attributed target references, 532 source references (463 distinct), 80 manual targets, 21 exact raw bodies, 618 combined references and 42 aggregate-bearing ABI targets. No unresolved binding/signature/alias issues or unsafe actual hooks were found. The extracted compiled compatibility profile equals the source profile. Both providers and all 223 wrapper files are hash-verified, as are the live native binary/metadata inputs.

No native callable definitions, native target/signature contracts or paired compatibility profiles changed. The compiled reference count grew through additional calls to existing definitions. The original native body/profile evidence is reused explicitly, hash-bound in native-evidence-reuse.json and the new receipt. Baseline 0D16BE041548C631DD4B32BC2C9339604D30BBBED2B89225FDE596323C70F060 retains all 1448 protected methods including the unchanged prior 299 records, 137 comparison sites and 109 squad queries. The fresh raw audit rechecks current native bytes; no old body capture is presented as a new native execution. No hardcoded old verify_final.py was run. The new finalizer derives hashes/counts/paths and records source/payload manifests; controller verify_reviewed_candidate.py independently checks the receipt without rewriting it.

## Boundaries and pending decisions

All native/human obligations retain the controller dispositions in canonical FINAL-REVIEW-ADJUDICATION.md. Item13 (later native-edited goalie mask/head retained on Studio re-export) still awaits the human scope answer; dependent parity is unchanged. Item14 is covered by the existing restart-required campaign-selection UI; no live switching was added. Strong ownership registries remain process-lifetime retained because no safe audited cleanup event exists; this is a disclosed local-candidate limitation, not a bounded-memory claim. Legacy outer close/discard wiring, native async/Harmony ordering and rollback, ES3 graph/cache execution, rendering/provider lifetime, launch/completion chronology, controller UI and contract gameplay remain exact-candidate acceptance work. Existing canceled-before-permit and committed-origin policies remain intact.

No game was launched, no install or real-save write occurred, and no canonical product source, Git mirror, R23 release, original final-integration evidence, push, publication, promotion or cleanup was performed. Candidate guide/handoff identify the corrected candidate and pending review while retaining earlier history. Controller owns review, promotion and any subsequently authorized native validation.
