# Temporary goalie contract feasibility — 2026-10-20

Assessment only. Temporary goalie contracts are feasible through a separate mod lifecycle. This assessment does not authorize or implement expiry, bench goalies, or restoration of a replaced goalie. The user confirmed that the current recruitment feature permanently replaces the single active goalie for this run, until another goalie is recruited. The temporary lifecycle below is future feasibility work only.

## Verified native constraints

Evidence is from the preserved build 26085704 inputs and `artifacts/game-update-26085704-20261020/reward-review/dump/dump.cs`.

| Native contract | Finding |
| --- | --- |
| `FreeAgent` (dump line 150233) | Contains an ID and leaving conditions; the record itself is not forward-typed. |
| `FreeAgentManager.EnsureTemporaryContractsRegistered`, RVA `0x3f08200` | Enumerates `TeamData.GetForwards`, skips Angus, and reads the forward's `TemporaryContract` talent. It does not register goalies by ID prefix. `CCF_GFA_` alone does not create a contract. |
| `FreeAgentManager.OnCampaignMatchEnded`, RVA `0x3f07f00` | Calls `FreeAgent.AddMatch` only for IDs in the home team's participation tracker. That tracker is populated from forward lists. |
| `FreeAgent.AddMatch`, RVA `0x3f07320`; `MaxGamesCondition.Check`, RVA `0x3f09410` | Increments the match count and expires when count reaches the configured maximum. Reusing these records alone would not supply goalie participation or departure behavior. |
| `FreeAgentManager.HandleFreeAgentsLeaving`, RVA `0x3f087f0` | Resolves forwards, captures their talents, records departed forwards, queues forward notifications, and removes/replaces forward roster entries. It cannot replace the active goalie. |
| `TeamData` (dump line 164957) | Has one `GoaltenderData goalie`; bench entries are `ForwardData`. `TeamRosterData` (line 228830) likewise has one goalie and a forward bench. |
| `RunData` / `RunDataModelV2` (lines 164201 / 154528) | Persist native free-agent records and departed forwards. There is no previous-goalie or goalie-bench record. |
| `GoaltenderDataModelV2` (line 154675) | Persists the active goalie's native identity, rewards, equipment, colors and modeled stats. Supplemental fields such as number format, pass-read skill and away helmet require the mod's existing snapshot support. |

The current `PlayerContractRuntime` binds and signs `ForwardData` and checks ownership through `GetForwards`. `GoalieRecruitSnapshots` stores the recruited goalie's supplemental sign-time fields, keyed to campaign/run/player. It does not store an outgoing goalie or contract countdown. Goalie recruits must not be inserted into the native forward contract list as a substitute for implementing goalie lifecycle support.

## Minimum extension if approved

1. Persist a separate run-scoped goalie contract with active goalie ID, positive signed duration, completed-match count, and an idempotent completion marker.
2. Count qualifying completed campaign matches exactly once for the contracted active goalie. Verify completion/save ordering when implementing the hook.
3. Apply expiry before another match can start, keeping the active slot populated and displaying the result clearly.
4. Save and restore contract state consistently with the native active-goalie save and supplemental snapshot. Continuing a run must not reset or double-count the duration.
5. If expiry restores the replaced goalie, capture that goalie's full runtime state at signing, including native modeled data and omitted mod fields. Reloading the authored template would lose run-earned changes.

## Decision needed only for a future temporary-contract feature

When the contract expires, should the goalie replaced at signing return, or should the player select another replacement? Restoring the previous goalie requires an additional saved snapshot. A replacement-selection policy requires an explicit fallback when no replacement is available. Rehiring before expiry and retention effects such as Handcuffs also need defined behavior before temporary contracts ship.

No gameplay validation is claimed. This conclusion is based on source, metadata, and focused native control-flow review; no live game or user save was modified.
