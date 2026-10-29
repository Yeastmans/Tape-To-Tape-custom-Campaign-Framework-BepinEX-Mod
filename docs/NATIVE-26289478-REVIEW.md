# Native compatibility review: Steam build 26289478

Reviewed 2026-10-29 against immutable copies of the installed Windows game.

- GameAssembly SHA-256: `0E1B540314D591A17215BBC46FD17C2605CCFA58A7D30418D62453FFF0FEB3B6`
- Metadata SHA-256: `DE0A546D36EB3C59C68178B5B523B56D30C4609E62D770FA78A9DA0DE93037DC`
- Baseline ID: `792054CC4B95FF33EA5A44A5356FFF5AE105F3F622CE3B9CD96DEA0BD529F146`
- Runtime ABI remains `t2t-native-v3-20261024`.

All 1,485 previously protected method identities and signatures remain protected. Of those bodies, 1,105 retain the canonical hash, 290 more were proved equivalent after relocation, and the remaining 90 were individually reviewed. None remain unresolved. The new baseline adds native LoadRunData and GoalCreditedArg construction, for 1,487 methods total. It retains all 137 comparisons (109 query calls and 28 inline comparisons), 54 named hooks, and 21 raw adapters. The query inventory is unchanged. Independent verification against the installed game generated the exact same profile as baseline creation.

## Required changes

The native North goalie initializer now forwards its true campaign flag to its base initializer. The completion observer accepts that flag only with its existing exact North override witness. An owned North receiver without that witness invalidates only the corresponding initialization or side-handoff scope. Pointer, owner, scope, native-execution and duplicate/nesting guards remain in force.

The native run repository now supplies its logo repository to the fourth deserializer constructor parameter. The managed bridge forwards that same dependency, and the wrapper API check verifies all four injected types. Null remains allowed, matching native behavior.

Native code now places separately framed anonymous helpers after some named comparison owners. The inline validator uses exact PE runtime-function bounds for its stack proof, including contiguous unwind fragments only when CHAININFO explicitly identifies the same owner. Full instruction and body-hash coverage remains unchanged; adjacent helper bodies and incoming edges remain protected. Missing, malformed, mismatched and cyclic ownership fail closed.

The fresh-run site moved to RVA `0x4430217`, instruction index 915. Its bytes `400FB6C60A4758`, register ABI, state offsets and reviewed jump-table targets are unchanged. No emitter change was required. Raw reward addresses, prologues, metadata slots and exact generated-interop fingerprints were refreshed after review. The reward goal counter and cancellation event changes preserve the existing adapter's semantics. All 221 independently generated interop DLLs match installed output outside the permitted MVID field; provider and Unity base DLLs are unchanged.

## Mirror rosters

Custom campaign matches skip native AssignSubstitute for owned opponents. Both authored rosters are preserved even when player names overlap, so the squad's recruitment pool is not used to replace opponents. Default mode and unowned native opponents retain native behavior. Existing line-ID repair remains available for other substitutions.

## Verification

The final compiled runtime passed 67 Harmony/API, 28 cache/interop, 3 logo-dependency, 30 generated-identity and 232 starting-flow checks. Source-linked checks passed 526 goalie lifecycle cases and 238 opponent/mirror cases. Native-frame checks passed 57 cases, including all 28 inline contracts and malformed-frame negatives. Compatibility checks passed 139 cases, and the published local Studio payload passed 107 package checks.

The native review, red/green logs, regenerated baseline/profile, package and live-test receipts are retained under workspace `artifacts/compatibility-26289478-mirror-20261029`. These automated checks establish their stated contracts; the separate live-test receipt records actual game observations. Included in Beta 1.0 revision 32. See RELEASE-R32-VALIDATION.md for observed gameplay and package verification.
