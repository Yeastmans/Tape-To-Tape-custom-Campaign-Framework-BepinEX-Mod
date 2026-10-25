# Beta 1.0 revision 27 validation

Revision 27 adds readable filenames for new Play Now team/player exports and community campaign ZIP downloads. It updates compatibility for Tape to Tape public v1.0, Steam build **26188487**. Human gameplay and browser acceptance remain **pending**.

## Technical evidence

- The reviewed filename implementation passed **304 checks** across PlayNowRosterChecks (107), PlayNowUpdateChecks (68), PlayNowPersistenceChecks (34), CommunityWorkerChecks (37), and CampaignStudioChecks --export (58). These results are reused because those tested product files are unchanged.
- The current native game and metadata are pinned as a pair, covering 1,485 protected native methods, 137 squad sites, 109 query calls, 54 named contracts and 21 raw targets. The update passed 137 compatibility checks, 232 compiled starting-flow ABI checks, 30 generated-interop identity checks, 66 goalie Harmony binding checks and 28 goalie cache contract checks.
- Independent review covers moved native targets, the changed new-run selection sequence and Play Now's string identity/load-save paths. All 221 generated DLLs agree outside their module identifiers across three isolated generations, including the loader's exact .NET 6.0.7 host. The jersey dependency check ignores only those 16 identifier bytes and continues hashing every other image byte; changed code, metadata, headers and trailing bytes are rejected.
- The release uses fresh runtime, self-contained Windows Studio and Inno Setup installer builds. The runtime retains one existing unused-variable compiler warning. All 231 pinned dependency files, approved artwork inputs and six map seams are checked. The extracted installer is checked against an exact file allowlist and contains the same runtime at its Studio and game destinations.
- The published installer, update manifest and checksum file are downloaded again and compared to the uploaded artifacts and GitHub SHA-256 digests. The release tag resolves to the recorded source commit.
- Community worker version `0a4dd948-5c93-40c5-95bd-4de57d33e7c9` is deployed. Both existing public ZIPs retained their original content hashes, catalog identities and management access. Existing encrypted secrets, aliases, routes and runtime settings were preserved.

Local release receipts are under `artifacts/release-r27-readable-filenames-20261024`; native input and compatibility evidence is under `artifacts/compatibility-26188487-r27-20261024`. Compilation, native inspection and package verification are diagnostics; they do not establish observed gameplay behavior. The release process does not launch Setup, change game saves or replace installed game files.

## Human validation

### Setup

Save your Studio edits and close Studio and Tape to Tape. Keep a backup of authored campaigns and an older exported Play Now team. Install the published revision 27 installer, use Steam's public branch (Betas: None), and confirm game build 26188487. Open Studio and check that its update/compatibility checks recognize the matching bundle.

### 1. New named exports

Create a complete team named `Ice Wolves`, with a skater named `Jane Smith` and goalie named `Sam Lee`. Export it as a new Play Now team. Export a standalone skater named `Émile / Smith` and a standalone goalie. Inspect `%USERPROFILE%\AppData\LocalLow\Excellent Rectangle\Tape to Tape\TeamDataModels`.

Expected: new files start with `CustomTeam-Ice-Wolves-`, `CustomForward-Jane-Smith-`, `CustomGoalie-Sam-Lee-` or the corresponding standalone name, followed by complete unique IDs. Accents are simplified and unsafe filename characters become separators, while displayed player names remain intact. Repeated names and repeated exports produce separate files.

### 2. Updating old and new exports

Record the filenames of an older export and a new named export. Change their display names in Studio and use **Update existing export** for each. If applicable, add and complete a second line on a formerly single-line team.

Expected: existing team and owned-player filenames stay unchanged, with no duplicate team. Only newly allocated roster slots receive new named identities. In Play Now, select the exported team/players, edit and save the lineup, quit the game, and reopen it. The same team, roster links, appearance and supplemental settings should reload.

### 3. New runs on the updated game

Select an authored campaign with player squads and try its intended starting screens, including configured bench and superstar selections. Start a new run and play into the first encounter. Repeat with the base campaign selected.

Expected: selection screens complete, the intended squad and map appear, and the first encounter starts. There should be no repeated selection screen, stalled loading screen or unexpected reset. Check Home/Away jersey logos and opponent presentation in selection previews and a match.

### 4. Continue and retained campaign features

Save and quit a run, restart the game, and use **Continue**. Visit a custom Coach with a known restricted pool. For squad maps, edit one squad map, leave another inherited, save, and start a new run with that squad; compare with another squad. Use **Reset this map to campaign**, then make another new run.

Expected: Continue restores the active run's squad, map and saved reward definitions. The Coach uses its custom world-map name/art and grants only eligible relics from its pool. The edited squad map is independent, the unedited map still follows campaign changes, other squads are unaffected, and Reset restores inheritance. See [custom Coach checks](superpowers/specs/2026-10-23-custom-coach-nodes-design.md) and [squad map checks](superpowers/specs/2026-10-23-squad-map-overrides-design.md) for the approved behavior.

### 5. Readable community downloads

Download a campaign through its existing public link in a browser, including a title with Unicode and an older upload. Extract or import the resulting ZIP.

Expected: the browser offers a readable campaign-title ZIP filename, and the same complete campaign imports. Studio's **Download and install** action continues to install that campaign; its temporary internal filename is not a user-facing export.

Report pass/fail, game build, the names/campaigns used and any unexpected filename, screen, missing roster member or error. Until those observations are supplied, human acceptance remains pending. The [filename validation document](READABLE-EXPORT-FILENAMES-VALIDATION.md) contains additional edge cases.
