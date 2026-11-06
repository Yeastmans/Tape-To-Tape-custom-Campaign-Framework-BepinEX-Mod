# R44 compatibility portability validation

Release `beta-1.0-r44-build-1793926964328` builds on R43 commit `2540cdb6c4697f4dc61788ad4936734160fdf3b7` and targets Tape to Tape Steam build 26372689.

## Reproduced failure and correction

R43 could reject the same supported game files with a squad-data query inventory mismatch because the inventory used the current text comparer. Windows NLS and ICU can put those strings in a different order. Regional formatting could also change a floating-point default value in a protected method signature. R44 uses ordinal ordering and invariant number formatting, then rebuilds the protected baseline and runtime profile as one pair.

The regression checks exercise the actual compatibility verifier against copied native game files. The focused evidence includes the original rejection, successful verification under different cultures and globalization engines after the correction, and rejection of changed protected code. These are compatibility and package checks; no live gameplay or elevated installation is claimed.

## Focused checks

| Suite | Checks passed |
| --- | ---: |
| ModCompatibilityChecks-ICU | 209 |
| ModCompatibilityChecks-NLS | 209 |
| CompatibilityUpdaterRegeneration-NLS-fr-FR | 32 |
| BaselineGeneration-ICU | 24 |
| BaselineGeneration-NLS | 24 |
| **Total** | **498** |

## Windows package

The full Windows installer includes a fresh Studio build and the paired runtime. Verification extracts the installer and compares every file, both runtime destinations, all six MapSeams PNGs per destination, the native baseline, runtime hash sidecar and existing upgrade identities. Publication verifies the source commit and tag, all three uploaded asset digests, and anonymous downloads.

| Item | SHA-256 or ID |
| --- | --- |
| Runtime | `12C9CFA29E41ED0ECEFF60BE00D254524B84F9AB2B89A1D6027F29D2DB7E7BA2` |
| Embedded and external profile | `4E323F89A8392F0471B843F1A5D88E3C2F63039B766B19B4A829D27123E4AD8A` |
| Native baseline file | `1A6EBE8E1A3383E24DACBD339E3CC41CFA0F74F854D3F3940A5FEAAB7CE08F5E` |
| Native baseline ID | `583561E7D038E69651C2FD4F9988BDA1B543B53DE1B09082A0466E8E099A3E5A` |

## Player check

Close Tape to Tape, install this release, reopen Studio and run the compatibility check against the same game folder. The supported unmodified Steam build should pass. Then start the game and check that the custom campaign menu and your usual campaign load. Live installation and gameplay acceptance remain pending a player report.
