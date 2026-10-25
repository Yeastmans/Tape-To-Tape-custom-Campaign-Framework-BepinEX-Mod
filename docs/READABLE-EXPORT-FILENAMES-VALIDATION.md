# Readable export filenames: validation

Status: Included in the revision 27 installer; the community worker is deployed and its existing downloads have been verified. Human validation remains pending. See [revision 27 release validation](RELEASE-R27-VALIDATION.md).

## What changed

New Play Now exports use names such as `CustomTeam-Ice-Wolves-<guid>.json`, `CustomForward-Jane-Smith-<guid>.json`, and `CustomGoalie-Sam-Lee-<guid>.json`. Supplemental files and exported-library snapshots use the matching identity. The prefix is limited to 48 ASCII characters; accents are simplified and unsupported characters become separators. Displayed names retain the original text. A complete GUID keeps repeated names independent.

Updating an existing export retains its IDs and filenames even after its display name changes. This applies to both older GUID-only exports and new named exports. Newly added roster slots receive named identities.

The community worker advertises the campaign title as the downloaded ZIP filename, with a readable legacy fallback. Filenames preserve Unicode, exclude filesystem-unsafe characters, and are bounded to 120 UTF-8 bytes before `.zip`. Stored paths, file contents, ownership codes, and replacement identities remain unchanged.

## Local checks

- PlayNowRosterChecks: 107 assertions passed, including production team and standalone package/write paths, name normalization, uniqueness, and matching IDs.
- PlayNowUpdateChecks: 68 checks passed, including GUID-only and named export updates, owned-player reuse, and new named second-line slots.
- PlayNowPersistenceChecks: 34 cases passed, including named team/forward/goalie save pairs and reloads.
- CommunityWorkerChecks: 37 tests passed, including readable HTTP filenames, legacy paths, Unicode, unsafe names, malformed metadata, unchanged ZIP bytes, and existing storage alias behavior.
- Studio Release build: passed with zero warnings and zero errors.
- Existing Studio export regression checks: 58 passed, including appearance, custom rewards, supplemental data, and rollback behavior.
- Fresh read-only code review: no actionable issues; approved for selective integration. Total: 304 passing checks/assertions/cases across five suites.

Evidence folder: `artifacts/readable-export-filenames-20261024` in the canonical workspace. All diagnostic export files were created in isolated fixtures. These checks do not establish gameplay acceptance.

## Human checks

### Setup

Use the published revision 27 installer, which includes this change and compatibility for Steam public build 26188487. Keep a copy of an older Studio-exported team for the update check. Close Tape to Tape before exporting. The default native save folder is `%USERPROFILE%\AppData\LocalLow\Excellent Rectangle\Tape to Tape\TeamDataModels`.

Revision 27 targets Steam public build 26188487. Start with the matching game build and complete the new-run and Continue checks in the release validation document as well as the export checks below.

### 1. Named team and roster

Create or select a complete team named `Ice Wolves`, with a skater named `Jane Smith` and a goalie named `Sam Lee`. Export it as a new Play Now team. Inspect the native save folder.

Expected: the corresponding JSON filenames start with `CustomTeam-Ice-Wolves-`, `CustomForward-Jane-Smith-`, and `CustomGoalie-Sam-Lee-`, followed by unique IDs. Export the same team again. Both complete copies should remain, with different IDs. Repeat with two players sharing the same name; both should have separate files.

### 2. Standalone players and unusual names

Export a standalone skater named `Émile / Smith` and a goalie named `Sam Lee`.

Expected: filenames start with `CustomForward-Emile-Smith-` and `CustomGoalie-Sam-Lee-`. The in-content player names remain unchanged. A name containing only unsupported characters uses `Player-` or `Goalie-` in the filename; a long name has a shortened prefix and a complete unique suffix. No export should replace an earlier file with the same display name.

### 3. Update without renaming files

Record the filename of the older exported team. Change its team/player names in Studio, choose Update existing export, and select that export. Repeat with one of the new named exports.

Expected: each team's original filename remains and no duplicate team appears. Owned roster players retain their original files. If you enable and complete a second line on a formerly single-line team, only the newly allocated players get new named files.

### 4. Compatible-game save and reload

With a supported game build, launch Play Now and select the new team and standalone players. Confirm the displayed names and roster. Save a lineup/name edit, quit, and relaunch.

Expected: the same team/player identities load, roster links remain correct, and files keep their original named identity after display-name edits. Report any missing player, duplicated team, or missing supplemental settings.

### 5. Shared ZIP downloads after deployment

The updated worker is deployed. Download a campaign through its existing public download link in a browser, including one with a Unicode title and one older upload without presentation metadata.

Expected: the browser offers a readable `<campaign title>.zip` filename. Existing storage prefixes disappear from the suggested download name. Extract/import the ZIP and confirm its campaign is intact. Studio's Download and install action still installs the same campaign; its temporary internal filename is not a user-facing export.

Record pass/fail for each step, the names used, and any unexpected filename or error. Human results remain pending until reported.
