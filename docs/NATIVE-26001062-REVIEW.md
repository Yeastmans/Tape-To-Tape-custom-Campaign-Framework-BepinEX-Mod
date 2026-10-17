# Native review for Steam build 26001062

GameAssembly: `864DB76DF3B6F14D7F47AAB044B8F44E95EC111FA15D52F9D8E516AF31D9CE2F`.
Metadata: `08563F2DB54CCC926C310229B8043AEB8D295BC1A024EBE1D41510E67A769525`.

The previous baseline correctly rejected this update. The new baseline retains
262 protected methods, 142 comparison sites and the same 110 squad-query callers.
All comparisons were remapped by their owning method, query literal, query kind
and ordinal; 141 retain their instruction index. The moved comparison in
`LineupMenu.LoadCurrentTeam` follows the new native `RefreshStartersCount` call.
Its inactive-line list, card construction, locks and ordering remain intact.

The startup hook remains the same two instructions, `410FB6C60A4758`, at
`0x3B91972`: R14B and the state-machine flags at RDI+0x58/+0x59 retain their roles.
The surface hook has its unchanged 18-byte prologue at `0x39A7E90`.

181 protected bodies retain their exact canonical hash. Changed bodies were
reviewed against the prior canonical instruction records. Compiler/runtime
relocations and shared-callee inventories account for most changes. The native
behavior/layout changes relevant to the mod are:

- `Forward` adds `_bareFists`, moving later fields by eight bytes. The rebuilt
  runtime uses current generated field accessors. Skin assembly adds the bare
  fist skin; the golfer outro now calls `ShowBareFists` instead of assembling it
  inline. Existing skin/appearance hooks still run through `SkinHelper`.
- `Forward.Knockout` appends `keepGrounded` and `fallDirection`. Existing leading
  arguments retain their order; the current interop supplies the new defaults.
  Native collision, ball/chain and offside callers pass the new arguments.
  `KnockoutState` adds grounded-chain timing and uses `SwitchToVerticalLanding`.
  The mod's quick-recovery postfix still accesses native `_timeKnockout`.
- Collision guaranteed-knockout logic moves into `IsGuaranteedKnockout`.
  Capability interception remains on the native generic talent/relic queries,
  so those queries also cover the extracted helper.
- `SkaterPreviewInUI` adds `IgnoreRunState`, shifting later fields; its native
  continuation forwards this to `GetSkinForForward`. Existing color/skin hooks
  use generated properties and preserve the native argument.
- `LineupMenu` adds a starters-count refresh and shifts later fields. The
  preview and recruitment adapters use current generated properties and retain
  the native inactive-line ordering.

Eleven separately pinned native reward entries were reviewed by symbol, current
body, parameter/return ABI, unique entry and original prologue. The ten generic
entries retain owner + MethodInfo arguments, byte/pointer returns, generic
context lookup, list/cache behavior and native fallback. Their RVAs move by
0x1010. `Events.GoalScoredArg`'s constructor moves from 0x3603700 to 0x3605D90;
its argument order and field writes remain unchanged. Runtime MethodInfo pointer
checks and prologue checks remain enabled, as do exact game/metadata pins.

The stamina repair hooks the unique `TeamStaminaSystem.Initialize` entry
(0x394DBF0). It applies the authored player-squad setting before
`Team.Initialize` invokes the native HUD factory (0x3BDC510). Fatigue, recovery,
line selection and rendering remain native. Vanilla squads and CPU opponents
are unchanged; explicit Off remains disabled. No shared getter is detoured.

Local audit evidence: `artifacts/line-stamina-20261017/` contains native site
mapping, protected body records, field/call differences, raw-hook disassembly,
before/after live telemetry and screenshots. Compatibility verification is
separate from the recorded in-game stamina test.
