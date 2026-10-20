# Opponent abilities — game build 25899138

The opponent player editor allows only abilities with a native normal-hockey AI
activation path or an automatic effect used by that AI. Player squads, free
agents, superstars and reward pools retain their existing catalogs. Existing
unsupported opponent assignments are preserved with a warning until replaced;
they are not selectable in the new dropdown. This is editor filtering, not a
runtime rewrite of imported or hand-written campaign files.

## Evidence

The current re-audit uses
`artifacts/release-audit-20261013/audit-opponent-abilities.py` with the current
`artifacts/qa13-update-25899138/dump` and an exact installed GameAssembly SHA-256
guard (`450B43499FD4E66C482720790454E3F33C3360FADBA53003774E4CC17F71ED66`).
It records 3,305 native methods and 120 ability-relevant consumer disassemblies
in `opponent-ability-references.json` and `opponent-ability-native.json` beside
the script. The older `tools/audit_ai_abilities.py` and its September 22 output
remain historical evidence only.
Direct call symbols can have shared-RVA aliases; a coincident name alone is not
proof of ability usage. Typed GetAbility/HaveAbility metadata and actual native
AI paths were used for the allowlist.

| Allowed family | Native evidence |
| --- | --- |
| Gas | DefensiveStateAI.Fart and DefenseBreakoutNewState.Fart get FartRepellentController and spend a use |
| Grappling Hook | AIState/defensive grappling paths get GrapplingHookPowerupController |
| Tomahawk / Mjolnir+ | Defensive ThrowStick gets ThrowingStickController or MjolnirAbilityController |
| Jump | BreakoutWithPuck.TryEvasiveJumpAgainstIncomingHit invokes JumpController |
| Spin-O-Rama | BreakoutWithPuck.OnFixedUpdate gets SpinoramaController and starts the spin movement |
| Mega-Sauce | BreakoutWithPuck gets MegaLobController and calls SkaterAbility.MegaLob |
| Yo-Yo | BreakoutWithPuck.CheckForYoyo and YoyoAIState launch/recall logic |
| Prod | DefensiveStateAI.ShouldHit calls ProbeController.TryUseInsteadOfBodycheck |
| Tornado | AIState.TryStartTornadoAI and TornadoAIState.OnFixedUpdate |
| Wet Towel | DefensiveStateAI.ShouldHit and WetTowelAIState.ChaseTowel invoke its controller |
| Headshot Redirect | BreakoutWithPuck.OnFixedUpdate/DuringShot explicitly check HeadShotRedirect and use GetBestHeadshotAim; shot APIs carry headshot/redirect flags |
| Mega Pokecheck | Forward.OnFixedUpdate and CanPokecheckCarrierForPrediction, plus PokecheckBounds.IsColliding, check MegaPokecheck for native pokechecking |

Both levels and the native Spin-O-Rama/Grappling Hook variants are included.
20 of the current 56 catalog assets are allowed.

## Native requirements

Equipping an allowed ability provides the native ability/controller. It does
not force the AI to activate it: available uses, movement state, cooldowns,
target validity, distance and the AI's tactical decisions still apply. The
opponent-effect checkboxes do not equip an ability.

| Family | Important native trigger conditions |
| --- | --- |
| Gas | Defensive AI evaluates the puck carrier or a nearby shot/pass and spends a controller use only when its possession, distance and movement checks pass. |
| Grappling Hook | CPU/locked control, a valid target, faceoff grace period, movement state, distance and hook timing remain native. |
| Tomahawk / Mjolnir+ | Requires a stick, the actual ThrowingStick ability, an eligible movement state and an opposing puck carrier. Native range, prediction and chance checks decide the throw. |
| Jump | Puck-carrier breakout AI reacts to an incoming hit threat after checking possession time, motion, movement state and the Jump controller. |
| Spin-O-Rama / Mega-Sauce | Breakout-with-puck AI chooses these within its existing possession, target and shooting logic. |
| Yo-Yo | Puck-carrier AI checks possession time, offensive-zone positions, timing, chance and a suitable teammate/goal situation before launching and recalling. |
| Prod | Defensive AI attempts the Probe controller instead of an otherwise eligible bodycheck; it does not bypass normal target and movement checks. |
| Tornado | CPU control, equipped Tornado/controller and the native game/movement-state checks are required before entering Tornado AI. |
| Wet Towel | General defensive bodycheck decisions can invoke it. The separate towel-chasing AI additionally requires Prisoner classification, the appropriate game state and an eligible target; the prisoner-towel checkbox supplies only that classification. |
| Headshot Redirect | Equipped HeadShotRedirect is checked within the normal breakout/shooting decisions; the native aim and redirect shot logic still choose the shot. |
| Mega Pokecheck | Automatic effect used by Forward and PokecheckBounds during eligible pokecheck prediction/collision; it is not a separately scheduled cast. |

These are code-derived requirements, not guarantees of immediate use in a match.
The 20 assets share 12 ability families; upgrades use their native variant
parameters and do not require separate mod activation implementations.

## Hidden from the opponent picker

Dash, Deke, Disco Dance, Dragon Shout, Exploding Puck, Fake Slapper, Flop,
Gravity Well, Head Smasher, Immunity, Kazoo, Magnet, Polymorphic Ability,
Rage Burst, Side Dash, Slapshot Yoyo, Slowmo, Stickpocket, Throwing Helmet,
Totally Normal Puck and Zap, including upgrades where present.

No normal-hockey AI activation was established for these. Some have brawl-only
support; the asset's `brawlAiUsage` explicitly describes a separate fighting AI
path and does not prove hockey support. `StickThiefAI` is for the separate
StickThiefPowerup **talent**, not the active Stickpocket ability. Ordinary CPU
deking and movement also do not prove activation of the Deke or Dash abilities.

This is static native-code evidence and focused editor verification. It is not
a claim that every allowed ability has been exercised in a live match. Re-audit
the allowlist when the game changes.
