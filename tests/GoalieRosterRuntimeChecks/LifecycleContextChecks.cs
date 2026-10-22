using CampaignCore;
using Data;
using EndlessMode;
using State;
using Tape2Tape;

static class LifecycleContextChecks
{
    internal static void Run(Action<bool, string> check)
    {
        Plugin.ModFolder = Path.Combine(Path.GetTempPath(), "ccf-worldmap-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Plugin.ModFolder);
        Plugin.IsDefaultMode = false; BridgeDoubleHooks.Enable();
        var goalie = new GoaltenderData { id = "map-starter" };
        var campaign = CampaignUtil.CampaignState = new CampaignState { runData = new RunData {
            runID = "worldmap-run", squadId = "map-squad", teamData = new() { goalie = goalie }, teamState = new() { goaltenderState = goalie.SaveState() } } };
        var roster = GoaliePersistenceBridge.Roster;
        roster.Fresh(campaign.runData, Plugin.ModFolder, true);
        GameManager.GameTypeUnavailable = true; T2TUtils.IsInMatch = false; T2TUtils.IsOnWorldMap = true;
        check(roster.CanEdit(campaign), "WorldMapEditingWorksWithoutMatchManager");
        T2TUtils.IsOnWorldMap = false;
        check(!roster.CanEdit(campaign), "PlayNowAndTitleCannotBorrowRetainedCampaignRoster");
        T2TUtils.IsOnWorldMap = true;
        check(!roster.CanEdit(new CampaignState { runData = campaign.runData }), "WorldMapStillRequiresExactCampaignOwner");
        roster.NoSelectedRun();
        var manager = new RunManager { CampaignState = campaign };
        FreshRunLifecycle.MapPrefix(manager);
        check(roster.Selection == null, "EmptyMapAndNoSelectedRunAreNotFreshAuthority");
        FreshRunLifecycle.ResetPostfix(campaign, null, true);
        check(roster.Selection == null, "NativeResetWitnessDefersCaptureUntilStartingEffectsComplete");
        CustomRewardRunStart.Completing = state => state.runData.teamData.goalie.skill++;
        goalie.skill = 94; SquadBehaviorRuntime.ReserveEnabled = true;
        FreshRunLifecycle.MapPrefix(manager);
        check(roster.Selection?.ReserveEnabled == true && roster.Selection.Active.PlayerId == goalie.id, "WitnessedMapCreationBindsReserveBeforeFirstSave");
        check(GoalieNativeSnapshot.Restore(roster.Selection.Active).skill == 95, "FreshCaptureIncludesNativeAndSharedStartingEffects");
        campaign.SaveRunData();
        check(ES3File.persisted.ContainsKey(campaign.Repository._fileName), "FirstNativeMapSaveHasBoundRoster");
        var model = campaign.Repository.LoadFromPersistentStorage().GetAwaiter().GetResult();
        GoaliePersistenceBridge.Hydrated(new(), campaign.runData, model);
        roster.NoSelectedRun();
        FreshRunLifecycle.MapPrefix(manager);
        check(roster.Selection == null, "ContinueCannotReuseConsumedResetWitnessEvenWithEmptyMap");
        CustomRewardRunStart.Completing = null;
        int calls = CustomRewardRunStart.Calls;
        foreach (string rejection in new[] { "same-object", "same-id", "skipped-reset", "hydrate", "foreign-manager", "foreign-global", "folder", "run-replaced" }) {
            var old = campaign.runData;
            var fresh = new RunData { runID = Guid.NewGuid().ToString("N"), squadId = old.squadId, teamData = old.teamData, teamState = old.teamState };
            campaign.runData = fresh;
            if (rejection == "same-object") old = fresh;
            if (rejection == "same-id") fresh.runID = old.runID;
            FreshRunLifecycle.ResetPostfix(campaign, old, rejection != "skipped-reset");
            string folder = Plugin.ModFolder;
            var selected = CampaignUtil.CampaignState;
            var target = manager;
            if (rejection == "hydrate") FreshRunLifecycle.HydratePrefix(fresh);
            if (rejection == "foreign-manager") target = new RunManager { CampaignState = new CampaignState { runData = fresh } };
            if (rejection == "foreign-global") CampaignUtil.CampaignState = new CampaignState { runData = fresh };
            if (rejection == "folder") Plugin.ModFolder = folder + "-other";
            if (rejection == "run-replaced") campaign.runData = new RunData { runID = fresh.runID };
            FreshRunLifecycle.MapPrefix(target);
            check(CustomRewardRunStart.Calls == calls && roster.Selection == null, "FreshProvenanceRejects_" + rejection);
            Plugin.ModFolder = folder; CampaignUtil.CampaignState = selected;
        }
        var previousRun = campaign.runData;
        campaign.runData = new RunData { runID = Guid.NewGuid().ToString("N"), squadId = "final-squad", teamData = new() { goalie = goalie }, teamState = new() { goaltenderState = goalie.SaveState() } };
        FreshRunLifecycle.ResetPostfix(campaign, previousRun, true);
        var finalGoalie = new GoaltenderData { id = "final-selected-squad" };
        campaign.runData.teamData = new() { goalie = finalGoalie };
        campaign.runData.teamState = new() { goaltenderState = finalGoalie.SaveState() };
        SquadBehaviorRuntime.ReserveEnabled = false;
        FreshRunLifecycle.MapPrefix(manager);
        check(roster.Selection.Active.PlayerId == finalGoalie.id && !roster.Selection.ReserveEnabled && roster.Selection.Active.Contract == null,
            "FinalSquadReplacementAndBothOptionsOffStillBindOnce");
        FreshRunLifecycle.MapPrefix(manager);
        check(CustomRewardRunStart.Calls == calls + 1, "FreshEffectsAndContractsCompleteExactlyOnce");
        roster.NoSelectedRun();
        FreshRunLifecycle.ResetPostfix(campaign, null, true);
        FreshRunLifecycle.ResetPrefix(campaign, out var abortedResetPrior); // native reset then throws: no postfix
        FreshRunLifecycle.MapPrefix(manager);
        check(CustomRewardRunStart.Calls == calls + 1 && roster.Selection == null && abortedResetPrior == campaign.runData,
            "ThrowingResetCannotRetainEarlierFreshWitness");
        GoaliePersistenceBridge.SetEnabled(false);
        previousRun = campaign.runData;
        campaign.runData = new RunData { runID = Guid.NewGuid().ToString("N"), squadId = "final-squad", teamData = previousRun.teamData, teamState = previousRun.teamState };
        FreshRunLifecycle.ResetPostfix(campaign, previousRun, true); FreshRunLifecycle.MapPrefix(manager);
        check(CustomRewardRunStart.Calls == calls + 2 && roster.Selection == null, "StartingConsumerIndependentOfGoalieActivation");
        GoaliePersistenceBridge.SetEnabled(true);
        CustomRewardRunStart.Completing = null; GameManager.GameTypeUnavailable = false;
    }
}
