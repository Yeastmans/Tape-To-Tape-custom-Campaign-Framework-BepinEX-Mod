# Changelog — Tape to Tape Custom Campaign Framework

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
