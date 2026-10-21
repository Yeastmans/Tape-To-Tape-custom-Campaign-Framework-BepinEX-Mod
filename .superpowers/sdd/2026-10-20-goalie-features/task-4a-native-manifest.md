# Task 4a native dependency and hook manifest

Pinned build: 26085704. GameAssembly SHA256 `92E73E6D0BE549DD8C695302B7AAE6A77CBBF4DE27F27D26ED01F4B3DABC5B8D`; metadata SHA256 `1009F63178E260153CBBF224338BDC9124F9468D0F462F7A58327F638188914A`. Evidence: canonical `artifacts/followup-four-features-20261020/goalie-contract-native-research.md`, its adjacent `goalie-contract-native-evidence/index.json`, and `goalie-match-start-proof.md`. No repeated disassembly or native execution performed in this unit.

## New direct callable

- Exact managed call: `System.Boolean Data.TeamData.HaveRelicCached<Rogue.Handcuffs>()`, instance receiver is the exact canonical `CampaignState.runData.teamData`. Shared reference generic body RVA `0xED8E10`. The Handcuffs specialization is chosen by the closed generic MethodInfo, as in native FreeAgentManager.HandleFreeAgentsLeaving at `0x3F08A0A..0x3F08A14`. Never detour that shared RVA globally for goalie contracts.
- Its cache-miss dependency is `System.Boolean Data.TeamData.HaveRelic<Rogue.Handcuffs>()`, shared body `0xED8CF0` (cache-miss call at `0xED8F48`). The predicate uses actual native type/cache identity, not names, authored IDs, or display text. The existing CustomRewardTeamCapabilities shared-generic detour routes other exact MethodInfos and must preserve Handcuffs native behavior.
- New call occurs only inside `GoalieContractRuntime.TryDepartureProtection` after the existing checkpoint compatibility gate, default-mode rejection, exact current CampaignState pointer and exact bound run/folder ownership. Exceptions or unavailability return unknown (`false` from Try), never an unprotected decision. The boolean out value is valid only when Try returns true.
- Compile evidence: new closed generic call compiles against the fresh reviewed InteropLive/InteropDir. This is structural evidence only; native closed-generic invocation, cache invalidation and coexistence with current native detours remain final native validation obligations.

## Existing hook bodies modified, no new hooks installed

- Manually gated `GoaliePersistenceContracts.Required` id `goalie-fresh`: `System.Void State.CampaignState.ApplyNewRunStartEffects()` postfix `GoaliePersistenceBridge.FreshPostfix`. Existing exact named gate is unchanged. It now freezes selected squad goalie authoring into the new roster and ignores repeated effects callbacks for the same already-owned run. No call from hydration/Continue signs contracts.
- Existing attribute hook: `System.Void Rogue.FreeAgents.FreeAgentManager.HandleFreeAgentsLeaving()` at `0x3F087F0`, prefix `CustomRewardContractRetention.Prefix`. Extracted `Prevents(Data.TeamData)` is exactly the existing custom `Owned(team,"Contract expiry").Any(KeepFreeAgents)` decision. Prefix still returns its negation. Adapter reuses the same method for deferred reserve signing. Existing `_campaignState -> runData -> teamData` field access and custom ownership evaluator remain dependencies. No new manager ordering or event callback is installed here.
- Existing manually gated menu `GoalieRosterMenu` hooks and save/persistence hooks are unchanged. Pick now evaluates the shared decision before pre-confirmation and again at commit; `TryRecruit` checks the expected decision and uses existing Stage/SaveChange and exact receipt reconciliation. No separate save mechanism was introduced.

## Final integration obligations

The current native profile is unchanged; checkpoint and menu hooks remain unavailable until final exact-set compatibility integration. Do not release a profile that enables partial4a as complete contracts. Audit the new typed Handcuffs call and modified existing hook bodies together with4b/4c. No native freeAgents registration was added. No match-start, completion, participation or post-boss hook was added in4a.

For4c, the reusable evaluator supplies current known native/custom protection, but the manager's actual custom prefix skip must still be captured explicitly in its scoped phase (an arbitrary `__runOriginal == false` is not protection). Do not promote until successful whole CampaignUpdate completion after boss awards. The 4a policy accepts only the exact armed key or a known processed duplicate;4b must supply durable authority and4c proven live ownership/participation.
