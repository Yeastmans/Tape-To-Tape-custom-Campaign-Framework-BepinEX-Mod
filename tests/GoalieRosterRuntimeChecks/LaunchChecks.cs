using System.Reflection;
using CampaignCore;
using Data;
using EndlessMode;
using State;
using Tape2Tape;

static class LaunchChecks
{
    internal static object Hook(string name, params object[] args) {
        var method = typeof(GoaliePersistenceBridge).Assembly.GetType("EndlessMode.GoalieMatchLaunchGate")?.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
        try { return method == null ? null : method.Invoke(null, args); }
        catch (TargetInvocationException e) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
    }
    static GoalieRosterRuntime Roster => GoaliePersistenceBridge.Roster;
    static (CampaignState Campaign, MatchPreviewMenu Menu) Setup(bool deferred = true, bool contract = true) {
        ES3.FailMarker = false; ES3.FailStore = false; ES3.WrongMarkerReadback = false; ES3File.FailPhysicalRead = false;
        ES3.AdmissionError = null; ES3.AfterStore = null;
        Plugin.ModFolder = Path.Combine(Path.GetTempPath(), "ccf-launch-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Plugin.ModFolder);
        Plugin.IsDefaultMode = false; T2TUtils.IsInMatch = false; GameManager.GameType = ScriptableStates.GameType.Rogue;
        BridgeDoubleHooks.Enable();
        var goalie = new GoaltenderData { id = "launch-goalie", OriginalId = "career-owner" };
        var run = new RunData { runID = "launch-run", squadId = "launch-squad", currentMap = "{\"act\":1,\"path\":[]}", CurrentAct = 1,
            teamData = new() { id = "home", goalie = goalie }, teamState = new() { goaltenderState = goalie.SaveState() } };
        var campaign = CampaignUtil.CampaignState = new() { runData = run };
        Roster.Fresh(run, Plugin.ModFolder, true, contract ? GoalieContractPolicy.Sign(4) : null);
        campaign.Repository.CompleteImmediately = !deferred; campaign.Repository._fileName = "launch-" + Guid.NewGuid().ToString("N") + ".es3";
        var menu = new MatchPreviewMenu { _campaignState = campaign, currentMapNode = new STS.Map.MapNode() };
        menu.ShowMenu(menu.currentMapNode); return (campaign, menu);
    }
    static void Finish(CampaignState campaign) {
        var source = campaign.Repository.Pending[0]; campaign.Repository.Pending.RemoveAt(0);
        campaign.Repository.Complete(source); GoaliePersistenceBridge.Tick();
    }
    static void FinishAll(CampaignState campaign) {
        int remaining = 10;
        while (campaign.Repository.Pending.Count > 0 && remaining-- > 0) Finish(campaign);
        if (remaining <= 0) throw new Exception("Cancellation save loop");
    }
    static void Reload(CampaignState campaign) {
        var prior = campaign.runData;
        var model = campaign.Repository.LoadFromPersistentStorage().GetAwaiter().GetResult();
        var loaded = new RunData { runID = model.runID, squadId = model.squadId, currentMap = prior.currentMap, CurrentAct = prior.CurrentAct,
            currentNodePoint = new() { x = prior.currentNodePoint.x, y = prior.currentNodePoint.y },
            teamData = new() { id = prior.teamData.id, goalie = new DeserializerV2().ConvertGoaltenderDataModelToGoaltenderData(model.team.skaters[0]) }, teamState = model.teamState };
        GoaliePersistenceBridge.Hydrated(new DeserializerV2(), loaded, model); campaign.runData = loaded;
    }
    static STS.Map.MapNode OtherDestination() { var node = new STS.Map.MapNode(); node.Node.NodeIndex = 6; return node; }
    static void CancellationChecks(Action<bool, string> check) {
        var (eventCampaign, eventMenu) = Setup();
        void CancelDuringOrigin(GoalieMatchLaunchGate.LaunchOrigin _) => eventMenu.OnCancelInput(new());
        GoalieMatchLaunchGate.LaunchAuthorized += CancelDuringOrigin;
        eventMenu.StartMatch(); FinishAll(eventCampaign); GoalieMatchLaunchGate.LaunchAuthorized -= CancelDuringOrigin;
        check(eventMenu.Starts == 0 && Roster.Selection.Attempt.Lifecycle == "CanceledBeforeLaunch",
            "CancellationDuringOriginHandoffCannotReachNativeStartup");
        var (campaign, menu) = Setup(); menu.StartMatch(); string old = Roster.Selection.ArmedAttemptToken;
        menu.OnCancelInput(new()); menu.ShowMenu(OtherDestination()); menu.StartMatch();
        check(campaign.Repository.Saves == 1 && menu.Starts == 0, "NewPreviewCannotOvertakePendingCanceledArmSave");
        Finish(campaign); // arm completion schedules the cancellation save through same coordinator
        menu.StartMatch();
        check(campaign.Repository.Saves == 2 && Roster.Busy && menu.Starts == 0, "PendingCancellationSaveSuppressesReplacementClicks");
        Finish(campaign); menu.StartMatch(); string newer = Roster.Selection.ArmedAttemptToken;
        check(newer != old && Roster.Selection.ProcessedAttemptTokens.Contains(old), "CancellationRetirementAndNewTokenShareOneProposedCheckpoint");
        Finish(campaign); check(menu.Starts == 1 && Roster.Selection.Active.Contract.PlayedMatches == 0, "PendingCancellationCannotAutoLaunchOldPreviewOrDebitIt");

        (campaign, menu) = Setup(); menu.StartMatch(); menu.OnCancelInput(new()); FinishAll(campaign); old = Roster.Selection.ArmedAttemptToken;
        var visit = new GoalieRecruitmentVisitState { Witness = "preserved-visit", Generation = Guid.NewGuid().ToString("N"), Quota = 2,
            ConsumedOfferIds = new[] { "offer-used" }, OfferedPoolIds = new[] { "goalie-pool" } };
        var withReserve = Roster.Selection; withReserve.Reserve = GoalieNativeSnapshot.Capture(new GoaltenderData { id = "reserve" });
        withReserve.Reserve.Contract = GoalieContractPolicy.Sign(7); withReserve.RecruitmentVisit = visit;
        Roster.Stage(campaign.runData, withReserve, new DeserializerV2()).Publish();
        check(Roster.TrySwap(campaign, Roster.Selection.Revision), "CanceledPreviewAllowsNormalBetweenMatchGoalieSwap"); FinishAll(campaign);
        menu.ShowMenu(menu.currentMapNode); menu.StartMatch();
        check(Roster.Selection.ArmedAttemptToken != old && Roster.Selection.Attempt.GoalieId == "reserve"
            && Roster.Selection.ProcessedAttemptTokens.Contains(old) && Roster.Selection.Active.Contract.PlayedMatches == 0
            && Roster.Selection.Reserve.Contract.PlayedMatches == 0 && Roster.Selection.RecruitmentVisit.ConsumedOfferIds[0] == "offer-used",
            "CanceledGoalieSwapCreatesNewIdentityPreservingBothContractsAndRecruitment");
        FinishAll(campaign); check(menu.Starts == 1, "ReplacementAfterGoalieSwapLaunchesOnlyAfterSave");

        foreach (string failure in new[] { "false", "fault", "cancel" }) {
            (campaign, menu) = Setup(); menu.StartMatch(); old = Roster.Selection.ArmedAttemptToken; menu.OnCancelInput(new());
            Finish(campaign); campaign.Repository.Outcome = failure; Finish(campaign);
            check(Roster.Selection.Attempt.Lifecycle == "CanceledBeforeLaunch" && !Roster.Busy && Roster.UnavailableReason == null,
                "LocallyCommittedCancellationSurvivesNativeFailure_" + failure);
            menu.ShowMenu(OtherDestination()); menu.StartMatch(); newer = Roster.Selection.ArmedAttemptToken; Finish(campaign);
            check(menu.Starts == 0 && Roster.Selection.ArmedAttemptToken == newer && newer != old
                && Roster.Selection.ProcessedAttemptTokens.Contains(old) && Roster.Selection.Active.Contract.PlayedMatches == 0,
                "LocallyCommittedReplacementFailureRetainsExactNewTokenAndRetiredOld_" + failure);
            campaign.Repository.Outcome = "true"; menu.StartMatch();
            check(Roster.Selection.ArmedAttemptToken == newer, "ReplacementRetryNeverMintsUnrelatedToken_" + failure);
            Finish(campaign); check(menu.Starts == 1, "ReplacementRetryLaunchesOnceAfterDurableReceipt_" + failure);
        }

        (campaign, menu) = Setup(); campaign.Repository.Outcome = "false"; menu.StartMatch(); Finish(campaign); old = Roster.Selection.ArmedAttemptToken;
        ES3.FailStore = true; menu.OnCancelInput(new()); ES3.FailStore = false;
        check(!Roster.Busy && Roster.Selection.Attempt.Lifecycle == "Armed" && Roster.Selection.ArmedAttemptToken == old,
            "ProvenNoWriteCancellationFailureRestoresOriginalArmedContext");
        campaign.Repository.Outcome = "true"; menu.ShowMenu(OtherDestination()); menu.StartMatch();
        check(menu.Starts == 0 && Roster.Selection.ArmedAttemptToken == old && Roster.Busy, "NextStartRetriesUnwrittenCancellationBeforeReplacement");
        FinishAll(campaign); menu.StartMatch(); FinishAll(campaign);
        check(menu.Starts == 1 && Roster.Selection.ArmedAttemptToken != old, "CancellationRecoveryThenReplacementCanLaunch");

        (campaign, menu) = Setup(); menu.StartMatch(); menu.OnCancelInput(new()); FinishAll(campaign); old = Roster.Selection.ArmedAttemptToken;
        ES3.FailStore = true; menu.ShowMenu(OtherDestination()); menu.StartMatch(); ES3.FailStore = false;
        check(Roster.Selection.Attempt.Lifecycle == "CanceledBeforeLaunch" && Roster.Selection.ArmedAttemptToken == old
            && !Roster.Selection.ProcessedAttemptTokens.Contains(old), "UnwrittenReplacementRollsBackBothRetirementAndNewContext");
        menu.StartMatch(); FinishAll(campaign); check(menu.Starts == 1 && Roster.Selection.ProcessedAttemptTokens.Contains(old), "RetryAfterUnwrittenReplacementRetiresCanceledTokenOnce");

        (campaign, menu) = Setup(); menu.StartMatch(); menu.OnCancelInput(new()); Finish(campaign);
        ES3File.FailPhysicalRead = true; Finish(campaign); ES3File.FailPhysicalRead = false;
        old = Roster.Selection.ArmedAttemptToken; menu.ShowMenu(OtherDestination()); menu.StartMatch();
        check(menu.Starts == 0 && Roster.UnavailableReason != null && Roster.Selection.ArmedAttemptToken == old
            && menu.m_DescriptionText.text.Contains("Continue"), "UnknownCancellationReadbackCannotAuthorizeReplacement");
        Reload(campaign); menu.ShowMenu(OtherDestination()); menu.StartMatch(); FinishAll(campaign);
        check(menu.Starts == 1 && Roster.Selection.ProcessedAttemptTokens.Contains(old), "ContinueReconcilesActuallySavedCancellationBeforeReplacing");

        (campaign, menu) = Setup(); menu.StartMatch(); menu.OnCancelInput(new()); FinishAll(campaign); old = Roster.Selection.ArmedAttemptToken;
        Reload(campaign);
        check(Roster.Selection.Attempt.Lifecycle == "CanceledBeforeLaunch", "DurableCancellationSurvivesNativeContinueHydration");
        menu.ShowMenu(OtherDestination()); menu.StartMatch(); FinishAll(campaign);
        check(menu.Starts == 1 && Roster.Selection.ArmedAttemptToken != old && Roster.Selection.Active.Contract.PlayedMatches == 0,
            "RecoveredCanceledContextCanBeReplacedWithoutDebit");
        var after = Roster.Selection;
        check(GoalieContractPolicy.CompleteMatch(after, old, new[] { "launch-goalie" }, true).Active.Contract.PlayedMatches == 0,
            "DelayedCompletionForRetiredCanceledTokenIsRunLevelNoOp");

        (campaign, menu) = Setup(); menu.StartMatch(); FinishAll(campaign); old = Roster.Selection.ArmedAttemptToken;
        menu.BackToMap(); menu.ShowMenu(menu.currentMapNode); menu.OnCancelInput(new());
        check(Roster.Selection.Attempt.Lifecycle == "Armed" && campaign.Repository.Saves == 1, "AlreadyPermittedOriginCannotBecomeCanceledPreview");
        menu.ShowMenu(OtherDestination()); menu.StartMatch();
        check(menu.Starts == 1 && Roster.Selection.ArmedAttemptToken == old && campaign.Repository.Saves == 1
            && menu.m_DescriptionText.text.Contains("may already have started"), "StartedOriginCannotBeSupersededByLaterPreviewCancel");

        (campaign, menu) = Setup(); campaign.Repository.Outcome = "false"; menu.StartMatch(); FinishAll(campaign); old = Roster.Selection.ArmedAttemptToken;
        Reload(campaign); menu.ShowMenu(menu.currentMapNode); menu.OnCancelInput(new()); menu.ShowMenu(OtherDestination()); menu.StartMatch();
        check(Roster.Selection.ArmedAttemptToken == old && Roster.Selection.Attempt.Lifecycle == "Armed" && campaign.Repository.Saves == 1
            && menu.Starts == 0 && menu.m_DescriptionText.text.Contains("may already have started"), "RecoveredArmedIntentCannotInventPrelaunchCancellationProof");
        campaign.Repository.Outcome = "true"; var originalNode = new STS.Map.MapNode(); menu.ShowMenu(originalNode); menu.StartMatch(); FinishAll(campaign);
        check(menu.Starts == 1 && Roster.Selection.ArmedAttemptToken == old, "AmbiguousRecoveredArmedIntentStillSupportsSameContextRetry");
    }
    internal static void Run(Action<bool, string> check) {
        var origins = new List<GoalieMatchLaunchGate.LaunchOrigin>();
        var receipts = new List<GoaliePersistenceBridge.SaveReceipt>();
        GoaliePersistenceBridge.SaveCompleted += receipts.Add;
        GoalieMatchLaunchGate.LaunchAuthorized += origins.Add;
        var (cancelCampaign, cancelMenu) = Setup();
        cancelMenu.StartMatch(); string canceledToken = Roster.Selection.ArmedAttemptToken;
        cancelMenu.OnCancelInput(new()); FinishAll(cancelCampaign);
        check(Roster.Selection.Attempt.Lifecycle == "CanceledBeforeLaunch" && Roster.Selection.Active.Contract.PlayedMatches == 0,
            "PendingLaunchCancelPersistsUnlaunchedLifecycleWithoutDebit");
        var changedDestination = new STS.Map.MapNode(); changedDestination.Node.NodeIndex = 6;
        cancelMenu.ShowMenu(changedDestination); cancelMenu.StartMatch();
        check(Roster.Selection.ArmedAttemptToken != canceledToken && Roster.Selection.ProcessedAttemptTokens.Contains(canceledToken)
            && Roster.Selection.Active.Contract.PlayedMatches == 0 && cancelMenu.Starts == 0,
            "CanceledDifferentDestinationReplacesTokenAndRetiresOldWithoutDebit");
        FinishAll(cancelCampaign); check(cancelMenu.Starts == 1, "SupersedingAttemptRequiresDurableSaveBeforeLaunch"); origins.Clear();
        var (campaign, menu) = Setup();
        menu.StartMatch();
        check(menu.Starts == 0 && !menu.m_IsExiting && campaign.Repository.Saves == 1,
            "ContractedPreviewWaitsBeforeNativeStartSideEffects");
        var armed = Roster.Selection;
        string token = armed.ArmedAttemptToken;
        check(armed.Attempt?.Token == token && armed.Attempt.Act == 1 && armed.Attempt.NodeIndex == 2 && armed.Attempt.LayerIndex == 3
            && armed.Attempt.GoalieId == "launch-goalie" && armed.Attempt.HomeTeamId == "home" && campaign.runData.currentNodePoint.x == 0,
            "AttemptCapturesIntendedDestinationBeforeNativeCurrentNodeChanges");
        menu.StartMatch(); menu.OnStartInput(new());
        check(campaign.Repository.Saves == 1 && Roster.Selection.ArmedAttemptToken == token && origins.Count == 0, "RepeatedClicksJoinOnePendingAttempt");
        Finish(campaign);
        check(menu.Starts == 1 && menu.InputStarts == 0 && origins.Count == 1 && Roster.Selection.Revision > 0, "DurableAttemptReentersOriginalStartExactlyOnce");
        menu.StartMatch();
        check(menu.Starts == 1 && campaign.Repository.Saves == 1, "AlreadyExitingStartCannotRunTwice");
        check(receipts.Last().Original.ResultReads == 1, "GateDoesNotConsumeNativeSaveTaskAgain");
        var original = origins.Last();
        check(original.Menu == menu && original.SourceGoalie == campaign.runData.teamData.goalie && original.Input == null, "LaunchOriginRetainsExactMenuAndSourceGoalieOwnership");
        campaign.runData.currentNodePoint.x = 2; campaign.runData.currentNodePoint.y = 3; // observed native SetCurrentNode
        check(GoalieMatchLaunchGate.TryReuseOrigin(original, campaign, campaign.runData.teamData), "RematchRetainsExplicitUnconsumedOrigin");
        campaign.runData.currentNodePoint.x = 9;
        check(!GoalieMatchLaunchGate.TryReuseOrigin(original, campaign, campaign.runData.teamData), "RematchCannotReuseOriginAtDifferentNativeDestination");
        campaign.runData.currentNodePoint.x = 2;
        check(!GoalieMatchLaunchGate.TryReuseOrigin(original, campaign, new TeamData { id = "home", goalie = campaign.runData.teamData.goalie }), "VisitorOrForeignOwnerCannotBorrowStableIds");
        check(GoalieMatchLaunchGate.MatchesDestination(original, campaign, menu.currentMapNode, campaign.runData.teamData)
            && !GoalieMatchLaunchGate.MatchesDestination(original, campaign, new(), campaign.runData.teamData), "DestinationObservationRequiresExactNativeOrigin");
        (campaign, menu) = Setup();
        var input = new Tape2Tape.Customization.UI.UIInputShortcutBase(); menu.OnStartInput(input); Finish(campaign);
        check(menu.Starts == 0 && menu.InputStarts == 1 && menu.LastInput == input && origins.Last().OriginEntry == GoalieMatchLaunchGate.Entry.OnStartInput,
            "InlinedInputGateReentersExactInputMethodAndArgument");
        (campaign, menu) = Setup(false); menu.StartMatch();
        check(menu.Starts == 1 && campaign.Repository.Saves == 1, "SynchronousSaveCompletionStillStartsOnlyOnce");
        foreach (string invalidation in new[] { "cancel", "back", "destroy", "reopen", "node", "run", "goalie", "hidden", "inactive", "exiting", "route", "squad" }) {
            (campaign, menu) = Setup(); menu.StartMatch(); token = Roster.Selection.ArmedAttemptToken;
            int emitted = origins.Count;
            switch (invalidation) {
                case "cancel": menu.OnCancelInput(new()); break;
                case "back": menu.BackToMap(); break;
                case "destroy": menu.OnDestroy(); break;
                case "reopen": menu.ShowMenu(menu.currentMapNode); break;
                case "node": menu.currentMapNode = new(); break;
                case "run": CampaignUtil.CampaignState = new(); break;
                case "goalie": campaign.runData.teamData.goalie = new() { id = "other" }; break;
                case "hidden": menu.IsShown = false; break;
                case "inactive": menu.gameObject.activeInHierarchy = false; break;
                case "exiting": menu.m_IsExiting = true; break;
                case "route": campaign.runData.currentMap = "{\"act\":1,\"path\":[{\"x\":1,\"y\":1}]}"; break;
                case "squad": campaign.runData.squadId = "changed"; break;
            }
            FinishAll(campaign);
            check(menu.Starts == 0 && origins.Count == emitted, "LateSaveCannotLaunchAfter_" + invalidation);
            if (invalidation is "cancel" or "reopen") {
                menu.ShowMenu(menu.currentMapNode); menu.StartMatch();
                check(Roster.Selection.ArmedAttemptToken == token && campaign.Repository.Saves == (invalidation == "cancel" ? 3 : 2), "CancelledOrReopenedPreviewReusesSavedAttempt_" + invalidation);
                Finish(campaign); check(menu.Starts == 1, "FreshClickAfterCancellationCanLaunch_" + invalidation);
            }
        }
        foreach (string failure in new[] { "false", "fault", "cancel" }) {
            (campaign, menu) = Setup(); campaign.Repository.Outcome = failure; menu.StartMatch(); token = Roster.Selection.ArmedAttemptToken; Finish(campaign);
            check(menu.Starts == 0 && Roster.Selection.ArmedAttemptToken == token && Roster.Selection.Revision > 0
                && menu.m_DescriptionText.text.Contains("saved locally"), "LocalCommitPlatformFailureKeepsAttemptAndCannotLaunch_" + failure);
            campaign.Repository.Outcome = "true"; menu.StartMatch();
            check(Roster.Selection.ArmedAttemptToken == token, "PlatformRetryReusesActuallySavedToken_" + failure);
            Finish(campaign); check(menu.Starts == 1, "PlatformRetryRequiresNewDurableReceipt_" + failure);
        }
        (campaign, menu) = Setup(); menu.StartMatch();
        var fake = new GoaliePersistenceBridge.SaveReceipt { CallerToken = new(), OperationId = "foreign", NativeResult = true, Reconciled = true,
            AttemptToken = Roster.Selection.ArmedAttemptToken, RunPointer = campaign.runData.Pointer, RepositoryPointer = campaign.Repository.Pointer,
            Admission = Roster.Selection, Checkpoint = Roster.Selection };
        var handlers = (Action<GoaliePersistenceBridge.SaveReceipt>)typeof(GoaliePersistenceBridge).GetField("SaveCompleted", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        handlers(fake);
        check(menu.Starts == 0 && Roster.Busy, "ForeignSavedOperationCannotAuthorizePendingLaunch");
        Finish(campaign); check(menu.Starts == 1, "ExactOperationCanStillCompleteAfterForeignReceipt");
        foreach (string changed in new[] { "receipt-token", "admission-token", "checkpoint-token", "checkpoint-id", "caller" }) {
            (campaign, menu) = Setup();
            void Corrupt(GoaliePersistenceBridge.SaveReceipt receipt) {
                if (changed == "receipt-token") receipt.AttemptToken = "unrelated";
                if (changed == "admission-token") receipt.Admission.ArmedAttemptToken = "unrelated";
                if (changed == "checkpoint-token") receipt.Checkpoint.ArmedAttemptToken = "unrelated";
                if (changed == "checkpoint-id") receipt.Checkpoint.CheckpointId = "";
                if (changed == "caller") receipt.CallerToken = null;
            }
            // Install before SaveChange's handler to model drift at an actual
            // receipt boundary, not an independently invented gate verdict.
            GoaliePersistenceBridge.SaveCompleted += Corrupt;
            menu.StartMatch(); Finish(campaign);
            GoaliePersistenceBridge.SaveCompleted -= Corrupt;
            check(menu.Starts == 0, "SuccessfulNativeSaveRequiresExactAttemptProof_" + changed);
            if (changed is "receipt-token" or "admission-token")
                check(!menu.m_DescriptionText.text.Contains("platform save failed"), "ProofMismatchDoesNotClaimPlatformFailure_" + changed);
        }
        (campaign, menu) = Setup(); menu.BeforeReentry = () => menu.OnStartInput(new()); menu.StartMatch(); Finish(campaign);
        check(menu.Starts == 1 && menu.InputStarts == 0, "UnrelatedNestedInputCannotStealStartPermit");
        (campaign, menu) = Setup(); menu.BeforeOriginal = () => menu.StartMatch(); menu.StartMatch(); Finish(campaign);
        check(menu.Starts == 1 && campaign.Repository.Saves == 1, "ConsumedPermitCannotAuthorizeNestedSecondNativeStart");
        (campaign, menu) = Setup(); ES3.FailMarker = true; menu.StartMatch(); ES3.FailMarker = false; Finish(campaign);
        check(menu.Starts == 0 && Roster.UnavailableReason != null, "NativeTrueWithoutMatchingCompanionCannotLaunch");
        check(menu.m_DescriptionText.text.Contains("Continue"), "UnknownPersistenceExplainsContinueRecoveryInPreview");
        (campaign, menu) = Setup(); menu.StartMatch(); ES3File.FailPhysicalRead = true; Finish(campaign); ES3File.FailPhysicalRead = false;
        check(menu.Starts == 0 && Roster.UnavailableReason != null, "UnknownReadbackNeverAuthorizesLaunch");
        menu.StartMatch(); check(campaign.Repository.Saves == 1 && menu.m_DescriptionText.text.Contains("Continue"), "UnknownReadbackDoesNotMintRetryToken");
        (campaign, menu) = Setup(); menu.StartMatch(); token = Roster.Selection.ArmedAttemptToken;
        menu.OnCancelInput(new()); FinishAll(campaign);
        var repo = campaign.Repository;
        var model = repo.LoadFromPersistentStorage().GetAwaiter().GetResult();
        var continued = new RunData { runID = model.runID, squadId = model.squadId, currentMap = campaign.runData.currentMap, CurrentAct = 1,
            teamData = new() { id = "home", goalie = new DeserializerV2().ConvertGoaltenderDataModelToGoaltenderData(model.team.skaters[0]) }, teamState = model.teamState };
        GoaliePersistenceBridge.Hydrated(new DeserializerV2(), continued, model);
        campaign.runData = continued; menu.ShowMenu(menu.currentMapNode); menu.StartMatch();
        check(Roster.Selection.ArmedAttemptToken == token && menu.Starts == 0, "ContinueRehydratesAttemptAndReturnsToGatedPreview");
        Finish(campaign); check(menu.Starts == 1 && !GoalieMatchLaunchGate.TryReuseOrigin(original, campaign, continued.teamData), "ContinueRequiresFreshLaunchOriginOldRunOriginRejected");
        var currentOrigin = origins.Last();
        var completion = GoalieContractPolicy.CompleteMatch(Roster.Selection, token, new[] { "launch-goalie" }, true);
        var mutation = Roster.Stage(continued, completion, new DeserializerV2()); mutation.Publish();
        check(completion.Attempt == null && !GoalieMatchLaunchGate.TryReuseOrigin(currentOrigin, campaign, continued.teamData), "ConsumedAttemptCannotBeReusedByRematch");
        menu.ShowMenu(menu.currentMapNode); menu.StartMatch(); string later = Roster.Selection.ArmedAttemptToken;
        check(later != token && Roster.Selection.ProcessedAttemptTokens.Contains(token), "LegitimateLaterAttemptIsDistinctAndRetainsRunReplayLedger");
        Finish(campaign);
        check(!GoalieMatchLaunchGate.TryReuseOrigin(currentOrigin, campaign, continued.teamData), "DelayedOldOriginCannotAttachToLaterToken");
        foreach (string bypass in new[] { "permanent", "base", "playnow", "zoom" }) {
            (campaign, menu) = Setup(contract: bypass != "permanent");
            if (bypass == "base") Plugin.IsDefaultMode = true;
            if (bypass == "playnow") GameManager.GameType = ScriptableStates.GameType.QuickPlay;
            if (bypass == "zoom") menu.m_TestZoomOnly = true;
            menu.StartMatch(); check(menu.Starts == 1 && campaign.Repository.Saves == 0, "NativeBehaviorPreserved_" + bypass);
        }
        CancellationChecks(check);
        Plugin.IsDefaultMode = false; GameManager.GameType = ScriptableStates.GameType.Rogue;
        GoalieMatchLaunchGate.LaunchAuthorized -= origins.Add;
        GoaliePersistenceBridge.SaveCompleted -= receipts.Add;
    }
}
namespace STS.Map {
    public class Node : Il2CppSystem.Object { public int LayerIndex = 3, NodeIndex = 2; public string nodeType = "Elite"; }
    public class MapNode : Il2CppSystem.Object { public Node Node = new(); }
}
namespace Tape2Tape.Customization.UI { public class UIInputShortcutBase : Il2CppSystem.Object {} }
class LaunchText { public string text; }
class LaunchGameObject { public bool activeInHierarchy = true; public void SetActive(bool active) {} }
class MatchPreviewMenu : Il2CppSystem.Object {
    public State.CampaignState _campaignState;
    public STS.Map.MapNode currentMapNode;
    public bool m_IsExiting, m_TestZoomOnly, IsShown = true;
    public LaunchGameObject gameObject = new(), m_DescriptionContainer = new();
    public LaunchText m_DescriptionText = new();
    public int Starts, InputStarts; private int startInvocations;
    public Action BeforeReentry, BeforeOriginal;
    public Tape2Tape.Customization.UI.UIInputShortcutBase LastInput;
    public void ShowMenu(STS.Map.MapNode node) { LaunchChecks.Hook("ShowPrefix", this); currentMapNode = node; m_IsExiting = false; IsShown = true; LaunchChecks.Hook("ShowPostfix", this); }
    public void StartMatch() { if (++startInvocations == 2) BeforeReentry?.Invoke(); if ((bool?)LaunchChecks.Hook("StartPrefix", this) == false) return; BeforeOriginal?.Invoke(); Starts++; m_IsExiting = true; }
    public void OnStartInput(Tape2Tape.Customization.UI.UIInputShortcutBase input) {
        if ((bool?)LaunchChecks.Hook("InputPrefix", this, input) == false || m_IsExiting) return; InputStarts++; LastInput = input; m_IsExiting = true;
    }
    public void BackToMap() { LaunchChecks.Hook("BackPrefix", this); IsShown = false; }
    public void OnCancelInput(Tape2Tape.Customization.UI.UIInputShortcutBase input) { LaunchChecks.Hook("CancelPrefix", this, input); if (!m_IsExiting) BackToMap(); }
    public void OnDestroy() { LaunchChecks.Hook("DestroyPrefix", this); IsShown = false; }
}
