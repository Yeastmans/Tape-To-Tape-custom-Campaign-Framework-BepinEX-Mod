# Goalie native head/mask Studio roundtrip follow-up

## Approved requirement and scope

The user answered yes to item13: after an in-game goalie mask/head edit and Studio import, a later export must preserve the edited mask/head. This report covers that follow-up only. Source baseline: 7af880d5e2d355015a87c069945e859058554838, feature/four-approved-features-20261020. The implementation commit is recorded in the external FINAL-RECEIPT.json after this report is committed. The earlier four-feature reports, receipts, candidates and release remain preserved.

Evidence root: C:/Users/kiran/Desktop/tape to tape mods/artifacts/followup-four-features-20261020/goalie-head-roundtrip.

## Root cause and native semantics

Existing native evidence is reused, not recaptured: canonical task4b-native-review.md:13 identifies GoaltenderData.headSkin at +0xD0 separately from helmetSkin at +0x108; CustomizeGoalieData.ApplyToSkater 0x4510A90 copies staged +0x58 into headSkin, and GoalieDataModel serializes headSkin at +0x38. The runtime's reviewed HeadConfirmed/Reconcile path already preserves that confirmed identity/clear independently of painted helmet equipment. CustomHeadRuntime continues to register custom goalie mask artwork using helmetSkin and its separate native template.

GameCustomContent already imported headSkin as Face and helmetSkin as Helmet Skin. Two Studio boundaries then lost the head: UpgradeCleanup validated goalie Face through an absent selectable catalog and cleared it; PlayerExport unconditionally emitted an empty goalie headSkin. A second loss affected explicit empty native helmetSkin: it became a blank authored Helmet Skin, which means inherit/default.

The smallest Studio-only repair copies goalie Face exactly into headSkin and preserves goalie Face during import/upgrade cleanup, without skater catalog validation or HeadAppearance.Base normalization. Known imported native empty helmetSkin becomes the existing none selection, which cleanup now retains for goalie Helmet Skin. Authored blank equipment still means inherit/default. Face remains independent identity in either appearance mode; intentional Override Team Settings=no still selects the team's goalie equipment. Native-only loose imports retain their existing no policy. Existing independent Studio sidecars retain their real yes policy through import and export; the regression does not flip it to obtain a pass. Authored mask validation and unrelated skater validation remain enforced.

## RED/GREEN and actual boundary coverage

- red-boundary.log: 29 existing groups passed; new actual roundtrip failed at ContentImport.ReadDocument retaining the goalie head.
- red-export.log: after the import correction, 29 existing groups passed; new roundtrip failed at the serialized standalone headSkin.
- green-boundary.log records an intermediate fixture error: Black_Helmet was not in the shipped selectable skater catalog when copied-team import validation ran. The new fixture now uses the actual shipped Faces/Toronto/Mathieu identity. No production catalog relaxation was made.
- PlayNowPersistenceChecks.log: final 30 groups passed, zero failed. The new group covers seven cases: distinct head/helmet; head clear; helmet clear; both clear; an exact known skater suffix retained opaquely in the native goalie field; a custom mask reference in headSkin; and a validated custom mask project in helmetSkin with its native template retained. Each starts with actual Studio BuildTeam/Write output and a stale original head, confirms a head edit through source-linked runtime save/journal, loads actual GameCustomContent, applies actual ReadDocument/ValidateCopiedFile cleanup, and writes standalone, new-team and existing-team-update payloads. All three emitted JSON boundaries assert independent head/helmet identities or clears. Supplemental Pass Read and the imported independent policy remain intact. Additional controls cover no-Face legacy/default goalies, own-equipment=no team inheritance, raw loose-import policy, unknown authored mask rejection, and skater face cleanup.

Native host objects and JsonUtility are managed doubles in this suite; the production Studio conversion/import/export and source-linked persistence are exercised, not a fabricated parallel converter. These results establish managed/file behavior, not native UI execution or rendering.

| Final affected check | Result |
|---|---|
| PlayNowPersistenceChecks | 30 groups passed, including seven new roundtrip cases |
| PlayNowUpdateChecks | 56 checks passed |
| ExportTeamAppearanceChecks | 31 checks passed |
| CampaignStudioChecks --export-skip | 38 checks passed |
| GameArtPackageChecks on actual new publish + reused runtime | passed |
| Self-contained win-x64 Studio publish | passed, no warnings/errors reported |
| git diff --check | passed |

An additional local CampaignStudioChecks --import-values run is **failed**, excluded from green results: after 12 import assertions it throws MissingMethodException at ImportValueChecks.cs:33-35. Its Activator call supplies 13 arguments ending emptyLabel/emptyHint/customColors nulls; EquipmentStylePicker.cs:53 has 14 constructor parameters including trailing Action<bool>? selectCustomColors. The production constructor file matches baseline7af (Git blob 3d1847447c7d9ae1847918a7b899d1998589447b). The preexisting local ImportValueChecks.cs is ignored/untracked and absent from that commit (current blob b0100fff349f6837cca7d43a002913200a638d0c); it was read/run, not edited here, so no Git baseline equivalence or baseline execution is claimed for that local fixture. Its unrelated constructor repair remains outside this follow-up. The new production goalie regression directly covers the changed import logic.

## Changed files

- src/CampaignCreator/PlayerExport.cs: preserve exact native goalie head identity/clear.
- src/CampaignCreator/GameCustomContent.cs: distinguish known native empty helmet selection from authored blank.
- src/CampaignCreator/UpgradeCleanup.cs: retain opaque goalie Face and explicit goalie Helmet Skin none; other validation remains active.
- tests/PlayNowPersistenceChecks/GoalieHeadRoundtripChecks.cs and Program.cs: actual boundary regression and registration.
- docs/FOUR-FEATURE-CANDIDATE.md and SESSION_HANDOFF.md: replace only the formerly pending head-parity disposition with this follow-up and evidence pointer.
- This report.

## Build receipt and commands

New output: build/GoalieHeadRoundtrip/studio-publish. Runtime source: build/FourFeatureCandidate-FinalReview/runtime/CustomCampaignFramework.dll. Runtime SHA256 before build and in the new mod-runtime bundle: F646E8665FAEEFAE0775DCFAC7C9B53A24141BD25CA3EAC8CA6ECB8B4379DB1C. Runtime sources were not changed or rebuilt; existing native audits are not rerun on this identical runtime.

Published Studio DLL SHA256: 2A1AC2524F4D7C62F0BE3F09F8E48504F861B3F6BF3A19BF67A5E67DF33D5717.
Published Studio EXE SHA256: 7FFAB32B79EB964062105B3AB0B218CA243C96CA30C8B9CF61461F96FA5FDAC4.

Use C:/Program Files/dotnet/dotnet.exe for these commands from the isolated checkout:

```
dotnet run --project tests/PlayNowPersistenceChecks -c Release
dotnet run --project tests/PlayNowUpdateChecks -c Release
dotnet run --project tests/ExportTeamAppearanceChecks -c Release
dotnet run --project tests/CampaignStudioChecks -c Release -- --export-skip <evidence>/export-skip
dotnet run --project tests/CampaignStudioChecks -c Release -- --import-values <evidence>/import-values

dotnet publish src/CampaignCreator/CampaignCreator.csproj -c Release -r win-x64 --self-contained true -p:StudioRuntimeBinary=<checkout>/build/FourFeatureCandidate-FinalReview/runtime/CustomCampaignFramework.dll -p:StudioRuntimeHash=<checkout>/build/FourFeatureCandidate-FinalReview/runtime/runtime-sha256.txt -o build/GoalieHeadRoundtrip/studio-publish

dotnet run --project tests/GameArtPackageChecks -c Release -- build/GoalieHeadRoundtrip/studio-publish build/FourFeatureCandidate-FinalReview/runtime/CustomCampaignFramework.dll
```

The external source-manifest.json, payload-manifest.json and FINAL-RECEIPT.json record actual paths/hashes, source commit, command results and runtime reuse. Existing final-review-fixes/FINAL-RECEIPT.json remains historical runtime identity/evidence; it is not rewritten.

## Limits and ownership

No game launch, installation, real-save access/write, canonical product-source write, Git mirror edit, push, release, promotion or artifact cleanup occurred. No rendering, gameplay, Unity serialization, native Harmony/provider order or cache lifetime confirmation is claimed. Existing native/human obligations still apply to the exact subsequently authorized candidate. The controller owns independent review and any selective canonical promotion. Older final-review docs retain their historical statements; only the current guide/handoff disposition is updated here.
