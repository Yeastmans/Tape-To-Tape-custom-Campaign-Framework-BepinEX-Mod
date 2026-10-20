# Squad rule extraction

This workflow reads the installed game without launching it or modifying saves,
campaigns, installed plugins, or base assets.

Outputs live in `artifacts/squad-rules/`:

- `SQUAD_RULES.md`: readable per-squad rules, maps, restrictions, roster evidence,
  and implementation sites that depend on a base squad ID.
- `templates.json`: normalized template contracts for the picker implementation.
- `catalog.json`: lossless decoded fields, linked gameplay objects, game English
  squad descriptions, source fingerprints, and unresolved references.
- `code-references.json`: instruction-verified native references to squad IDs.

Run from the repository root, with a Python containing UnityPy or the workspace's
`artifacts/unity-asset-tools` dependency folder:

```powershell
& 'C:/Users/kiran/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe' tools/extract_squad_rules.py
& 'C:/Users/kiran/AppData/Local/Python/pythoncore-3.14-64/python.exe' tools/extract_squad_code_refs.py
& 'C:/Users/kiran/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe' tools/report_squad_rules.py
```

The second Python has Capstone installed. The asset dependency directory may need
read access outside the restricted tool account. No network access is required.
Both extraction steps reject mismatched binary fingerprints. When Steam updates,
regenerate an Il2CppDumper metadata directory and its `source-hashes.json`; update
the code-reference script's metadata path and pass the asset extractor's
`--metadata` option.

Verified on build 25470389:

- Current binary and global metadata match the fresh dump's hashes.
- 19 unique concrete squads; all 57 map-sequence slots resolve to map variants.
- All authored line IDs resolve back to roster entries.
- Double Decker: two lines, ten distinct assigned players, twelve array entries,
  and null entries at indices 10 and 11.
- Trio: left wing and right defense creation locks.
- Six unresolved linked visual helmet objects, explicitly recorded. No root
  squad, starting-team, map, opponent, or directly listed rule-effect decode failed.

The native reference scan verifies RIP-relative MOV/LEA instruction boundaries.
It identifies relevant code sites, not every possible indirect rule and not the
behavior of a custom clone. The report marks placeholder descriptions and unknown
category bits. It must not be described as gameplay validation or a completed
in-game template picker.
