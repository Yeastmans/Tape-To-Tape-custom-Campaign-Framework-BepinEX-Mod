# Play Now export translation

Campaign Studio's player export and team editor's **Export to Play Now** use
`PlayerExport.cs` and `ExportTranslation.cs`. The matching runtime is
`PlayNowTranslation.cs`, installed from `PlayNowOverrides.Install` even in default mode.

Native JSON contains the starting roster, names, stats, equipment supported by the
save model, team palettes, talent IDs, ability IDs, and relic IDs. Version 3 files
in the installed mod's `play_now_overrides` carry missing equipment/color fields,
complete talent/ability identities, goalie pass-read, and team relic identities.
Version 3 also carries the complete native player model and a checksum-verified
PNG logo snapshot. The same `AppearanceContract` resolves editor and exported
home/away colors, equipment defaults, inheritance, and number channels.
The editor resolves against the installed game's catalog again at export time.

Both talent fields are merged regardless of player role. The talent picker keeps
the normal position list; the cross-position opt-in was removed at user request.
An explicit ability assignment is retained even if the usual editor for that
position does not expose it. Native repository deserialization is intercepted
after adapter conversion so curated reward pools cannot replace the exported
assignments. Asset lookup uses the exact ID across loaded assets.

Home and away palettes have separate equipment channels; player inheritance is
flattened for the exported roster. Native color merging receives an independent
copy before per-player channels are applied, preventing teammate palette writes.
The team adapter/repository hooks restore explicit palettes after native jersey
normalization. Upgraded relics preserve their distinct IDs.

All team conversion completes before any save files are written. Translation
files and player files precede the team file; failures roll back newly written
files and preserve previous exports. Each export creates fresh IDs.

## Current boundaries

- Play Now export accepts five starting skaters and one goalie. Additional lines,
  duplicate/missing positions, unresolved import references, random talent rolls,
  and unapplied team stat scales fail explicitly before writing.
- Relics are team effects in the native engine. A relic on a player document is
  collected when exporting its team; standalone player export explains that the
  team must be exported too.
- Preserving an asset assignment does not implement a new behavior for it: an
  effect whose native code requires a particular role or campaign subsystem can
  still have that requirement. Such effects need in-game checks.
- Restart the game after installing the matching DLL, then reopen the editor
  against refreshed game catalogs. Restart after export to reload custom saves.
- The legacy Python Creator is unchanged; use Campaign Studio for this path.

## Logos and exact preview

Use **Import PNG** in the team logo control. The editor shows the selected image
on the jersey. The exported sidecar embeds its bytes using a content-derived ID;
changing or deleting the source file cannot change an existing export. Native
render hooks provide the embedded crest even when the player joins another team.
Missing or corrupt logos block export rather than substituting another crest.

Base-game names use the packaged `team-logos` extracted directly from the game
bundle by `tools/extract_team_logos.py`. Older GPU-readback caches contained
constant RGBA(205,205,205,205) placeholders, which were also embedded in exports.
`tools/repair_blank_team_logos.py` repairs only that exact placeholder signature,
with backups; `--apply` performs the repair. Runtime dumping rejects constant
images so they cannot overwrite valid cached artwork.

**Game preview** in the player editor sends the same version 3 export recipe to
the installed DLL, including unsaved appearance edits and the selected home/away
kit. The DLL applies it to a detached player and renders with the game's Spine
renderer. The editor accepts the resulting picture only after a matching native
field receipt appears in `play_now_overrides/receipts`. Start the updated game and
open team selection/customization if its render service is not ready. The flat
equipment diagram remains an editing illustration; Game preview is the native
rendered view. Field receipts check native identity, stats, equipment, colors,
talent IDs, and ability IDs; they do not prove effect activation in a match.

## Verification (2026-09-22)

Focused export checks cover cross-role talents, ID resolution, home/away palette
inheritance, explicit equipment defaults, number handedness, embedded logo bytes,
source deletion, recipe serialization, missing assets, complete roster writes,
and rollback. Player editor checks cover inheritance, saved edits and layout at
960 and 1320 widths. Release plugin and editor builds are also required.

The running game must be restarted after installation. No live match or native
render parity is claimed from the offline checks alone.

## Custom loadout badges (2026-09-22)

The base-game player/team customization lists show a mint CUSTOM ribbon on the
native talent, ability, and relic icons only when a versioned export assignment
is absent from the game's customization picker for that role.
The original icon, effect asset and native description remain intact. This marks only
missing choices, by exact ID from GetCustomizationSkater/GoalieTalents,
GetCustomizationSkater/GoalieAbilities and GetCustomizationRelics; it does
not add a fake powerup to the save or replace native gameplay behavior. Ordinary
players/teams and empty add slots receive no badge. It is scoped to customization
lists, not the gameplay HUD or global reward catalog.

Implementation: ExportLoadoutBadges.cs, installed by PlayNowTranslation.
Validation: Release DLL builds with zero warnings/errors; all six hooked concrete
methods have unique RVAs in the current game dump. Installed DLL SHA256:
FB52772B7BEC63C376E0772EE29830EF954B650991792127C5F7599EA329651F.
Native visual placement still needs an in-game check after launch.


Missing-only correction: built and installed SHA256
9FD5522FE9F9456EB2DA6866C5BAF41E4086A1F6BCFCAC7D82AE5416D3AD1F08.
Silent native startup passed, with no badge-hook/startup errors. Availability
report: artifacts/custom-options/missing-customization-options.md; raw selector
results: customization-options.tsv; runtime.log in the same folder.
Missing distinct IDs: skater talents 250/319; goalie talents 304/319; skater
abilities 27/55; goalie abilities 55/55; relics 208/292. These totals include
internal assets, upgrades and cross-role assignments. Visual placement is not
proven by the headless startup. Ordinary picker choices receive no badge.
