using CampaignCore;
using Data;
using EndlessMode;
using State;
using Tape2Tape;

static class TransitionChecks
{
    internal static void Run(Action<bool, string> check)
    {
        Plugin.ModFolder = Path.Combine(Path.GetTempPath(), "ccf-transitions-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Plugin.ModFolder);
        Plugin.IsDefaultMode = false; BridgeDoubleHooks.Enable();
        var active = new GoaltenderData { id = "starter", skill = 81, Earned = new() { Id = "starter", talentStates = new() { new CounterState { Id = "counter", Earned = 14 } } } };
        var run = new RunData { runID = "run", squadId = "squad", teamData = new() { goalie = active }, teamState = new() { goaltenderState = active.SaveState() } };
        var campaign = CampaignUtil.CampaignState = new() { runData = run };
        var roster = GoaliePersistenceBridge.Roster;
        check(campaign._runDataRepository.Pointer != campaign.Repository.Pointer
            && campaign._runDataRepository.TryCast<RunDataRepository>()._runDataRepositoryV2 == campaign.Repository,
            "NativeRepositoryFacadeAndStorageHaveDistinctIdentity");
        GoalieNativeSnapshot.Deserializer = new();
        roster.Fresh(run, Plugin.ModFolder, false);
        var candidate = new GoaltenderData { id = "candidate", skill = 92 };
        check(!roster.TryRecruit(campaign, candidate, GoalieSlot.Reserve, 0) && run.teamData.goalie == active, "ReserveDisabledRejectsReserveWithoutMutation");
        check(roster.TryRecruit(campaign, candidate, GoalieSlot.Active, 0) && run.teamData.goalie.id == "candidate" && roster.Selection.Reserve == null, "ReserveDisabledReplacesOnlyStarter");
        check(!roster.TryRecruit(campaign, active, GoalieSlot.Active, 0), "DoubleClickCommitsOnce");
        roster.Fresh(run, Plugin.ModFolder, true);
        check(roster.TryRecruit(campaign, active, GoalieSlot.Reserve, 0) && run.teamData.goalie.id == "candidate" && roster.Selection.Reserve.PlayerId == "starter", "ChooseEmptyReserve");
        long revision = roster.Selection.Revision;
        check(roster.TrySwap(campaign, revision) && run.teamData.goalie.id == "starter" && ((CounterState)run.teamData.goalie.Earned.talentStates[0]).Earned == 14 && roster.Selection.Reserve.PlayerId == "candidate", "SwapPreservesEarnedState");
        T2TUtils.IsInMatch = true;
        check(!roster.TrySwap(campaign, roster.Selection.Revision), "InMatchCannotSwap"); T2TUtils.IsInMatch = false;
        GameManager.GameType = ScriptableStates.GameType.QuickPlay; T2TUtils.IsOnWorldMap = false;
        check(!roster.TrySwap(campaign, roster.Selection.Revision), "PlayNowCannotSwap"); GameManager.GameType = ScriptableStates.GameType.Rogue; T2TUtils.IsOnWorldMap = true;
        // Reentrant unrelated saves can notify before the initiating caller's
        // admission callback, but must never steal its scoped caller token.
        var outsider = new RunDataRepositoryV2 { _fileName = "other.es3" };
        bool reentered = false;
        void Unrelated(GoaliePersistenceBridge.SaveReceipt receipt) {
            if (reentered) return; reentered = true;
            outsider.SaveToPersistentStorage(new() { runID = run.runID, squadId = run.squadId,
                team = new() { skaters = new() { new SerializerV2().SerializeToModel(run.teamData.goalie) } }, teamState = run.teamState });
        }
        GoaliePersistenceBridge.SaveAdmitted += Unrelated;
        GoalieRosterRuntime.ChangeResult completion = null;
        check(roster.TryRecruit(campaign, new() { id = "reentrant" }, GoalieSlot.Reserve, roster.Selection.Revision, result => completion = result), "ReentrantSaveTransitionAccepted");
        GoaliePersistenceBridge.SaveAdmitted -= Unrelated;
        check(Plugin.Log.Infos.Any(line => line.StartsWith("[GoalieSave]") && line.Contains("outcome=Committed")
            && line.Contains("reconciled=True") && line.Contains("operation=")), "CommittedResultDiagnosticIncludesExactReceipt");
        check(completion?.Outcome == GoalieRosterRuntime.ChangeOutcome.Committed && completion.Receipt.RepositoryPointer == campaign.Repository.Pointer
            && outsider.Saves == 1, "ForeignReentrantAdmissionCannotStealExactSaveReceipt");
        var originalFacade = campaign._runDataRepository;
        int saves = campaign.Repository.Saves; var before = roster.Selection;
        foreach (var unsupported in new IRunDataRepository[] { new(), new RunDataRepository() }) {
            campaign.RepositoryFacade = unsupported;
            check(!roster.TryRecruit(campaign, new() { id = "unsupported-facade" }, GoalieSlot.Reserve, before.Revision)
                && campaign.Repository.Saves == saves && roster.Selection.Revision == before.Revision
                && roster.Selection.Reserve.PlayerId == before.Reserve.PlayerId && !roster.Busy,
                "UnknownOrMissingNestedRepositoryRejectsBeforeMutation");
        }
        campaign.RepositoryFacade = originalFacade;
    }
}
