# Windows Studio installation and upgrades

Build with `tools/Build-CampaignStudio.ps1` from the repository root; [README_BUILD.md](../README_BUILD.md) documents prerequisites and options. The wrapper builds the plugin, publishes self-contained Windows Studio and compiles `build/installer/T2T_Custom_Campaign_Framework_Setup.exe`. It does not run Setup, install into the game or publish a release.

## Installation locations

- Application: the selected application folder, defaulting to `%ProgramFiles%\T2T Campaign Studio`.
- User content: `%LOCALAPPDATA%\T2T Campaign Studio`, including campaigns, library, custom heads, drafts, overrides, settings and local game catalogs/artwork.
- Game: the selected Tape to Tape folder receives BepInEx, `CustomCampaignFramework.dll` and MapSeams assets.

Setup has separate application and game folder pages, rejects overlapping application/game/data locations, and validates `Tape to Tape.exe`. The application destination must be empty or an existing Studio installation. Setup does not recursively erase an arbitrary selected application folder.

Run Setup from the Windows account that will play the game, then accept its UAC request. Saved-content migration and the new Studio cleanup command run as that original desktop user, whose Local AppData owns the content.

## Migration and cleanup

Migration copies supported saved content directly into Local AppData and verifies copied files. Existing AppData files win when names collide. It creates no migration archive. Old portable application's executables, runtime libraries, generated base libraries and extracted artwork are not imported as authored content.

After the new Studio is installed, its upgrade cleanup uses the shipped field schema and catalogs. It removes unknown campaign/team/player fields and retired random-value/runtime-copy directives; unsupported values become empty. Modern reward pools, the independent free-agent talent pool, supported squad behaviors and native-copy metadata remain recognized product settings. The current editor Import actions remain available. Maps, assignments, run saves and native LocalLow exports use separate formats and are preserved.

Old creator/content folders are removed only after verified copying and cleanup succeed. Linked paths, missing catalogs or migration/cleanup failures stop that retirement. Setup also replaces the pinned BepInEx core/runtime and clears its generated interop/cache files, allowing the next game launch to regenerate current interop. This first launch can take longer than later launches.

Unrelated plugins, BepInEx configuration and native game saves are preserved. Native exported teams and logos remain in the game's LocalLow save locations. Uninstall retains AppData content and the shared BepInEx runtime.

## Release verification

The build wrapper validates package contents and identities. An interactive upgrade must separately confirm the selected folders, original-user migration, shortcuts, Studio launch and uninstall behavior. A representative game launch and save/Continue flow establishes runtime behavior. Do not treat installer compilation or package checks as confirmation that those user flows have run.
