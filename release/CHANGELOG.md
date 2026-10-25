# Changelog — Tape to Tape Custom Campaign Framework

## Beta 1.0 revision 27 — 2026-10-24

- New Play Now team, skater and goalie exports include the team/player name plus a unique ID in their filenames. Updating an existing export keeps its original filenames, including after changing display names.
- Community campaign ZIP downloads use the campaign title. Existing campaigns, download links and ownership codes remain unchanged; the community worker has been deployed and its existing ZIP contents verified.
- Supports public v1.0 game build 26188487. Updates the native compatibility baseline and repairs the custom starting-flow integration for the game's changed new-run sequence.
- Retains revision 26's reusable custom Coach nodes and squad-specific maps.

Validation: 304 filename and export checks on the reviewed implementation, fresh compatibility diagnostics, independent native review, fresh runtime/Studio/installer builds, and exact extracted/published package checks. Human gameplay acceptance remains pending. See [revision 27 validation](../docs/RELEASE-R27-VALIDATION.md).

## Beta 1.0 revision 26 — 2026-10-23

- Custom Coach nodes: choose **Custom → Create new / Load saved**, then set a world-map name, optional PNG and strict relic pool. Saved library entries and placed copies remain independent. Coach encounter art and dialogue retain native behavior.
- Squad maps: enable **Use own maps** on a campaign player team, then **Edit team maps**. Each map follows the campaign until edited; count and order remain shared. Reset restores inheritance, and disabling retains edits for later reuse. Disabled overrides can be repaired through **Edit saved team maps**.
- Whole-campaign sharing includes squad maps and custom Coach dependencies. Existing runs retain their applied map and reward definitions through Continue; new runs/stages use the current saved settings.
- Supports public game build 26150666 and preserves revision 25's corrected installer staging.

Validation: 2,258 focused checks on unchanged reviewed product code, fresh runtime/Studio/installer builds, and exact extracted/published package checks. Human gameplay acceptance for these additions remains pending. See [revision 26 validation](../docs/RELEASE-R26-VALIDATION.md).

## Beta 1.0 revision 25 — 2026-10-23

- Corrects the withdrawn revision 24 installer: the game and Studio receive the same verified public-v1.0 runtime and complete map-seam assets. Both destinations are checked in the extracted installer before publication.
- Includes two-line Play Now exports with stamina, independent Home/Away logos and kit copy controls, campaign goalie recruitment, reserve swapping and optional contracts. Assigned visible goalie talents appear in the native GM talent box and lineup Abilities panel; GM offers show goalie stats except Shot Power.
- Supports public game build 26150666. Runtime code is unchanged from the reviewed final revision 24 candidate; the packaging correction also covers the normal Studio build workflow.

Live checks confirmed goalie talent display, recruitment, reserve swapping, contract expiry after a played match, palette persistence through restart and two-line match swapping. Complete visual verification of every Creator screen and a native Play Now lineup edit/save/restart roundtrip remain unverified. See [revision 25 validation](../docs/RELEASE-R25-VALIDATION.md) for exact coverage. Save and close Studio and the game before installing.

## Beta 1.0 revision 24 — 2026-10-23 (withdrawn)

The installer contained a stale game-install DLL. Use revision 25; the original tag is preserved for traceability.

- Supports Tape to Tape public v1.0.0 CS13980, Steam build 26150666. Use the public branch (Betas: None).
- Export two full skater lines to Play Now with the authored stamina setting. Native lineup edits survive restart; Studio warns before an update replaces them.
- Give Home and Away kits independent logos. Copy the full selected kit to the other side, including goalie settings, then Save. Embedded Away artwork survives Home changes; campaign artwork is resolved before same-name global images.
- Enable goalie recruitment per campaign and a reserve slot per playable squad, both off by default. Goalies nodes require campaign recruitment. Recruits can occupy Active or Reserve when enabled; swap between matches.
- Optional goalie contracts count played matches, honor departure-prevention effects, promote the reserve on expiry, and retain an active goalie when no replacement is available. GM offers and lineup show duration in the native badge; GM offers show goalie attributes except Shot Power.
- Preserve independent player appearance and goalie face/mask choices through export and reimport. Personal goalie colours survive recruitment, save/reload and lineup previews. Assigned visible goalie talents appear in the native GM talent box and lineup Abilities panel. Campaign opponents retain custom identities and logos.
- Repairs include native goalie save/cache compatibility and the North-end Initialize handoff used when teams switch ends.

Validation: independent code review, managed regression checks, compiled hook/binding audits and live public-build startup/match/overtime observations. These are distinct forms of evidence. Complete live acceptance for every authoring screen, native lineup Save/restart roundtrip, contract expiry branch and appearance combination is not established; Windows capture/accessibility limitations interrupted Studio checks. See the release validation document for exact coverage and remaining checks. Save and close Studio and the game before installing.


## Beta 1.0 revision 23 — 2026-10-20

- Compatibility repair for Tape to Tape roguelite build **26085704**, including native squad behavior and custom reward queries affected by the game update.
- **Goalie recruitment:** choose Skaters (default) or Goalies on a General Manager node. Studio's Free Agents tab now has a Goalies pool with create, import, edit and selection controls. Recruits replace the active goalie and stay for the run unless replaced again.
- **Copy to Home / Copy to Away** copies the full kit draft, including skater and goalie settings. Use the existing Save button to keep changes.
- Campaign opponent logos resolve from the active campaign before global caches, fixing custom jersey and map-icon substitutions.
- Exported players with independent equipment retain that ownership when assigned to a custom team. **Re-export older affected players** to record the ownership setting.

Technical validation includes focused editor/runtime checks, independent review and a 299-method compatibility baseline. Live gameplay, rendered goalie cards, and complete native Save/Continue acceptance remain pending. Save and close Studio and the game before installing.

Not included in this revision: two-line Play Now exports, separate home/away logos, bench-goalie swapping or temporary goalie contracts. These are follow-up work.


## Beta 1.0 revision 22 - 2026-10-20

- **Fix: in-match lag in campaigns with many custom rewards.** Custom stat changes, While-conditions and other per-tick rules checked every reward in the campaign for every skater on every physics step (a campaign with about 340 custom rewards dropped to about 4 fps). They now check only the rewards owned by the players and teams in the match.
- **The revision 21 General Manager fix no longer hooks a method the game calls constantly during matches.** For squads with no skaters, the offered player is briefly placed on the roster while the game scales that one offer, then removed.
- Still supports Tape to Tape roguelite build 26071671. Native compatibility baseline unchanged from revision 21.

Not yet tested in game: neither the lag fix nor the empty-roster GM fix has been played; both are verified by build, native-compatibility and source-linked checks only. Save and close Studio and the game before installing.


## Beta 1.0 revision 21 - 2026-10-20

- **Fix: General Manager offers for squads with no skaters.** A GM free-agent node offered nothing when the squad had no players (a campaign with no starting roster): the game averages the team's skaters to scale each offer and failed on an empty roster. For that one offer the game now averages the offered player itself, and the offer keeps its own four base stats. Team data is unchanged.
- Still supports Tape to Tape roguelite build 26071671 (same as revision 20). The native compatibility baseline adds one pinned method (`TeamData.GetForwards`) for this fix; everything else is unchanged.

Not yet tested in game: the empty-roster GM fix is verified by build and native-compatibility checks only. Save and close Studio and the game before installing.


## Beta 1.0 revision 20 - 2026-10-19

- **Supports Tape to Tape roguelite build 26071671** (Steam updates of 19 October 2026, 26070875 then 26071671). The native compatibility profile, interop and the three moved native entry points used by custom rewards (goal-scored event, Extra goals repeat and denial) are updated.
- The game rewrote the Benchwarmer draft check: Trio now requires three valid skaters through a new check, and the native Gauntlet bench requirement was removed (all squads now get an "open spots" confirmation instead). Custom squads with **Trio draft requirement** follow the new native trio check; **Gauntlet draft requirement** now matches the native gauntlet squad, which no longer enforces a bench minimum.
- Native review: of 589 game methods the mod depends on, 532+ are unchanged or differ only by address; the boss bodycheck cooldown and squad-earned unlock reveal (26070875) and the Benchwarmer rewrite (26071671) leave the mod's hooks valid. 139 squad comparison sites and 110 squad queries are re-verified.

Gameplay validation on 26071671 is limited to a clean plugin load and a campaign match start. Save and close Studio and the game before installing.


## Beta 1.0 revision 19 - 2026-10-19

- **No arbitrary number limits** for custom talents and relics. Amounts, activations, stacks, recipient counts, radius and durations are open-ended, with Unlimited options for activations and stacks. Only real limits remain (non-zero/positive values where needed, 1-100% chances, native levels). The game runtime bounds a few values that could stall or overflow it (sizes, puck-bag length, timer rate, puck speed, 9,999 extra goals per goal).
- **Opposing targets everywhere**: effects that can reach your players can also target the opposing team, random opponents, the closest opponent, the opposing puck carrier, nearby opponents or the other player in the event. Timed stat changes, statuses, While-conditions, AI, capability and centred effects all resolve on opponents. Combinations that cannot work are listed with a reason.
- **New Extra goals effect**: each goal counts as N+1 goals through the game's own per-goal scoring (score, scorer, assists, stats), reversed by goal denials; works for items owned by opponents.
- **New triggers** from a full base-game coverage audit: Goalie save, Team first goal value, Final 30/60 seconds goal value, Injured by an opponent, Any skater injured.

Validation: 1,131 creator, 96 mechanic and 262 runtime technical checks pass on game build 26067234. Gameplay validation is pending; the extra-goal native hook has not yet run in a live match.

Save and close Studio and the game before installing.


## Beta 1.0 revision 18 - 2026-10-19

- Fixes Copy vanilla campaign failing when local dumps contain custom rewards such as Rally Towels.
- Keeps custom relics and talents out of base-game reward offers. Custom campaign rewards require explicit selection; native fallback cannot reintroduce them.
- Preserves custom libraries, authored campaign content, and the revision 17 game compatibility update.

Validation: all 19 native squad copies match clean defaults when local custom rewards are present; 262 source-linked reward adapter checks pass. Native Unity/gameplay validation remains pending.

Save and close Studio and the game before installing. Try Copy vanilla campaign, then check a normal run and an explicitly configured custom campaign's reward offers.


## Beta 1.0 revision 17 — 2026-10-19

- Updates Creator and mod compatibility for Tape to Tape roguelite build **26067234**.
- Adds **Delete from library** to custom talents, with confirmation. Existing campaign and player copies are preserved.
- Retains revision 16 features, including per-skater contracts and campaign referee heads.

Validation: 53 native compatibility checks and seven isolated talent-delete UI checks pass. Fresh interop compilation and native hook/signature review pass. Human gameplay acceptance remains pending.

Close the game and save/close Studio before installing. After updating, check Continue, complete a match and return to the map, try custom talent/relic triggers, and verify both roster lines. In Creator, test Delete from library on a disposable talent and confirm existing campaign copies remain.


## Beta 1.0 revision 16 — 2026-10-18

Set contract durations for individual skaters, choose referee heads per campaign, and identify community campaigns with readable names, descriptions and custom PNG icons.

- **Player editor → Details & attributes → Custom contract duration**: enable it for any skater and choose a positive whole number of matches played. It works for starting players, selected bench players, superstars and future recruits, including non-GM campaigns. Off uses normal game rules; matches spent on the bench do not count.
- Contract changes affect **new runs and future recruits only**. Existing signed players retain their saved duration and progress. Recruitment offers display the duration they will sign with. Native effects that prevent departures still apply, and two-line squads preserve vacancies and surviving positions.
- **Campaign → Game options → Referee head**: choose a native or installed custom skater head independently for each campaign. **Game default** restores the normal choice. Saving and sharing preserve the selection.
- The community browser displays readable campaign names. Selecting a campaign shows its title, author, icon and multiline description in the sidebar.
- **Upload campaign** includes **Choose PNG…**, preview and **Remove**. Custom PNG files are the only accepted icons. Studio preserves transparency, proportions and the original image; the icon is optional.
- Existing uploads retain their ZIP identities and ownership controls. Older entries use readable names and placeholders where metadata was never supplied. Replacing a ZIP preserves its description and icon. The matching metadata backend has been deployed and verified.

Includes revision 15's recruit-ability setting and targets Steam roguelite build **26020247**.

Validation: focused editor, sharing, PNG, backend, contract lifecycle, recruitment and two-line regression checks passed. Native contract bodies and save consumers were reviewed on the exact supported game build. The final installer and copied-fixture upgrade are verified before publication. **Human gameplay acceptance remains pending**, including contract counting/departures and referee rendering.

Save Studio edits and close the game before installing **T2T_Custom_Campaign_Framework_Setup.exe**, or use Studio's **Check for updates**. The [contract guide](https://github.com/Yeastmans/Tape-To-Tape-custom-Campaign-Framework-BepinEX-Mod/blob/main/docs/PLAYER-CONTRACTS.md) includes the gameplay checks for bench matches, Continue, future recruitment and both lines.

## Beta 1.0 revision 15 — 2026-10-18

Adds a per-campaign setting to stop new free-agent offers receiving abilities such as YoYo and Yeet.

- Open your campaign, select **Free agents**, enable **Disable recruit abilities**, then **Save campaign**.
- Enabled removes every ability from newly created GM recruitment offers, including abilities explicitly assigned in Studio. It also applies when a new offer is created from a cached recruit.
- The setting defaults **off**. Turning it off allows abilities on future offers again.
- Talents, source player templates and already recruited players stay unchanged. Existing offer cards are not rewritten; check a newly generated recruitment offer.
- The choice survives saving, upgrading, exporting and importing the campaign, independently of the stat and extra-talent switches.

Includes the revision 14 squad editing fix and targets Steam roguelite build **26020247**.

Validation: 385 focused managed runtime assertions and 29 Studio save/reopen, upgrade and ZIP checks passed. The packaged Studio and revision-14 update path are checked before publication. Native gameplay confirmation remains pending.

Save Studio edits and close the game before installing **T2T_Custom_Campaign_Framework_Setup.exe**, or use Studio's **Check for updates**. After enabling the setting, verify a newly generated offer and its hired player have no ability, including after save/reload.

## Beta 1.0 revision 14 — 2026-10-18

Fixes existing squad names doing nothing after opening a campaign, even though editing from the main menu worked.

- Squad names on the **Squads page** and **campaign overview** open the editor without first saving and validating the entire campaign.
- Older campaigns with missing default settings no longer appear edited immediately after opening.
- Genuine unsaved edits still require **Save campaign** before leaving. Explicit saves retain validation and preserve edits when validation fails.
- Opening a saved squad leaves authored campaign files unchanged.

The game runtime is unchanged from revision 13 and targets Steam roguelite build **26020247**. Campaign upload management and Play Now updates remain included.

Validation: pointer-driven headless Studio checks cover both campaign entry points, main-menu editing, legacy defaults, invalid saved act/reward settings, file preservation, and successful/failed saves. Related squad and opponent navigation checks passed; the Windows Studio build and installer completed. A desktop retry of the reported campaign remains pending.

Save Studio edits and close the game before installing **T2T_Custom_Campaign_Framework_Setup.exe**, or use Studio's **Check for updates**.

## Beta 1.0 revision 13 — 2026-10-17

Repairs the native crash during match loading and updates compatibility for Steam **roguelite build 26020247**.

- Removes the unsafe `ForwardStats.GetOverall` interception that crashed while initializing a team. Random shot power/accuracy rules retain their existing caller scopes and pre-shot roll behavior.
- Custom boosts pass an empty native nullable value when no forced direction is requested. Recovery checks skip actors that do not yet have a team.
- Updates the reviewed native baseline and hook addresses, preserving exact game, method and instruction guards. The updater accepts only the exact known previous runtime DLLs in its replacement list and preserves unknown runtimes.
- Retains revision 12's campaign upload replacement/deletion and explicit **Export as new / Update selected team** choice for Play Now.

Validation: 21 random-shot scope, six boost and eight recovery regressions; 53 compatibility checks; eight isolated updater checks; and 530 compiled callback bindings with zero binding issues. The [native audit](../docs/NATIVE-26020247-REVIEW.md) records the build and ABI evidence. Human confirmation of the Winnipeg match transition and gameplay remains pending. No FPS improvement is claimed.

No NHL campaign ZIP is included. Close the game and save Studio edits before installing **T2T_Custom_Campaign_Framework_Setup.exe**, or use Studio's **Check for updates** button.

## Beta 1.0 revision 12 — 2026-10-17

Update or delete your posted campaign from **Home → Campaigns → Manage uploads**. The matching community service is now deployed.

- **Replace ZIP…** updates an existing post while keeping its identity. The previous download stays available until the replacement commits successfully.
- **Delete from Dropbox…** asks for confirmation and removes the community post. Local campaigns and ZIPs remain intact.
- Studio saves private management access automatically outside campaign folders. Older posts support a one-time administrator-issued code, verified and saved by Studio.
- **Export to Play Now** now offers **Export as new** or **Update selected team**, with an explicit target, stable team identity, and protection for players shared with other teams.
- Includes the revision 11 custom talent/relic loading repair and compatibility for Steam roguelite build **26019854**. The runtime DLL is unchanged from revision 11.

Custom reward definitions and icons continue to travel with campaign ZIPs. Private management access does not. No NHL campaign ZIP is included in this release, and this update does not restore missing talents in existing saves.

Validation: self-contained Windows Studio build and installer completed; 68 scoped client, ownership-store, worker and Play Now checks passed on the unchanged feature sources. Public service health advertises campaign management. Live replacement/deletion and human UI acceptance remain pending; no existing post was replaced or deleted for testing.

Close the game and save Studio edits before installing **T2T_Custom_Campaign_Framework_Setup.exe**, or use Studio's **Check for updates** button.

## Beta 1.0 revision 11 — 2026-10-17

Restores compatibility with Tape to Tape Steam roguelite build **26019854** and fixes custom campaign rewards that failed to load.

- Custom talents and neutral relics register without waiting for a base-game donor asset to be loaded.
- Check/Checked custom effects use the game's match-wide hit callback, fixing the unsupported event-delegate error that stopped reward event setup.
- Updates the reviewed native hook addresses and compatibility data while preserving game-version and instruction guards.
- Removes the repeated global donor-asset searches from reward lookups. FPS improvement has not yet been measured.

The existing Studio interface and upload behavior are retained. Campaigns and saves are not rewritten by the runtime fix; missing starting talents in existing saves are not retroactively granted. Campaign ZIPs continue to include custom reward definitions and icons. No NHL campaign ZIP is included in this release.

Validation: successful runtime build; 86 managed reward checks, 53 compatibility checks, eight isolated updater checks, and compiled callback-binding review passed. In-game reward behavior and performance still need human confirmation on this build.

Install **T2T_Custom_Campaign_Framework_Setup.exe**, or use Studio's built-in update checker.

## Beta 1.0 revision 10 — 2026-10-17

Restores compatibility with Tape to Tape Steam **roguelite build 26015379**.

- Updated the reviewed native compatibility data and relocated the four changed squad-behavior patches.
- Restored the custom-reward hook groups, including the moved goal-event constructor. Game-version, method-pointer and instruction checks remain enforced.
- Retains **Keep authored stats**, **Disable extra recruit talents**, the opponent lineup repair, and the Act Sequence cleanup fix.
- Moved **Discard draft** beside Save in the talent/relic creator and made it red.

This revision also clears the compatibility error that prevented revision 9's **Launch Tape to Tape** button from starting the updated game. Install the new Studio package to replace the older compatibility data.

Validation: 32 native recruit-offer cases, eight moved-patch callback cases, reward-hook installation and native fallback checks, match startup, player and opponent line changes, and roster contract badges. Source regression and packaged Studio checks also passed.

Install **T2T_Custom_Campaign_Framework_Setup.exe**, or use Studio's built-in update checker.

## Beta 1.0 revision 9 — 2026-10-17

- Fixed upgrade cleanup deleting **Act Sequence**, which stores the campaign's
  map order and length. Authored sequences now survive cleanup in saved campaigns,
  the library, drafts and staged content, including export/import afterward.
- Corrected the cleanup-list generator to include the shared act-order parser.
  Invalid sequences remain invalid; campaigns without an authored sequence retain
  their existing default behavior.

Validation: 61 upgrade-cleanup checks passed, including the regression that failed
before the fix. The packaged Studio cleanup command was also checked against
isolated campaign files. Revision 8's recruit options and runtime are retained.

## Beta 1.0 revision 8 — 2026-10-17

- Added **Keep authored stats** on the Free agents page. New offers can preserve
  their source player's Speed, Shot Accuracy, Shot Power and Checking instead
  of native GM scaling. Applies to custom and base-game recruits; defaults off.
- Added **Disable extra recruit talents**. Blocks fresh and cached random extras
  on new offers while preserving fixed talents, contract rules, abilities and
  already signed players. Turning it off restores the saved pool choices and
  weights. Both options survive saving and campaign export/import.
- Cached extra talents now respect the current free-agent talent pool when a
  new GM offer is created, including an explicitly empty pool.
- Fixed opponent substitutions leaving stale lineup IDs and hiding the second
  line in custom or owned native-copy teams.
- Updated native compatibility coverage for Steam build **26001062**.

Validation: 190 runtime assertions, 17 editor/save/import checks and 16 native
offer cases passed for the final recruit-options candidate. Earlier checks
covered keep-stats off/on, opponent line switching and contract badges.

Known issues: the existing upgrade-cleanup path can remove Act Sequence; earlier
popup/startup-reset reports remain unexplained. Missing contract labels were
not reproduced in the tested roster views, so no display fix is claimed.

## Beta 1.0 revision 2 — 2026-10-13

- Preserve custom-head assignments when upgrading or importing content. Custom
  heads use authored identities outside the shipped native equipment catalog;
  cleanup no longer clears those identities, including helmet variants and
  squad, bench and Zamboni head choices.

## Beta 1.0 — 2026-10-13

**Campaign Studio and the bundled Custom Campaign Framework mod, for Windows
x64 and Tape to Tape's Steam `roguelite` branch, build 25917988.** Beta 1.0 starts
the Studio release family; v2.1.x below refers to the previous creator. This
release contains the Windows installer only; native Linux/macOS and Steam
Deck/Proton integration are not validated.

### Campaign Studio

- Every launch begins with a beta disclaimer before the main window or startup
  tasks. It explains that features may be broken or differ from their descriptions,
  links to Discord and GitHub bug reports, and offers Open Studio or Exit.
- Refreshed compatibility for game build 25917988, including the SNES penalty
  timer presentation rule. Added the previously omitted Angus Fresh, Angus
  McGratton and Canada Frog native head choices and their supported variants.
- Custom squads remain selectable when the game version differs from the reviewed
  baseline. Unverified native hooks stay disabled; features that depend on those
  hooks may require a compatibility update. Invalid squad settings still report
  their own errors.
- Up to two nodes per layer, connected routes and a single final Boss.
  Editor fit and zoom controls change the builder view, not the in-game camera zoom.
- Goalie Pass Read accepts native fractional values from 0 to 1.
- Empty imported native helmet fields become None; ordinary Team colors and
  Wing choices equip native physical helmets, while internal reference art is hidden.

- A visual editor for campaigns, branching maps, opponents, playable squads,
  players, goalies, equipment, custom heads and reward pools. Copy native
  campaigns, keep reusable library content, import/share campaign ZIPs and
  export supported players and teams to Play Now.
- Built-in **Help / F1** points to controls on the current page. The searchable
  **Studio guide**, validation messages and **Needs attention** links explain
  requirements and take you to content that needs an assignment or repair.
- Squad rules show **Inherit / On / Off** and their effective setting.
  Opponent help describes native trigger conditions, required abilities and
  identity-dependent behavior. Supported opponent abilities are filtered;
  unsupported imported assignments remain visible and preserved until edited.
- **Read game artwork** reads previews and native catalogs from the user's own
  installed game. Current installed map data takes priority over stale runtime
  dumps when importing and validating maps.

### Recruitment and native behavior

- Restored the missing native fixed talents Performance Bonus, Ref Block Shot
  Talent, Stat Stuffer and General McShaggy's contextual three-game contract.
  They remain outside default random pools. Old imported catalogs no longer hide
  reviewed release entries; extra local IDs remain visible. Legacy Masquerade
  retains its saved identity and now clearly says it is unavailable in this game.
- Free-agent talent choices are independent of Regular and Chaos rewards.
  Begin with native outcomes checked, then choose exact allowed levels. Newly
  generated random recruitment extras and upgrade outcomes cannot use
  unchecked levels. Checked base talents retain native upgrade chances only
  when the resulting level is also checked.
- Recruitment weights multiply the native direct-roll weight. Weight zero
  blocks a direct roll, while a checked level can still be an upgrade result;
  unchecking excludes both. Fixed authored player talents and already generated
  offers are unchanged. Imported random recruitment-template lists now obey
  the same allowlist and native eligibility rules.
- General Manager recruitment nodes no longer consume Regular talent reserve.
  Fixed compact native talent IDs changing format when selected in the
  recruitment editor.
- Bench help now explains that No Effects needs **Bench draft On** and a
  positive bench size to allow later recruits, with two-line mode off. Contract
  help distinguishes native starting contracts, countdown, expiry and
  replacement eligibility from recruitment pools and match rewards.
- Added **Counts as Prince for Crusader brawls**. This only extends the native
  Prince check in that trigger: an Elite campaign node, the controlled player's
  bodycheck against a charging CPU opponent, the Bodycheck brawl effect and
  native delay/offer limits remain required.
- Edited copies of native opponent teams now retain their extra reserves,
  identities, powers and native flags outside the five or ten authored slots.
  The native Golfers' 13 forwards are preserved; custom rosters are not expanded
  into native boss rosters automatically. Explicit team-wide Stat Scale still
  applies to the entire roster.
- Helmet handling follows native head restrictions and equipment state instead
  of treating a fitted head as permanent appearance. Native helmet loss and
  restoration remain gameplay operations.
- Per-player number styles can inherit the team default or preserve an explicit
  native style. Continue restores supported authored number styles without
  overwriting earned stats, talents or jersey numbers. Play Now exports retain
  each exported player's effective style through the installed mod.

### Installation and upgrade

- The installer includes the editor runtime and BepInEx 6 IL2CPP. Studio is
  installed separately from the game; projects, library and settings live in
  `%LOCALAPPDATA%\T2T Campaign Studio`. No Python or separate .NET installation
  is required to use the Windows package.
- Existing authored content is migrated, retired fields/import directives are
  cleaned up and unsupported choices are cleared. Back up the old content and
  existing Studio workspace first, then review migrated content. Successful
  migration removes the old folders and old uninstaller; it does not retain an
  automatic backup.
- Native catalogs replace the old launch-time library dumps. Windows Studio
  content no longer uses `defaults.txt`; new-content look defaults are set in
  Studio, while unchanged imported templates retain their own appearance.

### Validation scope

The source/data audit and technical checks cover shared mappings, eligibility,
pool isolation, native prerequisites and persistence boundaries. Existing
human reports confirm Crusader shot/charge sounds and contract countdown and
expiry. These are not a claim that every ability, stochastic trigger or rule
combination has been observed in gameplay. Beta feedback remains useful for
distinct native triggers and final installation/save/Continue behavior.

## v2.1.35 — 2026-08-19

Rebuilt for the **2026-08-18 game update**. If the mod stopped loading after that
update, that is why — the update removes part of BepInEx, and reinstalling puts it
back.

### Offside, for a whole run

The base game only offers offside in Play Now's Advanced Settings. A campaign can
now turn it on for every match of a run — new **Offside** checkbox in the campaign
editor, with an optional **Offside Penalty** of *Whistle*, *Lose Puck* or
*Knockout*.

Leave the penalty on *(player's setting)* and whatever you picked in Advanced
Settings is used. **Your own saved settings are never rewritten** — the rule is
forced on for the match and released when it ends, the same way the *Linesman*
talent does it.

### Fixed

* **Talents that exist but were reported "not found".** Talent lookup only ever
  searched the pool the game draws random rewards from, so a real talent outside
  that pool — *Linesman*, the offside one — could not be given to a player at all.
  Every loaded talent is now searched.

* **Uniform and colour overrides did nothing on players exported to Play Now.**
  The game's save format for a custom skater has room for six parts of a look:
  face, jersey, away jersey, stick, logo and number. The editor offers eighteen.
  Helmet, glasses, bicep, gloves, pants, skates, every away variant and all the
  colour overrides had nowhere to be stored, so they were silently lost on the way
  out — which is why a player could look right in a campaign and wrong in Play Now.
  Those parts now travel alongside the export and are applied by the mod, through
  the same code the campaign side uses.

* **Upgraded (LVL2) relics never applied.** Picking one gave you nothing, with no
  error anywhere — the relic was looked up at level 1 and the level-2 version was
  filtered out. Affects both team relics and a custom squad's starting relics.

* **Teams exported to Play Now arrived with no starting relics.** The team file
  stores relics by id and the Creator had no way to look one up, so it wrote an
  empty list every time.

* **Custom squad slots set up for looks only were ignored.** A lineup slot given a
  helmet, gloves or a jersey colour and nothing else was treated as unconfigured
  and skipped entirely. It now gets its look, while whoever you drafted into that
  slot keeps their own stats and talents.

---

## v2.1.34 — 2026-08-01

### Shared campaigns now bring their logos with them

Team logos live in the game's `CustomLogos/` folder, **outside** the campaign — so a
shared campaign arrived with every team pointing at a PNG the recipient did not have,
and the game quietly fell back to its default crest. The campaign looked broken
through no fault of the person who made it.

Sharing a campaign now bundles every logo it actually uses, and installing one puts
that artwork where the game looks for it. This covers all of it — uploading to the
community folder, downloading from it, and importing a zip from disk.

* Both `Logo From` (team crest) and `Squad Head` (squad tile icon) are followed.
* **Your own artwork is never overwritten.** If you already have a logo with the same
  name, yours is kept and the install dialog tells you how many were skipped.
* Names that aren't custom artwork are left alone — `Logo From` may legitimately name
  a base-game team, and those need nothing bundled.
* The upload dialog shows how many logos went along with the campaign.

Logos travel inside the campaign as a `_logos/` folder, so nothing about the zip
layout changes and older versions of the mod simply ignore it.

---

## v2.1.33 — 2026-08-01

### Map Opponents — choose who you play at every node

Until now a team's folder number *was* its play order: `01 …` played game 1, `02 …`
played game 2. Two things were impossible as a result — a branching layer could
only ever hold one team, and a team could only be used once.

Campaigns can now pin a team to a specific **map node**:

* **Branch layers get one dropdown per branch.** The two paths through a layer can
  face different opponents. Previously both siblings always got the same team.
* **Any team can be used on as many nodes as you like** — "every elite game is the
  soccer players" is now a thing you can just set.
* **Elite *and* boss nodes are individually assignable**, on whichever map the
  campaign selects.

New **Map Opponents** section in the Campaign Creator, laid out to match the map you
picked. Branch dropdowns are labelled *top* / *bottom* to match how the nodes are
drawn on screen. Anything left on **(default order)** keeps the original behaviour,
so existing campaigns are untouched until you change something.

Stored in a new per-campaign `assignments.txt`:

```
Map 1 / Layer 2 / Node 0 = 05 Sogger Team
Map 1 / Layer 2 / Node 1 = 02 The pain
```

Clearing every dropdown deletes the file outright, restoring the sequential order
exactly — there is no half-configured state.

**Notes**
* Assignments are anchored to map coordinates, so they cannot drift as you play.
* Nodes whose type is rolled at map generation cannot be pre-assigned and stay on
  the sequential order.
* Challenge nodes appear as assignable only when the campaign replaces them
  (`Replace Challenges`), since that is what turns them into real matches.

### Added
* **`_game_maps.txt`** — the layer/node layout of every map, written on each launch:
  node types, branch connections, positions, reward counts, and the ids the Gauntlet
  uses to pin its elites. Includes a `TEAM IDS` table so those ids are readable.
* The Creator reads that file **live at startup** rather than baking it into a build,
  so the editor always matches the installed game.

### Fixed
* **The Creator could report the wrong version of itself.** It read a `VERSION.txt`
  shipped next to the exe, which drifted from the code three separate times — once
  frozen at 2.1.23, which made every install prompt to update forever. It now reports
  the version compiled into the build, which cannot disagree with the code it
  describes. Update checks against GitHub are unchanged.

### Documented
* A custom squad that fills **all five forward slots** starts with a full lineup, so
  the run-start superstar pick has nowhere to sit and that screen does not appear.
  Configure four or fewer forwards and it returns. This is intended — the alternative
  was dropping one of your configured players to make room.

---

## v2.1.32 — 2026-07-31

Tagged but never published as a release, so these changes reach users here.

* **Map nodes show the campaign team's logo.** The node icon is a UI image rather
  than a world renderer, which is why earlier attempts never found it.
* **GM squad free-agent nodes.** Picking the GM squad guarantees a GM node on the
  opening layer of map 1 whatever act the campaign starts on, with that squad's own
  selection count, and those nodes are exempt from the free-agent node cap.
* **`Stat Scale` now works on hand-configured campaign teams.** It had only ever been
  applied to imported teams, so the setting silently did nothing on the common case.
* **Player faces no longer collapse.** Skin paths that resolved to a shortened name
  which pointed at no face are now written in full — the five Golfers, for example,
  kept ending up as one name that resolved to nothing.
* **Creator dropdowns are generated from the game itself.** Every value the game
  actually uses, harvested each launch, so no option is offered that does not exist
  and no real option is missing (faces roughly doubled).
* **Data dumps now run with the mod switched off**, so a fresh install can generate
  the lists the Creator needs before any campaign exists.
* **Fixed the player library dumping 0 players** into a fresh or emptied library.
