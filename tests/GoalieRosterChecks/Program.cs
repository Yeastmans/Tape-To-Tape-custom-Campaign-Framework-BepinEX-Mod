using CampaignCore;

int checks = 0;
void Check(bool result, string name) { if (!result) throw new Exception(name); Console.WriteLine("PASS " + name); checks++; }
void Reject(Action action, string name) { try { action(); } catch (Exception e) when (e is IOException or InvalidDataException) { Check(true, name); return; } throw new Exception(name); }
string root = Path.GetFullPath(Path.Combine("artifacts", "four-features", "goalie-rosters", Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(root);
GoalieRosterState State(string run = "run-1") => new() {
    CampaignId = GoalieRosterStore.CampaignIdentity(root), RunId = run, SquadId = "squad-1", ReserveEnabled = true,
    NativeFingerprint = "native-run-payload-sha256", Active = new() { PlayerId = "active", NativePayload = "{\"earned\":72}", Supplemental = "Recruit Pass Read = 81\n" },
    Reserve = new() { PlayerId = "reserve", NativePayload = "{\"earned\":63}", Supplemental = "Jersey Logo Away = none\n", Contract = new() { Duration = 3, PlayedMatches = 1, LastMatchKey = "match-1", AwaitingReplacement = true } },
    ArmedAttemptToken = "armed-attempt", ProcessedAttemptTokens = new[] { "previous-attempt" }
};
string token = GoalieRosterStore.Prepare(root, State());
string marker = GoalieRosterStore.Marker(token), checkpoint = GoalieRosterStore.Hash(marker);
GoalieRosterStore.Commit(token, checkpoint);
var loaded = GoalieRosterStore.Load(root, "run-1", checkpoint);
Check(loaded.Active.NativePayload == State().Active.NativePayload && loaded.Reserve.NativePayload == State().Reserve.NativePayload
    && loaded.Active.Supplemental == "Recruit Pass Read = 81\n" && loaded.Reserve.Supplemental == "Jersey Logo Away = none\n"
    && loaded.ReserveEnabled && loaded.SquadId == "squad-1" && loaded.Reserve.Contract.Duration == 3
    && loaded.Reserve.Contract.LastMatchKey == "match-1" && loaded.Reserve.Contract.AwaitingReplacement
    && loaded.Reserve.Contract.PlayedMatches == 1 && loaded.ArmedAttemptToken == "armed-attempt"
    && loaded.ProcessedAttemptTokens.SequenceEqual(new[] { "previous-attempt" }), "FullStateRoundTrip");
Check(GoalieRosterStore.Load(root, "run-2", checkpoint) == null, "NoCrossRunRestore");
Check(GoalieRosterStore.Load(Path.Combine(root, "other"), "run-1", checkpoint) == null, "NoCrossCampaignRestore");
string foreignFolder = Path.Combine(root, "other", GoalieRosterStore.FolderName);
Directory.CreateDirectory(foreignFolder);
File.Copy(token, Path.Combine(foreignFolder, Path.GetFileName(token)));
Check(GoalieRosterStore.Load(Path.Combine(root, "other"), "run-1", checkpoint) == null, "ExistingForeignRecordRejectedByCampaignOwnership");
var directClone = loaded.Clone(); directClone.Reserve.Contract.Duration = 99; directClone.Active.PlayerId = "different";
directClone.ProcessedAttemptTokens[0] = "different";
Check(loaded.Reserve.Contract.Duration == 3 && loaded.Active.PlayerId == "active" && loaded.ProcessedAttemptTokens[0] == "previous-attempt", "DirectCloneDetachesRecordsContractsAndLedger");
loaded.ProcessedAttemptTokens[0] = "changed";
Check(GoalieRosterStore.Load(root, "run-1", checkpoint).ProcessedAttemptTokens[0] == "previous-attempt", "NoSharedMutableLedger");
var before = File.ReadAllBytes(token);
var second = State(); second.Reserve.NativePayload = "{\"earned\":80}";
string next = GoalieRosterStore.Prepare(root, second), nextHash = GoalieRosterStore.Hash(GoalieRosterStore.Marker(next));
Check(checkpoint != nextHash, "ReserveOnlyChangesHaveDistinctCheckpoint");
Check(File.ReadAllBytes(token).SequenceEqual(before) && GoalieRosterStore.Load(root, "run-1", checkpoint).Reserve.NativePayload == State().Reserve.NativePayload,
    "NativeSaveFailedKeepsPreviousCheckpoint");
GoalieRosterStore.Fault = point => { if (point == "commit:before-publish") throw new IOException("injected commit failure"); };
Reject(() => GoalieRosterStore.Commit(next, nextHash), "CompanionCommitFailureInjected");
GoalieRosterStore.Fault = null;
Check(GoalieRosterStore.Load(root, "run-1", nextHash).Reserve.NativePayload == second.Reserve.NativePayload, "CompanionCommitFailedRecoversPreparedMatch");
File.WriteAllText(next, "corrupt newest");
Check(GoalieRosterStore.Load(root, "run-1", checkpoint).Active.PlayerId == "active", "CorruptNewestUsesMatchingPrevious");
Check(GoalieRosterStore.Load(root, "run-1", nextHash) == null, "CorruptMatchingRecordNeverGuessesPrevious");
Check(GoalieRosterStore.Load(root, "old-run", checkpoint) == null, "OldRunStartsWithoutReserve");
string priorRecord = File.ReadAllText(token);
File.WriteAllText(token, priorRecord.Replace("\"Version\":1", "\"Version\":99"));
Check(GoalieRosterStore.Load(root, "run-1", checkpoint) == null, "UnknownVersionKeepsNativeActive");
File.WriteAllText(token, priorRecord);
Reject(() => GoalieRosterStore.Commit(token, nextHash), "CommitCannotBindDifferentNativeMarker");
var bad = State(); bad.Version = 2;
Reject(() => GoalieRosterStore.Prepare(root, bad), "UnknownVersionRejected");
bad = State(); bad.Reserve.PlayerId = bad.Active.PlayerId;
Reject(() => GoalieRosterStore.Prepare(root, bad), "DuplicateSlotIdentityRejected");
bad = State(); bad.ReserveEnabled = false;
Reject(() => GoalieRosterStore.Prepare(root, bad), "DisabledCapacityCannotContainReserve");
foreach (string stage in new[] { "prepare:before-write", "prepare:after-write", "prepare:before-publish", "prepare:after-publish",
    "commit:before-write", "commit:after-write", "commit:before-publish", "commit:after-publish" })
{
    byte[] old = File.ReadAllBytes(token);
    string fixtureNative = Path.Combine(root, "native.fixture"); File.WriteAllText(fixtureNative, marker);
    byte[] nativeBytes = File.ReadAllBytes(fixtureNative);
    string prepared = stage.StartsWith("commit") ? GoalieRosterStore.Prepare(root, State()) : null;
    GoalieRosterStore.Fault = point => { if (point == stage) throw new IOException("injected " + stage); };
    Reject(() => { if (prepared == null) GoalieRosterStore.Prepare(root, State()); else GoalieRosterStore.Commit(prepared, GoalieRosterStore.Hash(GoalieRosterStore.Marker(prepared))); }, stage);
    GoalieRosterStore.Fault = null;
    Check(File.ReadAllBytes(token).SequenceEqual(old) && File.ReadAllBytes(fixtureNative).SequenceEqual(nativeBytes), stage + " preserves exact prior pair bytes");
    Check(GoalieRosterStore.Load(root, "run-1", checkpoint) != null, stage + " retains matching previous checkpoint");
}
Console.WriteLine($"PASS {checks} goalie roster store checks; fixtures {root}");
string outside = Path.Combine(root, "linked-target.record");
File.Copy(token, outside); byte[] outsideBytes = File.ReadAllBytes(outside);
File.Delete(token);
File.CreateSymbolicLink(token, outside);
Check(GoalieRosterStore.Load(root, "run-1", checkpoint) == null, "SelectedLinkedRecordRejected");
Check(File.ReadAllBytes(outside).SequenceEqual(outsideBytes), "SelectedLinkTargetPreserved");
Console.WriteLine($"PASS {checks} goalie roster store checks including selected-path guard");

var withVisit = State();
withVisit.RecruitmentVisit = new() { Witness = "saved-native-route", Generation = Guid.NewGuid().ToString("N"), Quota = 2,
    ConsumedOfferIds = new[] { "offer-1" }, OfferedPoolIds = new[] { "pool-a", "pool-b" } };
var visitToken = GoalieRosterStore.Prepare(root, withVisit);
var visitReload = GoalieRosterStore.Load(root, withVisit.RunId, GoalieRosterStore.Hash(GoalieRosterStore.Marker(visitToken)));
Check(visitReload.RecruitmentVisit.ConsumedOfferIds.SequenceEqual(new[] { "offer-1" }) && visitReload.RecruitmentVisit.Quota == 2, "RecruitmentConsumptionRoundTripsWithSameCheckpoint");
var visitClone = visitReload.Clone(); visitClone.RecruitmentVisit.ConsumedOfferIds[0] = "mutated";
Check(visitReload.RecruitmentVisit.ConsumedOfferIds[0] == "offer-1", "RecruitmentLedgerCloneHasNoMutableAliases");
visitClone.RecruitmentVisit.ConsumedOfferIds = new[] { "same", "same" };
Reject(() => visitClone.Validate(), "DuplicateRecruitmentConsumptionRejected");
visitClone = visitReload.Clone(); visitClone.RecruitmentVisit.Quota = 0;
Reject(() => visitClone.Validate(), "InvalidSavedRecruitmentQuotaRejected");
Console.WriteLine($"PASS {checks} goalie roster store checks including recruitment ledger");
ContractChecks.Run(Check);
Console.WriteLine($"PASS {checks} goalie store and contract policy checks");
