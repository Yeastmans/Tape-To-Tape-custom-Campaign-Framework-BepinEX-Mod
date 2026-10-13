# Campaign Studio Windows installer

Build from the repository root using the supported wrapper:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Build-CampaignStudio.ps1 -GameDirectory 'D:\SteamLibrary\steamapps\common\Tape to Tape'
```

The installer is `build/installer/T2T_Custom_Campaign_Framework_Setup.exe`; its identities and checksums are recorded in `build/installer/build-receipt.json`. See [README_BUILD.md](../README_BUILD.md) for prerequisites, pinned dependency restore, optional tool paths and package validation.

Self-contained Studio installs to the application folder selected during Setup, defaulting to Program Files. The bundled BepInEx Unity IL2CPP Windows x64 loader, mod DLL and MapSeams assets install into the separately selected Tape to Tape folder. Saved content migrates to Local AppData. See [STUDIO_UPGRADE.md](STUDIO_UPGRADE.md) for migration, cleanup and preservation rules.

The loader's version, download source and hashes are pinned in [bepinex-bundle.json](bepinex-bundle.json). Setup replaces its core/runtime as a unit, preserves unrelated plugins and configuration, and clears generated interop/cache files for regeneration by the game.

Building creates a package; it does not install, publish or migrate this computer's content. Package validation does not establish a successful interactive upgrade, game launch or save/Continue result.
