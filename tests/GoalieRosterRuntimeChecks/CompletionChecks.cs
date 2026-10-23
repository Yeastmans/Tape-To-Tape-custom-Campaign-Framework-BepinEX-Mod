using System.Reflection;
using CampaignCore;
using Data;
using EndlessMode;
using State;
using State.GameModeControllers;
using State.GameModeControllers.NormalMatch;
using State.EndOfGame;
using Tape2Tape;
using Cysharp.Threading.Tasks;

static class CompletionChecks
{
    internal static object Call(string method, params object[] args) {
        var type = typeof(GoalieContractRuntime).Assembly.GetType("EndlessMode.GoalieMatchCompletion");
        try { return type?.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)?.Invoke(null, args); }
        catch (TargetInvocationException ex) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }
    sealed class Fixture {
        internal CampaignState Campaign; internal MatchPreviewMenu Menu; internal GameControllerBase Controller;
        internal GameManager Manager; internal EndOfGameContext Context;
    }
    static GoalieRosterRuntime Roster => GoaliePersistenceBridge.Roster;
    static Fixture Setup(int duration = 4, bool reserve = true, bool shootout = false) {
        Call("Subscribe");
        var (campaign, menu) = LaunchChecks.Setup(); RetentionRegistryFixture.Reset();
        var initial = Roster.Selection; initial.Active.Contract = duration == 0 ? null : GoalieContractPolicy.Sign(duration);
        if (reserve) {
            initial.Reserve = GoalieNativeSnapshot.Capture(new GoaltenderData { id = "bench-goalie", skill = 33 });
            initial.Reserve.Contract = GoalieContractPolicy.Sign(9);
        }
        initial.RecruitmentVisit = new() { Witness = "visit", Generation = Guid.NewGuid().ToString("N"), Quota = 2, ConsumedOfferIds = new[] { "used" }, OfferedPoolIds = new[] { "pool" } };
        Roster.Stage(campaign.runData, initial, new DeserializerV2()).Publish();
        menu.StartMatch(); LaunchChecks.Finish(campaign);
        object[] nodeArgs = { menu.currentMapNode, null }; Call("NodePrefix", nodeArgs);
        campaign.runData.currentNodePoint.x = 2; campaign.runData.currentNodePoint.y = 3;
        Call("NodeFinalizer", menu.currentMapNode, nodeArgs[1], true, null);
        GameControllerBase controller = shootout ? new State.GameModeControllers.Shootout.ShootoutGameModeController() : new NormalMatchGameModeController();
        controller.campaignState = campaign; controller.teamSelectionState = new() { homeTeam = campaign.runData.teamData, gameType = ScriptableStates.GameType.Rogue };
        var manager = GameManager.Instance = new() { _homeTeam = new() { _teamData = campaign.runData.teamData, goaltender = new() } };
        manager._homeTeam.goaltender.Team = manager._homeTeam; manager._homeTeam.isHomeTeam = true; manager._homeTeam.teamSelection = controller.teamSelectionState;
        var f = new Fixture { Campaign = campaign, Menu = menu, Controller = controller, Manager = manager };
        f.Context = new() { CampaignState = campaign, GameModeController = controller, GameType = ScriptableStates.GameType.Rogue, MatchNode = menu.currentMapNode.Blueprint };
        Call("FlowFinalizer", new EndOfGameFlowController { _context = f.Context, _campaignState = campaign,
            _teamSelection = controller.teamSelectionState, _gameModeController = controller }, true, null);
        Initialize(f); T2TUtils.IsInMatch = true; return f;
    }
    static void Initialize(Fixture f) {
        object[] init = { f.Controller, null }; Call("InitializePrefix", init);
        var first = GoalieNativeSnapshot.Restore(GoalieNativeSnapshot.Capture(f.Campaign.runData.teamData.goalie));
        object[] goalie = { f.Manager._homeTeam.goaltender, first, true, null }; Call("GoaliePrefix", goalie);
        var second = GoalieNativeSnapshot.Restore(GoalieNativeSnapshot.Capture(first));
        f.Manager._homeTeam.goaltender.goaltenderData = second; f.Manager._homeTeam.goaltender._skaterData = second;
        Call("GoalieFinalizer", f.Manager._homeTeam.goaltender, goalie[3], true, null);
        Call("InitializeFinalizer", init[1], true, null);
    }
    static void Side(Fixture f, bool replaceReceiver = false, string fault = null, Goaltender.Goaltender target = null) {
        var team = f.Manager._homeTeam;
        bool north = !team.isNorth;
        object[] args = { team, north, null }; Call("SidePrefix", args);
        team.isNorth = north;
        if (replaceReceiver) team.goaltender = new() { Team = team };
        if (target != null) { team.goaltender = target; target.Team = team; }
        var source = f.Campaign.runData.teamData.goalie;
        if (fault == "source") source = GoalieNativeSnapshot.Restore(GoalieNativeSnapshot.Capture(source));
        if (fault == "receiver-team") team.goaltender.Team = new();
        if (fault == "selection") team.teamSelection = new() { homeTeam = f.Campaign.runData.teamData, gameType = ScriptableStates.GameType.Rogue };
        object[] goalie = { team.goaltender, source, fault != "visitor", null };
        if (fault != "missing") Call("GoaliePrefix", goalie);
        var live = GoalieNativeSnapshot.Restore(GoalieNativeSnapshot.Capture(source));
        if (fault == "id") live.id = "other-goalie";
        team.goaltender.goaltenderData = live; team.goaltender._skaterData = live;
        if (fault != "missing") Call("GoalieFinalizer", team.goaltender, goalie[3], fault != "initialize-skip", fault == "initialize-throw" ? new Exception("init failed") : null);
        if (fault == "side") team.isNorth = !north;
        Call("SideFinalizer", args[2], fault != "skip", fault == "throw" ? new Exception("side failed") : null);
    }
    static void Play(Fixture f) { f.Manager.isGamePlaying = true; Call("NormalPlayFinalizer", f.Manager, true, null); }
    static void NextMatch(Fixture f) {
        T2TUtils.IsInMatch = false; f.Menu.ShowMenu(f.Menu.currentMapNode); f.Menu.StartMatch(); LaunchChecks.Finish(f.Campaign);
        object[] node = { f.Menu.currentMapNode, null }; Call("NodePrefix", node); Call("NodeFinalizer", f.Menu.currentMapNode, node[1], true, null);
        f.Manager = GameManager.Instance = new() { _homeTeam = new() { _teamData = f.Campaign.runData.teamData, goaltender = new() } };
        f.Context = new() { CampaignState = f.Campaign, GameModeController = f.Controller, GameType = ScriptableStates.GameType.Rogue, MatchNode = f.Menu.currentMapNode.Blueprint };
        Call("FlowFinalizer", new EndOfGameFlowController { _context = f.Context, _campaignState = f.Campaign,
            _teamSelection = f.Controller.teamSelectionState, _gameModeController = f.Controller }, true, null);
        Initialize(f); T2TUtils.IsInMatch = true;
    }
    static object Begin(Fixture f) { object[] args = { f.Context, null }; Call("UpdatePrefix", args); return args[1]; }
    static void Departure(Fixture f, string mode = "normal") {
        var manager = new Rogue.FreeAgents.FreeAgentManager { _campaignState = f.Campaign };
        object[] args = { manager, null }; Call("DeparturePrefix", args);
        var prefix = typeof(CustomRewardContractRetention).GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static);
        var pars = prefix.GetParameters().Length == 1 ? new object[] { manager } : new object[] { manager, true };
        bool original = false; Exception error = mode == "exception" ? new Exception("departure failed") : null;
        try { original = mode != "foreign-before" && (bool)prefix.Invoke(null, pars); }
        catch (TargetInvocationException ex) { error = ex.InnerException; }
        Call("DepartureFinalizer", manager, args[1], original && mode != "foreign-after", error);
    }
    static void End(Fixture f, object state, Exception error = null, UniTaskStatus status = UniTaskStatus.Succeeded) {
        Call("UpdateFinalizer", f.Context, state, new UniTask { Status = status }, true, error);
    }
    internal static void Run(Action<bool, string> check) {
        var originalError = new Exception("foreign failure");
        check((string)Call("CaptureSide", new Func<string>(() => throw new InvalidOperationException("diagnostic getter"))) == "snapshot-error:InvalidOperationException",
            "ThrowingDiagnosticCaptureCannotEscapeIntoHandoffDecision");
        check(ReferenceEquals(Call("InitializeFinalizer", null, false, originalError), originalError), "MissingInitializationScopePreservesNativeException");
        check(ReferenceEquals(Call("DepartureFinalizer", new Rogue.FreeAgents.FreeAgentManager(), null, false, originalError), originalError), "MissingDepartureScopePreservesNativeException");
        check(ReferenceEquals(Call("UpdateFinalizer", new EndOfGameContext(), null, new UniTask(), false, originalError), originalError), "MissingUpdateScopePreservesNativeException");
        check(ReferenceEquals(Call("SideFinalizer", null, false, originalError), originalError), "MissingSideScopePreservesNativeException");
        var f = Setup(); Play(f); var update = Begin(f); Departure(f);
        check(!Roster.TrySwap(f.Campaign, Roster.Selection.Revision), "CompletionBoundaryDoesNotEnableInMatchUiSwaps");
        f.Campaign.runData.teamData.goalie.skill = 99;
        End(f, update);
        check(Roster.Selection.Active.Contract.PlayedMatches == 1, "ProvenPlayThenSuccessfulPostBossUpdateDebitsExactStarter");
        LaunchChecks.Finish(f.Campaign);
        check(Roster.Selection.Reserve.Contract.PlayedMatches == 0 && Roster.Selection.Active != null, "BenchOnlyGoalieNeverDebited");
        check(f.Campaign.runData.teamData.goalie.skill == 99 && Roster.Selection.RecruitmentVisit.ConsumedOfferIds.Single() == "used", "FinalEarnedStateAndRecruitmentLedgerShareCompletionCheckpoint");
        int saves = f.Campaign.Repository.Saves;
        End(f, update); check(Roster.Selection.Active.Contract.PlayedMatches == 1 && f.Campaign.Repository.Saves == saves, "DuplicateUpdateFinalizerDoesNotSaveOrDebitAgain");
        var again = Begin(f); Departure(f); End(f, again);
        check(Roster.Selection.Active.Contract.PlayedMatches == 1 && f.Campaign.Repository.Saves == saves, "RepeatedCompletedUpdateContextIsNoOp");
        LaunchChecks.Reload(f.Campaign); again = Begin(f); Departure(f); End(f, again);
        check(Roster.Selection.Active.Contract.PlayedMatches == 1 && Roster.Selection.Attempt == null, "ReloadedCompletedTokenRejectsOldLiveContext");
        var old = f; var oldUpdate = update;
        f = Setup(); Play(f); update = Begin(f); Departure(f); End(old, oldUpdate);
        check(Roster.Selection.Active.Contract.PlayedMatches == 0 && Roster.Selection.Attempt != null && Roster.UnavailableReason == null, "DelayedOldCompletionCannotConsumeOrLockNewAttempt");
        End(f, update); LaunchChecks.Finish(f.Campaign);

        f = Setup(1); Play(f); update = Begin(f); var departing = f.Campaign.runData.teamData.goalie;
        Departure(f);
        check(f.Campaign.runData.teamData.goalie.Pointer == departing.Pointer && Roster.Selection.Active.Contract.PlayedMatches == 0, "NativeDeparturePhaseLeavesBossBonusRecipientInstalled");
        departing.skill = 110; End(f, update); LaunchChecks.Finish(f.Campaign);
        check(departing.skill == 110 && f.Campaign.runData.teamData.goalie.id == "bench-goalie" && f.Campaign.runData.teamData.goalie.skill == 33,
            "ExpiryPromotionOccursAfterBossBonusAndDoesNotAwardBenchedReserve");
        check(Roster.Selection.Active.Contract.Duration == 9 && Roster.Selection.Active.Contract.PlayedMatches == 0 && Roster.Selection.Reserve == null,
            "PromotionPreservesReserveSignedDurationWithoutRenewal");

        foreach (string mode in new[] { "no-play", "departure-missing", "foreign-before", "foreign-after", "exception", "update-throw", "pending", "faulted", "native-unknown", "custom-unknown" }) {
            f = Setup(1); if (mode != "no-play") Play(f); update = Begin(f);
            if (mode == "native-unknown") f.Campaign.runData.teamData.FailProtection = true;
            if (mode == "custom-unknown") CustomRewardCampaignRules.Fail = true;
            if (mode != "departure-missing") Departure(f, mode.StartsWith("foreign") || mode == "exception" ? mode : "normal");
            CustomRewardCampaignRules.Fail = false; f.Campaign.runData.teamData.FailProtection = false;
            End(f, update, mode == "update-throw" ? new Exception("boss failed") : null,
                mode == "pending" ? UniTaskStatus.Pending : mode == "faulted" ? UniTaskStatus.Faulted : UniTaskStatus.Succeeded);
            check(Roster.Selection.Active.Contract.PlayedMatches == 0 && Roster.Selection.Active.PlayerId == "launch-goalie"
                && Roster.Selection.Attempt != null && f.Campaign.Repository.Saves == 1, "UnverifiedCompletionNeverDebitsOrPromotes_" + mode);
            check(Roster.UnavailableReason?.Contains("Continue") == true, "UnverifiedCompletionBlocksLaterMismatchedAttempt_" + mode);
        }

        foreach (string protection in new[] { "native", "custom" }) {
            f = Setup(1); if (protection == "native") f.Campaign.runData.teamData.NativeHandcuffs = true;
            else RetentionRegistryFixture.SaveAuthoredProtection(f.Campaign.runData.teamData);
            Play(f); update = Begin(f); Departure(f);
            // Freeze actual departure phase: later changes cannot change that
            // enclosing update's protection result.
            f.Campaign.runData.teamData.NativeHandcuffs = false;
            f.Campaign.runData.teamData.RetentionOwnedIds.Clear();
            End(f, update); LaunchChecks.Finish(f.Campaign);
            check(Roster.Selection.Active.PlayerId == "launch-goalie" && Roster.Selection.Active.Contract.PlayedMatches == 1
                && Roster.Selection.Reserve.PlayerId == "bench-goalie", "ActualScopedProtectionKeepsExpiredStarter_" + protection);
            T2TUtils.IsInMatch = false;
            string token = Roster.Selection.Active.Contract.LastMatchKey;
            var priorContext = f.Context;
            NextMatch(f);
            var currentContext = f.Context; f.Context = priorContext;
            var stale = Begin(f); Departure(f); End(f, stale); f.Context = currentContext;
            check(Roster.Selection.Active.Contract.PlayedMatches == 1 && Roster.Selection.Attempt != null && Roster.UnavailableReason == null,
                "OldContextCannotBorrowReusedNativeController_" + protection);
            Play(f); update = Begin(f); Departure(f); End(f, update); LaunchChecks.Finish(f.Campaign);
            check(Roster.Selection.Active.PlayerId == "bench-goalie" && Roster.Selection.Active.Contract.PlayedMatches == 0
                && Roster.Selection.ProcessedAttemptTokens.Contains(token), "LaterProtectionRemovalReconcilesWithoutRenewal_" + protection);
        }
        f = Setup(1, false); Play(f); update = Begin(f); Departure(f); End(f, update); LaunchChecks.Finish(f.Campaign);
        check(Roster.Selection.Active.Contract.AwaitingReplacement && Roster.Selection.Active.Contract.PlayedMatches == 1 && Roster.Selection.Reserve == null,
            "EmptyReserveRetainsExpiredActiveWithDeferredReplacement");
        T2TUtils.IsInMatch = false;
        check(Roster.TryRecruit(f.Campaign, new GoaltenderData { id = "replacement" }, GoalieSlot.Reserve, Roster.Selection.Revision,
            signedContract: GoalieContractPolicy.Sign(6), expectedDeparturePrevention: false), "DeferredReplacementUsesExistingReviewedRecruitmentTransaction");
        LaunchChecks.Finish(f.Campaign);
        check(Roster.Selection.Active.PlayerId == "replacement" && Roster.Selection.Active.Contract.Duration == 6 && Roster.Selection.Reserve == null,
            "ReplacementCompletesDeferredDeparture");

        f = Setup(shootout: true); f.Manager.isGamePlaying = true; Play(f); update = Begin(f); Departure(f); End(f, update);
        check(Roster.Selection.Active.Contract.PlayedMatches == 0, "ShootoutPrematurePlayingSetterAndNormalEventDoNotProvePlay");
        f = Setup(shootout: true); f.Manager.isGamePlaying = true;
        var round = new State.GameModeControllers.Shootout.InRoundState { _gameModeController = (State.GameModeControllers.Shootout.ShootoutGameModeController)f.Controller };
        Call("ShootoutPlayFinalizer", round, true, null); Call("ShootoutPlayFinalizer", round, true, null);
        update = Begin(f); Departure(f); End(f, update); LaunchChecks.Finish(f.Campaign);
        check(Roster.Selection.Active.Contract.PlayedMatches == 1, "SuccessfulShootoutRoundEntryCountsOnceAcrossRounds");

        f = Setup(); Play(f); string restartToken = Roster.Selection.ArmedAttemptToken;
        var oldManager = f.Manager;
        Call("RestartFinalizer", f.Controller, true, null);
        f.Manager = GameManager.Instance = new() { _homeTeam = new() { _teamData = f.Campaign.runData.teamData, goaltender = new() } };
        f.Context = new() { CampaignState = f.Campaign, GameModeController = f.Controller, GameType = ScriptableStates.GameType.Rogue, MatchNode = f.Menu.currentMapNode.Blueprint };
        Call("FlowFinalizer", new EndOfGameFlowController { _context = f.Context, _campaignState = f.Campaign,
            _teamSelection = f.Controller.teamSelectionState, _gameModeController = f.Controller }, true, null);
        Initialize(f); Play(f); Call("NormalPlayFinalizer", oldManager, true, null);
        check(Roster.UnavailableReason == null, "DelayedOldManagerPlayDoesNotInvalidateCurrentRematch");
        update = Begin(f); Departure(f); End(f, update); LaunchChecks.Finish(f.Campaign);
        check(Roster.Selection.Active.Contract.PlayedMatches == 1 && Roster.Selection.Active.Contract.LastMatchKey == restartToken
            && f.Campaign.Repository.Saves == 2, "RematchReconstructionReusesOriginAndChargesOnceWithoutNewArm");

        foreach (bool cloneAgain in new[] { true, false }) {
        f = Setup(); Play(f);
        var loader = new GameLoader { campaignState = f.Campaign, teamSelectionState = f.Controller.teamSelectionState, _gameModeController = f.Controller };
        object[] transfer = { loader, null }; Call("SwitchPrefix", transfer);
        f.Controller = new State.GameModeControllers.Shootout.ShootoutGameModeController { campaignState = f.Campaign, teamSelectionState = loader.teamSelectionState };
        loader._gameModeController = f.Controller;
        f.Context = new() { CampaignState = f.Campaign, GameModeController = f.Controller, GameType = ScriptableStates.GameType.Rogue, MatchNode = f.Menu.currentMapNode.Blueprint };
        Call("FlowFinalizer", new EndOfGameFlowController { _context = f.Context, _campaignState = f.Campaign,
            _teamSelection = f.Controller.teamSelectionState, _gameModeController = f.Controller }, true, null);
        if (cloneAgain) Initialize(f); Call("SwitchFinalizer", transfer[1], true, null);
        f.Manager.isGamePlaying = true;
        round = new() { _gameModeController = (State.GameModeControllers.Shootout.ShootoutGameModeController)f.Controller };
        Call("ShootoutPlayFinalizer", round, true, null); update = Begin(f); Departure(f); End(f, update);
        check(Roster.Selection.Active.Contract.PlayedMatches == 1 && f.Campaign.Repository.Saves == 2, "OvertimeShootoutControllerRetainsOriginalAttemptAcrossReinjection_" + cloneAgain);
        LaunchChecks.Finish(f.Campaign);
        }

        foreach (bool replaceReceiver in new[] { false, true }) {
            f = Setup(1); Play(f); var token = Roster.Selection.ArmedAttemptToken;
            Side(f, replaceReceiver); Play(f); Side(f, replaceReceiver); Play(f);
            check(Roster.UnavailableReason == null && Roster.Selection.ArmedAttemptToken == token && Roster.Selection.Active.Contract.PlayedMatches == 0,
                "ReviewedNativeSideHandoffKeepsExactAttemptAcrossPeriods_" + replaceReceiver);
            update = Begin(f); Departure(f); End(f, update); LaunchChecks.Finish(f.Campaign);
            check(Roster.Selection.Active.PlayerId == "bench-goalie" && Roster.Selection.Active.Contract.PlayedMatches == 0
                && Roster.Selection.ProcessedAttemptTokens.Contains(token), "SideHandoffStillDebitsStarterOnceAndPromotesOnlyAtCompletion_" + replaceReceiver);
        }
        f = Setup(); Side(f); Play(f);
        check(Roster.UnavailableReason == null, "OwnedSideSetupBeforeFirstPlayPreservesBinding");
        var visitor = new Team { isHomeTeam = false, teamSelection = f.Controller.teamSelectionState, goaltender = new() };
        visitor.goaltender.Team = visitor;
        for (int period = 0; period < 3; period++) {
            var priorHome = f.Manager._homeTeam.goaltender;
            Side(f, target: visitor.goaltender);
            object[] visitorScope = { visitor, !visitor.isNorth, null }; Call("SidePrefix", visitorScope);
            visitor.isNorth = !visitor.isNorth; visitor.goaltender = priorHome; priorHome.Team = visitor;
            var visitorSource = new GoaltenderData { id = "native-opponent" };
            object[] visitorGoalie = { priorHome, visitorSource, false, null }; Call("GoaliePrefix", visitorGoalie);
            var visitorLive = GoalieNativeSnapshot.Restore(GoalieNativeSnapshot.Capture(visitorSource));
            priorHome.goaltenderData = visitorLive; priorHome._skaterData = visitorLive;
            Call("GoalieFinalizer", priorHome, visitorGoalie[3], true, null);
            Call("SideFinalizer", visitorScope[2], true, null); Play(f);
            check(Roster.UnavailableReason == null && Roster.Selection.Active.Contract.PlayedMatches == 0,
                "NativeSafeEventHomeThenVisitorAlternatesReceiversWithoutBorrowing_" + period);
        }
        update = Begin(f); Departure(f); End(f, update); LaunchChecks.Finish(f.Campaign);
        check(Roster.Selection.Active.Contract.PlayedMatches == 1, "PairedPeriodEventsStillDebitExactlyOnce");
        foreach (string fault in new[] { "source", "receiver-team", "selection", "visitor", "missing", "id", "side", "skip", "throw", "initialize-skip", "initialize-throw" }) {
            f = Setup(); Play(f); Plugin.Log.Warnings.Clear(); Side(f, fault: fault); Play(f);
            update = Begin(f); Departure(f); End(f, update);
            check(Roster.UnavailableReason != null && Roster.Selection.Active.Contract.PlayedMatches == 0,
                "UnprovenSideHandoffCannotRepairBinding_" + fault);
            check(Plugin.Log.Warnings.Any(x => x.Contains("[GoalieSide]") && x.Contains("rejected") && x.Contains("token=")),
                "RejectedSideHandoffPreservesPredicateEvidence_" + fault);
        }
        f = Setup(); Play(f);
        object[] failedSide = { f.Manager._homeTeam, true, null }; Call("SidePrefix", failedSide);
        check(ReferenceEquals(Call("SideFinalizer", failedSide[2], true, originalError), originalError), "OwnedSideFailurePreservesNativeException");
        check(typeof(GoalieContractRuntime).Assembly.GetType("EndlessMode.GoalieMatchCompletion")
            .GetField("sideHandoff", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null) == null,
            "FailedSideFinalizerRestoresThreadScopeBeforeAnyFixtureReset");
        f = Setup(); Play(f); Side(f); Play(f);
        check(Roster.UnavailableReason == null, "FailedSideScopeRestoresBeforeIndependentOwnedHandoff");
        // Opponent switching cannot claim or invalidate the home attempt.
        object[] opponentSide = { new Team(), true, null }; Call("SidePrefix", opponentSide);
        Call("SideFinalizer", opponentSide[2], true, null); Play(f);
        check(Roster.UnavailableReason == null, "UnownedOpponentSideLeavesHomeBindingIntact");
        f = Setup(); Play(f); var live = f.Manager._homeTeam.goaltender.goaltenderData;
        Plugin.Log.Warnings.Clear();
        live.id = "illegal-in-match-goalie"; Play(f); live.id = "launch-goalie";
        update = Begin(f); Departure(f); End(f, update);
        check(Roster.Selection.Active.Contract.PlayedMatches == 0 && Roster.UnavailableReason != null, "ChangedLiveParticipantCannotBeRepairedIntoFalseCompletionProof");
        check(Plugin.Log.Warnings.Any(x => x.Contains("[GoalieIdentity]") && x.Contains("reason=live-id")
            && x.Contains("expectedId=launch-goalie") && x.Contains("observedId=illegal-in-match-goalie")),
            "RejectedLiveIdentityReportsExactStableIdMismatch");

        f = Setup(); Play(f); Plugin.Log.Warnings.Clear();
        var unrelatedClone = GoalieNativeSnapshot.Restore(GoalieNativeSnapshot.Capture(f.Manager._homeTeam.goaltender.goaltenderData));
        f.Manager._homeTeam.goaltender.goaltenderData = unrelatedClone;
        f.Manager._homeTeam.goaltender._skaterData = unrelatedClone;
        Play(f);
        check(Roster.UnavailableReason != null && Roster.Selection.Active.Contract.PlayedMatches == 0
            && Plugin.Log.Warnings.Any(x => x.Contains("[GoalieIdentity]") && x.Contains("reason=live-data")),
            "UnprovenSameIdCloneStillRejectedAndIdentifiesChangedDataPointer");

        foreach (string invalid in new[] { "first-clone", "second-clone", "visitor", "failed-initialize", "failed-play", "foreign-play" }) {
            f = Setup();
            if (invalid == "failed-play" || invalid == "foreign-play") {
                f.Manager.isGamePlaying = true;
                Call("NormalPlayFinalizer", f.Manager, invalid != "foreign-play", invalid == "failed-play" ? new Exception("faceoff failed") : null);
            } else {
                object[] init = { f.Controller, null }; Call("InitializePrefix", init);
                var first = GoalieNativeSnapshot.Restore(GoalieNativeSnapshot.Capture(f.Campaign.runData.teamData.goalie));
                if (invalid == "first-clone") first.id = "different-first";
                object[] goalieArgs = { f.Manager._homeTeam.goaltender, first, invalid != "visitor", null }; Call("GoaliePrefix", goalieArgs);
                var second = GoalieNativeSnapshot.Restore(GoalieNativeSnapshot.Capture(f.Campaign.runData.teamData.goalie));
                if (invalid == "second-clone") second.id = "different-live";
                f.Manager._homeTeam.goaltender.goaltenderData = second; f.Manager._homeTeam.goaltender._skaterData = second;
                Call("GoalieFinalizer", f.Manager._homeTeam.goaltender, goalieArgs[3], true, null);
                Call("InitializeFinalizer", init[1], true, invalid == "failed-initialize" ? new Exception("init failed") : null);
                Play(f);
            }
            update = Begin(f); Departure(f); End(f, update);
            check(Roster.Selection.Active.Contract.PlayedMatches == 0 && Roster.Selection.Attempt != null,
                "InvalidLiveProvenanceCannotChargeArmedIntent_" + invalid);
        }

        f = Setup(0); Play(f); update = Begin(f); Departure(f); End(f, update); LaunchChecks.Finish(f.Campaign);
        check(Roster.Selection.Active.Contract == null && Roster.Selection.Reserve.Contract.PlayedMatches == 0 && Roster.Selection.Attempt == null,
            "PermanentActiveAndSignedBenchConsumePlayedAttemptWithoutInventingContract");
        f = Setup(); Play(f);
        f.Menu.currentMapNode.Blueprint = new(); f.Context.MatchNode = f.Menu.currentMapNode.Blueprint;
        update = Begin(f); Departure(f); End(f, update);
        check(Roster.Selection.Active.Contract.PlayedMatches == 0 && f.Campaign.Repository.Saves == 1,
            "ReplacedNativeBlueprintCannotRewriteOriginatingCompletionContext");
        f = Setup(); var expiredReserve = Roster.Selection; expiredReserve.Reserve.Contract.PlayedMatches = 9;
        Roster.Stage(f.Campaign.runData, expiredReserve, new DeserializerV2()).Publish();
        Play(f); update = Begin(f); Departure(f); End(f, update); LaunchChecks.Finish(f.Campaign);
        check(Roster.Selection.Reserve == null && Roster.Selection.Active.Contract.PlayedMatches == 1, "ExpiredBenchDepartsWithoutAnotherParticipationDebit");

        foreach (string outcome in new[] { "false", "fault", "cancel", "no-write", "unknown" }) {
            f = Setup(); Play(f); update = Begin(f); Departure(f);
            GoaliePersistenceBridge.SaveReceipt receipt = null;
            void Observe(GoaliePersistenceBridge.SaveReceipt value) { if (value.RepositoryPointer == f.Campaign.Repository.Pointer) receipt = value; }
            GoaliePersistenceBridge.SaveCompleted += Observe;
            if (outcome == "no-write") ES3.FailStore = true;
            else if (outcome != "unknown") f.Campaign.Repository.Outcome = outcome;
            End(f, update); ES3.FailStore = false;
            if (outcome == "unknown") ES3File.FailPhysicalRead = true;
            if (f.Campaign.Repository.Pending.Count > 0) LaunchChecks.Finish(f.Campaign);
            GoaliePersistenceBridge.SaveCompleted -= Observe;
            ES3File.FailPhysicalRead = false;
            if (outcome == "no-write") check(Roster.Selection.Active.Contract.PlayedMatches == 0 && Roster.Selection.Attempt != null
                && Roster.UnavailableReason != null, "NoWriteCompletionRollsBackDebitAndRequiresExactRecovery");
            else if (outcome == "unknown") check(Roster.UnavailableReason != null && !Roster.CanEdit(f.Campaign), "UnknownCompletionCannotAuthorizeLaterMatchOrClaimDurableSuccess");
            else {
                check(Roster.Selection.Active.Contract.PlayedMatches == 1 && Roster.Selection.Attempt == null && Roster.UnavailableReason == null,
                    "LocalCompletionPersistsDespiteActualNativeFailure_" + outcome);
                check(receipt.Reconciled && !receipt.Durable && !receipt.NativeResult
                    && (outcome == "false" ? receipt.NativeError == null : receipt.NativeError != null)
                    && receipt.Checkpoint.Active.Contract.PlayedMatches == 1 && receipt.Checkpoint.Attempt == null,
                    "CompletionReceiptPreservesPlatformResultAndExactLocalPair_" + outcome);
            }
        }
        T2TUtils.IsInMatch = false;
    }
}
