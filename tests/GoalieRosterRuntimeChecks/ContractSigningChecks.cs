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
                check(GoalieContractRuntime.TryDepartureProtection(campaign, out bool prevented) && !prevented && loaded.teamData.LastRelicQuery == typeof(Rogue.Handcuffs), "UnprotectedDecisionUsesExactTypedNativePredicate");
        loaded.teamData.NativeHandcuffs = true;
        check(GoalieContractRuntime.TryDepartureProtection(campaign, out prevented) && prevented, "NativeHandcuffsProtectsGoalie");
        loaded.teamData.NativeHandcuffs = false;
        CustomRewardCampaignRules.Rules.Add(new() { Trigger = "Contract expiry", Effect = CustomRewardEffects.KeepFreeAgents });
        var prefix = typeof(CustomRewardContractRetention).GetMethod("Prefix", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var manager = new Rogue.FreeAgents.FreeAgentManager { _campaignState = campaign };
        check(GoalieContractRuntime.TryDepartureProtection(campaign, out prevented) && prevented && !(bool)prefix.Invoke(null, new object[] { manager }), "GoalieAndNativeSkaterPrefixShareActualCustomRetentionDecision");
        CustomRewardCampaignRules.Rules.Clear();
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
        SquadPoolRuntime.Teams.Clear();
    }
}
