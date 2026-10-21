# Task 3 native menu and visit dependency manifest

Pinned Windows x64 build 26085704. Static metadata/body evidence and source-linked boundary doubles only; no game, native fixture or rendered UI execution.

## Identity and gate

GameAssembly SHA256 `92E73E6D0BE549DD8C695302B7AAE6A77CBBF4DE27F27D26ED01F4B3DABC5B8D`; metadata SHA256 `1009F63178E260153CBBF224338BDC9124F9468D0F462F7A58327F638188914A`. Inputs and exact method signatures come from canonical `artifacts/game-update-26085704-20261020/reward-review/dump/{dump.cs,script.json}`, with body captures under this checkout's `artifacts/four-features/goalie-task3-research/`. Task2b manifest remains applicable for snapshots, ES3, repositories, typed Tasks and compatibility support.

`GoalieRosterMenu.Required` is the authoritative seven-contract manifest, feature `goalie-roster-menu`, assembly `MainScriptsAssembly`. These hooks are manually installed, so attribute-only audits miss them. `EntryLabel` calls `Enabled`, which attempts installation only after the persistence bridge is enabled; it loads the live paired NativeCompatibility profile, requires the exact feature set, resolves all targets, then patches, rolling back partial installation. Existing initial-lineup postfix supplies this entry. There is no HarmonyPatch class attribute on the new menu. No profile has been enabled/modified in this task. Final integration must review/add BOTH Task2b and this feature's complete dependencies through the existing protected profile flow.

## Manual hooks (all return System.Void)

| ID suffix | Exact declaring type / method / managed parameters | Native RVA | Patch |
| --- | --- | --- | --- |
| init | Tape2Tape.Hockey.UI.LineupMenu.InitData(Il2CppSystem.Object) | 0x43861B0 | Initialize postfix |
| cards | Tape2Tape.Hockey.UI.LineupMenu.LoadCurrentTeam() | 0x43887D0 | Refresh postfix |
| pick | Tape2Tape.Hockey.UI.LineupMenu.OnTogglePlayerCard(Tape2Tape.Customization.UI.SkaterCard) | 0x438ACD0 | Pick prefix |
| back | Tape2Tape.Hockey.UI.LineupMenu.TryLeaveMenu(System.Boolean) | 0x4388690 | Leave prefix |
| recruit | Tape2Tape.Hockey.UI.EventChoiceMenu.OnItemClicked(BishopGames.UI.AbstractNavigable) | 0x4333180 | RecruitClick prefix |
| legacy-open | EventChoicePanel.ShowMenu(STS.Map.MapNode,STS.Map.EventNode) | 0x41AEFE0 | LegacyShown postfix |
| legacy-close | EventChoicePanel.HideMenu() | 0x41AEA40 | LegacyLeave prefix |

Each ID is prefixed `goalie-menu-`. `EventChoicePanel` is in the global namespace. Existing `LineupInitialRoster` InitData postfix is retained and only adds `GoalieRosterMenu.EntryLabel` AFTER `FillInactiveLine`; existing inactive-line policy is unchanged. Existing GoalieRecruitment SetCurrentNode/Initialize/NextChoices patches still select the authored goalie-only node and produce offers; this task replaces their direct goalie assignment callback with a pending selection.

## Semantic native calls beyond hook targets

| Exact call (namespace as above unless qualified) | RVA / evidence |
| --- | --- |
| LineupMenu.SelectFirstPlayer() -> void | 0x438B9A0; body selects first active card Navigable through native NavigationController |
| SkaterCardList.Refresh(Il2CppSystem.Collections.Generic.IReadOnlyList<SkaterDataForUI>) -> void | 0x44D2880; body removes prior card listeners, creates/refreshes cards, attaches native click/toggle listeners, calls RefreshNavigation at 0x44D2F55 |
| SkaterCardList.Clear() -> void | 0x44D1FB0 |
| SkaterCardList.RefreshNavigation(bool) -> void (indirect) | 0x44D26B0; async MoveNext 0x44D4070; owns layout/navigation refresh |
| SkaterDataForUI(Data.TeamData,Data.SkaterData,ESkaterPosition,Skaters.Injuries.InjurySystem,Rogue.PowerUp,EStatBonusesPreviewMode,Data.CustomStatBonusesData) | 0x44D5BA0; explicit goalie slot including null reserve; exact wrapper compiles |
| SkaterCard.SetLocked(bool,bool) -> void | 0x44CDBA0 |
| SkaterCard.get_CurrentSkaterData() -> SkaterDataForUI; get_Toggle() -> BishopGames.UI.Toggle | 0x44CD400; 0x4069750 |
| TeamRosterData.UpdateTemporaryTeamData() -> void | 0x4501150; refreshes pending lineup preview from m_G and existing forwards |
| EventChoiceMenu.SetVisibility(bool,bool) -> void | 0x4331020 |
| EventChoiceMenu.SelectDefaultItem() -> void | 0x432D650 |
| EventChoiceMenu.GoToNextSetOfChoices() -> Cysharp.Threading.Tasks.UniTaskVoid | 0x432C430, state MoveNext 0x43604D0; refresh transition after explicit NextChoices |
| EventChoiceMenu.Exit() -> void | 0x432C910, state MoveNext 0x4358C60 |
| Rogue.NPC.GeneralManagerNPC.GeneralManagerEvent.NextChoices(int) -> bool | 0x3EED4A0; existing goalie-only replacement prefix supplies offers |
| EventChoicePanel.InitializeChoicesButtons(Il2CppSystem.Collections.Generic.List<EventChoice>) -> void; ClearChoicesButtons() -> void | 0x41AF4C0; 0x41AF970 |
| State.CampaignState.SaveRunData() -> void | 0x3F28240; TeamData.SaveState call 0x3F28398 replaces teamState BEFORE repository save |
| GameManager.get_GameType() -> ScriptableStates.GameType | 0x401F800; require Rogue, alongside existing Tape2Tape.T2TUtils.IsInMatch and Plugin.IsDefaultMode checks |
| T2TUIModule.ShowMenuAsync<LineupMenu>(BishopGames.Catalogs.MenuDefinitionCatalogReference,Il2CppSystem.Object) -> Cysharp.Threading.Tasks.UniTask<LineupMenu> | shared reference body 0xEB7660 (`<object>` in dump); source uses generated closed generic wrapper, no RVA delegate |

Menu open additionally uses `BishopModule.Get<UIModule>()`, `Cast<T2TUIModule>()`, `MenuDefinitionCatalogReference.Lineup`, `LineupMenu.Metadata` (Team, LockPlayers, IsHomeTeam), and the returned UniTask's single `GetAwaiter`, `IsCompleted`, `GetResult` through generated wrappers. Existing native menu coroutine host uses `MonoBehaviour.StartCoroutine` and BepInEx WrapToIl2Cpp. This is one consumer of the pooled UniTask, never Task conversion or duplicate await. Final native fixture must exercise the actual generic instantiation and cancellation/destruction timing; compilation alone does not establish them.

Other UI/support dependencies, all through generated accessors (no raw offsets):

- `LineupMenu.Team`, `m_ActiveSkaters`, `m_BenchSkaters`, `m_ActivePlayersList`, `m_BenchPlayersList`, `m_HideBench`, `m_Header.m_Title`; metadata offsets respectively 0x1F0,0x200,0x208,0x170,0x178,0x218,0x108.
- `SkaterCardList.m_SkaterCards`, cards' `Navigable`, `CurrentSkaterData.ActivePosition`, `Toggle.IsSelected`, `Toggle.SetSelected(bool,bool)`, `m_PositionLetter` (0xE0), `m_SkaterName` (0xD0), `m_FreeAgentDurationContainer` (0x70), `m_FreeAgentDuration` (0x78); TMP_Text.text and GameObject.SetActive(bool).
- `TeamRosterData.OriginalTeamData`, `m_G` (0x50), `UpdateTemporaryTeamData`; do not apply TeamRosterData to canonical ownership or discard its pending forward edits.
- `AbstractMenu.IsHiding` getter 0x9EA330; `OnHidingCompleted` and `OnDestroy` SingleEvent<AbstractMenu>.Register(Il2CppSystem.Action<AbstractMenu>,string) / Unregister(Il2CppSystem.Action<AbstractMenu>). Delegates are retained for exact unregister; close/destroy finalization is idempotent.
- `EventChoiceMenu.SourceNode`, `IgnoreInput`, `m_Items` / item Navigable and ChoiceData (interactable, Skater), `m_IsTakenOver`, `m_MenuOverSelf`, `m_ChoicesMade`, `m_IsExiting`; `GeneralManagerEvent.NumTimesToOfferChoices` and `eventChoices`; legacy `_canvasGroup` alpha/interactable/blocksRaycasts.
- Actual Unity Object Instantiate<TeamData>, Destroy(Object), FindObjectOfType<LineupMenu>, Il2Cpp List<SkaterDataForUI>/IReadOnlyList casts, wrapper Pointer equality, GoaltenderData.FullName, CampaignUtil.CampaignState and CampaignState.runData/currentNode; existing snapshot/restore delegates own the goalie graphs and supplements. No guessed singleton repository or direct native thunk.
- New persistence correlation reads exact `CampaignState._runDataRepository.Pointer`; receipt captures admitted full roster clone (scope + long revision), run pointer and repository pointer. No new persistence hooks; prior physical readback reuses Task2b `GoalieNativeCheckpoint.Persisted` APIs.

All generic/accessor/core Unity calls above must be included in final structural/native review; a generated wrapper whole-file hash is provenance only, per Task2b. The static dump does not give a distinct body for every closed generic instantiation. No unobserved RVA is invented for those calls.

## Persisted visit witness evidence

`CampaignState.SetCurrentNode(MapNode)` RVA 0x3F28E70 sets blueprint/currentNodePoint/lastNodePoint. `GeneralManagerEvent.Initialize` RVA 0x3EED3A0 clears old names/choices and starts NextChoices; native menu choice count is not persisted. EventChoiceMenu.Exit MoveNext invokes `MapPlayerTracker.SendPlayerToNode` at 0x4358F49. That method RVA 0x38C2980 appends the completed Point to CurrentMap.path, calls `MapManager.SaveMap` at 0x38C2A63 and updates RunData.currentMap at 0x38C2A78. SaveMap RVA 0x38ABA20 serializes Map (act,nodes,path) with Newtonsoft; Point x/y are integers. Captures: CampaignState-SetCurrentNode.asm, GeneralManagerEvent-Initialize.asm, lifecycle-0x4358c60.asm, SendPlayerToNode.asm, MapManager-SaveMap.asm.

Therefore hires saved before GM continuation share persisted route path. The witness canonicalizes persisted map.act + run.CurrentAct + currentNodePoint + ordered persisted path points; folder/run/squad are the containing roster identity. It ignores full map JSON formatting, nodes and unrelated reward fields, so those cannot mint a fresh quota. New route/path is a new visit generation; same coordinates are not permanently blacklisted. Invalid/missing serialized route fails closed, never fabricates migration. Actual runtime serialization/lifecycle remains a required final native fixture.

## Pending native evidence

Mouse/controller rendering and navigation, two-step confirmation clarity, empty-slot rendering, retained UIModule menu timing, real SingleEvent/hide/destroy callbacks, late native completion/reload, real map witness JSON and actual local store/reconciliation remain unexecuted. The source-linked checks model these boundaries and the compile establishes fresh-wrapper API compatibility, not those native behaviors.
