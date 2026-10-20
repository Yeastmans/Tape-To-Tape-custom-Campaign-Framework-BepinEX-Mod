# Tape to Tape build 26071671 compatibility review

Reviewed 19 October 2026 against the installed Windows x64 roguelite build. Steam shipped two
updates that evening: 26070875 (22:52) and 26071671 (23:20). Both were reviewed; this baseline
targets 26071671 only.

- GameAssembly SHA-256: `92C6308809D5E324178C7D7E03F767C2E7D9AFCB9F41B2390B9EEAA77216BC58`.
- Metadata SHA-256: `1009F63178E260153CBBF224338BDC9124F9468D0F462F7A58327F638188914A`.
- Baseline: `B262B1E1FFA626710EC0F528258868D2BF076C6598A01070B5A7D84581B19D35` (277 methods, 139 comparison sites, 110 query calls).

## Native changes relevant to the integration

26070875: `MovementManager.CanBodycheck` gained a boss-aggressive cooldown branch (the mod's
postfix only turns an allowed check off, so it is unaffected). `ChooseMetaMenu.SetupMetas`
now asks `UnlockSystem.GrantUnlocksEarnedBySquads` for squad-earned unlocks before building
the squad grid; it iterates the unlock system's own data, not `CampaignState.squads`, so the
mod's prefix/postfix injection of custom squads keeps its contract. NPC intro dialogue and
tutorial changes touch nothing the mod reads.

26071671: `BenchwarmerMenu` replaced `IsBenchwarmerRosterReady` and `ShowRosterInvalidPrompt`
with `IsTrioLineupReady`, `ShowTrioLineupIncompletePrompt`, `ConfirmStartWithOpenSpots` and
`HaveBenchwarmerToAdd`. The trio rule is now `squad.id != "trio"` (inlined in `Confirm` and in
`IsTrioLineupReady`); the native gauntlet bench minimum was removed. The squad manifest drops
the four rows in the removed code and the `Confirm` gauntlet row, maps the `Confirm` trio row
to the new `neq` comparison and adds `IsTrioLineupReady` (trio_draft/neq). Custom squads with
Trio draft requirement follow the native trio check; Gauntlet draft requirement no longer has a
native site, matching the native gauntlet squad. Every other comparison site keeps its
instruction index; the starting-screen flow stays at index 770 (RVA `3BBEBF2`).

All other differing bodies among the 589 reviewed identities are relocation-only (runtime stub
displacements, shared delegate-constructor aliases, byte flags that collide with string
literals); instruction codes and counts are identical.

## Pinned native addresses (re-derived from disassembly)

Ten hard-coded reward RVAs are unchanged (`0xc458f0`, `0xc45b60`, `0xc45cc0`, `0xc459d0`,
`0xc45250`, `0xc451b0`, `0xc45370`, `0xc85b20`, `0xc85ec0`, `0xc85a00`). Moved:
`GoalScoredArg..ctor` 0x3628380 → 0x3629F70; `GameStats.<OnGoalScored>g__AddGoal` 0x38E07F0 →
0x38E2450; `GameStats.<OnDeniedGoal>g__CancelStats` 0x38E0510 → 0x38E2170. All bodies match
after relocation mapping, prologue guards match, 13 helper/tail bodies have zero normalized
deltas, 14 payload layouts are unchanged, and the native callers still call AddGoal 6 times and
CancelStats 3 times with identical normalized bodies.

## Verification and limits

The mod compiles against 223 freshly generated interop files. Compiled audit: 317 attributed
targets, no signature issues; source inventory 508 references / 450 targets, none unresolved;
aggregate ABI inventory unchanged. 52 compatibility checks pass. The plugin loaded on
26071671 with every native reward hook installed and no mod errors. This is not full gameplay
acceptance: Trio draft custom squads, squad-earned unlock reveal with custom squads and Extra
goals on this build still need a human check.
