using System.Reflection;
using CampaignCore;
using Data;
using EndlessMode;
using State;
using Tape2Tape;

static class LegacyChecks
{
    // Actual old postfix body is extracted on every build, so both possible
    // Harmony orderings are exercised without relying on patch ordering.
    static void OldPostfix(RunData run) => typeof(GoalieRecruitLoaded).GetMethod("Postfix", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { run });
    internal static void Run(Action<bool, string> check)
    {
        Plugin.ModFolder = Path.Combine(Path.GetTempPath(), "ccf-legacy-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Plugin.ModFolder);
        Plugin.IsDefaultMode = false; SquadBehaviorRuntime.ReserveEnabled = true;
        var goalie = new GoaltenderData { id = "CCF_GFA_saved", OriginalId = "native-stats", numberFormat = NumberFormat.Standard,
            passReadSkill = .91f, helmetAwaySkin = "new-pair-mask", HomeLogo = "new-home", AwayLogo = "new-away",
            Earned = new() { Id = "CCF_GFA_saved", talentStates = new() { new CounterState { Id = "native-talent", Earned = 29 } } } };
        var run = new RunData { runID = "legacy-run", squadId = "legacy-squad", teamData = new() { goalie = goalie },
            teamState = new() { goaltenderState = goalie.SaveState(), forwardStates = new[] { "native-forward" }, relicStates = new[] { "native-relic" } } };
        CampaignUtil.CampaignState = new() { runData = run };
        GoaliePersistenceBridge.Roster.Fresh(run, Plugin.ModFolder, true);
        var repository = new RunDataRepositoryV2 { _fileName = "legacy-fixture.es3" };
        var model = new RunDataModelV2 { runID = run.runID, squadId = run.squadId, teamState = run.teamState,
            team = new() { skaters = new() { new SerializerV2().SerializeToModel(goalie) } } };
        check(repository.SaveToPersistentStorage(model).GetAwaiter().GetResult(), "LegacyFixtureHasValidNewCheckpoint");
        var saved = ES3File.Copy(ES3File.persisted[repository._fileName]);
        var legacy = new CampaignDocument("Recruit Number Format = 1\nRecruit Pass Read = 0.24\nRecruit Away Helmet = old-mask\nAppearance Own Stick Color = 12,34,56\nJersey Home = old-home\nJersey Away = old-away");
        GoalieRecruitSnapshots.Save(Plugin.ModFolder, run.runID, goalie.id, legacy);

        foreach (string mode in new[] { "missing", "corrupt", "unaccepted", "accepted" })
        foreach (bool oldFirst in new[] { true, false })
        {
            ES3File.persisted[repository._fileName] = ES3File.Copy(saved);
            if (mode == "missing") ES3File.persisted[repository._fileName].Remove(GoalieRosterStore.MarkerKey);
            string corruptPath = null; byte[] original = null;
            if (mode == "corrupt")
            {
                corruptPath = Directory.GetFiles(Path.Combine(Plugin.ModFolder, GoalieRosterStore.FolderName), "*.record").Single();
                original = File.ReadAllBytes(corruptPath); File.WriteAllText(corruptPath, "invalid new companion");
            }
            var selected = repository.LoadFromPersistentStorage().GetAwaiter().GetResult();
            if (mode == "unaccepted") selected = new() { runID = selected.runID, squadId = selected.squadId, team = selected.team, teamState = selected.teamState };
            var nativeGoalie = new DeserializerV2().ConvertGoaltenderDataModelToGoaltenderData(selected.team.skaters[0]);
            nativeGoalie.LoadState(selected.teamState.goaltenderState);
            var loaded = new RunData { runID = selected.runID, squadId = selected.squadId, teamData = new() { goalie = nativeGoalie }, teamState = selected.teamState };
            var state = loaded.teamState.goaltenderState; var forwards = loaded.teamState.forwardStates; var relics = loaded.teamState.relicStates;
            if (oldFirst) OldPostfix(loaded);
            GoaliePersistenceBridge.Hydrated(new DeserializerV2(), loaded, selected);
            if (!oldFirst) OldPostfix(loaded);
            string label = mode + (oldFirst ? "_old-first" : "_old-last");
            if (mode == "accepted")
            {
                check(GoaliePersistenceBridge.Roster.Selection != null && loaded.teamData.goalie.passReadSkill == .91f
                    && loaded.teamData.goalie.helmetAwaySkin == "new-pair-mask" && loaded.teamData.goalie.numberFormat == NumberFormat.Standard
                    && loaded.teamData.goalie.HomeLogo == "new-home" && loaded.teamData.goalie.AwayLogo == "new-away",
                    "AcceptedFullPairCannotBeOverwrittenByLegacySupplement_" + label);
            }
            else
            {
                check(loaded.teamData.goalie.passReadSkill == .24f && loaded.teamData.goalie.numberFormat == NumberFormat.Rounded
                    && loaded.teamData.goalie.helmetAwaySkin == "old-mask" && loaded.teamData.goalie.HomeLogo == "old-home" && loaded.teamData.goalie.AwayLogo == "old-away"
                    && loaded.teamData.goalie.StickColor.primaryColor.r == 12 / 255f && loaded.teamData.goalie.StickColor.primaryColor.g == 34 / 255f
                    && loaded.teamData.goalie.StickColor.primaryColor.b == 56 / 255f && loaded.teamData.goalie.StickColor.primaryColor.a == 1,
                    "UnavailableFullPairRestoresValidLegacySupplement_" + label);
                check(loaded.teamData.goalie == nativeGoalie && loaded.teamState.goaltenderState == state && nativeGoalie.Earned == state
                    && ((CounterState)state.talentStates[0]).Earned == 29 && GoaliePersistenceBridge.Roster.Selection == null
                    && !string.IsNullOrEmpty(GoaliePersistenceBridge.Roster.UnavailableReason),
                    "LegacyFallbackPreservesNativeOwnersAndLeavesReserveUnavailable_" + label);
            }
            check(loaded.teamState.forwardStates == forwards && loaded.teamState.relicStates == relics, "LegacyResolutionPreservesForwardRelicOwners_" + label);
            if (corruptPath != null) File.WriteAllBytes(corruptPath, original);
        }
    }
}
