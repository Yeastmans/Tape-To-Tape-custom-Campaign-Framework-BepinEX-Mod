# Native compatibility review: Steam build 26150666

The Steam update changed GameAssembly and metadata, so the previously reviewed
26141904 profile correctly refused activation. This change accepts only the
reviewed 26150666 inputs and regenerated interop. It preserves the current goalie
recruitment, checkpoint, contract badge and GM stat preview implementations.

## Exact inputs and resulting profile

| Input | SHA-256 |
| --- | --- |
| GameAssembly.dll | `FD2E6FDF3900272C2E9B201F3280AF15C44D330D28E131E00FE6C99D007C4B93` |
| global-metadata.dat | `22E6F00B8CE5F83A3D167410FE6B7C76086B201BC48CB83CFAA3BD4E577CAC37` |
| MainScriptsAssembly.dll | `83FAB7CBC1625A290AA5B72699F78BD2C300D3245414F6C3976184F8D2A5C9AF` |
| spine-csharp.dll | `DEC561B73C5328C01275C395E0180A75A4355C0AD87960BDEEDBBDD78713EBAD` |

The normal game launch regenerated installed interop on October 23; its
assembly-hash is `c54dfb7474bedf944a6b41a041e53d7c`. Native inputs were copied to
the evidence directory before review. Baseline ID:
`B5AAD9100629E15F49F820FAA258480F41B01B99349AA0F6148E44A51319C458`.

## Native review

All 1,452 previously protected method identities resolve. Of these, 1,137 retain
their normalized body hashes. Full disassembly review accounts for the remaining
315: 314 are equivalent after reviewed relocation, table, metadata-alias and
compiler-ordinal changes; one has a substantive game change.

The substantive change is `Rogue.BenchwarmerStrip.OnPickup`: the native routine
now resets talent caches and revises native stat application. Its signature and
object layouts remain valid. `CustomRewardBenchDonation` still calls the native
routine for ordinary rewards. Its owned custom rewards replace the routine with
their existing explicit stat/talent transfer, including donor and recipient cache
resets. No custom donation behavior was rewritten to imitate native changes.

Six native switch tables retain every relative destination. GC bitmap references
move from `0x8CFB3E0` to `0x8CFAC00`. Two shared getters retain their complete
bodies: `0xA89F20` gains a metadata alias, while `0x9B1720` changes four compiler
display-class aliases only. The Forward knockout state-machine ordinal changes
from 344 to 345; its raw layout is unchanged.

All 21 raw callbacks retain equivalent complete instruction sequences, sizes and
alias counts. Their exact RVAs, full-body hashes and prologues were regenerated.
All 33 reward native adapters also retain equivalent complete bodies. The goal
constructor moves to `0x3C7B830`; goal-stat add/cancel move to `0x403D280` and
`0x403CF20`. Fixed query prologues incorporate the reviewed metadata relocations.
The unnamed builder fault helper remains uniquely identified through its callers
and complete masked body, rather than a guessed address.

The profile retains all 137 comparison sites, 109 squad query calls, 51 named
contracts and 21 raw contracts. The named inventory includes all 37 current
goalie hooks and three independent fresh-run hooks. No protected identity or
contract was removed. The independent review also checks all 120 raw structures
(116 exact, four equivalent after the knockout ordinal change) and the actual
goalie, save, menu and campaign owner layouts.

## Source changes and checks

Only exact native/build/interop pins, reviewed addresses/prologues, generated
profiles, the predecessor hash list and compatibility assertions change.
`FreshRunActivation` receives the same reviewed pins. The currently installed
runtime `CAA5D138F0576FCA0F16C1CD814CBCD3A287C91C75FB4BA52338CC1C5E6669EA`
is an accepted updater predecessor. Existing predecessor entries remain intact.
Tracked release binaries and `runtime-sha256.txt` remain unchanged; final local
Studio packaging supplies its actual runtime through the existing build overrides.

The new build assertion failed against the old profile before production edits.
After regeneration, all 132 compatibility checks pass against the installed
native inputs, including rejection of the previous baseline, changed native
instructions, missing feature contracts and invalid inline patch frames.
Independent source/profile/native review found no issue at this stage.

The controller owns final compilation, actual regenerated-interop callable/ABI
checks, regression checks, packaging, install and live validation. Those results
belong to the final candidate receipt; this source review alone does not establish
gameplay or visual acceptance. Earlier goalie identity/expiry observations must
not be described as resolved by a compatibility update.

## Evidence

Evidence root: `artifacts/compatibility-26150666-20261023` in the canonical
workspace. It includes `previous-native-baseline.json`, native snapshots/dump,
`native-old` and `native-new`, raw/reward comparison reports, refined semantic
review, six-table review, shared getter reports, independent layout/target/profile
reviews, and red/green compatibility logs. The exact final runtime, Studio,
source manifests and additional compiled diagnostics are recorded separately by
the controller after this scoped source commit.
