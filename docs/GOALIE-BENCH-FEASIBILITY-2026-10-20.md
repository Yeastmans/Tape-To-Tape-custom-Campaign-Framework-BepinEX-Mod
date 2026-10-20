# Goalie reserve and swapping feasibility

Assessment only, requested 20 October 2026. Goalie recruitment with immediate
replacement is being implemented separately; a reserve goalie is not part of
that implementation.

Keeping a reserve goalie and swapping with the starter between matches is
feasible through additional mod support. It is not supported by the existing
native bench representation.

The build 26085704 metadata has one `Data.TeamData.goalie` field of type
`GoaltenderData`. Its roster collection is `List<ForwardData>`, and `Lineup`
contains the five forward-position IDs only. `TeamData.GetBench` returns
`List<ForwardData>`. A goalie cannot safely be inserted into that list or treated
as a second skater line.

A separate goalie reserve needs:

- A run-scoped record for the reserve's full goalie model, stats, abilities,
  talents and appearance, plus fields omitted by native serialization.
- A goalie slot in the lineup/bench screen, with a swap action that exchanges
  the reserve and `team.goalie` without losing either player's earned changes.
- Save/Continue handling that ties both players to the exact campaign and run.
- Recruitment behavior that explicitly chooses whether a new goalie replaces
  the starter or the reserve when both slots are occupied.

Between-match swapping is the simpler implementation: the next match can
initialize the chosen starter normally. Swapping during a match would also need
to replace/reinitialize the live goalie actor, input/control ownership, camera
and goal-net references, effects, and any references held by puck/AI code.
That requires a separate live gameplay investigation and validation.

Evidence: current local `reward-review/dump/dump.cs` under
`artifacts/game-update-26085704-20261020` contains `TeamData`, `Lineup`,
`RunDataModelV2`, `TeamDataModelV2`, and goalie models. Existing mod bench guards
are in `src/CustomCampaignFramework/RosterLoadPatches.cs`; the native starter
appearance reapplication is in `Plugin.cs`'s player-team configuration path.
This is source/metadata feasibility evidence, not a working reserve prototype.
