# Custom reward native adapters on Steam build 26471720

R44 fails during Harmony startup on this Windows x64 build. Its compiled live-query iterator asks for `Team.__c._HasRefBlockShotTalent_b__177_0`, which resolves to zero generated methods. The public game emits `_HasRefBlockShotTalent_b__187_0(Forward)`. Its native body at `0x42B8CB0` retains the power-up gate and the RefBlockShotTalent query. The other 12 R44 targets still resolve uniquely.

The reviewed GameAssembly SHA-256 is `0709D469AE6C47B695BBA71664BF5619982EE6C3E17EAEF6DC8B98FDA0A61CEC`. The global metadata SHA-256 is `50ABD75E31FBBD6DD3AB098507B40B9F01EA100B564F593A62EFEE8FD5AD44FC`.

## Native entries

Metadata and decoded instructions identify all 34 previous entry points in the new image. Each moved. Across 1,841 instructions, the native argument and result registers, branch destinations, field accesses, relative calls and referenced metadata identities match after relocation. Fifteen immediate anonymous dependencies with changed machine bytes were compared through 678 instructions, including their imported functions, constants and a 16-entry switch table.

| Native method | Public RVA |
| --- | --- |
| Skater.GetTalent, GetTalentController | `E98540`, `E985C0` |
| Skater.HaveTalent, HaveTalentCached | `E987F0`, `E98850` |
| SkaterData.GetTalent, HaveTalent, HaveTalentCached | `E98910`, `E989B0`, `E98AD0` |
| Talent.Get from a talent list | `EC2E40` |
| Team.HaveRelicCached | `EE13F0` |
| TeamData.HaveRelic, HaveRelicCached | `EE16C0`, `EE17E0` |
| GameStats add-goal and cancel-stats helpers | `40BFDB0`, `40BF9A0` |
| GoalScoredArg constructor | `3CFC410` |
| SlapshotState.ReleaseShot | `3C55450` |

The constructor keeps its scorer, goal type, duration, game mode, shot type and three boolean arguments. OnGoalScored retains the native FirstBlood and Hidden Ace order. Its batch-depth increment follows the existing `StatCollector._unlockCheckBatchDepth` field from offset `38` to `40` hex. OnDeniedGoal and the full native helpers retain their accounting sequence.

The other 19 R44 entries are optimized one-argument Skater wrappers. Each wrapper's RIP-relative metadata load identifies its exact talent and query. All 19 require new 24-byte prologues. The optimizer now also emits `HaveTalentCached<SadoMaso>` at `0x64AB20`. Its native original checks the skater's power-up gate, obtains the skater data and tail-calls `SkaterData.HaveTalentCached`. A scan of both executable sections found two callers. The normal injury calculation calls it at `0x40FC4C9` after checking for an injury. The new balanced calculation calls it at `0x40FC5C4` before accumulating injury deductions. The custom **Ignore injury penalties** effect can satisfy this same query in either path. The game still applies its own stat and fatigue calculations.

There are now 20 fixed wrappers and 15 other reviewed entries. Twelve callers still need the managed scope for inlined live queries. The normal injury calculation's inline query moved into the fixed wrapper, so it no longer needs that managed scope. The compatibility profile protects both native injury calculations.

## Charged release and verification

`ReleaseShot(Vector2, float, bool, bool)` retains the complete 96-byte body, including its early return at `+95`. Its SHA-256 is `F8E630EED796F25F0827060FFAC0EAE87318755BD5A7612A349E47B267BEDD81`. The MethodInfo descriptor, six pinned Harmony/BepInEx/Il2CppInterop/Dobby binaries, and the owned-trampoline validation remain in force. The generated MainScriptsAssembly fingerprint is `F46652CBC6BBFAB80D67D2ADE3F77A5FC926F1BAEAC80B95754C35D92B162B7E`.

On the reviewed image, `CustomRewardNativeChecks` passed 36 compiled-target and native-byte checks, including both calls to the new fixed wrapper. `ChargedReleaseChecks` passed 27 checks against the pinned Dobby library and a private copy of the actual function bytes, covering both branch outcomes, original arguments and rollback. `CustomRewardHookAudit` resolved 143 statically declared managed patch signatures. It reports ten dynamic declarations separately. These tests establish the adapter contracts. Menu startup and match behavior require their own observations.
