# Task 4c native dependency manifest

Pinned build 26085704. GameAssembly SHA256 `92E73E6D0BE549DD8C695302B7AAE6A77CBBF4DE27F27D26ED01F4B3DABC5B8D`; metadata SHA256 `1009F63178E260153CBBF224338BDC9124F9468D0F462F7A58327F638188914A`. Consumed canonical `artifacts/followup-four-features-20261020/goalie-contract-native-research.md`, `goalie-match-start-proof.md` and their referenced annotated bodies and pinned dump. No native capture or game run was performed in this unit.

## Resolve-only targets

Assembly is MainScriptsAssembly. All targets are instance methods. Feature is `goalie-match-completion`; IDs use `goalie-completion-` plus the first column. Prefix/finalizer names and exact parameter/return names are authoritative in GoalieMatchCompletion.Required. All void targets use `System.Void`.

| ID | Native managed target | RVA | Patches |
| --- | --- | --- | --- |
| boss | STS.Map.BossMapNode.LaunchMatch() | 0x38AEC10 | NodePrefix / NodeFinalizer |
| challenge | STS.Map.ChallengeMapNode.LaunchMatch() | 0x38B0740 | NodePrefix / NodeFinalizer |
| elite | STS.Map.EliteMapNode.LaunchMatch() | 0x38B1AA0 | NodePrefix / NodeFinalizer |
| initialize | State.GameModeControllers.GameControllerBase.InitializeGoaltenders() | 0x3F912F0 | InitializePrefix / InitializeFinalizer |
| goalie | Goaltender.Goaltender.Initialize(Data.GoaltenderData,System.Boolean) | 0x3C15610 | GoaliePrefix / GoalieFinalizer |
| faceoff | GameManager.OnFaceoff(Events.FaceoffArgs) | 0x4022790 | NormalPlayFinalizer |
| puckdrop | GameManager.OnPuckDropLaunch() | 0x40227F0 | NormalPlayFinalizer |
| shootout | State.GameModeControllers.Shootout.InRoundState.OnEnter() | 0x3FCA7E0 | ShootoutPlayFinalizer |
| normal-restart | State.GameModeControllers.NormalMatch.NormalMatchGameModeController.OnRestart() | 0x3FDC200 | RestartFinalizer |
| shootout-restart | State.GameModeControllers.Shootout.ShootoutGameModeController.OnRestart() | 0x3FD0F10 | RestartFinalizer |
| overtime | GameLoader.SwitchToShootout(System.Int32) | 0x401ACC0 | SwitchPrefix / SwitchFinalizer |
| departure | Rogue.FreeAgents.FreeAgentManager.HandleFreeAgentsLeaving() | 0x3F087F0 | DeparturePrefix / DepartureFinalizer |
| context | State.EndOfGame.EndOfGameFlowController.Initialize() | 0x3FE0D50 | FlowFinalizer |
| update | State.EndOfGame.Steps.CampaignUpdateStep.Execute(State.EndOfGame.EndOfGameContext) : Cysharp.Threading.Tasks.UniTask | 0x3FE94A0 | UpdatePrefix / UpdateFinalizer |

14 targets, 8 prefixes, 14 finalizers. They are not installed here. Final profile must include actual runtime evidence/aliases and validate all tuple patches, not just Required strings. Prefixes and finalizers require Priority.First as described in task-4c-interfaces.md; audit actual foreign patch ordering and `__runOriginal`/`__state`/`__exception` injection. Finalizers preserve the original exception. A shared generic method is not detoured.

## Body dependencies reused

- Boss/Challenge/Elite LaunchMatch selects current node before scene initialization; their LoadGame states remain native. Preview authorization is still the complete reviewed 4b pair; node hooks cannot authorize an unpermitted origin. Inherited/custom launch methods that do not traverse these exact bodies remain outside proven coverage.
- GameControllerBase.InitializeGoaltenders first clones canonical TeamSelection.homeTeam.goalie. Goaltender.Initialize then clones its input again and assigns `goaltenderData` and inherited `_skaterData`. North.Initialize (0x3C20F00) calls base; no duplicate hook. Native Unity Instantiate generic bodies are dependencies of this native path, not new direct calls.
- Normal GameManager callbacks set playing true after faceoff/puck drop. Shootout pre-round playing setter is premature; only InRoundState.OnEnter qualifies. No new setter hook.
- Normal restart's native load state is 0x3FDC710; shootout restart reenters pregame. Overtime wrapper 0x401ACC0 is an async-void entry; its state-machine body 0x401DDC0 in existing canonical evidence performs controller replacement/reinjection (0x401E2AF onward), retains the scene, then starts overtime (0x401E68F onward). Successful wrapper return is only a provenance handoff, not async completion/play proof. Final audit must include the wrapper and this state machine; this unit did not add a new body capture for the wrapper.
- EndOfGameFlowController.Initialize allocates `_context` at 0x3FE0DF2, sets CampaignState (+0x20) and GameModeController (+0x38), then assigns/registers its flow. Context binding uses that successful initialization, not a completion-time current token. Exact runtime allocation/initialization order remains native validation.
- EndOfGameContext.MatchNode is a **MatchBlueprint**, not STS.Map.Node. MapNode.Blueprint (+0x48) is frozen at accepted native launch; the node's mutable Blueprint reference cannot rewrite old completion identity. CampaignUpdate dispatches MatchNode's concrete blueprint mapNode (+0x80) OnGameEnd. Earned copy 0x3FE97C0 precedes base event; Boss.OnGameEnd 0x38AF570 awards +10 skill/+10 standingSpeed/+5 butterflySpeed after base event; CampaignUpdateStep returns after remaining bookkeeping.
- Existing typed `Data.TeamData.HaveRelicCached<Rogue.Handcuffs>()` and cache-miss HaveRelic dependencies from 4a remain required (shared bodies 0xED8E10 / 0xED8CF0). Exact custom source readiness/ownership from final reviewed 4a remains required. No missing call or swallowed loader failure supplies false.
- Existing CampaignState.SaveRunData, injected repository/task, native graph, ES3 profile and co-flushed marker dependencies are unchanged from Task2b/3. No CampaignSaveStep patch and no UniTask result consumption were introduced.

## Wrapper fields and callable structure

Fresh interop compilation checks all direct references. Final startup audit must additionally validate native structure/semantics for:

- MapNode.Node/Blueprint/Pointer and the 4b origin properties; native current coordinates/type are validated by 4b.
- GameControllerBase.campaignState and teamSelectionState; TeamSelection.homeTeam/gameType; canonical TeamData.goalie/id and GoaltenderData.id.
- GameManager.Instance, `_homeTeam`, isGamePlaying, GameType and Pointer; home Team.goaltender; Goaltender.goaltenderData and inherited `_skaterData`. GameManager.Instance uses the existing MonoSingleton generic getter; it is called, not patched.
- InRoundState._gameModeController; GameLoader.campaignState/teamSelectionState/_gameModeController; native TryCast<ShootoutGameModeController> for the retained-goalie overtime path.
- EndOfGameFlowController._context/_campaignState/_teamSelection/_gameModeController; EndOfGameContext.GameType/CampaignState/GameModeController/MatchNode/Pointer; IGameModeController.Pointer/TryCast; controller GetIl2CppType().FullName for normal play classification.
- FreeAgentManager._campaignState/Pointer; exact existing custom prefix now receives Harmony `bool __runOriginal` and reports its unchanged actual allowed/prevented decision. Existing default-mode short circuit remains unchanged. The event is unobserved until Subscribe.
- Cysharp.Threading.Tasks.UniTask.Status and UniTaskStatus.Succeeded. Status is checked only on the pinned synchronous native CampaignUpdate return, without GetAwaiter/GetResult or a second consumer.

No current compatibility profile, startup installer, native gameplay fixture, installed payload or shipped receipt was changed. See report for the final candidate hash and source-linked limits.
