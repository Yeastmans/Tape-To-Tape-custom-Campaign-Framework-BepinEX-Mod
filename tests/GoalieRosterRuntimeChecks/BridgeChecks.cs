using CampaignCore;
using Data;
using EndlessMode;
using State;
using Tape2Tape;

static class BridgeChecks
{
    internal static void Run(Action<bool, string> check)
    {
        Plugin.ModFolder = Path.Combine(Path.GetTempPath(), "ccf-bridge-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Plugin.ModFolder);
        Plugin.IsDefaultMode = false; SquadBehaviorRuntime.ReserveEnabled = true;
        BridgeDoubleHooks.Enable();
        var goalie = new GoaltenderData { id = "active", OriginalId = "career-stats", skill = 90, HomeLogo = "home", AwayLogo = "away", numberFormat = NumberFormat.Rounded,
            HasGoldenHelmet = true, powerups = new() { "earned" }, Earned = new() { Id = "active", HasGoldenHelmet = true,
                talentStates = new() { new CounterState { Id = "earned", Earned = 71, History = new() { 4, 5 } } } } };
        var run = new RunData { runID = "run", squadId = "squad", teamData = new() { goalie = goalie },
            teamState = new() { goaltenderState = goalie.SaveState(), forwardStates = new[] { "forward earned" }, relicStates = new[] { "relic earned" } } };
        CampaignUtil.CampaignState = new() { runData = run };
        BridgeDoubleHooks.Call("FreshPostfix", new object[] { CampaignUtil.CampaignState });
        var beforeMutationGoalie = run.teamData.goalie; var beforeMutationState = run.teamState.goaltenderState;
        var proposed = GoaliePersistenceBridge.Roster.Selection;
        var reserve = GoalieNativeSnapshot.Restore(proposed.Active); reserve.id = "reserve"; reserve.Earned.Id = "reserve";
        proposed.Reserve = GoalieNativeSnapshot.Capture(reserve);
        var mutation = GoaliePersistenceBridge.Roster.Stage(run, proposed, new DeserializerV2());
        check(run.teamData.goalie == beforeMutationGoalie && GoaliePersistenceBridge.Roster.Selection.Reserve == null, "DetachedMutationPreparationDoesNotPublish");
        mutation.Publish();
        check(GoaliePersistenceBridge.Roster.Selection.Reserve.PlayerId == "reserve", "ValidatedMutationPublishesBothSlots");
        mutation.Rollback();
        check(run.teamData.goalie == beforeMutationGoalie && run.teamState.goaltenderState == beforeMutationState
            && GoaliePersistenceBridge.Roster.Selection.Reserve == null, "MutationRollbackRestoresExactPreviousOccupantsAndStateOwner");
        mutation = GoaliePersistenceBridge.Roster.Stage(run, proposed, new DeserializerV2()); mutation.Publish(); goalie = run.teamData.goalie;
        RunDataModelV2 Model() => new() { runID = run.runID, squadId = run.squadId,
            team = new() { skaters = new() { new SerializerV2().SerializeToModel(run.teamData.goalie) } }, teamState = run.teamState };
        var repository = new RunDataRepositoryV2();
        var receipts = new List<GoaliePersistenceBridge.SaveReceipt>();
        GoaliePersistenceBridge.SaveCompleted += receipts.Add;
        var saved = repository.SaveToPersistentStorage(Model());
        check(GoalieNativeSnapshot.Deserializer.Talents == repository._talentRepository && GoalieNativeSnapshot.Deserializer.Relics == repository._relicRepository
            && GoalieNativeSnapshot.Deserializer.Abilities == repository._abilityRepository && GoalieNativeSnapshot.Deserializer.Logos == null,
            "FreshRunRestorerUsesExactNativeRepositoryBindingsAndNativeNullLogo");
        check(saved.IsCompleted && saved.GetAwaiter().GetResult() && receipts.Last().Durable && repository.Saves == 1,
            "BridgePreparesInjectsStoresReconcilesAndForwardsNativeTrue");
        string checkpointMarker = (string)ES3File.cachedFiles[repository._fileName].Load(GoalieRosterStore.MarkerKey).TryCast<Il2CppSystem.String>();
        byte[] priorNative = ES3File.persisted[repository._fileName]["runData"].bytes.ToArray();
        var model = repository.LoadFromPersistentStorage().GetAwaiter().GetResult();
        SquadBehaviorRuntime.ReserveEnabled = false;
        ES3File.cachedFiles[repository._fileName].cache.Remove(GoalieRosterStore.MarkerKey);
        var loaded = new RunData { runID = "run", squadId = "squad", teamData = new() { goalie = new DeserializerV2().ConvertGoaltenderDataModelToGoaltenderData(model.team.skaters[0]) }, teamState = model.teamState };
        var forwards = loaded.teamState.forwardStates; var relics = loaded.teamState.relicStates;
        GoaliePersistenceBridge.Hydrated(new DeserializerV2(), loaded, model);
        loaded.teamData.goalie.LoadState(loaded.teamState.goaltenderState);
        check(GoaliePersistenceBridge.Roster.Selection?.ReserveEnabled == true && loaded.teamData.goalie.HasGoldenHelmet
            && ((CounterState)loaded.teamData.goalie.Earned.talentStates[0]).Earned == 71 && loaded.teamData.goalie.OriginalId == "career-stats",
            "ContinueUsesImmutableSelectedModelSavedPolicyAndLateNativeStateOwner");
        check(ReferenceEquals(forwards, loaded.teamState.forwardStates) && ReferenceEquals(relics, loaded.teamState.relicStates), "ContinuePreservesNativeForwardRelicOwnership");
        check(GoaliePersistenceBridge.Roster.Selection.Reserve.PlayerId == "reserve", "ContinueRestoresValidatedReserveFromSameCheckpoint");
        var persistedGood = ES3File.Copy(ES3File.persisted[repository._fileName]);
        var validState = GoaliePersistenceBridge.Roster.Selection;
        var invalidReserve = validState.Clone(); invalidReserve.Reserve.NativePayload = invalidReserve.Reserve.NativePayload.Replace("Goaltender", "Forward");
        var invalidToken = GoalieRosterStore.Prepare(Plugin.ModFolder, invalidReserve);
        ES3.Save(GoalieRosterStore.MarkerKey, (Il2CppSystem.String)GoalieRosterStore.Marker(invalidToken), new(repository._fileName, ES3Settings.defaultSettings));
        ES3.StoreCachedFile(repository._fileName);
        var badModel = repository.LoadFromPersistentStorage().GetAwaiter().GetResult();
        var nativeFallback = new RunData { runID = "run", squadId = "squad", teamData = new() { goalie = goalie }, teamState = badModel.teamState };
        var originalOwner = nativeFallback.teamState.goaltenderState;
        GoaliePersistenceBridge.Hydrated(new DeserializerV2(), nativeFallback, badModel);
        check(nativeFallback.teamData.goalie == goalie && nativeFallback.teamState.goaltenderState == originalOwner
            && GoaliePersistenceBridge.Roster.Selection == null, "InvalidReserveRejectsWholePairBeforeAnyNativeRosterMutation");
        ES3File.persisted[repository._fileName] = ES3File.Copy(persistedGood);
        var actualSelected = repository.LoadFromPersistentStorage().GetAwaiter().GetResult();
        var unselected = new RunDataModelV2 { runID = actualSelected.runID, squadId = actualSelected.squadId, team = actualSelected.team, teamState = actualSelected.teamState };
        GoaliePersistenceBridge.Hydrated(new DeserializerV2(), nativeFallback, unselected);
        check(GoaliePersistenceBridge.Roster.Selection == null && nativeFallback.teamData.goalie == goalie, "EqualSemanticModelCannotStealSelectedModelAssociation");
        var selectedPath = Directory.GetFiles(Path.Combine(Plugin.ModFolder, GoalieRosterStore.FolderName), "r-*-" + GoalieRosterStore.Hash(checkpointMarker) + ".record").Single();
        var validRecordBytes = File.ReadAllBytes(selectedPath); File.WriteAllText(selectedPath, "corrupt selected companion");
        var corruptModel = repository.LoadFromPersistentStorage().GetAwaiter().GetResult();
        GoaliePersistenceBridge.Hydrated(new DeserializerV2(), nativeFallback, corruptModel);
        check(GoaliePersistenceBridge.Roster.Selection == null && nativeFallback.teamData.goalie == goalie && nativeFallback.teamState.goaltenderState == originalOwner,
            "CorruptSelectedCompanionKeepsExactNativeActiveAndEarnedState");
        File.WriteAllBytes(selectedPath, validRecordBytes);
        // Rebind the original run for independent save fault fixtures.
        SquadBehaviorRuntime.ReserveEnabled = true; BridgeDoubleHooks.Call("FreshPostfix", new object[] { CampaignUtil.CampaignState });
        foreach (string fault in new[] { "prepare", "inject", "readback", "store", "platform-false", "platform-fault", "platform-cancel", "commit" })
        {
            ES3.FailMarker = fault == "inject"; ES3.WrongMarkerReadback = fault == "readback"; ES3.FailStore = fault == "store";
            repository.Outcome = fault == "platform-false" ? "false" : fault == "platform-fault" ? "fault" : fault == "platform-cancel" ? "cancel" : "true";
            GoalieRosterStore.Fault = point => { if (fault == "prepare" && point == "prepare:before-write" || fault == "commit" && point == "commit:before-publish") throw new IOException(fault); };
            var task = repository.SaveToPersistentStorage(Model()); var receipt = receipts.Last();
            check(task.IsCompleted && receipt.Original != null && receipt.Proxy == task, fault + " exact task completion association");
            check(receipt.Durable == (fault == "commit"), fault + " cannot invent durable success");
            check(run.teamData.goalie == goalie && run.teamState.goaltenderState.Id == "active", fault + " preserves native occupants");
            if (fault == "store") check(ES3File.persisted[repository._fileName]["runData"].bytes.SequenceEqual(priorNative), "NativeStoreFailurePreservesExactPriorFixtureBytes");
            GoalieRosterStore.Fault = null;
        }
        ES3.FailMarker = ES3.WrongMarkerReadback = ES3.FailStore = false; repository.Outcome = "true";
        repository.CompleteImmediately = false;
        var first = repository.SaveToPersistentStorage(Model());
        var secondModel = Model(); var second = repository.SaveToPersistentStorage(secondModel);
        secondModel.team.skaters[0].skill = 2; secondModel.teamState.goaltenderState.HasGoldenHelmet = false;
        var third = repository.LoadFromPersistentStorage();
        check(!first.IsCompleted && !second.IsCompleted && !third.IsCompleted && repository.Pending.Count == 1, "BridgeSerializesAcceptedSaveSaveLoadWithoutBusyFailure");
        repository.Complete(repository.Pending[0]); GoaliePersistenceBridge.Tick();
        check(first.IsCompleted && !second.IsCompleted && repository.Pending.Count == 2 && !third.IsCompleted, "BridgeStartsSecondOriginalOnlyAfterFirstCompletion");
        repository.Complete(repository.Pending[1]); GoaliePersistenceBridge.Tick();
        var selected = third.GetAwaiter().GetResult();
        check(second.IsCompleted && selected.team.skaters[0].skill == 90 && selected.teamState.goaltenderState.HasGoldenHelmet, "ImmutableNativeAdmissionBytesSurviveMutableModelAndQueuedLoad");
        run.teamState.goaltenderState.HasGoldenHelmet = true;
        var interfered = repository.SaveToPersistentStorage(Model());
        ES3File.CacheFile(new(repository._fileName, ES3Settings.defaultSettings));
        repository.Complete(repository.Pending.Last()); GoaliePersistenceBridge.Tick();
        check(interfered.IsCompleted && !receipts.Last().Reconciled, "UnexpectedSameContainerCacheReplacementInvalidatesReconciliation");
        var driftOwner = repository.SaveToPersistentStorage(Model());
        var driftModel = Model(); var drifted = repository.SaveToPersistentStorage(driftModel);
        driftModel.team.skaters[0].skill = 3;
        ES3File.cachedFiles[repository._fileName].settings.prettyPrint = !ES3File.cachedFiles[repository._fileName].settings.prettyPrint;
        repository.Complete(repository.Pending[^1]); GoaliePersistenceBridge.Tick();
        repository.Complete(repository.Pending[^1]); GoaliePersistenceBridge.Tick();
        check(driftOwner.IsCompleted && drifted.GetAwaiter().GetResult() && !receipts.Last().Durable
            && receipts.Last().Failure?.Contains("profile drift") == true,
            "ExternalProfileDriftPreservesNativeResultButCannotAuthorizeCheckpoint");
        var driftSaved = ES3File.cachedFiles[repository._fileName].Load("runData").TryCast<RunDataModelV2>();
        check(driftSaved.team.skaters[0].skill == 3, "ExternalProfileDriftExplicitlyUsesNativePassThroughOutsideImmutableGuarantee");
        int before = repository.Saves; ES3.AdmissionError = new Il2CppSystem.Exception("native unsupported graph");
        var failedAdmission = repository.SaveToPersistentStorage(Model());
        check(failedAdmission.IsFaulted && repository.Saves == before && failedAdmission.GetExceptionDispatchInfos()[0].SourceException.Pointer == ES3.AdmissionError.Pointer,
            "FailedNativeSerializationAdmissionPreservesExceptionAndDoesNotInvokeOriginal");
        ES3.AdmissionError = null;
        repository.NullLoad = true;
        check(repository.LoadFromPersistentStorage().GetAwaiter().GetResult() == null && GoaliePersistenceBridge.Roster.Selection == null, "BridgeNativeNullLoadAdoptsNoStaleSelectedMarker");
        repository.NullLoad = false;
        bool gated = false;
        try { NamedManagedHookContract.RequireExactSet(GoaliePersistenceContracts.Required, Array.Empty<NamedManagedHookContract>(), GoaliePersistenceContracts.Feature); }
        catch (InvalidDataException) { gated = true; }
        check(gated, "MissingFinalCompatibilityContractsKeepInterceptionUnavailable");
        GoaliePersistenceBridge.SaveCompleted -= receipts.Add;
    }
}
