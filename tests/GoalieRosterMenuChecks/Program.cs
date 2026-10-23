using CampaignCore;
using EndlessMode;
using State;
using Data;
using Tape2Tape;
using Tape2Tape.Hockey.UI;
using Tape2Tape.Customization.UI;
using Rogue.NPC.GeneralManagerNPC;
using System.Reflection;

try {
int checks = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); checks++; }
BridgeDoubleHooks.Enable();
typeof(GoalieRosterMenu).GetField("installed", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, true);
Plugin.IsDefaultMode = false; GameManager.GameTypeUnavailable = true; T2TUtils.IsOnWorldMap = true;
CampaignState Fresh(bool reserve = true) {
    Plugin.ModFolder = Path.Combine(Path.GetTempPath(), "ccf-menu-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Plugin.ModFolder);
    RetentionRegistryFixture.Reset();
    var goalie = new GoaltenderData { id = "active" };
    var state = CampaignUtil.CampaignState = new CampaignState { currentNode = new(), runData = new RunData { runID = Guid.NewGuid().ToString("N"), currentMap = "{\"act\":1,\"path\":[]}", squadId = "squad", teamData = new() { goalie = goalie }, teamState = new() { goaltenderState = goalie.SaveState() } } };
    GoalieNativeSnapshot.Deserializer = new();
    GoaliePersistenceBridge.Roster.Fresh(state.runData, Plugin.ModFolder, reserve);
    state.SaveRunData(); // confirmed baseline for exact no-write rollback evidence
    UnityEngine.Object.CurrentMenu = new LineupMenu { Team = new() { OriginalTeamData = state.runData.teamData } };
    return state;
}
(GoalieRecruitment.Offer Offer, EventChoiceMenu Gm) Offer(CampaignState state) {
    var node = new GeneralManagerEvent();
    var map = new STS.Map.MapNode(); var gm = new EventChoiceMenu { SourceNode = node, mapNode = map };
    var visit = new GoalieRecruitment.Visit { Revision = 4, Node = node.Pointer, Event = node, Ledger = GoalieRecruitment.VisitLedger(state, 2),
        Selection = new() { State = state.Pointer, Run = state.runData.Pointer, Team = state.runData.teamData.Pointer,
            Blueprint = map.Blueprint.Pointer, Map = map, Event = node.Pointer, Menu = gm, Route = GoalieRosterRuntime.RecruitmentWitness(state, 0, 0), RunId = state.runData.runID, Folder = Plugin.ModFolder,
            Act = 0, Layer = 0, Index = 0, RecruitmentEnabled = true, ConfigurationError = "" } };
    typeof(GoalieRecruitment).GetField("visit", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, visit);
    var offer = new GoalieRecruitment.Offer { Visit = visit, Revision = visit.Revision, Id = GoalieRosterStore.Hash(visit.Ledger.Generation + ":offer"), Candidate = new() { id = "candidate" } };
    visit.Offers.Add(offer);
    gm.m_Items.Add(new() { ChoiceData = new() { Skater = offer.Candidate } });
    return (offer, gm);
}
GoalieRosterMenu.Selection Recruit(CampaignState state, (GoalieRecruitment.Offer Offer, EventChoiceMenu Gm) pair) {
    Check(!GoalieRosterMenu.RecruitClick(pair.Gm, pair.Gm.m_Items[0].Navigable), "NativeRecruitCallbackIsTakenOver");
    return GoalieRosterMenu.Current;
}
void Click(GoalieRosterMenu.Selection s, int slot) {
    var menu = s.Menu; int callbacks = menu.ToggleCallbacks, mutations = menu.NativeMutations;
    menu.m_ActivePlayersList.m_SkaterCards[slot].Navigable.Click();
    Check(menu.ToggleCallbacks == callbacks + 1 && menu.NativeMutations == mutations, "InputToggleListChainSuppressesNativeLineupMutation");
}
var state = Fresh();
var pair = Offer(state); var selection = Recruit(state, pair);
var activeLabel = selection.Menu.m_ActivePlayersList.m_SkaterCards[0].m_SkaterName;
var emptyCard = selection.Menu.m_ActivePlayersList.m_SkaterCards[1];
Check(selection.Menu.m_ActivePlayersList.m_SkaterCards.All(c => !c.m_FreeAgentDurationContainer.Active && c.m_FreeAgentDuration.text == ""), "ContractProseNeverUsesNarrowNativeBadge");
Check(activeLabel.text == "Active: active" && !activeLabel.enableWordWrapping && activeLabel.maxVisibleLines == 1 && activeLabel.overflowMode == TMPro.TextOverflowModes.Ellipsis, "NameUsesBoundedSingleLineWithoutContractProse");
Check(emptyCard.m_EmptySeatContainer.Active && emptyCard.EmptySeatLabel.text.Contains("select to hire") && !emptyCard.Locked, "ActualVisibleEmptySeatExplainsRecruitAction");
Check(emptyCard.EmptySeatLabel.color.Equals(activeLabel.color), "ActionableEmptyReserveUsesEnabledNativeNameColor");
Check(emptyCard.m_EmptySeatContainer.Children.Length == 4 && emptyCard.EmptySwapLabel.text == "Swap"
    && emptyCard.EmptyStatNum.text == "20" && emptyCard.EmptyStatLabel.text == "OVR", "ExactEmptyNameLeavesNativeComparisonLabelsUnchanged");
Check(selection.Menu.Focused == selection.Menu.m_ActivePlayersList.Navigables[0] && selection.Menu.ToggleCallbacks == 0
    && selection.Confirming == null && !pair.Offer.Taken, "InitialNativeFocusDoesNotActivateOrConfirm");
selection.Menu.Focus(selection.Menu.m_ActivePlayersList.Navigables[1]);
Check(selection.Menu.Focused == selection.Menu.m_ActivePlayersList.m_SkaterCards[1].Navigable
    && !selection.Menu.m_ActivePlayersList.m_SkaterCards[1].Locked && selection.Menu.ToggleCallbacks == 0,
    "EmptyReserveFocusIsDistinctFromExplicitActivation");
Check(selection.Active.Pointer != state.runData.teamData.goalie.Pointer && !selection.Menu.Team.HasChanged, "PreviewNeverOwnsCanonicalGoalie");
selection.Menu.TryLeaveMenu(false);
Check(!pair.Offer.Taken && pair.Offer.Visit.Pending == null && pair.Gm.m_ChoicesMade == 0 && pair.Gm.DefaultSelections == 1 && pair.Gm.Visible && !pair.Gm.m_IsTakenOver, "CancelDoesNotConsumeOffer");
Check(activeLabel.enableWordWrapping && !activeLabel.enableAutoSizing && activeLabel.maxVisibleLines == 100 && emptyCard.EmptySeatLabel.text == "Empty Seat", "CardTextPresentationRestoredOnExit");
Check(emptyCard.EmptySeatLabel.color.Equals(new UnityEngine.Color(0,0,0,.4980392f)), "EmptyReserveRestoresNativeColorOnClose");
GoalieRosterMenu.Finish(selection);
Check(pair.Gm.DefaultSelections == 1 && selection.Menu.OnDestroy.Actions.Count == 0 && selection.Menu.OnHidingCompleted.Actions.Count == 0, "CloseUnregistersAndRestoresGMExactlyOnce");
selection = Recruit(state, pair); var previousActive = state.runData.teamData.goalie;
Click(selection, 1);
Check(selection.Menu.m_Header.m_Title.text.Contains("Fill empty reserve") && !pair.Offer.Taken, "EmptyReserveConfirmationBeforeMutation");
Check(selection.Menu.m_ActivePlayersList.m_SkaterCards[1].EmptySeatLabel.text.Contains("Confirm hire") && selection.Menu.m_ActivePlayersList.m_SkaterCards[1].EmptySeatLabel.text.Contains("Reserve"), "RecruitConfirmationVisibleOnActualEmptyCard");
Click(selection, 1);
Check(pair.Offer.Taken && state.runData.teamData.goalie.Pointer == previousActive.Pointer && GoaliePersistenceBridge.Roster.Selection.Reserve.PlayerId == "candidate", "ChooseEmptyReserveChangesOnlyReserve");
Check(pair.Gm.m_ChoicesMade == 1 && pair.Gm.Refreshes == 1 && pair.Offer.Visit.Event.NextCalls == 1, "ConfirmedSigningAdvancesGMOnce");
Check(!GoalieRecruitment.BeginSelection(pair.Offer), "DoubleClickCommitsOnce");
state = Fresh(false); pair = Offer(state); selection = Recruit(state, pair);
Check(selection.Menu.m_ActivePlayersList.m_SkaterCards.Count == 1, "ReserveDisabledShowsExactlyStarter");
Click(selection, 0); Check(selection.Menu.m_Header.m_Title.text.Contains("Replace active") && state.runData.teamData.goalie.id == "active", "ChooseFilledSlotShowsReplacement");
Click(selection, 0); Check(state.runData.teamData.goalie.id == "candidate" && GoaliePersistenceBridge.Roster.Selection.Reserve == null, "ReserveDisabledReplacesOnlyStarter");
state = Fresh(); pair = Offer(state); selection = Recruit(state, pair); state.runData.currentMap = "{\"act\":1,\"path\":[{\"x\":0,\"y\":0}]}";
Click(selection, 0); Check(!pair.Offer.Taken && state.runData.teamData.goalie.id == "active", "StaleVisitCannotRecruit");
selection.Menu.TryLeaveMenu(false);
state = Fresh(); pair = Offer(state); selection = Recruit(state, pair); ES3.FailStore = true;
Click(selection, 0); Click(selection, 0); ES3.FailStore = false;
Check(selection.Menu.m_ActivePlayersList.m_SkaterCards[0].m_SkaterName.text.Contains("Save failed"), "SaveFailureVisibleInCardWithoutHeader");
Check(selection.Menu.m_ActivePlayersList.m_SkaterCards[1].EmptySeatLabel.text.Contains("select to hire"), "StarterFailureDoesNotMarkReserve");
Check(!pair.Offer.Taken && state.runData.teamData.goalie.id == "active" && !selection.Unresolved && selection.Menu.m_Header.m_Title.text.Contains("previous goalies retained"), "ConfirmedNoWriteRestoresOfferAndBothOccupants");
Click(selection, 0); Click(selection, 0); Check(pair.Offer.Taken, "ConfirmedNoWriteMayRetry");
state = Fresh(); pair = Offer(state); selection = Recruit(state, pair); ES3.FailStore = true;
Click(selection, 1); Click(selection, 1); ES3.FailStore = false;
Check(selection.Confirming == null && selection.Menu.m_ActivePlayersList.m_SkaterCards[1].EmptySeatLabel.text.Contains("Save failed")
    && selection.Menu.m_ActivePlayersList.m_SkaterCards[0].m_SkaterName.text == "Active: active", "ReserveFailureKeepsAttemptedSlotAfterConfirmationCleared");
Click(selection, 0);
Check(selection.Menu.m_ActivePlayersList.m_SkaterCards[0].m_SkaterName.text.Contains("Confirm hire")
    && selection.Menu.m_ActivePlayersList.m_SkaterCards[1].EmptySeatLabel.text.Contains("select to hire"), "ChangingTargetClearsPriorSlotStatus");
selection.Menu.TryLeaveMenu(false);
foreach (string outcome in new[] { "false", "fault", "cancel" }) {
    state = Fresh(); pair = Offer(state); selection = Recruit(state, pair); state.Repository.Outcome = outcome;
    Click(selection, 0); Click(selection, 0);
    Check(pair.Offer.Taken && state.runData.teamData.goalie.id == "candidate" && pair.Gm.m_ChoicesMade == 1, "ConfirmedLocalPairConsumesDespiteNative_" + outcome);
}
state = Fresh(); pair = Offer(state); selection = Recruit(state, pair); state.Repository.CompleteImmediately = false;
Click(selection, 0); Click(selection, 0); int saves = state.Repository.Saves;
Click(selection, 0); selection.Menu.TryLeaveMenu(false);
Check(selection.Saving && !selection.Menu.IsHiding && !pair.Offer.Taken && state.Repository.Saves == saves, "PendingSaveBlocksCancelAndDoubleClick");
state.Repository.Complete(state.Repository.Pending[0]); GoaliePersistenceBridge.Tick();
Check(pair.Offer.Taken && pair.Gm.Refreshes == 1 && GoalieRosterMenu.Current == null, "AsyncCompletionConsumesAndClosesExactlyOnce");
state = Fresh(); pair = Offer(state); selection = Recruit(state, pair); ES3.FailMarker = true;
Click(selection, 0); Click(selection, 0); ES3.FailMarker = false;
Check(selection.Unresolved && pair.Offer.Unresolved && !pair.Offer.Taken && !GoaliePersistenceBridge.Roster.CanEdit(state)
    && selection.Menu.m_Header.m_Title.text.Contains("Continue"), "UnpairedNativeWriteLocksOfferAndExplainsRecovery");
selection.Menu.TryLeaveMenu(false); Check(!GoalieRecruitment.BeginSelection(pair.Offer), "UnresolvedOfferCannotBeReusedAfterClose");
state = Fresh(); var roster = GoaliePersistenceBridge.Roster;
roster.TryRecruit(state, new GoaltenderData { id = "reserve" }, GoalieSlot.Reserve, roster.Selection.Revision, signedContract: new() { Duration = 4, PlayedMatches = 2 });
GoalieRosterMenu.Open(state); selection = GoalieRosterMenu.Current;
Check(selection.Menu.m_ActivePlayersList.m_SkaterCards[1].m_SkaterName.text == "Reserve: reserve", "ContractDurationDoesNotReplaceOrAppendToGoalieName");
Check(selection.Menu.m_ActivePlayersList.m_SkaterCards[1].m_FreeAgentDurationContainer.Active
    && selection.Menu.m_ActivePlayersList.m_SkaterCards[1].m_FreeAgentDuration.text == "2", "SignedGoalieUsesNativeSkaterRemainingMatchesBadge");
Click(selection, 1); Click(selection, 1);
Check(GoalieRosterMenu.Current == null && state.runData.teamData.goalie.id == "reserve" && UnityEngine.Object.CurrentMenu.Team.m_G.id == "reserve"
    && !UnityEngine.Object.CurrentMenu.IsHiding && roster.Selection.Active.Contract.PlayedMatches == 2, "SwapRefreshesCanonicalParentWithoutClosingLineupOrResettingContract");
var durableVisit = roster.Selection.RecruitmentVisit;
state = Fresh(); pair = Offer(state); selection = Recruit(state, pair);
Click(selection, 0); Click(selection, 0);
var saved = roster.Selection;
Check(saved.RecruitmentVisit.ConsumedOfferIds.Length == 1 && saved.RecruitmentVisit.Generation == pair.Offer.Visit.Ledger.Generation, "SigningQuotaCommittedInSameGoalieCheckpoint");
var model = state.Repository.LoadFromPersistentStorage().GetAwaiter().GetResult();
var loadedRun = new RunData { runID = state.runData.runID, currentMap = state.runData.currentMap, squadId = state.runData.squadId,
    teamData = new() { goalie = new DeserializerV2().ConvertGoaltenderDataModelToGoaltenderData(model.team.skaters[0]) }, teamState = model.teamState };
state.runData = loadedRun; GoaliePersistenceBridge.Hydrated(new DeserializerV2(), loadedRun, model);
var resumed = GoalieRecruitment.VisitLedger(state, 99);
Check(resumed.Generation == saved.RecruitmentVisit.Generation && resumed.Quota == 2 && resumed.ConsumedOfferIds.Length == 1, "ContinueBeforeGMAdvanceKeepsSignedQuotaAndGeneration");
loadedRun.currentMap = "{\"act\":1,\"path\":[{\"x\":0,\"y\":0}]}";
var later = GoalieRecruitment.VisitLedger(state, 2);
Check(later.Generation != resumed.Generation && later.ConsumedOfferIds.Length == 0, "LaterNativePathVisitDoesNotInheritCoordinateBlacklist");
state = Fresh();
File.WriteAllText(Path.Combine(Plugin.ModFolder, "campaign.txt"), "Goalie Recruitment = On");
var pool = GoalieFreeAgents.DirectoryPath(Plugin.ModFolder); Directory.CreateDirectory(pool);
for (int i = 0; i < 5; i++) File.WriteAllText(Path.Combine(pool, "goalie" + i + ".txt"), "Position = Goalie\nCustom Contract = yes\nContract Matches = 6\n");
var mapNode = new STS.Map.MapNode();
var gmNode = new GeneralManagerEvent();
var gm = new EventChoiceMenu { SourceNode = gmNode, mapNode = mapNode, Visible = false };
state.currentNode = null; state.runData.currentNodePoint.x = 9;
GoalieRosterMenu.RecruitInitialized(gm);
Check(GoalieRecruitment.Choices(gmNode, 0) && gmNode.eventChoices.Count == 2, "ActualOfferGenerationUsesAuthoredGoaliePool");
var actual = GoalieRecruitment.Current; var selectedOffer = actual.Offers[0];
Check(selectedOffer.Contract?.Duration == 6 && gmNode.eventChoices[0].translatedButtonName.Contains("6 played-match contract"), "GeneratedOfferDisplaysActualFrozenSignedDuration");
foreach (var file in Directory.GetFiles(pool)) File.WriteAllText(file, "Position = Goalie\nCustom Contract = yes\nContract Matches = 99\n");
var exposed = selectedOffer.Contract; exposed.Duration = 22;
Check(selectedOffer.Contract.Duration == 6, "OfferContractCannotBeMutatedThroughExposedSnapshot");
gm.m_Items.Add(new() { ChoiceData = gmNode.eventChoices[0] });
GoalieRosterMenu.RecruitClick(gm, gm.m_Items[0].Navigable); selection = GoalieRosterMenu.Current; Click(selection, 0); Click(selection, 0);
Check(roster.Selection.Active.Contract?.Duration == 6 && roster.Selection.Active.Contract.PlayedMatches == 0, "ConfirmedOfferSignsFrozenDurationDespiteAuthorEdits");
string consumedId = selectedOffer.Id; string generation = roster.Selection.RecruitmentVisit.Generation;
roster.TryRecruit(state, new GoaltenderData { id = "replacement-after-signing" }, GoalieSlot.Active, roster.Selection.Revision);
GoalieRosterMenu.RecruitInitialized(gm);
Check(GoalieRecruitment.Choices(gmNode, 0) && GoalieRecruitment.Current.Hires == 1 && GoalieRecruitment.Current.Ledger.Generation == generation
    && GoalieRecruitment.Current.Offers.All(o => o.Id != consumedId), "ReplacedAwayGoalieDoesNotRefundSavedHire");
string witness = GoalieRosterRuntime.RecruitmentWitness(state);
state.runData.currentMap = "{\"nodes\":[1,2],\"path\":[],\"act\":1}";
Check(GoalieRosterRuntime.RecruitmentWitness(state) == witness, "UnrelatedMapFieldsAndFormattingCannotMintQuota");
foreach (var file in Directory.GetFiles(pool)) File.Delete(file);
GoalieRosterMenu.RecruitInitialized(gm);
Check(!GoalieRecruitment.Choices(gmNode, 0) && GoalieRecruitment.Current.Hires == 1 && roster.Selection.RecruitmentVisit.ConsumedOfferIds.Length == 1,
    "MissingPoolDoesNotReplenishConsumedHires");
state = Fresh(); pair = Offer(state); selection = Recruit(state, pair);
ES3.AfterStore = () => ES3File.FailPhysicalRead = true;
Click(selection, 0); Click(selection, 0); ES3.AfterStore = null; ES3File.FailPhysicalRead = false;
Check(selection.Unresolved && !pair.Offer.Taken && state.runData.teamData.goalie.id == "candidate" && !roster.CanEdit(state), "ReadbackUnknownDoesNotRollbackOrRefundOffer");
selection.Menu.TryLeaveMenu(false);
state = Fresh(); pair = Offer(state); Tape2Tape.Customization.UI.T2TUIModule.FailOpening = true;
GoalieRosterMenu.RecruitClick(pair.Gm, pair.Gm.m_Items[0].Navigable); Tape2Tape.Customization.UI.T2TUIModule.FailOpening = false;
Check(GoalieRosterMenu.Current == null && !pair.Offer.Taken && pair.Offer.Visit.Pending == null && pair.Gm.Visible && !pair.Gm.m_IsTakenOver, "MenuPreparationFailureRestoresGMAndOffer");
state = Fresh(); pair = Offer(state); selection = Recruit(state, pair); state.Repository.CompleteImmediately = false;
Click(selection, 0); Click(selection, 0);
var oldState = state; var oldPair = pair;
selection.Menu.OnDestroy.Invoke(selection.Menu); state = Fresh();
oldState.Repository.Complete(oldState.Repository.Pending[0]); GoaliePersistenceBridge.Tick();
Check(oldPair.Offer.Taken && roster.CanEdit(state) && state.runData.teamData.goalie.id == "active" && GoalieRosterMenu.Current == null,
    "LateOldRunLocalCommitCannotDisableNewRunOrRefundSigning");

// I1: retain native parent presentation and pending forward ownership across takeover.
foreach (string exit in new[] { "cancel", "swap", "preparation-failure", "refresh-then-cancel", "initially-hidden" }) {
    state = Fresh();
    roster.TryRecruit(state, new GoaltenderData { id = "reserve" }, GoalieSlot.Reserve, roster.Selection.Revision);
    var parent = UnityEngine.Object.CurrentMenu;
    var forward = new GoaltenderData { id = "pending-forward" }; var bench = new GoaltenderData { id = "second-line-forward" };
    parent.Team.PendingForwards.Add(forward); parent.Team.BenchPlayers.Add(bench); parent.Team.HasChanged = true;
    parent.m_AreSkatersLocked = true; parent.m_HideBench = exit == "initially-hidden";
    parent.LoadCurrentTeam();
    bool originalVisibility = parent.m_BenchPlayersList.gameObject.activeSelf;
    if (exit == "preparation-failure") parent.FailNextFocus = true;
    GoalieRosterMenu.Open(state); selection = GoalieRosterMenu.Current;
    if (exit != "preparation-failure") {
        if (exit == "refresh-then-cancel") {
            var retiredCard = parent.m_ActivePlayersList.m_SkaterCards[1]; int callbacks = parent.ToggleCallbacks;
            parent.LoadCurrentTeam();
            retiredCard.Navigable.Click();
            Check(parent.ToggleCallbacks == callbacks && !parent.m_BenchPlayersList.gameObject.activeSelf
                && parent.m_ActivePlayersList.m_SkaterCards.Count == 2, "NativeSubviewRefreshHidesBenchAndUnsubscribesOldCards");
        }
        if (exit == "swap") { Click(selection, 1); Click(selection, 1); }
        else parent.TryLeaveMenu(false);
    }
    Check(GoalieRosterMenu.Current == null && !parent.IsHiding && parent.m_HideBench == (exit == "initially-hidden")
        && parent.m_BenchPlayersList.gameObject.activeSelf == originalVisibility
        && parent.m_BenchSkaters.Count == (exit == "initially-hidden" ? 0 : 1)
        && parent.m_BenchPlayersList.m_SkaterCards.Count == (exit == "initially-hidden" ? 0 : 1), "ParentBenchPolicyPopulationAndVisibilityRestored_" + exit);
    var normalForward = parent.m_ActivePlayersList.m_SkaterCards[1];
    Check(parent.m_AreSkatersLocked && parent.Team.HasChanged && ReferenceEquals(parent.Team.PendingForwards[0], forward)
        && normalForward.CurrentSkaterData.Skater.Pointer == forward.Pointer && normalForward.Locked
        && normalForward.m_PositionLetter.text == "C" && normalForward.m_SkaterName.text == "pending-forward"
        && !normalForward.m_FreeAgentDurationContainer.Active && normalForward.m_FreeAgentDuration.text == ""
        && !normalForward.Toggle.IsSelected && parent.m_Header.m_Title.text == "Lineup"
        && parent.m_ActivePlayersList.m_SkaterCards[0].m_PositionLetter.text == "G"
        && !parent.m_ActivePlayersList.m_SkaterCards[0].Locked
        && parent.m_ActivePlayersList.m_SkaterCards[0].m_SkaterName.text == state.runData.teamData.goalie.FullName
        && parent.m_ActivePlayersList.m_SkaterCards.Concat(parent.m_BenchPlayersList.m_SkaterCards)
            .All(c => !c.m_FreeAgentDurationContainer.Active && c.m_FreeAgentDuration.text == "" && !c.Toggle.IsSelected)
        && parent.m_BenchPlayersList.m_SkaterCards.All(c => c.Locked && c.m_PositionLetter.text == "C" && c.m_SkaterName.text == "second-line-forward"),
        "ParentNormalPresentationAndForwardEditsRestored_" + exit);
}

// Signing into reserve has a visible two-slot result when the active is expired.
foreach (string protection in new[] { "none", "native", "custom", "unknown-native", "unknown-custom" }) {
    state = Fresh();
    roster.Fresh(state.runData, Plugin.ModFolder, true, new() { Duration = 4, PlayedMatches = 4, LastMatchKey = "completed", AwaitingReplacement = true });
    state.SaveRunData();
    state.runData.teamData.NativeHandcuffs = protection == "native";
    state.runData.teamData.FailProtection = protection == "unknown-native";
    CustomRewardCampaignRules.Fail = protection == "unknown-custom";
    if (protection == "custom") { CustomRewardRuntime.EnsureAssets(); RetentionRegistryFixture.Protect(state.runData.teamData); }
    pair = Offer(state); pair.Offer.Contract = GoalieContractPolicy.Sign(6);
    selection = Recruit(state, pair); int beforeSaves = state.Repository.Saves;
    Click(selection, 1);
    if (protection.StartsWith("unknown")) {
        Click(selection, 1);
        Check(!pair.Offer.Taken && state.Repository.Saves == beforeSaves && selection.Menu.m_Header.m_Title.text.Contains("could not be verified"), "UnknownProtectionBlocksDeferredReserveSigning_" + protection);
        selection.Menu.TryLeaveMenu(false);
    } else {
        Check(!pair.Offer.Taken && selection.Menu.m_Header.m_Title.text.Contains(protection == "none" ? "becomes active" : "remains active"), "DeferredReplacementResultShownBeforeConfirmation_" + protection);
        Click(selection, 1);
        var result = roster.Selection;
        Check(pair.Offer.Taken && result.Active != null && result.RecruitmentVisit.ConsumedOfferIds.Length == 1
            && (protection == "none" ? result.Active.PlayerId == "candidate" && result.Reserve == null && result.Active.Contract.Duration == 6 && result.Active.Contract.PlayedMatches == 0
                : result.Active.PlayerId == "active" && result.Active.Contract.PlayedMatches == 4 && result.Reserve.PlayerId == "candidate"), "DeferredReplacementHonorsKnownProtectionAndPreservesSigningLedger_" + protection);
    }
    CustomRewardRuntime.Definitions.Clear(); CustomRewardCampaignRules.Fail = false;
}
state = Fresh();
roster.Fresh(state.runData, Plugin.ModFolder, true, new() { Duration = 4, PlayedMatches = 4, AwaitingReplacement = true }); state.SaveRunData();
pair = Offer(state); selection = Recruit(state, pair); Click(selection, 1);
state.runData.teamData.NativeHandcuffs = true; Click(selection, 1);
Check(!pair.Offer.Taken && selection.Menu.m_Header.m_Title.text.Contains("remains active"), "ChangedProtectionRequiresNewConfirmationOfUpdatedResult");
Click(selection, 1); Check(pair.Offer.Taken && roster.Selection.Active.PlayerId == "active" && roster.Selection.Reserve.PlayerId == "candidate", "UpdatedProtectedResultCanBeConfirmed");
state = Fresh();
roster.Fresh(state.runData, Plugin.ModFolder, true, new() { Duration = 4, PlayedMatches = 4, AwaitingReplacement = true }); state.SaveRunData();
string brokenDefinitions = Path.Combine(Plugin.ModFolder, "broken.txt");
File.WriteAllText(brokenDefinitions, "Export Custom Reward broken = not-base64");
pair = Offer(state); selection = Recruit(state, pair); int beforeDefinitionFailure = state.Repository.Saves;
var retainedActive = state.runData.teamData.goalie;
Click(selection, 1); Click(selection, 1);
Check(!pair.Offer.Taken && state.Repository.Saves == beforeDefinitionFailure && state.runData.teamData.goalie.Pointer == retainedActive.Pointer
    && roster.Selection.Reserve == null && roster.Selection.RecruitmentVisit == null
    && selection.Menu.m_Header.m_Title.text.Contains("could not be verified"), "SwallowedLoaderFailureCannotConsumeSigningOrRemoveExpiredActive");
File.Delete(brokenDefinitions);
Click(selection, 1); Check(!pair.Offer.Taken && selection.Menu.m_Header.m_Title.text.Contains("becomes active"), "RecoveredRegistryRequiresOrdinaryReplacementConfirmation");
Click(selection, 1); Check(pair.Offer.Taken && roster.Selection.Active.PlayerId == "candidate" && roster.Selection.RecruitmentVisit.ConsumedOfferIds.Length == 1, "RecoveredRegistryAllowsExactlyOneSigning");
Console.WriteLine($"PASS {checks} source-linked goalie menu checks");

// Native InitData precedes Show.Initialize and visibility; no SetCurrentNode.
state = Fresh(); state.currentNode = null; state.runData.currentNodePoint.x = 44;
File.WriteAllText(Path.Combine(Plugin.ModFolder, "campaign.txt"), "Goalie Recruitment = On");
var contextPool = GoalieFreeAgents.DirectoryPath(Plugin.ModFolder); Directory.CreateDirectory(contextPool);
File.WriteAllText(Path.Combine(contextPool, "permanent.txt"), "Position = Goalie\nCustom Contract = no\n");
var contextEvent = new GeneralManagerEvent();
var firstMap = new STS.Map.MapNode { Node = new() { LayerIndex = 1, NodeIndex = 2 } };
var firstMenu = new EventChoiceMenu { SourceNode = contextEvent, mapNode = firstMap, Visible = false };
GoalieRosterMenu.RecruitInitialized(firstMenu); GoalieRecruitment.BeginVisit(contextEvent);
Check(GoalieRecruitment.Choices(contextEvent, 0) && !firstMenu.Visible && GoalieRecruitment.Current.Offers[0].Contract == null,
    "ActualInitDataBindsPermanentOffersBeforeVisibilityWithoutMatchManager");
Check(contextEvent.eventChoices.All(c => c.FreeAgentLeavingConditions == null || c.FreeAgentLeavingConditions.Count == 0),
    "PermanentGmOffersDoNotInventNativeExpiryConditions");
Check(state.currentNode == null && state.runData.currentNodePoint.x == 44, "MenuBindingDoesNotSynthesizeNativeCurrentNode");
var firstVisit = GoalieRecruitment.Current; var firstWitness = firstVisit.Ledger.Witness;
Check(!GoalieRecruitment.OwnsEvent(new GeneralManagerEvent()), "UnrelatedEventDoesNotBorrowBoundGM");
var secondMap = new STS.Map.MapNode { Node = new() { LayerIndex = 1, NodeIndex = 3 } };
var secondMenu = new EventChoiceMenu { SourceNode = contextEvent, mapNode = secondMap, Visible = false };
GoalieRosterMenu.RecruitInitialized(secondMenu); GoalieRecruitment.BeginVisit(contextEvent);
Check(GoalieRecruitment.Choices(contextEvent, 0) && GoalieRecruitment.Current.Ledger.Witness != firstWitness
    && !GoalieRecruitment.IsCurrent(firstVisit), "ReusedGMEventAtSiblingNodeHasDistinctWitnessAndInvalidatesOldOffer");
var secondVisit = GoalieRecruitment.Current;
GoalieRecruitment.Closed(firstMenu);
Check(GoalieRecruitment.IsCurrent(secondVisit), "ClosingOldMenuCannotClearNewBinding");
secondMenu.SetVisibility(false, true);
Check(GoalieRecruitment.IsCurrent(secondVisit), "TemporaryTakeoverHidingRetainsExactVisit");
GoalieRecruitment.Closed(secondMenu);
Check(!GoalieRecruitment.IsCurrent(secondVisit), "ActualMenuClosureInvalidatesOffers");
GoalieRosterMenu.RecruitInitialized(firstMenu); GoalieRecruitment.Choices(contextEvent, 0);
GoalieRosterMenu.RecruitInitialized(new EventChoiceMenu { SourceNode = new STS.Map.EventNode(), mapNode = firstMap });
Check(!GoalieRecruitment.IsGoalieNode, "NonGmMenuInvalidatesStaleGoalieSelection");
var legacyPanel = new EventChoicePanel();
typeof(GoalieRosterMenu).GetMethod("LegacyOpening", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { legacyPanel, firstMap, contextEvent });
GoalieRecruitment.BeginVisit(contextEvent);
Check(GoalieRecruitment.Choices(contextEvent, 0) && GoalieRecruitment.Current.Legacy == legacyPanel, "LegacyEntryBindsBeforeNativeInitialize");
GoalieRecruitment.Closed(legacyPanel);
Check(!GoalieRecruitment.IsGoalieNode, "LegacyClosureInvalidatesExactBinding");
state = Fresh(); pair = Offer(state); pair.Offer.Candidate.id = new string('X', 100); selection = Recruit(state, pair);
Click(selection, 1);
var confirmLabel = selection.Menu.m_ActivePlayersList.m_SkaterCards[1].EmptySeatLabel;
Check(confirmLabel.text.StartsWith("Reserve: Confirm hire ") && confirmLabel.text.Length < 50 && !confirmLabel.enableWordWrapping,
    "LongCandidateKeepsActionAndSlotVisibleWithoutWrapping");
selection.Menu.TryLeaveMenu(false);
foreach (bool missingName in new[] { true, false }) {
    state = Fresh(); pair = Offer(state); selection = Recruit(state, pair);
    var slotCard = selection.Menu.m_ActivePlayersList.m_SkaterCards[1];
    var info = slotCard.m_EmptySeatContainer.transform.Find("PlayerInfo");
    if (missingName) info.Children.Remove("Name");
    else info.Children["Name"].Components = Array.Empty<object>();
    int warningCount = Plugin.Log.Warnings.Count;
    Click(selection, 1); Click(selection, 1);
    Check(pair.Offer.Taken && Plugin.Log.Warnings.Count == warningCount + 1
        && slotCard.EmptySwapLabel.text == "Swap" && slotCard.EmptyStatNum.text == "20" && slotCard.EmptyStatLabel.text == "OVR",
        "MissingExactEmptyNamePreservesRecruitmentAndOtherLabels_" + missingName);
}
state = Fresh(); pair = Offer(state); selection = Recruit(state, pair); state.Repository.CompleteImmediately = false;
ES3.FailMarker = true; Click(selection, 1); Click(selection, 1);
Check(selection.Menu.m_ActivePlayersList.m_SkaterCards[1].EmptySeatLabel.text.Contains("Saving")
    && selection.Menu.m_ActivePlayersList.m_SkaterCards[0].m_SkaterName.text == "Active: active", "PendingStatusOnlyMarksAttemptedReserve");
state.Repository.Complete(state.Repository.Pending[0]); GoaliePersistenceBridge.Tick(); ES3.FailMarker = false;
Check(!selection.Unresolved && selection.Menu.m_ActivePlayersList.m_SkaterCards[1].EmptySeatLabel.text.Contains("Save failed")
    && selection.Menu.m_ActivePlayersList.m_SkaterCards[0].m_SkaterName.text == "Active: active", "DelayedReserveRollbackRetainsAttemptedSlot");
selection.Menu.TryLeaveMenu(false);
state = Fresh(); pair = Offer(state); selection = Recruit(state, pair); ES3.FailMarker = true;
Click(selection, 0); Click(selection, 0); ES3.FailMarker = false;
Check(selection.Unresolved && selection.Menu.m_ActivePlayersList.m_SkaterCards[0].m_SkaterName.text.Contains("Save unresolved")
    && selection.Menu.m_ActivePlayersList.m_SkaterCards[1].EmptySeatLabel.text.Contains("select to hire"), "UnresolvedStatusOnlyMarksAttemptedStarter");
selection.Menu.TryLeaveMenu(false);
state = Fresh(); GoalieRosterMenu.Open(state); selection = GoalieRosterMenu.Current;
Check(selection.Offer == null && selection.Menu.m_ActivePlayersList.m_SkaterCards[1].Locked
    && selection.Menu.m_ActivePlayersList.m_SkaterCards[1].EmptySeatLabel.text.Contains("recruit at GM"),
    "ManagementEmptyReserveRemainsLockedWithRecruitInstruction");
selection.Menu.TryLeaveMenu(false);
state = Fresh(); pair = Offer(state); selection = Recruit(state, pair); state.Repository.CompleteImmediately = false;
ES3.FailMarker = true;
Click(selection, 0); Click(selection, 0);
var closedLabel = selection.Menu.m_ActivePlayersList.m_SkaterCards[0].m_SkaterName;
selection.Menu.OnDestroy.Invoke(selection.Menu);
selection.Menu.m_Header.m_Title.text = "Reused native menu";
state.Repository.Complete(state.Repository.Pending[0]); GoaliePersistenceBridge.Tick(); ES3.FailMarker = false;
Check(selection.Unresolved && selection.Finished && selection.Labels.Count == 0 && closedLabel.enableWordWrapping
    && selection.Menu.m_Header.m_Title.text == "Reused native menu", "LateUnresolvedCallbackCannotRestyleClosedMenu");
state = Fresh(); pair = Offer(state); pair.Offer.Contract = new() { Duration = 1000 }; selection = Recruit(state, pair);
var incomingCard = selection.Menu.m_ActivePlayersList.m_SkaterCards[1];
Check(!incomingCard.m_FreeAgentDurationContainer.Active, "EmptySlotHasNoCurrentContractBadge");
var badgeRect = incomingCard.m_FreeAgentDurationContainer.GetComponent<UnityEngine.RectTransform>();
int originalBadgeOrder = badgeRect.GetSiblingIndex();
float originalWidth = badgeRect.sizeDelta.x, originalLeft = badgeRect.anchoredPosition.x - badgeRect.sizeDelta.x / 2;
float originalTextWidth = incomingCard.m_FreeAgentDuration.rectTransform.sizeDelta.x;
Click(selection, 1);
Check(badgeRect.GetSiblingIndex() > originalBadgeOrder, "GoalieContractBadgeDrawsAboveLaterPortraitSiblings");
Check(incomingCard.m_FreeAgentDurationContainer.Active && incomingCard.m_FreeAgentDuration.text == "1000"
    && incomingCard.EmptySeatLabel.text.Contains("Confirm hire"), "ConfirmedEmptyTargetPreviewsFrozenIncomingDurationInNativeBadge");
Check(!incomingCard.m_FreeAgentDuration.enableWordWrapping && incomingCard.m_FreeAgentDuration.maxVisibleLines == 1
    && incomingCard.m_FreeAgentDuration.rectTransform.sizeDelta.x >= 40
    && Math.Abs(badgeRect.anchoredPosition.x - badgeRect.sizeDelta.x / 2 - originalLeft) < 0.001f,
    "MultiDigitBadgeKeepsNativePlacementAndFitsWithoutVerticalWrapping");
Click(selection, 0);
Check(!incomingCard.m_FreeAgentDurationContainer.Active
    && selection.Menu.m_ActivePlayersList.m_SkaterCards[0].m_FreeAgentDuration.text == "1000", "IncomingBadgeFollowsConfirmationTarget");
selection.Menu.TryLeaveMenu(false);
Check(badgeRect.GetSiblingIndex() == originalBadgeOrder, "GoalieBadgeRestoresNativeSiblingOrderOnExit");
Check(!incomingCard.m_FreeAgentDurationContainer.Active && incomingCard.m_FreeAgentDuration.text == ""
    && badgeRect.sizeDelta.x == originalWidth && incomingCard.m_FreeAgentDuration.rectTransform.sizeDelta.x == originalTextWidth
    && incomingCard.m_FreeAgentDuration.enableWordWrapping, "NativeBadgeGeometryTextStyleAndVisibilityRestoredOnExit");
state = Fresh(); GoaliePersistenceBridge.Roster.TryRecruit(state, new GoaltenderData { id = "expired" }, GoalieSlot.Active,
    GoaliePersistenceBridge.Roster.Selection.Revision, signedContract: new() { Duration = 4, PlayedMatches = 4, AwaitingReplacement = true });
GoalieRosterMenu.Open(state); selection = GoalieRosterMenu.Current;
Check(selection.Menu.m_ActivePlayersList.m_SkaterCards[0].m_FreeAgentDurationContainer.Active
    && selection.Menu.m_ActivePlayersList.m_SkaterCards[0].m_FreeAgentDuration.text == "0", "ExpiredGoalieNativeBadgeShowsZeroRemaining");
selection.Menu.TryLeaveMenu(false);
state = Fresh(); GoaliePersistenceBridge.Roster.TryRecruit(state, new GoaltenderData { id = "timed" }, GoalieSlot.Active,
    GoaliePersistenceBridge.Roster.Selection.Revision, signedContract: new() { Duration = 4, PlayedMatches = 1 });
pair = Offer(state); selection = Recruit(state, pair);
Check(selection.Menu.m_ActivePlayersList.m_SkaterCards[0].m_FreeAgentDuration.text == "3", "RecruitmentInitiallyShowsCurrentOccupantRemainingMatches");
Click(selection, 0);
Check(!selection.Menu.m_ActivePlayersList.m_SkaterCards[0].m_FreeAgentDurationContainer.Active,
    "PermanentIncomingOfferHidesExistingTimedBadgeDuringConfirmation");
selection.Menu.TryLeaveMenu(false);
state = Fresh(); GoaliePersistenceBridge.Roster.TryRecruit(state, new GoaltenderData { id = "timed-main" }, GoalieSlot.Active,
    GoaliePersistenceBridge.Roster.Selection.Revision, signedContract: new() { Duration = 8, PlayedMatches = 3 });
var ordinaryLineup = UnityEngine.Object.CurrentMenu;
ordinaryLineup.LoadCurrentTeam();
var ordinaryGoalie = ordinaryLineup.m_ActivePlayersList.m_SkaterCards[0];
Check(ordinaryGoalie.m_FreeAgentDurationContainer.Active && ordinaryGoalie.m_FreeAgentDuration.text == "5"
    && ordinaryGoalie.m_SkaterName.text == "timed-main" && GoalieRosterMenu.Current == null,
    "OrdinaryLineupGoalieShowsNativeContractBadgeBeforeOpeningSubview");
ordinaryLineup.LoadCurrentTeam(); ordinaryGoalie = ordinaryLineup.m_ActivePlayersList.m_SkaterCards[0];
Check(ordinaryLineup.OnHidingCompleted.Actions.Count == 1 && ordinaryLineup.OnDestroy.Actions.Count == 1,
    "RepeatedOrdinaryRefreshDoesNotAccumulateBadgeLifecycleHandlers");
GoalieRosterMenu.EntryLabel(ordinaryLineup);
Check(ordinaryGoalie.m_FreeAgentDuration.text == "5" && ordinaryLineup.OnDestroy.Actions.Count == 1,
    "InitEntryAfterLoadDoesNotCaptureAlreadyProjectedBadgeAsNativeBaseline");
ordinaryLineup.OnHidingCompleted.Invoke(ordinaryLineup);
Check(!ordinaryGoalie.m_FreeAgentDurationContainer.Active && ordinaryGoalie.m_FreeAgentDuration.text == ""
    && ordinaryLineup.OnHidingCompleted.Actions.Count == 0 && ordinaryLineup.OnDestroy.Actions.Count == 0,
    "OrdinaryBadgeRestoresAndUnregistersOnClose");
ordinaryLineup.LoadCurrentTeam(); ordinaryGoalie = ordinaryLineup.m_ActivePlayersList.m_SkaterCards[0];
GoalieRosterMenu.Open(state); selection = GoalieRosterMenu.Current;
Check(!ordinaryGoalie.m_FreeAgentDurationContainer.Active && ordinaryLineup.OnDestroy.Actions.Count == 1,
    "OpeningGoalieSubviewRestoresOrdinaryBadgeAndTransfersLifecycleOwnership");
selection.Menu.TryLeaveMenu(false);
ordinaryLineup.OnDestroy.Invoke(ordinaryLineup);
Check(ordinaryLineup.OnDestroy.Actions.Count == 0, "OrdinaryBadgeRestoresAfterSubviewReturnAndDestroy");
ordinaryLineup.LoadCurrentTeam(); ordinaryGoalie = ordinaryLineup.m_ActivePlayersList.m_SkaterCards[0];
// Model the native pool reusing this exact card before LoadCurrentTeam's postfix.
ordinaryLineup.Team.OriginalTeamData = new TeamData();
ordinaryGoalie.CurrentSkaterData = new(ordinaryLineup.Team.OriginalTeamData, new GoaltenderData { id = "foreign" }, ESkaterPosition.C, null, null, Tape2Tape.Customization.UI.EStatBonusesPreviewMode.None, null);
ordinaryGoalie.m_FreeAgentDuration.text = "7"; ordinaryGoalie.m_FreeAgentDurationContainer.SetActive(true);
GoalieRosterMenu.Refresh(ordinaryLineup);
Check(ordinaryGoalie.m_FreeAgentDuration.text == "7" && ordinaryGoalie.m_FreeAgentDurationContainer.Active
    && ordinaryGoalie.m_FreeAgentDuration.enableWordWrapping && ordinaryLineup.OnDestroy.Actions.Count == 0,
    "PooledForeignCardKeepsNativeRefreshedContractValuesAfterPostfixCleanup");
Check(ordinaryGoalie.m_FreeAgentDurationContainer.transform.GetSiblingIndex() == originalBadgeOrder,
    "PooledForeignCardRestoresNativeBadgeSiblingOrder");
state = Fresh(); ordinaryLineup = UnityEngine.Object.CurrentMenu; ordinaryLineup.LoadCurrentTeam();
ordinaryGoalie = ordinaryLineup.m_ActivePlayersList.m_SkaterCards[0];
state.runData = new RunData { teamData = state.runData.teamData };
ordinaryGoalie.m_FreeAgentDuration.text = "9"; ordinaryGoalie.m_FreeAgentDurationContainer.SetActive(true);
GoalieRosterMenu.EntryLabel(ordinaryLineup);
Check(ordinaryGoalie.m_FreeAgentDuration.text == "9" && ordinaryGoalie.m_FreeAgentDurationContainer.Active
    && ordinaryLineup.OnDestroy.Actions.Count == 0, "ChangedRunEntryPreservesNewNativeBadgeAndDropsOldOwnership");
state = Fresh(); File.WriteAllText(Path.Combine(Plugin.ModFolder, "campaign.txt"), "Goalie Recruitment = On");
contextPool = GoalieFreeAgents.DirectoryPath(Plugin.ModFolder); Directory.CreateDirectory(contextPool);
var finiteFile = Path.Combine(contextPool, "finite.txt");
File.WriteAllText(finiteFile, "Position = Goalie\nCustom Contract = yes\nContract Matches = 1000\n");
contextEvent = new GeneralManagerEvent(); firstMenu = new EventChoiceMenu { SourceNode = contextEvent, mapNode = new STS.Map.MapNode() };
GoalieRosterMenu.RecruitInitialized(firstMenu); GoalieRecruitment.BeginVisit(contextEvent); int previewSaves = state.Repository.Saves;
Check(GoalieRecruitment.Choices(contextEvent, 0), "FiniteGmOfferBuildsFromProductionChoices");
var frozenOffer = GoalieRecruitment.Current.Offers.Single();
var condition = contextEvent.eventChoices.Single().FreeAgentLeavingConditions.Single() as Rogue.FreeAgents.MaxGamesCondition;
Check(condition != null && condition.maxMatches == 1000 && condition.matches == 0 && frozenOffer.Contract.Duration == 1000,
    "FiniteGmOfferUsesNativeMaxGamesConditionWithFrozenDuration");
File.WriteAllText(finiteFile, "Position = Goalie\nCustom Contract = yes\nContract Matches = 1\n");
condition.matches = 1;
Check(frozenOffer.Contract.Duration == 1000 && frozenOffer.Contract.PlayedMatches == 0
    && GoaliePersistenceBridge.Roster.Selection.Active.Contract == null && state.Repository.Saves == previewSaves,
    "NativeGmPreviewConditionsRemainDetachedFromFrozenOfferAndRoster");
GoalieRecruitment.Closed(firstMenu);
state = Fresh();
var canonicalTeam = state.runData.teamData;
canonicalTeam.homeColors.jerseyScheme.primaryColor = new(0, .7f, .9f, 1);
canonicalTeam.awayColors.jerseyScheme.primaryColor = new(1, 1, 1, 1);
var gold = new GoaltenderData { id = "gold", colorSchemes = new() };
gold.colorSchemes.jerseyScheme.primaryColor = new(.96f, .78f, .08f, 1);
var green = new GoaltenderData { id = "green", colorSchemes = new() };
green.colorSchemes.jerseyScheme.primaryColor = new(.08f, .86f, .27f, 1);
roster.TryRecruit(state, gold, GoalieSlot.Active, roster.Selection.Revision);
roster.TryRecruit(state, green, GoalieSlot.Reserve, roster.Selection.Revision);
var canonicalGold = canonicalTeam.goalie;
var beforePreviewSave = state.Repository.Saves;
pair = Offer(state); selection = Recruit(state, pair);
var goldCardTeam = selection.Menu.m_ActivePlayersList.m_SkaterCards[0].CurrentSkaterData.ActiveTeam;
var greenCardTeam = selection.Menu.m_ActivePlayersList.m_SkaterCards[1].CurrentSkaterData.ActiveTeam;
Check(ReferenceEquals(selection.Menu.RenderedTeam, goldCardTeam) && selection.Menu.PreviewRefreshes == 1,
    "AsyncNewMenuReplacesNativeInitialSameIdPreviewAfterOwnershipIsReady");
Check(goldCardTeam.homeColors.jerseyScheme.primaryColor.Equals(gold.colorSchemes.jerseyScheme.primaryColor)
    && greenCardTeam.homeColors.jerseyScheme.primaryColor.Equals(green.colorSchemes.jerseyScheme.primaryColor),
    "GoalieCardCarriersContainDistinctEffectivePalettes");
Check(!ReferenceEquals(goldCardTeam, greenCardTeam) && !ReferenceEquals(goldCardTeam, canonicalTeam)
    && !ReferenceEquals(goldCardTeam.homeColors, greenCardTeam.homeColors)
    && !ReferenceEquals(goldCardTeam.homeColors.jerseyScheme, canonicalGold.colorSchemes.jerseyScheme),
    "GoaliePreviewTeamsAndSchemesAreDetachedPerCard");
Check(canonicalTeam.homeColors.jerseyScheme.primaryColor.Equals(new UnityEngine.Color(0, .7f, .9f, 1))
    && canonicalGold.colorSchemes.jerseyScheme.primaryColor.Equals(gold.colorSchemes.jerseyScheme.primaryColor)
    && state.Repository.Saves == beforePreviewSave, "PreviewPreservesCanonicalTeamGoalieAndSaves");
var nativeRender = new Tape2Tape.Customization.UI.SkaterPreviewInUI { Parent = selection.Menu };
var nativeMenuTeam = selection.Menu.Team.TemporaryTeamData;
TeamData RenderTeam(SkaterData player, Tape2Tape.Customization.UI.SkaterPreviewInUI preview) {
    var input = nativeMenuTeam;
    LineupPreviewKit.Prefix(preview, player, ref input);
    return input;
}
Check(ReferenceEquals(RenderTeam(selection.Active, nativeRender), goldCardTeam)
    && ReferenceEquals(RenderTeam(selection.Reserve, nativeRender), greenCardTeam),
    "NativeLargePreviewRoutesMenuTemporaryTeamThroughExactSelectedGoalie");
Check(ReferenceEquals(RenderTeam(new GoaltenderData { id = selection.Active.id }, nativeRender), nativeMenuTeam)
    && ReferenceEquals(RenderTeam(selection.Active, new Tape2Tape.Customization.UI.SkaterPreviewInUI { Parent = new LineupMenu() }), nativeMenuTeam),
    "PreviewRoutingRejectsSameIdCloneAndForeignMenu");
Check(ReferenceEquals(selection.Menu.Team.TemporaryTeamData, nativeMenuTeam), "PreviewRoutingDoesNotMutateNativeMenuRoster");
Plugin.IsDefaultMode = true;
Check(ReferenceEquals(RenderTeam(selection.Active, nativeRender), nativeMenuTeam), "DefaultModeRetainsNativePreviewTeam");
Plugin.IsDefaultMode = false;
var foreignTeam = new TeamData();
LineupPreviewKit.Prefix(nativeRender, selection.Active, ref foreignTeam);
Check(!ReferenceEquals(foreignTeam, goldCardTeam), "ForeignTeamInputCannotBorrowOwnedGoaliePalette");
selection.Menu.TryLeaveMenu(false);
Check(ReferenceEquals(RenderTeam(selection.Active, nativeRender), nativeMenuTeam), "ClosedGoalieMenuDoesNotRedirectLaterRender");
GoalieRosterMenu.Finish(selection);
Check(UnityEngine.Object.DestroyedObjects.Count(o => ReferenceEquals(o, goldCardTeam)) == 1
    && UnityEngine.Object.DestroyedObjects.Count(o => ReferenceEquals(o, greenCardTeam)) == 1
    && !UnityEngine.Object.DestroyedObjects.Contains(canonicalTeam) && !UnityEngine.Object.DestroyedObjects.Contains(canonicalGold),
    "CloseDestroysBothOwnedPreviewTeamsOnceAndPreservesCanonicalObjects");
var reusedMenu = UnityEngine.Object.CurrentMenu;
reusedMenu.m_CurrentlyPreviewedSkater = canonicalGold;
reusedMenu.RenderedTeam = canonicalTeam;
int previousPreviews = reusedMenu.PreviewRefreshes;
GoalieRosterMenu.Open(state);
var reopened = GoalieRosterMenu.Current;
Check(reopened != null && !ReferenceEquals(reopened.Active, canonicalGold)
    && reopened.Active.id == canonicalGold.id && reusedMenu.PreviewRefreshes == previousPreviews + 1
    && ReferenceEquals(reusedMenu.RenderedTeam, reopened.Preview),
    "InitialSameIdCachedPreviewIsRefreshedWithOwnedPaletteWithoutNavigation");
Check(state.Repository.Saves == beforePreviewSave && ReferenceEquals(canonicalTeam.goalie, canonicalGold)
    && reusedMenu.ToggleCallbacks == 0 && reopened.Confirming == null,
    "InitialPreviewRefreshDoesNotSaveSwapOrConfirm");
reopened.Menu.TryLeaveMenu(false);
CampaignAppearanceRuntime.RestoreRecruit(gold, new CampaignDocument("Appearance Team Painted = yes\nAppearance Own Custom jersey Colors = yes\nAppearance Own Jersey Color = 245,200,20\nAppearance Own Custom jersey Away Colors = no\n"));
Check(PlayNowTranslation.GoaliePreviewColors(canonicalTeam.awayColors, gold, true).jerseyScheme.primaryColor.Equals(new UnityEngine.Color(1, 1, 1, 1)),
    "HomeOnlyGoalieOverrideDoesNotLeakIntoInheritedAwayPreview");
var savedGoldPalette = gold.colorSchemes;
CampaignAppearanceRuntime.RestoreRecruit(gold, new CampaignDocument("Appearance Team Painted = yes\nAppearance Own Custom jersey Colors = yes\nAppearance Own Jersey Color = 245,200,20\nAppearance Own Custom jersey Away Colors = yes\nAppearance Own Jersey Away Color = 200,20,245\n"));
var homePreview = PlayNowTranslation.GoaliePreviewColors(canonicalTeam.homeColors, gold, false);
var awayPreview = PlayNowTranslation.GoaliePreviewColors(canonicalTeam.awayColors, gold, true);
Check(homePreview.jerseyScheme.primaryColor.Equals(new UnityEngine.Color(245f/255, 200f/255, 20f/255, 1))
    && awayPreview.jerseyScheme.primaryColor.Equals(new UnityEngine.Color(200f/255, 20f/255, 245f/255, 1)),
    "AuthoredGoalieHomeAndAwayUseTheirOwnExplicitPalette");
Check(ReferenceEquals(savedGoldPalette, gold.colorSchemes) && !CampaignAppearanceRuntime.Away(gold)
    && canonicalTeam.awayColors.jerseyScheme.primaryColor.Equals(new UnityEngine.Color(1, 1, 1, 1)),
    "ResolvingBothPreviewKitsDoesNotChangePlayerKitOrCanonicalAway");
var inheritedGoalie = new GoaltenderData { id = "inherited" };
CampaignAppearanceRuntime.RestoreRecruit(inheritedGoalie, new CampaignDocument("Appearance Team Painted = yes\nAppearance Own Custom jersey Colors = no\nAppearance Own Custom jersey Away Colors = no\n"));
Check(PlayNowTranslation.GoaliePreviewColors(canonicalTeam.homeColors, inheritedGoalie, false).jerseyScheme.primaryColor.Equals(canonicalTeam.homeColors.jerseyScheme.primaryColor)
    && PlayNowTranslation.GoaliePreviewColors(canonicalTeam.awayColors, inheritedGoalie, true).jerseyScheme.primaryColor.Equals(canonicalTeam.awayColors.jerseyScheme.primaryColor),
    "InheritedGoaliePreviewKeepsBothTeamKits");
Console.WriteLine($"PASS {checks} source-linked goalie menu and native-order checks");
} catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
