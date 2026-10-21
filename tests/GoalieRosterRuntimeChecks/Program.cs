using CampaignCore;
using EndlessMode;
using Data;
using State;

int checks = 0;
void Check(bool result, string name) { if (!result) throw new Exception(name); Console.WriteLine("PASS " + name); checks++; }
var goalie = new GoaltenderData { id = "earned-goalie", OriginalId = "stats-owner", skill = 89, passReadSkill = .81f,
    numberFormat = NumberFormat.Rounded, helmetAwaySkin = "away-mask", ignoreStatsGrouping = true,
    colorSchemes = new TeamColorsData { Value = "earned-palette" }, powerups = new() { "earned-talent" },
    HomeLogo = "embedded-home", AwayLogo = "embedded-away", isFemale = true, HasGoldenHelmet = true };
GoalieNativeSnapshot.Deserializer = new DeserializerV2();
goalie.Earned = new() { Id = goalie.id, HasGoldenHelmet = true,
    abilityState = new CounterState { Id = "ability", Earned = 17, History = new() { 3, 7 } },
    talentStates = new() { new CounterState { Id = "earned-talent", Earned = 42, History = new() { 9, 12 } } } };
var captured = GoalieNativeSnapshot.Capture(goalie);
((CounterState)goalie.Earned.talentStates[0]).Earned = 999;
((CounterState)goalie.Earned.abilityState).History.Clear();
goalie.colorSchemes.Value = "later-palette"; goalie.powerups[0] = "later-talent"; goalie.skill = 12;
var restored = GoalieNativeSnapshot.Restore(captured);
Check(restored.skill == 89 && restored.colorSchemes.Value == "earned-palette" && restored.powerups[0] == "earned-talent", "NativeEarnedFieldsDeepFrozen");
Check(restored.OriginalId == "stats-owner" && restored.ignoreStatsGrouping && restored.isFemale && restored.HasGoldenHelmet, "NativeOmittedIdentityAndPresentationRoundTrip");
Check(restored.passReadSkill == .81f && restored.numberFormat == NumberFormat.Rounded && restored.helmetAwaySkin == "away-mask", "SupplementalFieldsRoundTrip");
Check(restored.HomeLogo == "embedded-home" && restored.AwayLogo == "embedded-away", "BothJerseySlotsRoundTrip");
var bad = captured.Clone(); bad.PlayerId = "other";
bool rejected = false; try { GoalieNativeSnapshot.Restore(bad); } catch (InvalidDataException) { rejected = true; }
Check(rejected, "NativePayloadIdentityMismatchRejected");
Check(restored.Earned?.Id == "earned-goalie" && restored.Earned.HasGoldenHelmet
    && restored.Earned.abilityState is CounterState { Earned: 17 } ability && ability.History.SequenceEqual(new[] { 3, 7 })
    && restored.Earned.talentStates[0] is CounterState { Earned: 42 } talent && talent.History.SequenceEqual(new[] { 9, 12 }),
    "PolymorphicEarnedStateFrozenAndRestored");
void RejectRecord(GoalieRecord value, string name) { bool failed = false; try { GoalieNativeSnapshot.Restore(value); } catch (InvalidDataException) { failed = true; } Check(failed, name); }
bad = captured.Clone(); bad.NativePayload = bad.NativePayload.Replace("es3-json-utf8-byvalue-v1", "unreviewed-profile");
RejectRecord(bad, "UnknownSnapshotProfileRejectedBeforePublication");
bad = captured.Clone(); bad.NativePayload = bad.NativePayload.Replace("Goaltender", "Forward");
RejectRecord(bad, "WrongSnapshotRoleRejected");
bad = captured.Clone(); bad.NativePayload = bad.NativePayload.Replace("\"Version\":1,", "");
RejectRecord(bad, "MissingEnvelopeVersionCannotDefaultToSupportedVersion");
var owned = GoalieNativeSnapshot.RestorePair(captured, new DeserializerV2());
Check(owned.Goalie.Earned == owned.State && owned.Goalie.Earned.Id == owned.Goalie.id, "RestoredModelAndLateLoadStateOwnerArePaired");
var independent = GoalieNativeSnapshot.Restore(captured);
((CounterState)owned.State.talentStates[0]).History[0] = 888;
Check(((CounterState)independent.Earned.talentStates[0]).History[0] == 9, "RepeatedRestoreOwnsDetachedEarnedGraph");
Console.WriteLine($"PASS {checks} goalie runtime checks");
CoordinatorChecks.Run(Check);
CheckpointChecks.Run(Check);
ForwardingChecks.Run(Check);
BridgeChecks.Run(Check);
Console.WriteLine($"PASS {checks} source-linked goalie runtime checks using native boundary doubles");
