# Native compatibility review for Steam build 26471720

The starting point was published Beta 1.0 revision 44 on Steam build 26372689. Its compatibility verifier passed against the preserved old game and rejected the public 26471720 files.

| Identity | SHA-256 or value |
| --- | --- |
| GameAssembly.dll | `0709D469AE6C47B695BBA71664BF5619982EE6C3E17EAEF6DC8B98FDA0A61CEC` |
| global-metadata.dat | `50ABD75E31FBBD6DD3AB098507B40B9F01EA100B564F593A62EFEE8FD5AD44FC` |
| Reviewed baseline ID | `465ABA3E9CD0DF0CA18462346A23FC90CB580F5332B492DACC9936074856D577` |
| Runtime contract | `t2t-native-v3-20261024` |

## Protected methods

The comparison started with the 1,520 contracts in the published R44 baseline. It resolved each through IL2CPP metadata in both binaries, recording the signature, code extent, aliases, instructions, canonical hash and switch table. Of those contracts, 1,104 retain the R44 canonical hash. Disassembly of all 1,520, including the methods whose canonical hashes match, found 1,415 with matching instruction sequences after translating addresses into branch destinations, symbol or literal identities, and anonymous native call positions. The other 105 have retained instruction diffs in the review bundle. All 15 protected entries with embedded switch tables retain their destinations and data. The release verifier continues to require the exact reviewed native pair and native method contracts.

The candidate contains 1,521 entries. Two existing entries changed identity. The three-argument `State.DeserializerV2` constructor replaces the four-argument constructor, and the generated `<HasRefBlockShotTalent>b__187_0` predicate replaces `b__177_0`. The predicate still has a single native alias, the same `Boolean(Forward)` signature and 34 matching decoded instructions. The additional entry is `Skaters.ForwardStats.GetStatAfterBalancedInjuryCheck(Int32)`, a new direct consumer of the native injury-penalty exemption. The original injury consumer remains protected.

## Squad comparisons and fresh runs

The new executable contains the same multiset of 109 direct calls and tail calls to `CampaignUtil.IsPlayingSquad`, with the same owner and literal at every site. All 28 inline string comparisons passed the production validator for the expected literal, register, call overload, local branches, incoming edges and owner stack frame. Twenty-six use RCX on the left and two use RBX. The retired addresses fail the new binary check at all 137 sites.

Five instruction indices move inside their owners.

| Native owner | R44 index | 26471720 index |
| --- | ---: | ---: |
| BlademasterMapNode.CreateNodeReward | 151 | 170 |
| CoachEvent.Initialize | 168 | 152 |
| CoachEvent.Initialize | 185 | 169 |
| CampaignState.InstantiateStartingTeam | 531 | 485 |
| CampaignState.InstantiateStartingTeam | 1017 | 1040 |

For 108 of the 109 direct sites the six instructions preceding the call also match after relocation. The Coach speedrun site loads the same CampaignUtil singleton through its type's static fields using a different scratch register. RCX still holds the checked receiver, RDX the `speedrun` literal, R8B the value 1 and R9 the null MethodInfo. All 28 inline sites retain their 15-instruction comparison sequence.

The fresh-run site is instruction **915**, RVA **`0x448DF57`**, in `Tape2Tape.Hockey.UI.TitleScreen.<<NewRun>g__StartNewRun|#>d$$MoveNext`. The surrounding method starts at `0x448CE70`. It loads the async state into RDI and TitleScreen from its field at `[RDI+0x10]` into R15. At the hook, the two displaced instructions still copy SIL into EAX and OR AL with the old-school flag at `[RDI+0x58]`. Their bytes are `400FB6C60A4758` and the original conditional branch resumes at `0x448DF5E`. The adjacent flags at 0x59 and 0x5A still mean skip benchwarmer menu and has follow-up menu. All 14 fields in the declared state and all 11 state-machine jump targets retain their layout and destination. Across the 1,839-instruction method, nine references to the relocated IL2CPP write-barrier bitmap account for the remaining normalized operands. The existing Win64 emitter still matches this ABI. The rink surface hook moved to `0x41FC930` and retains its 18-byte prologue.

## Changes in native gameplay code

- The native save loader no longer injects `ILogoRepository` into `DeserializerV2`. Its constructor and native load coroutine retain the talent, relic and ability repositories in that order. The native team-model deserializer removes its former logo lookup by object name. The managed save bridge and preflight now use the three dependencies that the game supplies.
- The updated native AI introduces elite breakout, elite defense and reversed-net decisions. These paths affect state selection, skating targets, and rink-side switching. `Forward` adds `HookPushedBy`, the goalie adds facing/rig fields, and the team adds a solid-elite cache. Metadata and disassembly identify moved fields, including the forward's team data at `0x400`, goalie data at `0x288` and team relic controllers at `0x1C8`.
- Native starting-team construction now prepares a relic list. The two inline squad comparisons within that method retain their literals, registers and return paths. Blademaster reward generation now uses abilities already offered by other Blademasters as exclusions. Coach initialization also changes how it prepares offers. The original native owners continue to execute through the relocated squad sites.
- The ordinary and balanced injury calculations now both call the native SadoMaso exemption wrapper. The custom reward port covers that wrapper and preserves the publisher's stat calculation and control flow. The dedicated reward review records the individual entries, prologues, callers and charged-shot checks. [Custom reward native review](CUSTOM-REWARD-NATIVE-26471720.md)

## Goalie and jersey adapters

All 54 named managed hooks retain unique matching signatures. The 21 raw jersey callbacks span 7,229 decoded instructions. Their direct comparison also covers 41 anonymous dependencies, one renamed delegate closure and three shared Dictionary implementations. The previous aliases still exist at those shared Dictionary targets. The reviewed raw inventory retains 20 single-alias entries and one unnamed helper. Five independent MoveNext references identify that helper at `0x9D5950`.

Eight state machines, two builders, the closed generic runner and a closure retain their complete field layouts. The exact generated coroutine identities change for Forward.InitializeSkin (158 to 159), Goaltender.InitializeSkin (91 to 102) and PenaltyBoxSystem.ShowBoxSeat (39 to 40). Two Forward fields and five goalie fields that moved resolve to the same declarations. Three fixed nested-owner offsets keep their values.

Independent interop generation under the loader's .NET 6.0.7 runtime matched all 221 installed DLLs outside their module MVID. The reviewed fingerprints are `F46652CBC6BBFAB80D67D2ADE3F77A5FC926F1BAEAC80B95754C35D92B162B7E` for MainScriptsAssembly and `FE1AE612186B9863CF8009385E6751C6A431D1445DF865A40DFBAA360C2842EC` for spine-csharp.

## Reproduction and evidence

The release operation uses the checked-in squad TSV, Steam build 26471720, flow RVA `448DF57` and the full flow method identity above. Independent `CompatibilityTool verify` produced the same runtime profile as baseline creation. The reviewed pair must travel together with the runtime built from these contracts.

The staged pair passed 211 compatibility checks including culture invariance, rejection of the R44 profile on the changed game, and mutation checks. All 57 inline frame checks passed, including damaged unwind records and incoming branches into displaced bytes. The compiled runtime passed 232 Win64 fresh-run emitter checks covering callback arguments, shadow space, volatile register and vector restoration, branch flags and resumption.

The local case `artifacts/r44-public-steam-launch-20261107-124951` under the workspace contains the immutable R44 verifier inputs, old/new disassembly and method inventories, field declarations, register and switch receipts under `flow-review`, and dedicated `raw-review` and `reward-review` evidence. Static review verifies the patch locations, data layout and ABI. A live game launch and testing of specific campaign, save, match and roster behavior provide separate evidence.

## Windows Steam launch on November 7, 2026

Launching published R44 on build 26471720 regenerated 221 interop assemblies. Harmony looked for the previous referee predicate and aborted `CustomCampaignFramework` during `PatchAll`. The local game process stayed open. The affected player's own failure log was unavailable.

The repaired runtime has SHA-256 `2FC501B573A066845C15893CE1E23C9C1161A74CD1C5EADC838A530C6B4E72B1`. The paired Studio updated the game plugin and reported `Current` on a second check. Steam then launched the game twice, first with existing wrappers and again with the interop and cache folders removed from their active paths. The second launch regenerated all 221 assemblies. Both sessions logged custom squad registration in `TitleScreen.RefreshCampaignData`, all 40 goalie targets, and successful chainloader startup. Both game windows responded and both normal shutdowns returned exit code 0. The BepInEx logs contain zero error or fatal entries. Harmony's Unity type-enumeration warnings also appear in the previous supported build.

The active native run remained byte-identical. Tape to Tape migrated its statistics from version 1 to version 2 and preserved all 1,562 entity records and their values. Match play and loading a saved run through the Continue button remain untested.

## Revision 45 Windows installer

Revision 45 ships the runtime used in both Steam launches. The Windows package passed 107 checks. Extraction found 483 entries. Both copies of the runtime, all 244 Studio files and the loader payload matched the staged files. In a disposable copy of the current game files, Studio upgraded the R44 plugin, backed up the original DLL and reported `Current` on the second compatibility check.

The installer SHA-256 is `6CE9AF4E3E753B4D96A74767786C956BF1C51E8E7F912A3D9E9EE6C61F0CBDE7`. [GitHub release](https://github.com/Yeastmans/Tape-To-Tape-custom-Campaign-Framework-BepinEX-Mod/releases/tag/beta-1.0-r45-build-1794083660740)
