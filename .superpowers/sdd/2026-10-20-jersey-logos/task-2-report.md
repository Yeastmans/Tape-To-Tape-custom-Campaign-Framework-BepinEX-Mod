# Task 2 implementation report

Status: DONE_WITH_CONCERNS
Commit: `57f0412` Add team jersey logo controls and portable campaign artwork

## Implemented

- Added Home and Away jersey logo choices to the Team look editor. Each kit can inherit the team identity logo, explicitly show no jersey logo, or select an image through the existing picker. The controls read and write the editor draft and retain slot-specific embedded metadata until a selection changes.
- Both team uniform diagrams resolve the selected kit and check their own jersey compatibility. The replaced preview bitmap is disposed. The existing player-only shared logo picker and precedence were left intact.
- Kit copy now uses `JerseyLogoOptions.Copy` for the selected source kit and its embedded payload, including clearing the destination when the source inherits or selects no logo. Fill look excludes the identity logo and both kit slots. Default-look save and new-team application retain the three logo choices and payload; new players do not receive personal logo overrides.
- Campaign ZIP creation stages content-named PNG copies for identity and both jersey slots, rewriting only the packaged documents. Import checks the content-derived filename and rejects conflicts with existing CustomLogos bytes. Existing source documents remain unchanged. Upgrade cleanup retains both slot fields and export metadata. The existing `GameCustomContent` merge already preserves authored kit fields and independent personal overrides; an import check now exercises that path.
- Corrected nullable Avalonia item-panel template typing, removing the two carried CS8619 warnings.

## Focused verification

| Command | Result |
| --- | --- |
| `C:\Program Files\dotnet\dotnet.exe run --project tests/TeamLookFillChecks -c Release` | Passed; Fill preservation, both copy directions, default-look save/new team, and no personal overrides. |
| `C:\Program Files\dotnet\dotnet.exe run --project tests/CampaignStudioChecks -c Release -- --logo-picker artifacts/followup-four-features-20261020/logo-picker` | Passed; Home/Away draft, image choice, Save/reopen, and skater/goalie preview compatibility. |
| `C:\Program Files\dotnet\dotnet.exe run --project tests/CampaignStudioChecks -c Release -- --player-logo artifacts/followup-four-features-20261020/player-logo` | Passed, 20 checks; independent personal shared-logo precedence. |
| `C:\Program Files\dotnet\dotnet.exe run --project tests/CampaignStudioChecks -c Release -- --import-library artifacts/followup-four-features-20261020/import-library` | Passed, 31 checks; Play Now current-value reimport and cleanup allowlist. |
| `C:\Program Files\dotnet\dotnet.exe run --project tests/JerseyLogoChecks -c Release` | Passed, 76 assertions; portable same-name PNGs, ZIP-only rewriting, installation resolution, and strict embedded validation. |
| `C:\Program Files\dotnet\dotnet.exe run --project tests/CampaignLogoResolutionChecks -c Release` | Passed, 32 checks. |
| `git diff --cached --check` | Passed before commit. |

## Red and green evidence

- `TeamLookFillChecks` failed before implementation at `Copy to Away retains source crest and payload`; it passed after the kit/default changes.
- `JerseyLogoChecks` failed before sharing changes at `Portable kits stage distinct same-name PNGs`; it passed after content-named ZIP staging and import resolution.
- The new UI and import checks were added after the implementation and have green evidence only. An initial `--player-logo` run failed because its old assertion expected the shared identity field to disappear from the resolved preview; Task 1 retains that identity and clears the incompatible jersey slot. The assertion now checks the slot and all 20 player checks pass.

## Scope and limitations

- No game launch, install, live-save edit, native jersey rendering, publication, push, source sync, or full Studio suite was performed. The checks use checkout artifact fixtures or OS temporary files; the campaign resolution check reads its established fixture without writing it.
- Tests establish Studio draft/persistence, ZIP/import bytes, and offline compatibility behavior. They do not establish in-game rendering or human gameplay acceptance.
- The new test sources were explicitly allowlisted in `.gitignore`; generated outputs, bin/obj, and fixture folders were not staged.

## Review fix round 1: existing consumer verification

The reviewer found that the original Task 2 file list required hunks in `TeamEditorFillLook.cs` and `GameCustomContent.cs`. The controller ruled that the plan and brief should identify these as inspect/verify consumers because their current behavior already meets the requirements. I updated those two task documents; no source file was changed in this round.

- `src/CampaignCreator/TeamEditorFillLook.cs:33` iterates `TeamLookCopy.Keys`. The changed `TeamLookCopy.cs` key list excludes identity and both kit fields and metadata. `tests/TeamLookFillChecks/Program.cs:71-75` checks Fill look preserves all three choices and the Home payload; the focused TeamLookFillChecks run passed.
- `src/CampaignCreator/GameCustomContent.cs:71` calls `Merge(team, Team(model))`; `GameCustomContent.cs:127-131` writes only fields present in the native projection. That projection has no jersey-kit selections, so the authored snapshot retains both. The player merge at `GameCustomContent.cs:83` likewise preserves the independent personal shared-logo field, which the native player projection does not supply. `tests/CampaignStudioChecks/ImportLibraryChecks.cs:57-69` checks both kit selections and the personal override on current Play Now reimport; the focused `--import-library` run passed 31 checks.
- This correction changes documentation only. No suites were rerun. The reviewer’s minor picker-caption and extra custom-default assertion suggestions remain in the controller’s final-review ledger outside this correction.
