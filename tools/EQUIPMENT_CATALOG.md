# Extracting equipment options

Run from PowerShell:

```powershell
.\tools\Extract-EquipmentCatalog.ps1
```

The game does not need to be running. This reads `Tape to Tape_Data/data.unity3d`
without modifying the installation, saves the source skeletons and atlases, then
decodes actual Spine skin records with the existing workspace Spine runtime.
It requires the existing Python 3.12 UnityPy libraries in
`artifacts/unity-asset-tools` and `artifacts/spine-runtime/package`.
The wrapper accepts `-GameData`, `-Output`, `-Python`, and `-Node` overrides.

Outputs under `artifacts/equipment-catalog`:

- `SKATERS.md`: skater sticks, gloves, skates, helmets, heads, jerseys, derived
  socks, and the remaining equipment categories.
- `GOALIES.md`: separate goalie equipment lists with missing source views flagged.
- `skaters.json` / `goalies.json`: separately consumable catalogs.
- `CATALOG.md`: counts, all options, coverage and comparison against the installed
  mod's `_game_skins.txt`.
- `catalog.json` / `catalog.tsv`: role, category, exact full skin identity,
  corresponding mod field, source skeletons and attachment slots.
- `skeletons.json`: every decoded skeleton plus explicit decoding errors.
- `source/index.json`: bundle hash and extracted source identities/hashes.

The old dump records values worn by loaded ForwardData/GoaltenderData objects.
That is an observed sample, not a complete asset inventory. This extraction does
not depend on those objects or unlocks. It uses the observed dump only for the
comparison, never to define the options. For a different dump, call the Python
entry point with `--observed <file>`.

Socks are the `Leg left`/`Leg right` artwork inside skater Body skins, with a
separate colour channel. There is no independent sock skin field in the current
adapter. Goalie heads/skates/socks are not offered as invented independent skin
fields. Skater `Faces/.../Helmet_*` equipment stays separate from character heads;
character `_Helmet` variants remain distinct. Exact path and case are retained,
including duplicate leaf names and reference/development assets.

Both goalie skeletons are inventoried. A union alone can conceal that an asset
exists in only one view, so each entry retains sources and missing sources.
Presence in the game bundle does not prove unlockability or gameplay/export
compatibility. Read extraction failures in the report before claiming coverage
on a new game build. The current older cloud-map skeleton cannot be parsed by
the available 4.3 runtime; all three equipment skeletons decode successfully.

This produces an inventory; it does not overwrite the runtime dump, alter editor
dropdowns, or install a new mod DLL. Existing runtime files must not overwrite
this fuller catalog. Keep future integration role-aware and preserve view support.
