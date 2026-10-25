# Native compatibility review: Steam build 26188487

R27 targets the installed public-branch Windows x64 build 26188487. The R26
profile correctly rejects these changed native inputs. This review relocates
the native contracts, repairs the changed fresh-run callback ABI and makes the
generated-interop identity gate tolerate the loader's random module ID.

## Inputs and profile

| Input | SHA-256 |
| --- | --- |
| GameAssembly.dll | `54982E5C98932B701B4549086BF08ECC792DA5DE43A5E3627F8804FE657AFF58` |
| global-metadata.dat | `9FB0F8B4CBB1D238AF057973C3E65BA7CDC27201C3D3E5EE0664C87FCEE38F58` |
| UnityPlayer.dll, 6000.0.79f1 | `C97760D2EAF5699565C204D6FDA23500D07D272E9A39A137A0C24928172BFB02` |

Baseline ID: `BB7C1434C9528E142B042E131A39778C2A9A3B7CD0AFE899AFF4BBCE1C9900BA`.
Runtime contract: `t2t-native-v3-20261024`.

The actual R26 baseline has 1,483 protected methods; the older 1,469-method
summary omitted later R26 additions. R27 retains all 1,483 identities and
signatures, then adds `ProfileData.SelectRogueRunSettings` and its ref-data
callback, for **1,485 protected methods**. It preserves **137 comparison sites,
109 native query calls, 54 named hooks and 21 raw hooks**, including all 40
goalie lifecycle hooks. Protected dependencies are not installed hook counts.

The old native snapshot was verified against its own baseline. A fresh dump was
generated from the new binary and metadata together. Both generated JSON files
were staged, the baseline was verified against the installed game, and only then
were the matching baseline/profile promoted together. No protected identity or
named hook was removed.

## Reviewed native changes

Of the 1,483 previous method records, 1,158 retain their normalized body hashes.
Full instruction review accounts for the other 325: 321 are equivalent after
reviewed relocations, compiler ordinals, five switch tables, metadata aliases
and field offsets. Four records describe three changed native routines; the
starting-flow routine has both a stable-name and exact-signature record.

- `CampaignState.InstantiateStartingTeam` now checks that the stored custom
  campaign team's ID equals the selected profile custom-team ID before using
  it. Otherwise it clones the selected squad's starting team. The existing
  authored-roster adapter still captures that selected roster and preserves
  empty slots; no replacement game logic was added.
- `ChooseMetaMenu.ConfirmSelections` uses difficulty 1 for the native tutorial
  fallback and delegates squad run-setting persistence to
  `ProfileData.SelectRogueRunSettings`. That helper invokes its native
  `SquadModifier` callback, then saves the profile. The helper and callback are
  now protected dependencies.
- `TitleScreen.StartNewRun` adds a path for an active campaign tutorial which
  resets the run, selects the native vanilla squad, sets native run settings
  and fires the tutorial signal. Its normal menu path and native cancellation
  handling remain game-owned. The custom starting-screen callback still runs
  after native squad and bench initialization.

`ProfileData` adds `revealedFormerlyLockedStarters`; affected later fields move
by eight bytes after alignment. Updated generated interop resolves those fields.
The raw state-machine and owner layouts remain unchanged. GC bitmap references
move from `0x8CFAC00` to `0x8CFC480`; reviewed switch-table destinations and shared
getter bodies retain their behavior.

All 21 raw contracts retain complete equivalent instructions, native signatures,
sizes and alias counts. Their exact hashes, prologues and identities are updated.
The unnamed fault helper remains identified by its full body and five known
callers. Independent review checks 7,229 raw instructions, helper bodies/imports,
closed-generic ABI and 18 explicit object/state layouts. All 33 fixed reward
adapters retain equivalent full bodies; the goal constructor and two goal-stat
functions move, and the 19 fixed query prologues incorporate metadata relocation.

## Fresh-run callback repair

The hook moves from `0x4405600` to `0x4406307`. Its seven original bytes change
from `410FB6C60A4758` to `400FB6C60A4758`: native code now takes the intro flag
from **SIL**, with the async state in **RDI** and `TitleScreen` in **R15**.
The previous emitter used R14B and RSI. The repaired emitter supplies the new
callback arguments, returns AL to SIL, preserves volatile integer/XMM state,
and replays the original OR before the native branch. The v3 profile rejects
the former register contract and byte sequence.

`NativeStartingFlowChecks` executes the compiled production emitter in an
isolated Win64 wrapper, including a managed reverse-P/Invoke callback and a
callback that clobbers volatile registers. The old runtime fails the argument
check; the final runtime passes 232 checks for all intro/result/oldschool flag
combinations, stack alignment, registers, XMM0–5 and continuation flags.

## Generated interop identity

Interop was generated outside the installation using the copied, hash-verified
loader toolchain and Unity libraries, Cpp2IL, `AttributeInjectorProcessingLayer`
and `AsmResolverDllOutputFormatDefault`. The generator's loader-default parallel
write option was reproduced in a second run. Independent loader IL/configuration
review confirms the same pipeline, target corlib, method-reference scanning and
empty deobfuscation map configuration.

A third generation runs the same pipeline under an isolated copy of the actual
loader's **.NET 6.0.7** runtime. Its 221 DLLs also match the .NET 8 generation
byte-for-byte outside the module MVID. The host log records the copied
`System.Private.CoreLib.dll` hash
`2CAA46D2AE726E135592033B6C682232A08698C23DBD1A9207F86C4CCA081AD7`.

Across those two runs, **all 221 DLLs differ only in their 16-byte module MVID**.
The installed generator creates a fresh GUID when writing each module, so its
previous whole-file hash gate could reject equivalent generated interop.
`GeneratedInteropIdentity` locates the actual Module row's MVID entry through
the PE metadata reader and excludes only those 16 bytes. Every other image byte,
including IL, field/method signatures, names, references, metadata indexes and
PE headers, remains hashed. Invalid or empty MVIDs and malformed bounds fail.
Native input pins, named structural contracts and exact loader-provider hashes
are retained.

| Generated dependency | Reviewed fingerprint, only MVID excluded |
| --- | --- |
| MainScriptsAssembly | `1F8CDAC2969A1200FD7D9FC3D425A639526639FFFA697A48122DD2341D6E1814` |
| spine-csharp | `1A246C25CAE5262ECA51DDB6BDA305CC2D651B2404FDEE971783ED92A6D53CBA` |

The regression first fails with the former whole-file hashing behavior. The
compiled final helper passes 30 checks using both real generations, accepting
MVID changes and rejecting changes to method bodies, metadata, signatures,
adjacent bytes, PE timestamps, image tails and truncated inputs.

## Play Now filename and ID contracts

Independent native review checks the team, forward and goalie loaders/writers
and their identity paths. Native discovery accepts `CustomTeam-*.json`,
`CustomForward-*.json` and `CustomGoalie-*.json`. Identity remains a string used
in the filename; no GUID parser constrains that path. The readable name prefix
plus full GUID used by R27 remains compatible, and name-field editing does not
replace an existing ID. This is native source/contract evidence, not an observed
in-game save/load result. Managed filename checks are recorded in the separate
R27 filename validation receipt.

## Diagnostics and remaining acceptance

- Baseline generation and independent verification against installed native
  files succeed; 137 compatibility checks pass, including the previous profile
  and unsupported ABI rejection cases.
- Release runtime compilation against fresh interop succeeds with zero errors
  and the existing unused-variable warning at `Plugin.cs:8434`.
- The final runtime passes 232 starting-flow ABI checks, 30 generated-interop
  identity checks, 66 Harmony plan/provider checks and 28 goalie wrapper/cache
  checks. These inspect or execute isolated adapters, never the game process.
- R26 runtime hash
  `516AB7338E2E7C24E9188E841CFAA168FC16865BCC1B8234DFC48595EDDE8C5E`
  is added to the approved replacement list. Existing predecessors remain.

Gameplay acceptance remains pending: start custom/default runs, exercise the
native tutorial path, toggle authored starting screens including empty pools,
check custom Coach visits and squad maps, then save/Continue. For Play Now,
export readable filenames, load team/skaters/goalie, edit a display name, save
again and confirm the existing ID is retained. Confirm appearance and goalie
behavior in the game before claiming those observations.

## Evidence

Canonical evidence root: `artifacts/compatibility-26188487-r27-20261024`.
It retains the exact native snapshot and fresh dump, old/new method exports,
semantic/raw/reward/layout reviews, staged and verified profiles, isolated
interop generations/manifests, red/green logs and the compiled final runtime.
Independent review is under
`artifacts/release-r27-readable-filenames-20261024/independent-native-review`.
The machine-readable `compatibility-readiness.json` records final hashes,
commands, changed files, provenance and outstanding human acceptance.
The parent release receipt owns the published installer and final packaged
runtime identity. No game installation, launch or user-save mutation occurred
during this compatibility review.
