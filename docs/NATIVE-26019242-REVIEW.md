# Native compatibility audit for Steam build 26019242

The source compatibility update is reviewed and the strict verifier passes.
Installation, native event delivery, gameplay and performance acceptance remain
pending. The verifier's normalization and failure rules were not weakened.

## Exact inputs

- GameAssembly SHA-256: `3E34D99C3DAE9F7F3B38A4BA52FD78D2F4FCD0217B7B6E87C94BFD0DCAD5AEA4`.
- Metadata SHA-256: `747FF34AC7A56EF67945C8ABD7EE14119E79FBE28EC3E43355CD9BF951DD3DD2`.
- Previous reviewed build: 26015379, GameAssembly `2A6EB5B74057D101BD17D2E19F8A924E62FAEE1C02F17FDF975A53C744A6932E`.
- New baseline ID: `2AB74804AEA5187AB006FE20D7A143C2DB964475677DB4057FF36DF0C550893F`.

Evidence and reproducible diagnostics are in
`artifacts/game-update-26019242-20261017`. Original source is retained under
`source-before`. The old baseline rejects AIState.OnGoalScored as expected;
`initial-verification.log` records the rejection. Staged baseline creation and an
independent integrity-checked verification both pass. Their runtime profiles are
byte-identical. The reviewed baseline/profile/manifest were adopted together.

## Audit coverage

| Area | Current audit result |
| --- | --- |
| Existing protected contracts | All 266 signatures match; 230 canonical bodies match and 36 changes were reviewed |
| All discovered hook references | 508 references resolve to 450 unique native bodies; all old/current signatures match |
| Expanded hook body comparison | 415 canonical bodies match; 35 changes reviewed; includes 316 targets beyond the previous protected set |
| Branch destinations | 13,076 local branches checked in the expanded comparison |
| Squad routing | All 142 sites and the complete 110-call inventory retained |
| Raw custom reward detours | All 11 calling conventions, guarded prefixes, field accesses and native fallbacks reviewed |
| Generic MethodInfo families | All 248 identities retained; correct current concrete generic registrations verified |
| Event layouts | All 13 inspected native event/value payload field layouts unchanged |
| New check-event dependencies | Unique OnHit, Awake and OnDestroy contracts added; 269 protected contracts total |

These counts overlap and must not be added together. The expanded inventory
includes attribute patches, explicit installs, dynamically selected methods,
opponent consumers, display getters and string-based reflection targets. It
includes both OverrideSlotWithCustomizationMaterial overloads and the
TitleScreen.RefreshCampaignData lookup that the older audit script omitted.
There are no unresolved targets or unexpected shared hook addresses. The two
known shared Relic display getters retain their exact bodies and alias sets.
No raw detour overlaps another detour, Harmony target or routed-site prologue.

## Reviewed native changes

Three protected methods contain real game logic changes. AIState.OnGoalScored
requests an on-ice celebration when the scoring player is in KnockoutState.
KnockoutState.OnEnter clears the two celebration flags; DoBeforeMove uses the new
celebration flag to suppress movement through the existing zero-vector path.
The routed eliminator predicate retains its arguments, gates and result use.

Its instruction index moves from 49 to 51, and its address moves from
`0x3CE6844` to `0x3CE6DE0`. Every other routed comparison retains its index.
The starting-flow bytes `410FB6C60A4758` now occur at `0x3B90BD2`; the R14B value,
RDI flags at +0x58/+0x59 and following branch retain their meanings. Current
metadata confirms those flags are isOldSchoolSquad/isSkippingBenchwarmerMenu.
The surface entry moves to `0x39A8220` with its original 18-byte prefix intact.

Other changed hashes reduce to relocated runtime references, equivalent shared
aliases and byte-initialization flags coinciding with literal lookup addresses.
The latter affect BreakoutWithoutPuckState.OnFixedUpdate,
CampaignState.SetCurrentNode and TeamCustomizationArtifactsList.Init. Exact
bytes, branch destinations and underlying flag operations were inspected;
these are not changed string behavior. Production normalization remains strict.

The ten generic reward entries move by -0x640. GoalScoredArg construction moves
from `0x36008C0` to `0x36004D0`. All five exact game pins and eleven addresses
were changed; existing prologue strings, MethodInfo checks and callback behavior
are unchanged. Updating only the game hash would have left every raw entry stale.
See `reward-review/REVIEW.md` and `source-guards.json` for the full address table,
native argument layouts, cache/list semantics and runtime-helper comparisons.

## Integration repairs

The coordinated reward repair replaces the unsupported managed SkaterCheck
delegate conversion with a postfix on GameStats.OnHit. Native Awake and
OnDestroy subscribe/unsubscribe the same instance and method on SkaterCheck.
OnHit consumes the native struct with Skater at +0x10 and Hitted at +0x18.
All three methods are unique and now protected by the baseline. This establishes
the native contract; real managed postfix dispatch still needs a running-game check.

The broader source audit found two reflected-member defects in Plugin.cs:

- Superstar selection searched managed fields for m_CampaignState. Current
  interop exposes it as a property, so the menu-specific lookup always missed
  and could use a stale globally loaded campaign state. It now uses the typed
  menu property before the existing null fallback.
- The diagnostic skin collector likewise searched managed fields. It now reads
  typed cached-list properties; the goalie pad list uses `_padSkins`. Unsupported
  cached goalie head/body/away list names were removed. Actual equipped values
  continue to supply those skins.

The inventory also records historical optional soccer/id reflection probes.
Current soccer behavior is controlled by the audited TeamData classifier hooks;
the clone identity is assigned through its typed id setter before its optional
fallback. No new native flags were invented or required by this update.

The reward chat separately owns neutral talent/relic asset creation and the
check-event bridge. Its source-linked mechanic checks pass (86), and the earlier
managed candidate was superseded by the combined runtime
`A653AF0CD8EC2FECD612D4B2B14E3CBDE94169F2F69894FADDB9530DD1A3E187`.
Its final metadata audit resolves 233 attributed Harmony classes, 317
patch-target references and 531 callback parameter bindings with zero issues.
The matched Studio build succeeds and bundles that exact runtime and baseline.
All 53 compatibility checks pass, including rejection of changed check-event
contracts and of the actual old baseline. Eight isolated updater checks also
pass using copied game binaries: known-version upgrade, backup, exact profile,
idempotence, unknown-runtime preservation and unrelated-content preservation.

The replacement allowlist now includes only the freshly verified previous
revision-10 runtime hash, in addition to its existing accepted hashes. Without
that entry the final package correctly refused to replace revision 10; the
isolated updater check reproduces that refusal with the original list and then
verifies the corrected package. Installed/release binaries were not changed.
Exact candidate provenance is in `HANDOFF_GAME_UPDATE_26019242_2026-10-17.md`.

## Review records and validation boundary

- `method-review/REVIEW.md`: protected methods, exact prior canonical hashes,
  site discovery, flow/surface registers, GameStats ABI and event lifecycle.
- `method-review/expanded/`: exact old/current hook-body comparison, coverage
  tables, local branch validation and shared alias review.
- `reward-review/REVIEW.md`: raw detours, exact old binary reconstruction,
  MethodInfo inventories and runtime helper bodies.
- `coverage-review/`: current interop metadata, dynamically selected hooks,
  reflected members, callback arguments and event layouts.
- `patch-address-audit.json`: complete discovered address/alias map and detour
  overlap checks after source address updates.

No installed game/Studio files or campaign/native saves were modified by this
audit, and neither application was launched. The current run must remain untouched, as explicitly requested by the
user in the coordinated NHL chat. The reported low frame rate is not measured
or claimed fixed here. Native installation, actual Check/Checked delivery,
talent/relic previews and effects, knockout celebrations, squad starting flow,
surface selection and representative campaign/Play Now behavior remain live
acceptance work on the exact combined candidate.
