using CampaignCore;
using EndlessMode;

static class CheckpointChecks
{
    internal static void Run(Action<bool, string> check)
    {
        string folder = Path.Combine(Path.GetTempPath(), "ccf-goalie-task2b-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        GoalieRosterState Roster() => new() { CampaignId = GoalieRosterStore.CampaignIdentity(folder), RunId = "run", SquadId = "squad",
            NativeFingerprint = "pending", ReserveEnabled = true,
            Active = new() { PlayerId = "active", NativePayload = "active graph", Supplemental = "active supplement" },
            Reserve = new() { PlayerId = "reserve", NativePayload = "reserve graph", Supplemental = "reserve supplement" },
            ArmedAttemptToken = "attempt-1" };
        var state = Roster();
        var transaction = new GoalieCheckpointTransaction("operation-1", folder, state);
        state.Reserve.NativePayload = "later mutable reserve"; state.ArmedAttemptToken = "later attempt";
        string marker = transaction.Prepare("native-fingerprint");
        var completed = transaction.Reconcile(marker, "native-fingerprint");
        check(completed != null && completed.Reserve.NativePayload == "reserve graph" && completed.ArmedAttemptToken == "attempt-1"
            && transaction.OperationId == "operation-1", "ImmutableReserveAdmissionAndExactAttemptAssociation");
        var other = new GoalieCheckpointTransaction("operation-2", folder, Roster());
        string otherMarker = other.Prepare("native-fingerprint");
        check(marker != otherMarker && other.Reconcile(marker, "native-fingerprint") == null,
            "ReserveOnlyNativeEqualityNeverSelectsDifferentMarker");
        check(other.Reconcile(otherMarker, "different-native") == null, "CheckpointFingerprintMismatchRejected");
        check(other.Reconcile(null, "native-fingerprint") == null, "MissingMarkerDoesNotGuessPreparedRecord");
        byte[] prior = File.ReadAllBytes(transaction.Token);
        foreach (string stage in new[] { "prepare:before-write", "prepare:after-write", "prepare:before-publish", "prepare:after-publish" })
        {
            var failed = new GoalieCheckpointTransaction(stage, folder, Roster());
            GoalieRosterStore.Fault = point => { if (point == stage) throw new IOException(stage); };
            try { failed.Prepare("native-fingerprint"); throw new Exception("Fault was not reached"); } catch (IOException) { }
            finally { GoalieRosterStore.Fault = null; }
            check(File.ReadAllBytes(transaction.Token).SequenceEqual(prior) && failed.Reconcile(marker, "native-fingerprint") == null,
                stage + " keeps prior bytes and cannot adopt previous transaction");
        }
        GoalieRosterStore.Fault = point => { if (point == "commit:before-publish") throw new IOException("receipt failure"); };
        var recovered = other.Reconcile(otherMarker, "native-fingerprint");
        GoalieRosterStore.Fault = null;
        check(recovered?.Reserve.PlayerId == "reserve", "MatchingPersistedPrepareRecoversDespiteCommitReceiptFailure");
        check(File.ReadAllBytes(transaction.Token).SequenceEqual(prior), "ReconciliationDoesNotOverwritePreviousCheckpoint");
        File.WriteAllText(other.Token, "corrupt");
        check(other.Reconcile(otherMarker, "native-fingerprint") == null, "CorruptMatchingCompanionNeverUsesDifferentGeneration");
    }
}
