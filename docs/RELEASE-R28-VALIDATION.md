# Revision 28 uninstall validation

Revision 28 automatically removes BepInEx when Campaign Studio is uninstalled. It removes the complete `BepInEx` and `dotnet` directories and the bundled root loader files. Saved Studio content in Local AppData and Tape to Tape's game files remain outside the deletion set.

Existing users must run the revision 28 installer once before uninstalling. The older installed uninstaller cannot acquire this behavior without that update. Removal applies to other mods and configuration inside the game's BepInEx folder too; both Studio and Windows uninstall confirmations explain this.

## Verified

- Native Inno Setup installation and uninstall in disposable directories: fresh install, upgrade from the old `uninsneveruninstall` package, missing loader files, a nested junction, junctions replacing installed directories, loader junctions with OS RedirectionGuard disabled, a game already running, and a game launched after initial uninstall validation. All eight scenarios passed; protected game and AppData files retained their hashes.
- The regression test failed against the old installer because BepInEx remained. The late-start and loader-junction cases also failed before their respective fixes.
- Eight existing Studio Home uninstall checks passed, including unsaved-edit blocking, confirmation, Cancel and saved-content messaging. Existing installer migration checks passed.
- The Studio build completed with zero errors and zero warnings. The release reuses those unchanged, tested Studio assemblies; revision/build identity is supplied by `studio-build.json`.
- Compared 299 tracked Studio source/resource inputs with public main, checked for additional untracked build source files, and applied only the intended uninstall changes. The source runtime checksum is aligned with the unchanged published revision 27 DLL (`7E69782C0D63D4EBA661FCC719CC1B8AFFE64ADC97B141A0F31F312762413AC2`).
- Independent review found no remaining blocker introduced by the final change. Whole-tree loader removal is owned by native `UninstallDelete` records, preserving the original per-file loader retention flags to avoid traversing replaced DLL directories before recursive cleanup.

Release gates include a complete production installer compile, extraction against the exact staged file manifest and pinned loader hashes, verification of both runtime destinations, and re-downloading every public release asset for SHA-256 comparison. Local receipts are under `artifacts/release-r28-uninstall-bepinex-20261025`; behavior-test evidence is under `artifacts/uninstall-bepinex-20261025`.

## User check

1. Save edits and close Studio and Tape to Tape.
2. Run revision 28 Setup, selecting the existing Studio and game folders.
3. Uninstall Studio from Windows Settings or Studio's Uninstall button. Confirm the stated removal of BepInEx and other mods in that folder.
4. Check that `BepInEx`, `dotnet`, `winhttp.dll`, `doorstop_config.ini` and `.doorstop_version` are gone from the selected game directory. Tape to Tape's own files and Studio's Local AppData content should remain.

The real installed application/game was not uninstalled during automated validation. Live elevated removal and subsequent game launch are not claimed as observed. Gameplay runtime behavior is unchanged from revision 27; its prior gameplay-validation limits still apply.
