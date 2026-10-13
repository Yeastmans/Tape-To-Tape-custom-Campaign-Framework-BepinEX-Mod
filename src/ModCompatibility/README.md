# Automatic mod compatibility

Campaign Studio checks the installed game at startup and periodically while open. Home > **Update mod compatibility** requests another check. Compatibility checks run before artwork extraction because both readers use LibCpp2IL's process-wide state.

The Windows package includes the plugin, its SHA-256, a reviewed native baseline and an explicit list of older plugin hashes it may replace. Players need no SDK or Python. Unknown plugin builds are preserved rather than downgraded.

For a changed game, the updater resolves native methods from current IL2CPP metadata and compares normalized instructions, signatures, squad-query coverage, original patch bytes and supported hook ABI against the reviewed baseline. Address relocation and compiler-generated ordinal changes are allowed. Changed logic or missing/ambiguous methods require a developer review and repair.

Successful verification produces `BepInEx/plugins/CustomCampaignFramework.compatibility.json`, pinned to the full game and metadata hashes. The plugin also validates comparison coverage and original in-memory bytes when installing hooks. The updater waits while the game is running, detects concurrent changes, stages replacements and preserves replaced compatibility/plugin files under `BepInEx/compatibility-backups`. Repeated successful checks make no changes. This supports compatible updates; it does not automatically repair arbitrary native API or behavior changes.

A version mismatch alone does not block custom squad creation or selection. Custom rosters and managed menu support remain available while unverified address-based hooks stay disabled. Native behavior overrides that depend on those hooks may be incomplete until compatibility is restored. Invalid squad settings and actual load failures are still reported.

## Reviewed baseline

`Assets/native-baseline.json` currently describes Steam build **25917988**, with **262 protected methods, 142 comparison sites and 110 native squad query calls**. The matching runtime profile is `../CustomCampaignFramework/Assets/native-compatibility.json`. Protected contracts include squad routing, lineup previews, reward selection, authored periods, opponent consumers, map presentation and appearance integration. Their presence is technical compatibility evidence, not a live gameplay pass.

## Build maintenance

Only after reviewing a new game build's native contracts, use the [CompatibilityTool](../../tools/CompatibilityTool/README.md) to generate a baseline and matching profile. Its two commands are `baseline` and `verify`. Verification compares an existing reviewed baseline with the installed game and writes an explicitly requested profile; neither command installs files into the game. Never approve a build merely by replacing its hash.

The supported packaging entry point is `tools/Build-CampaignStudio.ps1`, documented in [README_BUILD.md](../../README_BUILD.md). It builds the plugin, stages `release/CustomCampaignFramework.dll`, updates the packaged runtime hash files, publishes Studio and validates the matching package. Keep `Assets/replaces-sha256.txt` limited to explicitly supported older plugins. A regenerated native baseline and embedded runtime profile must be reviewed and committed together before packaging.

After a game update, generate current BepInEx interop assemblies from the installed build before compiling the plugin. An old interop backup is not evidence of current compatibility. Release verification and a representative install/save/gameplay check remain distinct.
