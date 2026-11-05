# R42 reward performance validation

R42 targets the custom reward lookups seen in skating and stat calculation profiles from Yeastman's Wacky Crazy Funtime. It uses R41 commit `2b653b032fe75a7e602642bdc39e4ecc39902671` as its source baseline. The Windows release tag is `beta-1.0-r42-build-1793869552639`.

## Lookup work

A repeatable fixture ran 192 stat queries against 12 skaters with 32 unrelated team rewards. R41 made 73,728 owner power checks, 86,208 native team reads and 6,144 live condition checks. R42 made zero of each in that fixture. A Speed query with three eligible owners still evaluated all three. In another fixture, 96 unrelated player, opponent and match signals made zero owner power checks, native team reads or live condition checks.

The team and saved-player tests warmed the lookup with two team entries and one player entry. A further 1,000 iterations read zero additional native loadout elements. Same-size replacements, opponent rotation, acquisition, payoff suppression and out-of-match previews were covered.

These measurements count work done by the reward runtime. Post-release match frame rates for Mystery Inc., Billionaires and YouTubers still require a live comparison.

## Checks on the release sources

Eleven suites passed against the R41-based release projection. The native hook suite inspected the compiled candidate against the generated game wrappers.

| Suite | Checks passed |
| --- | ---: |
| Reward condition queries | 31 |
| Native reward lookups | 28 |
| Stat refresh and activation | 23 |
| Custom reward rules and progress | 284 |
| Charged release | 27 |
| Puck Repellent | 19 |
| Custom coach persistence | 23 |
| Goalie roster runtime | 526 |
| Goalie roster menus | 255 |
| Base game pool run rewards | 22 |
| Compiled native hook targets | 13 |
| **Total** | **1251** |

## Build and installation

The runtime build recorded 297 inputs and verified their hashes after compilation. The compiled runtime SHA-256 is `022B6ACC69B12ABEE26A6EAA06F8B60A53ACF4580A9412466B3742FFF7B57908`. Steam build 26372689 passed the compatibility preflight. All 143 static patch signatures matched the installed game's metadata.

The Windows installer includes the same DLL in the Studio payload and the game plugin directory. Extraction checks compare the packaged files with their build receipts. The upgrade list recognizes the official R41 runtime and the interim local performance build.

All 7,408 method bodies and all embedded resources in the rebuilt Studio, CampaignCore, GameArt and ModCompatibility assemblies matched the R41 package. The source commit recorded in their assembly information changed for the new build.
