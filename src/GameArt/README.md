# Artwork from the player's game

Campaign Studio reads heads, equipment, logos, map artwork, head templates and native roster records from the player's installed copy of Tape to Tape. The game can remain closed. Home > **Read game artwork** rebuilds the local cache; Studio offers extraction when the cache is missing or outdated and asks for the game folder if automatic discovery fails. Players need no Python, Node or Spine runtime.

The current extractor targets Steam build **25899138** and uses cache format **5**. The cache lives under `%LOCALAPPDATA%\T2T Campaign Studio\game-art\<fingerprint>`. It is user data and is excluded from releases. `T2T_GAME_ART_CACHE` can override that location for a development installation.

The fingerprint includes extractor format, game binary/metadata and bundle file sizes and timestamps, extractor identities, and embedded mapping hashes. Complete caches are reused. Rebuilds are staged; cancellation or a fatal failure preserves the existing cache. A failed individual extractor is recorded without exposing its partial output. The editor remains usable when previews or native imports are unavailable.

Native head templates report unavailable regions per head. Map-node extraction locates the atlas by its complete attachment-region catalog, avoiding a dependency on a numbered `sharedassets` filename. Missing or ambiguous sources produce an extraction error.

Native imports retain the supported source appearance fields, including number style. Empty native skater helmet fields become explicit No helmet; a newly authored custom player's default remains separate. Copied native opponents retain source reserves outside the five or ten editable skater slots.

## Source and packaging

`assets/game-art-maps` contains five geometry/name mapping files embedded into GameArt.dll: `flat-regions.json`, `head-poses.json`, `head-templates.json`, `mathieu-head-assets.json` and `node-meshes.json`. They contain coordinates, names and topology, not game pixels. If a game update changes its skeletons, asset names or atlas packing, review and update the mappings against that installed build. Repeating extraction with stale geometry cannot repair a changed native source. Increment `ArtCache.FormatVersion` when extractor output changes.

The project targets .NET 8. Its pinned Unity readers and image library are declared in `GameArt.csproj`; licenses are documented in `THIRD_PARTY_NOTICES.txt`. The six approved map seam images and Studio guide illustrations are packaged product assets. Extracted game images, caches and native source files are not packaged.

Use the supported repository build entry point described in [README_BUILD.md](../../README_BUILD.md):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Build-CampaignStudio.ps1 -GameDirectory 'D:\SteamLibrary\steamapps\common\Tape to Tape'
```

That wrapper validates the published package's allowed artwork and embedded mappings. It does not run the game or establish a gameplay result.

## Manual verification

Open Studio without a cache and choose Later: editing should remain available with missing previews. Run Home > Read game artwork, then inspect a player, equipment choices, native imports, map preview and Head Editor template. Close and reopen Studio to confirm cache reuse. Canceling a missing-game folder picker should leave editing available. A successful build or extraction does not establish in-game appearance, native helmet drops or installer migration behavior.
