# Task4b native dependency manifest (review candidate)

Pinned build26085704: GameAssembly SHA256 `92E73E6D0BE549DD8C695302B7AAE6A77CBBF4DE27F27D26ED01F4B3DABC5B8D`; metadata `1009F63178E260153CBBF224338BDC9124F9468D0F462F7A58327F638188914A`.

Authority: canonical `artifacts/followup-four-features-20261020/goalie-match-start-proof.md`, `goalie-match-start-evidence-20261020a/index.json`, `wrapper-metadata.json`, and `runtime-build-context.md`. These completed proofs were reused, not rediscovered. OnDestroy address/signature was read from the same pinned `reward-review/dump/script.json` lines20110-20113; its native body was not recaptured/audited in this unit. No live native execution or game validation.

## Declared manual hooks, not installed

Feature `goalie-match-launch`, assembly `MainScriptsAssembly`, declaring type `MatchPreviewMenu`, all instance System.Void methods. Source: GoalieMatchLaunchGate.Required/Resolve. There are no HarmonyPatch attributes and no startup installer call; final profile unchanged.

| Exact method | Native RVA | Native ABI after this | Prefix / postfix |
| --- | --- | --- | --- |
| StartMatch() |0x41B6370 | const MethodInfo* | StartPrefix / none |
| OnStartInput(Tape2Tape.Customization.UI.UIInputShortcutBase) |0x41B6D30 | UIInputShortcutBase_o*, const MethodInfo* | InputPrefix / none |
| ShowMenu(STS.Map.MapNode) |0x41B1CB0 | MapNode_o*, const MethodInfo* | ShowPrefix / ShowPostfix |
| BackToMap() |0x41B65A0 | const MethodInfo* | BackPrefix / none |
| OnCancelInput(Tape2Tape.Customization.UI.UIInputShortcutBase) |0x41B6E40 | UIInputShortcutBase_o*, const MethodInfo* | CancelPrefix / none |
| OnDestroy() |0x41B1AE0 | const MethodInfo* | DestroyPrefix / none |

Windows x64 generated managed wrapper + Harmony method resolution, no new raw function-pointer detour, generic shared-RVA detour or boxed-struct call. Prefixes use __instance and __0; ShowPostfix observes successful normal return. Both start entries are required because input inlines the full startup body. Reentry calls the same generated public StartMatch()/OnStartInput(originalArg) wrapper under a one-use scoped permit, leaving native tutorial/navigation/loading/zoom behavior to the original body. m_IsExiting is never assigned by this gate. Native Cancel remains callable throughout saving.

## Native reads/callables/UI dependencies

- MatchPreviewMenu properties: `_campaignState` (CampaignState), `currentMapNode` (MapNode), `m_IsExiting`/`m_TestZoomOnly` (bool), `IsShown` (bool, getter RVA0x41B1830), `gameObject.activeInHierarchy`. Exact generated fields/pointers remain wrapper-managed; retained proof offsets include node+0x170, campaign+0x1A8, exiting+0x1EA, zoomTest+0x208. Native IsShown body was present in retained41b1830.asm and composes menu/native UI visibility with exit state. The gate also tracks its own ShowMenu generation and explicit cancellation/destruction observations.
- UI recovery writes: `m_DescriptionText` (TMPro.TMP_Text).text and `m_DescriptionContainer` (UnityEngine.GameObject).SetActive(bool). These reuse the existing preview description area. Actual rendered/readable placement/controller behavior remains human validation; source-linked checks use boundary doubles. Existing custom-opponent ShowMenu postfix can also update this area, so final Harmony ordering must be checked.
- `STS.Map.MapNode.Node` -> `NodeIndex` and `LayerIndex`; `GetIl2CppType().FullName` copies exact native destination type. `CampaignState.runData.CurrentAct`, `currentNodePoint.x/y`, `currentMap`, `runID`, `squadId` are used for route/destination validation. The existing RecruitmentWitness parser supplies the prelaunch route witness but does not reuse RecruitmentVisit/consumption as attempt identity. No early SetCurrentNode call is added.
- `CampaignState.runData.teamData` exact native ownership, `Data.TeamData.id`, `teamData.goalie`, `GoaltenderData.id`; SourceGoalie reference and team ownership remain in runtime origin. These are source ownership reads, not live participation proof. GameManager.GameType/Rogue, T2TUtils.IsInMatch and CampaignUtil.CampaignState are existing between-match scope guards reused through CanEdit.
- Save path unchanged: GoalieRosterRuntime.Stage/SaveChange -> GoaliePersistenceBridge.SaveCampaign -> native CampaignState.SaveRunData() RVA0x3F28240 -> exact scoped repository SaveToPersistentStorage(model) Task<bool> RVA0x3F50810 -> existing persistent MatchRuleRunner/bridge Tick and local marker reconciliation. New GoalieMatchAttempt Core file is linked into runtime single DLL. No additional native asynchronous consumer, pooled UniTask, game coroutine or coroutine-host lifetime is introduced.

## Coherent activation and known limits

Task4b now implements the user-approved canceled-preview supersession policy and is ready for independent review. Resolve-only hooks remain off pending final combined integration. Final combined activation must require reviewed4a signing + checkpoint/menu save sets + both4b start entries and lifecycle observations +4c origin/live goalie/play/post-boss completion sets atomically. A profile containing only some sets is insufficient. Capture exceptions or missing proof must leave goalie completion unavailable rather than invent provenance.

Task4c must capture LaunchAuthorized origin, then verify expected native node selection and exact campaign home initialization before binding a live goalie. Preview event emission does not mean native original finished. TryReuseOrigin requires the explicit supplied origin and current selected destination; it cannot infer identity on repeated scene/controller initialization. Direct node/scene entry without this origin is unowned and must not debit. The completed proof only covers inspected native bodies/direct-call scans, not every indirect/third-party/debug launch. Nothing here claims to block all such paths. No new native match completion, rematch or actual-play hook is added in4b; those are4c dependencies. No contract debit occurs from launch.

Final audit must verify OnDestroy native body/aliases, all property ABI structure, actual Harmony ordering and single-use native wrapper reentry on the pinned build. The explicit fresh InteropLive+InteropDir candidate build demonstrates callable wrapper structure only. Existing task2b native queue/profile/dependency checks and task4a exact typed protection/readiness requirements remain necessary.

## Approved cancellation delta

No additional native hook, ABI, dependency or pooled-task consumer was introduced for cancellation. The existing OnCancelInput/BackToMap prefixes now record explicit cancellation only while retaining runtime proof that the token never received startup permission. They immediately invalidate the preview; the native Cancel/Back body remains callable. Cancellation uses existing SaveCampaign/SaveRunData and the same scoped save receipt protocol. If an arm save is already pending, its reconciliation callback schedules the cancellation save through the existing queue. CanceledBeforeLaunch is a persisted immutable lifecycle value, and changed-context replacement retires the old key and saves a new key atomically without debit. Same-context retry rearms the same key. An actually permitted origin or recovered ambiguous Armed record is not relabeled canceled merely because a later preview closes.

Final native/human checks must include ordinary input Cancel and direct BackToMap during pending arm save, asynchronous cancellation after preview scene/menu exit, local-write/platform-failure cancellation, rapid reopen/new destination, between-match goalie swap, Continue with saved cancellation, and the displayed limitation for ambiguous Armed records after process loss. The same persistent bridge host owns saves after the preview closes; no preview-owned coroutine was added. Managed source-linked checks validate these state/receipt boundaries but do not prove native navigation or rendering. Reentry validates ownership again after the4c origin handoff subscribers; cancellation inside that synchronous handoff cannot run the original startup body. ProcessedAttemptTokens includes retired canceled keys, so4c must not treat replay membership as evidence of contract progress.
