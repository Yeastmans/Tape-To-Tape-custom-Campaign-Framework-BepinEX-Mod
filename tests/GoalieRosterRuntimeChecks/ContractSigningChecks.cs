using CampaignCore;
using Data;
using EndlessMode;
using State;
using Tape2Tape;

static class ContractSigningChecks
{
    internal static void Run(Action<bool, string> check)
    {
        Plugin.ModFolder = Path.Combine(Path.GetTempPath(), "ccf-contract-sign-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Plugin.ModFolder);
        Plugin.IsDefaultMode = false; BridgeDoubleHooks.Enable();
        var config = new TeamConfig(); config.Goalie.Fields = new("Custom Contract = yes\nContract Matches = 4\n");
        SquadPoolRuntime.Teams["contract-squad"] = config;
        var goalie = new GoaltenderData { id = "fresh-starter" };
        var run = new RunData { runID = "fresh", squadId = "contract-squad", teamData = new() { goalie = goalie }, teamState = new() { goaltenderState = goalie.SaveState() } };
        var campaign = CampaignUtil.CampaignState = new() { runData = run };
        GoalieNativeSnapshot.Deserializer = new();
        GoaliePersistenceBridge.Roster.NoSelectedRun();
        BridgeDoubleHooks.Call("FreshPostfix", new object[] { campaign });
        var roster = GoaliePersistenceBridge.Roster;
        check(roster.Selection.Active.Contract?.Duration == 4 && roster.Selection.Active.Contract.PlayedMatches == 0, "ActualFreshBoundarySignsAuthoredStarter");
        config.Goalie.Fields.Set(PlayerContractOptions.MatchesKey, "12");
        BridgeDoubleHooks.Call("FreshPostfix", new object[] { campaign });
        check(roster.Selection.Active.Contract.Duration == 4, "RepeatedFreshCallbackCannotResignExistingOwnedRun");
        campaign.SaveRunData();
        var model = campaign.Repository.LoadFromPersistentStorage().GetAwaiter().GetResult();
        var loaded = new RunData { runID = run.runID, squadId = run.squadId, teamData = new() { goalie = new DeserializerV2().ConvertGoaltenderDataModelToGoaltenderData(model.team.skaters[0]) }, teamState = model.teamState };
        campaign.runData = loaded;
        GoaliePersistenceBridge.Hydrated(new(), loaded, model);
        check(roster.Selection.Active.Contract.Duration == 4, "ContinueUsesFrozenContractInsteadOfEditedAuthoring");
        var signed = GoalieContractPolicy.Sign(9);
        check(roster.TryRecruit(campaign, new() { id = "new-recruit" }, GoalieSlot.Active, roster.Selection.Revision, signedContract: signed), "ConfirmedRecruitAcceptsSignedSnapshot");
        signed.Duration = 80;
        check(roster.Selection.Active.Contract.Duration == 9 && roster.Selection.Active.Contract.PlayedMatches == 0, "RecruitStartsFreshAndDetachesSignedSnapshot");
        foreach (var value in new[] { "", "0", "-1", "bad", "2.5" }) {
            config.Goalie.Fields.Set(PlayerContractOptions.MatchesKey, value);
            Plugin.Log.Warnings.Clear();
            roster.NoSelectedRun(); BridgeDoubleHooks.Call("FreshPostfix", new object[] { campaign });
            check(roster.Selection.Active.Contract == null && Plugin.Log.Warnings.Count == 1 && Plugin.Log.Warnings[0].Contains("positive whole number") && Plugin.Log.Warnings[0].Contains("new-recruit"), "MalformedGoalieDurationFallsBackToPermanentWithWarning_" + value);
        }
        config.Goalie.Fields.Set(PlayerContractOptions.EnabledKey, "no"); config.Goalie.Fields.Set(PlayerContractOptions.MatchesKey, "4");
        roster.NoSelectedRun(); BridgeDoubleHooks.Call("FreshPostfix", new object[] { campaign });
        check(roster.Selection.Active.Contract == null, "DisabledGoalieContractKeepsPermanentStarter");
        RetentionRegistryFixture.Reset();
        check(GoalieContractRuntime.TryDepartureProtection(campaign, out bool prevented) && !prevented && loaded.teamData.LastRelicQuery == typeof(Rogue.Handcuffs), "UnprotectedDecisionUsesExactTypedNativePredicate");
        loaded.teamData.NativeHandcuffs = true;
        check(GoalieContractRuntime.TryDepartureProtection(campaign, out prevented) && prevented, "NativeHandcuffsProtectsGoalie");
        loaded.teamData.NativeHandcuffs = false;
        RetentionRegistryFixture.Protect(loaded.teamData);
        var prefix = typeof(CustomRewardContractRetention).GetMethod("Prefix", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var manager = new Rogue.FreeAgents.FreeAgentManager { _campaignState = campaign };
        check(GoalieContractRuntime.TryDepartureProtection(campaign, out prevented) && prevented && !(bool)prefix.Invoke(null, new object[] { manager }), "GoalieAndNativeSkaterPrefixShareActualCustomRetentionDecision");
        CustomRewardRuntime.Definitions.Clear();
        check((bool)prefix.Invoke(null, new object[] { manager }), "AbsentCustomRetentionPreservesOriginalNativeSkaterMethod");
        loaded.teamData.FailProtection = true;
        check(!GoalieContractRuntime.TryDepartureProtection(campaign, out _), "NativeProtectionExceptionIsUnknown");
        loaded.teamData.FailProtection = false; CustomRewardCampaignRules.Fail = true;
        check(!GoalieContractRuntime.TryDepartureProtection(campaign, out _), "CustomProtectionExceptionIsUnknown");
        CustomRewardCampaignRules.Fail = false;
        check(!GoalieContractRuntime.TryDepartureProtection(new CampaignState { runData = loaded }, out _), "ForeignCampaignCannotSupplyDepartureDecision");
        Plugin.IsDefaultMode = true;
        check(!GoalieContractRuntime.TryDepartureProtection(campaign, out _) && (bool)prefix.Invoke(null, new object[] { manager }), "DefaultModeDoesNotGainGoalieContractOrCustomRetention");
        check((bool)prefix.Invoke(null, new object[] { null }), "DefaultModeRetentionPrefixDoesNotReadNativeManager");
        Plugin.IsDefaultMode = false;
        string folder = Plugin.ModFolder; Plugin.ModFolder = null;
        check(!GoalieContractRuntime.TryDepartureProtection(campaign, out _), "UnavailableCampaignFolderCannotBecomeUnprotectedDecision");
        Plugin.ModFolder = folder;
        RetentionRegistryFixture.Reset();
        string malformed = Path.Combine(Plugin.ModFolder, "broken.txt");
        File.WriteAllText(malformed, "Export Custom Reward broken = not-base64"); Plugin.Log.Errors.Clear();
        bool known = GoalieContractRuntime.TryDepartureProtection(campaign, out _);
        check(!known && Plugin.Log.Errors.Any(e => e.Contains("Loading definitions")), "SwallowedDefinitionLoadFailureIsUnknownInsteadOfUnprotected");
        File.Delete(malformed);
        check(GoalieContractRuntime.TryDepartureProtection(campaign, out prevented) && !prevented, "DefinitionLoaderRecoversAfterBrokenSourceIsRemoved");
        Directory.CreateDirectory(Path.GetDirectoryName(CustomRewardLifecycle.RetainedPath));
        File.WriteAllText(CustomRewardLifecycle.RetainedPath, "Export Custom Reward broken = not-base64");
        CustomRewardLifecycle.LoadRetained(CustomRewardRuntime.Definitions);
        check(!GoalieContractRuntime.TryDepartureProtection(campaign, out _), "LaterRetainedRestoreFailureInvalidatesEarlierCompleteSnapshot");
        File.Delete(CustomRewardLifecycle.RetainedPath);
        check(GoalieContractRuntime.TryDepartureProtection(campaign, out prevented) && !prevented, "LaterRetainedRestoreRecoversAfterRepair");
        CustomRewardRuntime.DuringOwnership = () => {
            File.WriteAllText(CustomRewardLifecycle.RetainedPath, "Export Custom Reward broken = not-base64");
            CustomRewardLifecycle.LoadRetained(CustomRewardRuntime.Definitions);
        };
        check(!GoalieContractRuntime.TryDepartureProtection(campaign, out _), "RetainedFailureDuringOwnershipCannotAuthorizeEarlierReadySnapshot");
        CustomRewardRuntime.DuringOwnership = null; File.Delete(CustomRewardLifecycle.RetainedPath);
        check(GoalieContractRuntime.TryDepartureProtection(campaign, out prevented) && !prevented, "OwnershipTimeRetainedFailureRecoversOnNextDecision");
        CustomRewardRuntime.DuringOwnership = () => {
            CustomRewardRuntime.DefinitionSourceUnavailable();
            CustomRewardRuntime.EnsureDefinitionsForRetention(out _);
        };
        check(!GoalieContractRuntime.TryDepartureProtection(campaign, out _), "ReplacementSnapshotDuringOwnershipCannotCertifyEarlierGeneration");
        CustomRewardRuntime.DuringOwnership = null;
        check(GoalieContractRuntime.TryDepartureProtection(campaign, out prevented) && !prevented, "StableReplacementSnapshotCanAuthorizeNextDecision");
        string originalContentRoot = Plugin.ModContentRoot;
        CustomRewardRuntime.DuringOwnership = () => Plugin.ModContentRoot = originalContentRoot + "-changed";
        check(!GoalieContractRuntime.TryDepartureProtection(campaign, out _), "ScopeChangeDuringOwnershipCannotCertifyEarlierSnapshot");
        CustomRewardRuntime.DuringOwnership = null; Plugin.ModContentRoot = originalContentRoot;
        check(GoalieContractRuntime.TryDepartureProtection(campaign, out prevented) && !prevented, "RestoredScopeCanAuthorizeMatchingSnapshot");
        foreach (string source in new[] { "retained", "export" }) {
            RetentionRegistryFixture.Reset();
            string owned = RetentionRegistryFixture.SaveAuthoredProtection(loaded.teamData);
            string failing = source == "retained" ? CustomRewardLifecycle.RetainedPath : Path.Combine(Plugin.ModContentRoot, "play_now_overrides", "broken.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(failing)); File.WriteAllText(failing, "Export Custom Reward broken = not-base64");
            Plugin.Log.Errors.Clear(); CustomRewardRuntime.EnsureAssets();
            check(CustomRewardRuntime.Definitions.ContainsKey(owned) && Plugin.Log.Errors.Any(e => e.Contains(source == "retained" ? "Retained bench definitions" : "Export broken.txt")), "PartialSnapshotRetainsLegacyPublishedDefinitions_" + source);
            check(!GoalieContractRuntime.TryDepartureProtection(campaign, out _), "PartialDefinitionSourceFailureIsUnknown_" + source);
            check(!(bool)prefix.Invoke(null, new object[] { manager }), "LegacyPrefixStillUsesItsExistingPartialRegistryDecision_" + source);
            File.Delete(failing);
            check(GoalieContractRuntime.TryDepartureProtection(campaign, out prevented) && prevented, "PartialDefinitionLoadRecoversWithKnownOwnedProtection_" + source);
        }
        CustomRewardRuntime.FailNativeAssets = true; Plugin.Log.Errors.Clear();
        check(GoalieContractRuntime.TryDepartureProtection(campaign, out prevented) && prevented && Plugin.Log.Errors.Any(e => e.Contains("native artwork unavailable")), "NativeArtworkFailureDoesNotInvalidateCompleteDefinitionDecision");
        CustomRewardRuntime.FailNativeAssets = false;
        string firstCampaign = Plugin.ModFolder;
        Plugin.ModFolder = Path.Combine(Path.GetTempPath(), "ccf-other-contract-campaign-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Plugin.ModFolder);
        roster.Fresh(loaded, Plugin.ModFolder, false);
        check(roster.Owns(loaded, Plugin.ModFolder) && !GoalieContractRuntime.TryDepartureProtection(campaign, out _), "PriorCampaignRegistryCannotAuthorizeNewOwnedRun");
        RetentionRegistryFixture.Reset();
        check(GoalieContractRuntime.TryDepartureProtection(campaign, out prevented) && !prevented, "NewCampaignSnapshotDoesNotInheritPriorProtection");
        Plugin.ModFolder = firstCampaign; roster.Fresh(loaded, Plugin.ModFolder, false);
        check(!GoalieContractRuntime.TryDepartureProtection(campaign, out _), "ReturningCampaignRequiresMatchingRegistryScope");
        RetentionRegistryFixture.Reset();
        check(GoalieContractRuntime.TryDepartureProtection(campaign, out prevented) && prevented, "NormalRegistryRebindRestoresCurrentCampaignKnownDecision");
        SquadPoolRuntime.Teams.Clear();
    }
}
