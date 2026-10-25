# CompatibilityTool

Release maintenance CLI for the reviewed Windows x64 Tape to Tape native compatibility contract. Requires the .NET 8 SDK and the developer's installed game. It reads game files and writes only the output paths supplied below; it does not install a plugin or compatibility profile into the game.

Run commands from the repository root. Paths containing spaces must be quoted. PowerShell examples use single quotes for native method identities because they contain literal `$` characters.

## Verify an existing reviewed baseline

```powershell
dotnet run --project .\tools\CompatibilityTool -c Release -- verify 'D:\SteamLibrary\steamapps\common\Tape to Tape' '.\src\ModCompatibility\Assets\native-baseline.json' '26188487' '.\build\compatibility\verified-profile.json'
```

The input baseline is integrity-checked. Verification rejects changed protected behavior, unsupported patch contracts, missing or ambiguous methods and unexpected squad query coverage. Success writes a profile pinned to the inspected game and metadata hashes. This is compatibility validation, not gameplay confirmation. A failure requires native-code review; do not bypass it by replacing hashes.

## Create a reviewed baseline

Create a baseline only after reviewing the new game's native contracts and the squad comparison-site manifest. Supply the exact current flow RVA and stable method identity; old addresses must not be reused without review.

```text
CompatibilityTool baseline <game folder> <squad TSV> <Steam build> <flow RVA hex> <flow method identity> <baseline output> <runtime profile output>
```

The checked-in inputs for the current reviewed build are:

- Game build: `26188487`, public branch (runtime contract `t2t-native-v3-20261024`).
- Squad manifest: `src/CustomCampaignFramework/Assets/squad-behavior-sites.tsv`.
- Flow RVA: `4406307`; original bytes `400FB6C60A4758`.
- Flow identity: `Tape2Tape.Hockey.UI.TitleScreen.<<NewRun>g__StartNewRun|#>d$$MoveNext`.
- Baseline destination: `src/ModCompatibility/Assets/native-baseline.json`.
- Runtime profile destination: `src/CustomCampaignFramework/Assets/native-compatibility.json`.

Use separate staging output paths first, review both generated files, then replace the two source files together. The CLI creates missing output directories and overwrites the named output files. It rejects output paths that duplicate one another or an input file. It does not provide a transaction across two output files; retain the previous reviewed pair if writing either output fails.

## Help and failures

`dotnet run --project .\tools\CompatibilityTool -c Release -- --help` lists both commands. Unknown commands and wrong argument counts exit with code 2. Invalid inputs, native compatibility failures or output errors exit with code 1 and a concise error message. Success exits with code 0. No diagnostic-report command or gameplay fixtures are included.

The shipping build wrapper is documented in [README_BUILD.md](../../README_BUILD.md). Baseline generation is a reviewed maintenance operation, not part of automatic end-user compatibility updates.

## Integrated four-feature inventory

Assets/four-feature-contracts.json in ModCompatibility is embedded as FourFeatureContracts.json. Baseline generation includes its exact 54 source-owned named hook contracts, 21 jersey raw contracts and protected native callable groups. Verification preserves both contract sets and rejects missing, extra or altered contracts. The raw adapters have reviewed absolute RVAs and closed generic/unnamed identities, so this release requires the exact reviewed 26188487 GameAssembly and metadata; it does not infer relocation from an address-only update. Generated interop files are build provenance, while runtime structural preflight and the existing component-specific provider gates remain authoritative.

The reviewed public-build pair contains 1,485 protected native methods, 137 comparison sites and 109 query calls. It retains all 1,483 methods from the actual R26 baseline and adds the native run-setting persistence helper and callback now called by the fresh-run flow. Protected methods include dependencies and overloads; they are not an installed hook count. The v3 contract requires the reviewed RDI/SIL/R15 starting-flow register ABI and rejects the former R14B contract. See [the native review](../../docs/NATIVE-26188487-REVIEW.md). Generate and verify into separate staging paths before promoting the pair. Keep prior release receipts unchanged.
