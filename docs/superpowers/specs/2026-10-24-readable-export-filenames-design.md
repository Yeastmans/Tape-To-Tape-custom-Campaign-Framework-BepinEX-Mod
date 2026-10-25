# Readable export filenames

Date: 2026-10-24
Status: Approved by the user for implementation on 2026-10-24

## Intent and scope

Make exported files identifiable in a file browser. The user selected both Play Now exports and shared campaign ZIPs, with no second feature. Readable names apply to new exports; existing exports retain their filenames and identities when updated.

## Approaches

1. **Selected: readable name plus a unique identity for new exports.** Keep the game's existing filename-to-ID relationship. The readable part is fixed when the entity is first exported; later edits update that entity without renaming it. This preserves duplicate-name safety and existing update/save behavior.
2. Rename existing exports and follow subsequent display-name changes. This would require migrating references and persistence paths. The user chose to preserve existing exports instead.

## Behavior

- A newly exported team uses `CustomTeam-Ice-Wolves-<unique-id>.json`; its players use names such as `CustomForward-Jane-Smith-<unique-id>.json` and `CustomGoalie-Sam-Lee-<unique-id>.json`.
- Standalone player exports use the same naming rule. Supplemental text files and exported-library snapshots use the matching named identity, so the native save, roster links, supplemental data, and library remain consistent.
- Exporting the same name twice creates distinct copies. Updating an existing export preserves its current IDs and filenames, including older GUID-only exports. A later name change appears in the content but does not rename an existing export. Newly allocated players during an update receive named IDs.
- Shared campaign downloads advertise `<campaign-name>.zip` as the download filename. Use the existing campaign presentation title when available, otherwise the readable name already derived from the stored ZIP path. Keep stored paths, management codes, campaign contents, and replacement behavior intact.

## Implementation choices

These are proposed engineering choices rather than additional user requirements:

- Keep the complete randomly generated GUID suffix; do not reduce uniqueness. Use a bounded name prefix so filenames remain practical.
- Play Now IDs stay within the already accepted ASCII letters, digits, hyphen, and underscore character set. Normalize accents where possible, replace unsupported characters with separators, trim repeated separators, and use `Team`, `Player`, or `Goalie` if no usable name remains. Keep displayed names unchanged.
- ZIP download names remove filesystem-unsafe characters and trailing spaces/dots, have a bounded length and a nonempty fallback, and use properly encoded HTTP download headers. No stored campaign is renamed.
- Add a small shared Studio naming helper and use it at the three new-identity creation sites. Existing update and runtime persistence code should continue to use its current identity rules.
- Implement the ZIP response naming in the existing community worker. Preparing and checking the source is in scope; deploying the service or publishing another installer is a separate action.

## Compatibility evidence and validation

- Current Studio and runtime persistence accept filename-safe string IDs, not only GUID strings, and require filenames to match the embedded ID.
- Read-only inspection of the archived game binary matching R26/build 26150666 confirms native repositories enumerate `CustomTeam-*.json`, `CustomForward-*.json`, and `CustomGoalie-*.json`, then construct save paths from string IDs. Its SHA-256 matches R26's recorded game hash.
- The installed Steam game has since advanced to build 26188487. This filename change does not update the separate game compatibility baseline, and validation must not claim gameplay acceptance against the new build.
- Check named team and standalone player export, roster/sidecar identity consistency, duplicate names, unsafe/empty/long names, old-export updates, named-export updates, and save/reload identity behavior in the existing focused checks. Build the affected Studio project.
- Check campaign download headers for current metadata, legacy path fallback, punctuation, Unicode, missing metadata, and unchanged ZIP bytes/storage identity using the worker checks.
- Human validation: export a named team and standalone player, inspect their filenames, export the same name again, update an older export, and verify the resulting entities in a compatible game build. Download a shared campaign after the worker update is deployed and inspect its filename. Human results remain pending until observed.

## Exclusions

No bulk migration, automatic renaming on later edits, new export UI, save-format migration, installed-game changes, community deployment, or release publication is part of this filename-only change.
