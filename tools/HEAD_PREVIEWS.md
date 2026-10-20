# Editor head and helmet previews

`assets/head-preview/catalog.tsv` and its PNGs are copied into the editor output
and publish directories. No game launch or runtime preview cache is needed.
The catalog includes 223 skater head skins and 18 goalie helmet skins, retaining
full native identities. Goalies use `Helmet Skin`, never the skater `Face` field.
Internal `_iso` reference skins are excluded from equipment choices.

The legacy DLL preview exporter and editor's Game preview button have been
removed. The editor no longer reads `_preview_assets`; bundled head previews
and flattened equipment remain available without running the game.
`MatchRuleRunner` retains the soccer/offside scene polling independently.

To regenerate with the existing workspace UnityPy and Spine dependencies:

1. Refresh `artifacts/equipment-catalog/source` with `Extract-EquipmentCatalog.ps1`.
2. Run `export_head_poses.mjs` with the workspace Node runtime.
3. Run `render_head_artwork.py` with the workspace Python runtime.

The scripts use native front-facing Spine geometry and atlas textures. These
are static style previews; custom shader colour masks do not show authored tints.
Extraction records unavailable textures explicitly rather than substituting a face.

Focused rendered verification: run CampaignStudioChecks with
`--head-previews artifacts/head-preview-checks`. It checks bundled image decoding,
live selection updates, saved identities, separate goalie controls, and roster images.
