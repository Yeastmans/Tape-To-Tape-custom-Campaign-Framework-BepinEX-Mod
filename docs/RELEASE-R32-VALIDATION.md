# Revision 32: mirror rosters and Steam build 26289478

The release uses the reviewed Windows candidate on top of R31. Unfinished Linux/Proton, cover editing and unrelated reward work are excluded.

## Verification

Passing checks: 238 opponent/mirror, 526 goalie lifecycle, 232 compiled starting-flow, 67 Harmony/API, 28 cache/interop, 3 logo dependency, 30 independent interop identity, 57 native-frame and 139 compatibility checks. The fresh Studio payload has 107 package checks. The installer is extracted and every included file is compared against the staged Studio, pinned loader and approved game payload.

Native review and regenerated profiles preserve all 1,485 previous method contracts and add two dependencies (1,487 total), with 137 comparisons, 109 query calls, 54 named hooks and 21 raw hooks. See NATIVE-26289478-REVIEW.md.

## Live observations

A disposable two-line campaign contained the same ten Mirror Forward names on both sides, with 80-rated home skaters, 35-rated opponents, a named Mirror Keeper, and twelve POOL ONLY sentinel recruits. The actual opponent lineup displayed all ten authored players and their 35 stats with no pool replacements. On-ice play was observed. Native diagnostics recorded valid goalie initialization, play, and a new goalie receiver at the period-side handoff followed by play again, without invalidating the owned participant. The user reported: "seems to work".

Computer Use was then stopped by the user. No complete-match, goalie expiry or saved-run replay claim is made for this fixture. Once the game was closed, all 1,852 original native files, original active campaign and original mod/profile were restored and hash checked. Changed and newly generated test data were archived.

The original reporter's campaign remains unavailable. Source-linked coverage includes both one- and two-line opponents, empty/nonempty recruitment pools, copied native rosters and default-mode passthrough. Runtime and package diagnostics are distinct from the limited live observations above. Installer extraction does not claim an elevated installer execution.
