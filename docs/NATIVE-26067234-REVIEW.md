# Tape to Tape build 26067234 compatibility review

Reviewed 19 October 2026 against the installed Windows x64 roguelite build.

- GameAssembly SHA-256: `98D7EEF11DB0FB175CCC72C2ED9273D9B739677AD606D217F7B4E3897E2D0DA4`.
- Metadata SHA-256: `EE1B5BB1916ECC190A50B5CB83283B72FE9014D9F95209AC351CCBDC18CBD1A8`.
- Baseline: `1197A383B1E138D8593FCC1C7AEA5FCB219480B7FB21EA52D1BB2829EC0979BD`.

The old baseline correctly rejects this update. All 589 reviewed method identities
resolve with unchanged signatures; 449 retain their canonical body hashes and 140
change. Twelve have different instruction counts. This is not an address-only update.
The baseline retains all 278 protected methods, all 142 squad comparison sites and
110 squad query calls. Each comparison is remapped by owner, query and occurrence;
the starting-screen flow remains at instruction index 770, now RVA `3BBB522`.

## Native changes relevant to the integration

GameplayEvents adds event fields, shifting subsequent static event offsets. Fresh
interop was generated from the exact new binary and metadata rather than reusing old
field bindings. AI reset delegates breakout initialization to Initialize; breakout
movement and shooting now use the AIState skater accessor and changed steering.
Existing mod AI policies operate through managed properties and the reset postfix,
not copied native instructions. The game's updated AI code remains in place.

Forward/goalie repository asset lookup changes to interface dispatch while retaining
the input model/data contract and final appearance application. Talent-list Init now
calls the base list initializer. The mod's translation prefix and badge postfix keep
their original argument bindings. Knockout skin restoration adds cancellation tied
to object destruction; its state-machine body is retained, not replaced by the mod.

CampaignGoalTarget now compares the opponent identity directly in the relevant branch.
AssignSubstitute adds soccer-uniform/team filtering while preserving its public
signature. These updated native behaviors are explicitly included in the new reviewed
baseline; they are not represented as relocation-equivalent code.

The 11 raw custom reward hooks were separately compared, including complete entry
bodies, signatures, prologues, MethodInfo identities and 14 payload layouts. Entry
bodies match after relocation mapping. Thirteen full direct/helper-tail bodies match
after relocation mapping; recursive subcalls beyond these bodies were not exhaustively
reviewed. The five hook source files receive only exact game-hash and RVA updates.
Prologue guards, calling conventions, callbacks and MethodInfo checks remain intact.

Fresh compiled audit resolves 317 attributed target references without signature
issues. Source inventory resolves 508 references / 450 distinct targets without
unresolved methods or unexpected shared hook addresses. The native aggregate ABI audit
retains the exclusion of the unsafe CancellationToken overload and the previous
GetOverall interception remains absent.

## Verification and limits

The mod compiles against 223 freshly generated interop files. All 53 compatibility
checks pass, including rejection of the previous baseline. Seven isolated Creator UI
checks exercise the talent Delete from library confirmation/cancel paths, deletion,
list refresh and preservation of independent campaign/player copies.

Local detailed evidence is in `artifacts/game-update-26067234-20261019`. These are
static, build and isolated UI checks, not gameplay acceptance. Human checks should
cover Continue, a complete match and map return, both roster lines, custom talent/relic
triggers, substitution, and exported player appearance on this exact build. For Delete
from library, confirm Cancel preserves the item and confirmed deletion leaves existing
campaign/player copies usable.
