# Build Campaign Studio Beta 1.0 on Windows

The active entry point is `tools/Build-CampaignStudio.ps1`. It builds the mod,
publishes Studio and compiles the Windows installer. It does not install,
launch the game, migrate user content, create a Git tag or publish a release.
The old `_build_studio_installer.ps1` and `_sync_github.ps1` are not required.

## Prerequisites

- Windows x64 with PowerShell 5.1 or PowerShell 7.
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). Studio targets
  .NET 8; the mod targets .NET 6. NuGet restore must be able to obtain the .NET 6
  targeting/reference packs, or install the [.NET 6 SDK](https://dotnet.microsoft.com/download/dotnet/6.0)
  alongside .NET 8. The player's editor/runtime is included in the installer.
- Python 3 for `tools/build_upgrade_fields.py`, available as `python.exe` or
  `py.exe`, or supplied through `-PythonPath`.
- [Inno Setup 6](https://jrsoftware.org/isinfo.php), available as `ISCC.exe` or
  in a standard Inno Setup 6 installation folder, or supplied with `-IsccPath`.
- [7-Zip](https://www.7-zip.org/) for the pinned FFmpeg archive. Put `7z.exe` on
  PATH, install it to the standard Program Files location, or use `-SevenZipPath`.
- Your own Steam installation of **Tape to Tape, roguelite branch, build
  25899138**, with **current BepInEx-generated IL2CPP interop assemblies** in
  `BepInEx/interop`. Run your game once with the pinned BepInEx loader to
  generate them, then close it. The build wrapper does not install a loader
  into your game or generate interop by launching it.

Game assemblies, metadata, textures, native rosters and generated interop are
not source dependencies to redistribute. The build accepts your game folder
explicitly and checks GameAssembly and metadata against the source's reviewed
compatibility profile. An old `interop_backup` is not a substitute for the
current build's generated interop.

## Restore pinned build dependencies

From the checkout root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Restore-BuildDependencies.ps1
```

The script reads `tools/build-dependencies.json`, downloads only missing
redistributable dependencies, verifies the complete archive before extraction,
then verifies every selected payload file before copying it into the checkout.
Existing matching files are retained. A different existing file causes an
error; the script does not replace or delete it. It refuses linked paths and
destinations outside this checkout.

| Input | Pinned upstream source | Destination |
| --- | --- | --- |
| BepInEx Unity IL2CPP Windows x64 | [6.0.0-be.788+5b766a3](https://builds.bepinex.dev/projects/bepinex_be/788/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788%2B5b766a3.zip) | `installer/bepinex` |
| FFmpeg Windows static full build | [Gyan 8.1.2](https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-8.1.2-full_build.7z) | `tools/ffmpeg/ffmpeg.exe`, `LICENSE`, `README.txt` |

BepInEx archive SHA-256:
`F4CC496BD098A0DF4164B81E3737297707F13A47C2478DBA2F60EEFAB784817A`.
FFmpeg archive SHA-256:
`0FFF188997A499B5382E0F66E845D4556C48C54F0113EBED4853D556DBDD7059`
([upstream checksum](https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-8.1.2-full_build.7z.sha256)).
The packaged FFmpeg executable SHA-256 is
`AD8F211BC894755E0061C55AB280AE00E8D3D4F15A8CC4372B24CFA247B5942E`.

Archives and extraction directories are retained under `build/dependencies`.
To verify existing payloads without downloads or writes:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Restore-BuildDependencies.ps1 -VerifyOnly
```

For offline restore, place the exact pinned archives, using their filenames
from the manifest, in `build/dependencies` before running the script. NuGet
packages also need to be available locally. Preserve upstream licenses and
notices when distributing their binaries. The BepInEx manifest records all
228 payload files; FFmpeg records its executable and two accompanying notices.

## Build

Replace the example game path with your own installation:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Build-CampaignStudio.ps1 -GameDirectory 'D:\SteamLibrary\steamapps\common\Tape to Tape'
```

Optional parameters are `-DotnetPath`, `-PythonPath`, `-IsccPath`,
`-SevenZipPath` and `-StageName`. Tool paths must name executables. The wrapper
passes the supplied game's interop directory to MSBuild; it does not depend
on the historical default path inside the mod project.

The wrapper restores/verifies dependencies, regenerates the upgrade field
schema, builds the mod, updates its compatibility hash files, publishes a
self-contained Windows Studio and checks the shipping package. These checks
reject loose extracted game artwork, native runtime leftovers and user content;
verify the allowed embedded-art inputs, approved brand icon, six approved seams, required runtime
files and pinned FFmpeg payload; and confirm that Studio contains the exact
built mod and compatibility baseline. No test project is required.

| Output | Purpose |
| --- | --- |
| `build/CampaignStudio` | Published Studio application. |
| `build/installer/T2T_Custom_Campaign_Framework_Setup.exe` | Windows installer to upload as a release asset. |
| `build/installer/build-receipt.json` | Game, mod, Studio and installer identity/checksums. |
| `release/CustomCampaignFramework.dll` | Built mod input consumed by Studio and Inno Setup. |

The wrapper cleans only its own marked stage under this checkout's `build`
folder on later builds, and refuses to replace a running Studio stage. An
unmarked existing folder is preserved; use another `-StageName` or move it
yourself. Dependency cache and installer outputs are retained. Source build
outputs also appear under ordinary `src/*/bin` and `src/*/obj` folders.

Package validation is technical evidence. It does not confirm live gameplay,
native trigger timing, successful installer migration or save/Continue behavior.
Exercise the finished installer and a representative gameplay/save flow before
claiming those have been verified.

## Source package contents

A complete checkout needs these six source directories: `CampaignCreator`,
`CampaignCore`, `CustomCampaignFramework`, `CustomHeads`, `GameArt` and
`ModCompatibility`, all under `src`. Keep their project-local `Assets` and
third-party notices. Also retain:

- `assets` metadata, reward pools, game-art geometry maps and Studio guide images;
- `assets/reward-pools/reward_catalog.tsv`, the editor's reward catalog;
- `installer/T2T_CampaignFramework.iss`, `Migrate-UserContent.ps1`,
  `Retire-LegacyInstall.ps1` and `bepinex-bundle.json`;
- `tools/Build-CampaignStudio.ps1`, `Restore-BuildDependencies.ps1`,
  `build-dependencies.json` and `build_upgrade_fields.py`;
- the project license and public documentation.

The dependency bootstrap recreates the BepInEx and FFmpeg binary trees.
Build outputs, game files, interop, dependency binaries and installer EXEs do
not belong in Git. Publish the installer as a release asset. A source checkout
cannot be built from the old three-directory plugin-only mirror or the legacy
solution file; use the wrapper above.
