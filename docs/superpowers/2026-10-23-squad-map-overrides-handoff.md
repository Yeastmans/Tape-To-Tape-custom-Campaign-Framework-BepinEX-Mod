# Squad map overrides: local candidate and validation

Status: implementation and final technical checks complete. Independent review found two Important validation issues; both were fixed in one regression-led pass. No unresolved review findings remain. Human gameplay acceptance is pending. This task does not install the candidate, launch the game, publish a release, or change real campaigns or saves.

## Behavior

Open a campaign player team, select **Maps**, enable **Use own maps**, then choose **Edit team maps**. Pending player-team changes must save successfully first. An unchanged saved team opens directly, including when its map needs repair. The map window shows the squad's saved name and labels each map **Following campaign** or **Custom for this squad**.

An untouched map keeps following the campaign, including native template generation where no campaign layout has been saved. Opening, navigating, preview normalization, or saving without edits creates no override. Only an edited map becomes independent. Undoing the first edit restores inheritance. Saved custom maps stay independent until **Reset this map to campaign** is saved. Reset is undoable. Disabling the setting retains saved edits for re-enabling.

Disabled retained overrides expose **Edit saved team maps**, allowing repair or reset before activation. Repairing one squad does not require other squads' broken maps to be fixed first. Campaign-wide settings validate every selectable squad; when **Use player teams** is off, their retained overrides do not block those settings. Re-enabling validates them again.

Campaign count and order stay shared. Campaign deletions renumber each squad's overrides with the surviving original maps and keep recoverable backups. A squad editor detects changes to its campaign/squad source and rejects stale saves while keeping the draft open.

New stages resolve the selected run squad and current saved toggle. Continue uses the verified applied-map receipt and retained Coach reward definitions; later authoring/toggle edits do not replace an already-generated saved map. Invalid overrides are reported and use the existing native generation failure path.

Whole-campaign copies and ZIPs retain active/disabled overrides and custom Coach dependencies. Standalone team/library copies and Play Now exports omit campaign map settings and files. Local run receipts stay out of campaign ZIPs.

## Files and diagnostics

- Per-team setting: `Use Own Maps` in the campaign's `player_teams/<squad>/team.txt`.
- Full map overrides: `player_teams/<squad>/map_overrides/map-N.txt`.
- Backups: campaign `map_backups`, retaining the original squad and stage path.
- Local candidate and all diagnostic logs: `artifacts/squad-map-overrides-20261023` in the canonical workspace. The candidate folder is a self-contained Windows Studio package with its matching runtime. `candidate-identity.json` records hashes and source revision after packaging.
- Isolated branch: `feature/squad-map-overrides`; baseline `624761a00b7f94c9a738f397f228ebaee327a641`. The publishing mirror and installed runtime are not delivery targets.

Technical checks cover real editor interactions and file saves, core inheritance/remapping, source-linked runtime selection and receipts, actual campaign ZIP/copy/export paths, custom Coach dependencies, reward budgets and goalie rules. Managed native doubles exercise adapter logic; they do not prove live Unity gameplay. The runtime builds against installed game interop. The native compatibility baseline remains unchanged. The existing unused `ex` warning in `Plugin.cs` is unrelated.

All 13 targeted technical suites passed on the final product source:

| Suite | Checks |
| --- | ---: |
| SquadMapChecks | 22 |
| SquadMapEditorChecks | 33 |
| SquadMapIntegrationChecks | 24 |
| SquadMapRecoveryChecks | 16 |
| SquadMapRuntimeChecks | 11 |
| CustomCoachPersistenceChecks | 23 |
| CustomCoachChecks | 31 |
| CustomCoachEditorChecks | 23 |
| CustomCoachVisitChecks | 15 |
| CustomRewardRuntimeChecks | 275 |
| CustomRewardCreatorChecks | 1,131 |
| GoalieRosterRuntimeChecks | 523 |
| ModCompatibilityChecks | 131 |

`final-checks.json` and the per-suite logs retain the results. The recovery suite uses the actual MainWindow team save/launch path, including missing-opponent repairs, two invalid squads, disabled retained maps, failed activation, and pending required-field failures. The integration suite covers pending campaign-wide settings and reactivation. Older MapTabsChecks and CampaignMapSaveChecks assumptions recorded before this feature were not used as acceptance gates; they were not rerun in this final suite.

The local ZIP is `squad-map-overrides-local-candidate.zip` in the task artifact folder. `archive-receipt.json` records its SHA-256 and entry verification; `promotion-receipt.json` records guarded source copying and original-file backups. The candidate includes `LOCAL-CANDIDATE.json` and this guide as `VALIDATION.md`.

## Human validation: pending

Use a disposable campaign copy and squads A/B. Do not use a valued active save for these checks. The candidate has not been installed; live-game checks below are for when you choose to test it.

For an isolated Studio check, extract the ZIP, open PowerShell in its **Squad map candidate** folder, and run the following. This creates a separate content location when you create your test campaign; it does not select your existing campaign library.

```powershell
$testContent = Join-Path $env:TEMP ('T2T-SquadMaps-' + [Guid]::NewGuid().ToString('N'))
& '.\T2T Campaign Studio.exe' --content $testContent
```

1. **Set up three maps.** Give campaign Maps 1/2/3 distinct names and visibly different nodes, then save. Create two player teams A/B. Open A → Maps, enable Use own maps, and click Edit team maps. Expect the team to save first, A's name in the map-window title, all three maps following the campaign, and no map-add/remove controls. Open each tab and save without changes. Close/reopen; all must still follow.
2. **Edit one map.** Change only A's Map 2 name and a node/opponent, then save. Change all three shared campaign maps and reopen A's maps. Expect A's Maps 1/3 to reflect the new campaign, Map 2 to retain A's version, and B to use all shared maps. Check custom Coach name/art/pool edits in A's Map 2 as well.
3. **Undo and reset.** Make a first edit to an inherited map, then Undo before saving. Expect Following campaign and no pending edit. Redo should restore Custom for this squad. Reset saved Map 2 to campaign, Undo the reset, then Redo and save. Expect the reset version to inherit future campaign updates. Merely making a saved custom map look identical to the campaign must leave it custom.
4. **Disable/re-enable.** Save a distinct override, turn Use own maps off, and save the player team. A should use the shared map on its next new stage/run. Turn it on and save again; the retained override must return. An already-generated map in Continue must remain its saved version.
5. **Change campaign structure.** With a distinct A override on Map 2, remove campaign Map 1 and save. A's former Map 2 must become A's Map 1; the campaign's new map count/order must appear for both squads. Add a map; the new slot must follow the campaign. Removed override files must be recoverable under map_backups.
6. **Stale editor edge case.** Keep A's map draft open. In a second Studio instance using only this disposable campaign, change/save the shared map set. A's stale Save must be rejected with its draft preserved, rather than attached to a different stage. Reopen from the updated campaign before editing again.
7. **New run and Continue.** In the game, start with A, then B, and check the map name, nodes, opponents, Coach art and exact allowed relic pool. Save A on its custom stage; change its authoring file/toggle in Studio; Continue A. Expect the already-applied graph and Coach reward definitions to stay the same. Advance to a new stage and expect the latest saved setting for that stage. Test another squad and default game mode for isolation.
8. **Share and copy.** Export the whole disposable campaign to ZIP while an override is disabled. Import it into a fresh content folder, re-enable that squad, and verify names, art, nodes and Coach relic choices without the original library/images. A standalone team/library copy into another campaign should start with that destination's maps and no own-map flag/overrides.
9. **Repair after opponent removal.** Assign opponent X in A's and B's own maps, save, then remove X from campaign opponents. Open unchanged A → Maps → Edit team maps. Expect A's invalid map to open so you can reassign the opponent or reset the map. Save A's repair while B is still invalid; B's data must remain unchanged. Repeat with A's own maps disabled: use Edit saved team maps, repair/reset, then re-enable. Attempting activation before repair must report the invalid assignment without enabling it. A pending invalid team edit, such as clearing City, must still block map launch until corrected.
10. **Disable all player teams.** With goalie recruitment enabled, save a squad override containing a goalie recruitment node. Turn campaign Use player teams off and goalie recruitment off, then save the campaign. Expect saving to succeed and retain the unused squad override. Re-enabling player teams while recruitment stays off must report the retained node; turn recruitment back on or repair the map before activation.

Record the observed result for each step, including any unexpected screen, save message, or gameplay behavior. All live gameplay steps remain pending until the human reports results.
