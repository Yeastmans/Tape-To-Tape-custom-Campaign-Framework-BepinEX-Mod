# Flat equipment for Campaign Studio

The editor uses `assets/equipment-catalog.tsv`, extracted from the installed
game's Spine skin records, rather than only the equipment worn by the runtime
dump's sampled players. Reference lighting skins are excluded from the style
picker. Home and away choices share the native asset inventory and retain exact
paths. Existing saved choices are preserved.

`assets/flat-equipment/` contains transparent PNGs, an exact role/skin/view/part
index, and a provenance report. They are packaged with Campaign Studio. The
separated equipment layout is unchanged. These are reconstructions from native
atlas panels, with collar compression, straightened garment panels, exposed-skin
removal, curved shoulders and garment sides, tapered cuffs, and connected shorts
with rounded leg openings. Outlines are antialiased at four times output size.
They are not untouched screenshots or a
pixel-exact inverse of the game's character deformation. Some baked lighting,
numbers, and logos remain part of premade textures.

Regenerate with the workspace Node/Python runtimes:

1. `tools/Extract-EquipmentCatalog.ps1 -Output artifacts/equipment-catalog-current`
2. `node tools/export_flat_equipment.mjs`
3. `python tools/build_flat_equipment.py`
4. Copy the resulting catalog TSV to `assets/equipment-catalog.tsv`.

The generator reads the installed game; it does not modify the game. Its Python
dependencies are in `artifacts/unity-asset-tools`. The provenance report lists
missing texture pages (currently reference lighting variants). Missing native
views are not replaced by an unrelated style: the preview reports unavailable.

Focused verification: `CampaignStudioChecks --flat-equipment <output>` checks
inventory availability without runtime dumps, away aliases, native premade
colours, custom recolouring, and missing-style behavior, and renders both roles
from both sides. `--player-editor <output>` checks save/inheritance and renders
the editor at two sizes. These checks do not prove in-game appearance or export
compatibility.
