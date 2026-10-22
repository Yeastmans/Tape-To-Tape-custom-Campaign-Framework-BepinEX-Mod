# Tape to Tape build 26141904 compatibility review

Reviewed 22 October 2026 against the installed Windows x64 game and preserved 26085704 inputs. This candidate preserves the four-feature implementation and goalie roundtrip from f9df645b73f75a13b95bd80f7c70cc0ba21eee90. No game launch, installed-file update, real-save mutation, release, or canonical promotion was performed by this implementation task.

## Identity and root cause

Steam updated the game to 26141904. Studio correctly rejected the old release's exact native hash guard. Both the four-feature adapters and existing reward adapters had build-specific addresses/contracts; changing only the accepted hash would leave moved addresses and generated wrapper identities incorrect.

- GameAssembly SHA-256: `A58718A1AF369E064DF3A7CE149129C0130C4F316BC5E47EF7E6365853B1AA42`.
- Metadata SHA-256: `7CABE7311509564E5C2D4A0580212E2839FAA0CAC05949940015DD4D092E4EDB`.
- Baseline ID: `54C58BB4EE7C7FADA2B1A012F6B77FF05234E9E57632565FA33C902C1029ECD9`.
- Runtime SHA-256: `582617F45530CB9937E6A72117F22D8E76AFE6446AF91CDBACA615A9EC6042B1`.
- Runtime contract remains `t2t-native-v2-20261020`.

## Native review and adaptation

All 1,448 previous protected identities resolved. There are 1,089 unchanged normalized baseline hashes and 359 changed hashes. An independent disassembly/symbol pass found 309 of the 359 equivalent directly. The remaining differences were reviewed as relocated jump tables/GC bitmap references, a shared generic getter's compiler ordinal, and the native UI/audio changes below. Six relocated jump tables retain every destination's relative layout. The stricter original baseline hash algorithm is unchanged; all reviewed current bodies were regenerated and remain checked on every compatibility verification.

- `LineupMenu.LoadCurrentTeam` now links first starter and last bench navigation endpoints through new `LinkStartersAndBenchEnds`. Its existing visible-bench branch and native list population remain. The helper is now protected, increasing coverage to 1,449. The goalie screen still uses a hidden/cleared bench; custom two-line configuration retains native lists. Native transform access was inlined and explicit interface metadata arguments were added.
- `BenchwarmerMenu.LoadCurrentTeam` and `LineupMenu.SelectFirstPlayer` now pass explicit IReadOnlyList metadata to the existing item dispatch helper. Wrapper signatures and managed calls are unchanged.
- `SoundManager.OnEndOfGame` explicitly clears the released event handle at offset 0xA70. Existing stop/release and end-of-period parameter behavior remain native-owned; no adapter change is required.
- All 137 comparison sites relocated at their reviewed instruction indexes, including 28 inline comparisons and 109 direct queries. The exhaustive 109-entry squad-query inventory is unchanged. Starting flow is now RVA `0x4404FB0`; exact displaced instructions still validate.
- All 21 jersey raw targets retain complete normalized instruction behavior, signatures, sizes and alias counts. All 120 reviewed matching state-machine/owner layout structures are unchanged. The unnamed 172-byte builder fault helper remains `0x9D38F0`; multiple corresponding MoveNext fault calls identified it among two byte-pattern candidates. Exact new body SHA-256/prologue contracts and closed generic signatures replace the prior entries.
- All 33 existing reward raw targets retain normalized behavior and ABI. Named generic queries and goal helpers relocate. Eighteen unnamed fixed query functions retain addresses but their embedded metadata displacements change. Karlson's specialization moves `0x658400` to `0x658370`; its complete body and closed MethodInfo metadata uniquely identify it among 42 structural candidates. Exact prologues and live/data pointer cross-check constants were updated.
- Native field comparison found changes only in unrelated speech/VAT structures and renamed LineupMenu async state-machine types; the raw hook layouts remain identical. No managed feature algorithm was changed.

## Generated wrappers and compiled binding

`startup-before.log` records Unity 6000.0.79f1, outdated-wrapper detection, native metadata extraction and interop regeneration on the latest launch. Installed assembly-hash marker is `f5a12e281839e1f5af9a0ce5f8cc2ef6`. Candidate compilation uses those actual installed wrappers. Jersey's exact MainScriptsAssembly/spine-csharp hashes were updated after inspection. Provider hashes remain unchanged. The final receipt records every wrapper and input hash.

Fresh compiled inspection resolves 2,103 feature interop/provider calls with zero errors; 335 explicit and 15 dynamic Harmony signatures have zero issues. Native source inventory resolves 532 references to 463 distinct targets with zero unresolved references or unexpected aliases. All 80 manual targets (46 release-owned named targets included) bind without issue. ABI review retains zero unsafe actual small-wrapper aggregate hooks; the CancellationToken overload is still excluded by the existing selectors.

## Verification

The current-build regression first failed with the old guard/build. The updated candidate passes 131 compatibility checks, including full native methods/sites, wrong-hash/modified-baseline rejection, complete feature inventories and previous-baseline rejection. Managed source-linked checks pass: goalie completion 404, goalie menu 130, jersey runtime 33, Play Now persistence 30, Play Now roster runtime 24. Runtime compilation succeeds with the existing unused-variable warning at Plugin.cs:8431; Studio self-contained publish and actual published game-art package checks pass. These are diagnostics and managed checks, not native gameplay acceptance.

Studio's predecessor allowlist preserves previous entries and adds the currently installed F646E8665FAEEFAE0775DCFAC7C9B53A24141BD25CA3EAC8CA6ECB8B4379DB1C runtime. Its bundled runtime/profile/baseline agree. Unknown games and modified contracts remain rejected.

## Pending real-game observations

`STS.Map.MapNode.SetupTooltip` retains equivalent native instructions after symbol/relocation normalization and unchanged callable binding. The earlier original-native NullReferenceException therefore has no established changed-body cause. It is not suppressed or claimed fixed; the exact candidate must be launched and its fresh log inspected. Existing UnityEngine generated-type warnings likewise are not suppressed.

Controller/human validation remains pending: exact-candidate startup, all feature installation receipts, gamepad traversal across both two-line rows, Active/Reserve goalie navigation and save/Continue roundtrip, Play Now custom roster persistence, and both jersey-logo display paths. No success in these scenarios follows from offline verification.

## Evidence

Full evidence is under `C:/Users/kiran/Desktop/tape to tape mods/artifacts/compatibility-26141904-20261022/`: preserved `native-inputs`, fresh `native-dump`, old/new complete protected-method disassembly, `protected-diff.txt`, `ui-semantic-diff.txt`, `raw-comparison.json`, `raw-layouts.json`, `reward-comparison.json`, `reward-address-map.json`, `jump-table-review.json`, fresh `compatibility-audit`, red/green/build/regression logs, and final manifests/receipt. Candidate outputs are isolated in this checkout's `build/Game26141904Candidate/runtime` and `studio-publish`.
