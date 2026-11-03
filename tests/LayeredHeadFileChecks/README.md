# Layered head file checks

This isolated .NET 8 executable links only `LayeredPsd.cs` and SkiaSharp 2.88.9.
It creates eight PSD fixtures and checks input validation, source-pixel preservation,
compact output, safe replacement, and cleanup after a locked destination fails.
It does not build or launch Campaign Studio or the game.

`verify_psd.py` opens those fixtures with the independent `psd-tools` reader,
checks exact editable RGBA and Unicode names, recomposites visible layers, and
saves/reopens a layered document. Cached previews allow two color levels of
rounding from PSD's 8-bit white-matted RGB convention; layer pixels are exact.
Independent re-composition allows one level because psd-tools truncates floats.

Use a task-local directory outside the checkout for dependencies and fixtures:

```powershell
$psdChecks = 'F:\Projects\tape to tape mods\artifacts\head-blank-templates-20261103\psd-checks'
$psdPython = 'C:\Users\kiran\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'
& $psdPython -m pip install --target "$psdChecks\python-deps" --only-binary=:all: 'psd-tools[composite]==1.26.0'
& 'C:\Program Files\dotnet\dotnet.exe' run --project tests/LayeredHeadFileChecks -- "$psdChecks\fixtures"
& $psdPython tests/LayeredHeadFileChecks/verify_psd.py "$psdChecks\fixtures" "$psdChecks\python-deps"
```

The checks are silent on success and write `dotnet-results.json` and
`psd-tools-results.json` in the fixture directory. A nonzero exit code indicates
failure. The independent package is a development check only; it is not required
by the application. No Photoshop, Krita, or GIMP UI acceptance is claimed by these
file-format checks.

Format reference: [Adobe Photoshop file format specification](https://www.adobe.com/devnet-apps/photoshop/fileformatashtml/).
Reader: [psd-tools documentation](https://psd-tools.readthedocs.io/en/latest/).
