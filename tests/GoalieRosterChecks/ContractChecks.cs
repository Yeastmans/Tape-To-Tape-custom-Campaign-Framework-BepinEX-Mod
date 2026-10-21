using CampaignCore;

static class ContractChecks
{
    internal static void Run(Action<bool, string> check)
    {
        GoalieContractState Sign(int duration) => GoalieContractPolicy.Sign(duration);
        GoalieRosterState Complete(GoalieRosterState state, string key, string[] participants, bool prevented = false)
            => GoalieContractPolicy.CompleteMatch(state, key, participants, prevented);
        GoalieRosterState Reconcile(GoalieRosterState state, bool prevented = false)
            => GoalieContractPolicy.ReconcileDeparture(state, prevented);
        void Reject(Action action, string label) { try { action(); } catch (Exception e) when (e is ArgumentException or InvalidDataException) { check(true, label); return; } throw new Exception(label); }
        GoalieRosterState State() => new() {
            CampaignId = "campaign", RunId = "run", SquadId = "squad", NativeFingerprint = "native", ReserveEnabled = true,
            ArmedAttemptToken = "attempt-1", Active = new() { PlayerId = "active", NativePayload = "active earned state", Supplemental = "appearance", Contract = Sign(4) },
            Reserve = new() { PlayerId = "reserve", NativePayload = "reserve earned state", Supplemental = "reserve appearance", Contract = Sign(7) },
            RecruitmentVisit = new() { Witness = "route", Generation = Guid.NewGuid().ToString("N"), Quota = 2, ConsumedOfferIds = new[] { "hire-1" }, OfferedPoolIds = new[] { "pool-1" } }
        };
        check(Sign(4).Duration == 4 && Sign(4).PlayedMatches == 0 && Sign(4).LastMatchKey == "" && !Sign(4).AwaitingReplacement, "NewSigningHasFrozenDurationAndZeroProgress");
        Reject(() => Sign(0), "ZeroDurationRejected"); Reject(() => Sign(-1), "NegativeDurationRejected");
        var original = State(); var played = Complete(original, "attempt-1", new[] { "active", "active" });
        check(played.Active.Contract.PlayedMatches == 1 && played.Reserve.Contract.PlayedMatches == 0 && original.Active.Contract.PlayedMatches == 0, "OnlyActualParticipantDebitedOnceWithoutMutatingInput");
        check(played.Active.Contract.Duration == 4 && played.Active.Contract.LastMatchKey == "attempt-1" && played.ArmedAttemptToken == "" && played.ProcessedAttemptTokens.SequenceEqual(new[] { "attempt-1" }), "CounterAndConsumedAttemptAreOneTransition");
        check(played.Active.NativePayload == "active earned state" && played.Reserve.Supplemental == "reserve appearance" && played.RecruitmentVisit.ConsumedOfferIds[0] == "hire-1", "CompletePreservesEarnedAppearanceAndSigningLedger");
        played.RecruitmentVisit.ConsumedOfferIds[0] = "changed";
        check(original.RecruitmentVisit.ConsumedOfferIds[0] == "hire-1", "CompletionDetachesRecruitmentLedger");
        Reject(() => Complete(original, "wrong", new[] { "active" }), "UnarmedCompletionRejected");
        Reject(() => Complete(original, "attempt-1", new[] { "visitor" }), "UnknownParticipantRejected");
        check(Complete(original, "attempt-1", Array.Empty<string>()).Active.Contract.PlayedMatches == 0, "NoParticipantNeverDebitsBench");
        var after = Complete(original, "attempt-1", new[] { "active" }); after.ArmedAttemptToken = "attempt-2";
        after = Complete(after, "attempt-2", new[] { "active" });
        after.ArmedAttemptToken = "attempt-3";
        var delayed = Complete(after, "attempt-1", new[] { "active" });
        check(delayed.Active.Contract.PlayedMatches == 2 && delayed.ArmedAttemptToken == "attempt-3", "DelayedOldCompletionCannotConsumeLaterAttempt");
        string folder = Path.Combine(Path.GetTempPath(), "goalie-contract-policy-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        after.CampaignId = GoalieRosterStore.CampaignIdentity(folder);
        string prepared = GoalieRosterStore.Prepare(folder, after);
        var loaded = GoalieRosterStore.Load(folder, "run", GoalieRosterStore.Hash(GoalieRosterStore.Marker(prepared)));
        check(Complete(loaded, "attempt-1", new[] { "active" }).Active.Contract.PlayedMatches == 2, "DuplicateCompletionAfterCheckpointReloadIsNoOp");
        var expires = State(); expires.Active.Contract.PlayedMatches = 3;
        var promoted = Complete(expires, "attempt-1", new[] { "active" });
        check(promoted.Active.PlayerId == "reserve" && promoted.Reserve == null && promoted.Active.Contract.Duration == 7 && promoted.Active.Contract.PlayedMatches == 0 && promoted.Active.NativePayload == "reserve earned state", "ExpiryPromotesWholeUnplayedReserve");
        var protectedState = Complete(expires, "attempt-1", new[] { "active" }, true);
        check(protectedState.Active.PlayerId == "active" && protectedState.Active.Contract.PlayedMatches == 4 && protectedState.Reserve.PlayerId == "reserve", "DeparturePreventionRetainsGoaliesWithoutResettingDebit");
        check(Reconcile(protectedState).Active.PlayerId == "reserve", "RemovingProtectionReconcilesSavedExpiry");
        expires.Reserve = null; expires.ReserveEnabled = false;
        var pending = Complete(expires, "attempt-1", new[] { "active" });
        check(pending.Active.PlayerId == "active" && pending.Active.Contract.PlayedMatches == 4 && pending.Active.Contract.AwaitingReplacement, "EmptyReserveRetainsExpiredActiveAwaitingReplacement");
        pending.ReserveEnabled = true; pending.Reserve = State().Reserve;
        check(Reconcile(pending).Active.PlayerId == "reserve" && Reconcile(pending).Reserve == null, "AvailableReplacementCompletesDeferredDeparture");
        check(Reconcile(pending, true).Active.PlayerId == "active", "ProtectionAlsoDefersPendingDeparture");
        var expiredReserve = State(); expiredReserve.Reserve.Contract.PlayedMatches = 7;
        check(Reconcile(expiredReserve).Reserve == null && Reconcile(expiredReserve).Active.PlayerId == "active", "ExpiredReserveRemovedWithoutEmptyingActive");
        expiredReserve.Active.Contract.PlayedMatches = 4;
        var both = Reconcile(expiredReserve);
        check(both.Reserve == null && both.Active != null && both.Active.Contract.AwaitingReplacement, "BothExpiredNeverPromotesExpiredReserveOrEmptiesActive");
        check(Reconcile(expiredReserve, true).Reserve != null, "ProtectionRetainsExpiredReserve");
        var permanent = State(); permanent.Active.Contract = null;
        check(Complete(permanent, "attempt-1", new[] { "active" }).Active.Contract == null, "PermanentGoalieRemainsPermanent");
        foreach (var accepted in new[] { played, delayed, promoted, protectedState, pending, both }) { accepted.Validate(); check(accepted.Active != null, "AcceptedTransitionHasActiveGoalie"); }
    }
}
