using CampaignCore;
using EndlessMode;
using State;
using Data;
using Tape2Tape;
using Tape2Tape.Hockey.UI;
using Rogue.NPC.GeneralManagerNPC;
using System.Reflection;

int checks = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); checks++; }
BridgeDoubleHooks.Enable();
typeof(GoalieRosterMenu).GetField("installed", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, true);
typeof(GoalieRosterMenu).GetField("attempted", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, true);
Plugin.IsDefaultMode = false;
CampaignState Fresh(bool reserve = true) {
    Plugin.ModFolder = Path.Combine(Path.GetTempPath(), "ccf-menu-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Plugin.ModFolder);
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
    var visit = new GoalieRecruitment.Visit { Revision = 4, Node = node.Pointer, Event = node, Ledger = GoalieRecruitment.VisitLedger(state, 2),
        Selection = new() { State = state.Pointer, Run = state.runData.Pointer, Team = state.runData.teamData.Pointer,
            Blueprint = state.currentNode.Pointer, RunId = state.runData.runID, Folder = Plugin.ModFolder,
            Act = 0, Layer = 0, Index = 0, RecruitmentEnabled = true, ConfigurationError = "" } };
    typeof(GoalieRecruitment).GetField("visit", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, visit);
    var offer = new GoalieRecruitment.Offer { Visit = visit, Revision = visit.Revision, Id = GoalieRosterStore.Hash(visit.Ledger.Generation + ":offer"), Candidate = new() { id = "candidate" } };
    visit.Offers.Add(offer);
    var gm = new EventChoiceMenu { SourceNode = node }; gm.m_Items.Add(new() { ChoiceData = new() { Skater = offer.Candidate } });
    return (offer, gm);
}
GoalieRosterMenu.Selection Recruit(CampaignState state, (GoalieRecruitment.Offer Offer, EventChoiceMenu Gm) pair) {
    Check(!GoalieRosterMenu.RecruitClick(pair.Gm, pair.Gm.m_Items[0].Navigable), "NativeRecruitCallbackIsTakenOver");
    return GoalieRosterMenu.Current;
}
void Click(GoalieRosterMenu.Selection s, int slot) { var card = s.Menu.m_ActivePlayersList.m_SkaterCards[slot]; card.Toggle.IsSelected = true; Check(!GoalieRosterMenu.Pick(s.Menu, card), "NativeLineupMutationSuppressed"); }
var state = Fresh();
var pair = Offer(state); var selection = Recruit(state, pair);
Check(selection.Menu.m_ActivePlayersList.m_SkaterCards.Count == 2 && selection.Menu.m_ActivePlayersList.Navigables.Count == 2, "ControllerNavigationReachesBothSlots");
Check(selection.Active.Pointer != state.runData.teamData.goalie.Pointer && !selection.Menu.Team.HasChanged, "PreviewNeverOwnsCanonicalGoalie");
selection.Menu.TryLeaveMenu(false);
Check(!pair.Offer.Taken && pair.Offer.Visit.Pending == null && pair.Gm.m_ChoicesMade == 0 && pair.Gm.DefaultSelections == 1 && pair.Gm.Visible && !pair.Gm.m_IsTakenOver, "CancelDoesNotConsumeOffer");
GoalieRosterMenu.Finish(selection);
Check(pair.Gm.DefaultSelections == 1 && selection.Menu.OnDestroy.Actions.Count == 0 && selection.Menu.OnHidingCompleted.Actions.Count == 0, "CloseUnregistersAndRestoresGMExactlyOnce");
selection = Recruit(state, pair); var previousActive = state.runData.teamData.goalie;
Click(selection, 1);
Check(selection.Menu.m_Header.m_Title.text.Contains("Fill empty reserve") && !pair.Offer.Taken, "EmptyReserveConfirmationBeforeMutation");
Click(selection, 1);
Check(pair.Offer.Taken && state.runData.teamData.goalie.Pointer == previousActive.Pointer && GoaliePersistenceBridge.Roster.Selection.Reserve.PlayerId == "candidate", "ChooseEmptyReserveChangesOnlyReserve");
Check(pair.Gm.m_ChoicesMade == 1 && pair.Gm.Refreshes == 1 && pair.Offer.Visit.Event.NextCalls == 1, "ConfirmedSigningAdvancesGMOnce");
Check(!GoalieRecruitment.BeginSelection(pair.Offer), "DoubleClickCommitsOnce");
state = Fresh(false); pair = Offer(state); selection = Recruit(state, pair);
Check(selection.Menu.m_ActivePlayersList.m_SkaterCards.Count == 1, "ReserveDisabledShowsExactlyStarter");
Click(selection, 0); Check(selection.Menu.m_Header.m_Title.text.Contains("Replace active") && state.runData.teamData.goalie.id == "active", "ChooseFilledSlotShowsReplacement");
Click(selection, 0); Check(state.runData.teamData.goalie.id == "candidate" && GoaliePersistenceBridge.Roster.Selection.Reserve == null, "ReserveDisabledReplacesOnlyStarter");
state = Fresh(); pair = Offer(state); selection = Recruit(state, pair); state.runData.currentNodePoint.x++;
Click(selection, 0); Check(!pair.Offer.Taken && state.runData.teamData.goalie.id == "active", "StaleVisitCannotRecruit");
selection.Menu.TryLeaveMenu(false);
state = Fresh(); pair = Offer(state); selection = Recruit(state, pair); ES3.FailStore = true;
Click(selection, 0); Click(selection, 0); ES3.FailStore = false;
Check(!pair.Offer.Taken && state.runData.teamData.goalie.id == "active" && !selection.Unresolved && selection.Menu.m_Header.m_Title.text.Contains("previous goalies retained"), "ConfirmedNoWriteRestoresOfferAndBothOccupants");
Click(selection, 0); Click(selection, 0); Check(pair.Offer.Taken, "ConfirmedNoWriteMayRetry");
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
Check(selection.Menu.m_ActivePlayersList.m_SkaterCards[1].m_FreeAgentDuration.text == "2 / 4 played matches remaining", "ActualSignedContractDisplayed");
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
for (int i = 0; i < 5; i++) File.WriteAllText(Path.Combine(pool, "goalie" + i + ".txt"), "Position = Goalie");
var mapNode = new STS.Map.MapNode(); GoalieRecruitment.Selected(state, mapNode);
var gmNode = new GeneralManagerEvent();
Check(GoalieRecruitment.Choices(gmNode, 0) && gmNode.eventChoices.Count == 2, "ActualOfferGenerationUsesAuthoredGoaliePool");
var actual = GoalieRecruitment.Current; var selectedOffer = actual.Offers[0];
var gm = new EventChoiceMenu { SourceNode = gmNode }; gm.m_Items.Add(new() { ChoiceData = gmNode.eventChoices[0] });
GoalieRosterMenu.RecruitClick(gm, gm.m_Items[0].Navigable); selection = GoalieRosterMenu.Current; Click(selection, 0); Click(selection, 0);
string consumedId = selectedOffer.Id; string generation = roster.Selection.RecruitmentVisit.Generation;
roster.TryRecruit(state, new GoaltenderData { id = "replacement-after-signing" }, GoalieSlot.Active, roster.Selection.Revision);
GoalieRecruitment.Selected(state, mapNode);
Check(GoalieRecruitment.Choices(gmNode, 0) && GoalieRecruitment.Current.Hires == 1 && GoalieRecruitment.Current.Ledger.Generation == generation
    && GoalieRecruitment.Current.Offers.All(o => o.Id != consumedId), "ReplacedAwayGoalieDoesNotRefundSavedHire");
string witness = GoalieRosterRuntime.RecruitmentWitness(state);
state.runData.currentMap = "{\"nodes\":[1,2],\"path\":[],\"act\":1}";
Check(GoalieRosterRuntime.RecruitmentWitness(state) == witness, "UnrelatedMapFieldsAndFormattingCannotMintQuota");
foreach (var file in Directory.GetFiles(pool)) File.Delete(file);
GoalieRecruitment.Selected(state, mapNode);
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
Console.WriteLine($"PASS {checks} source-linked goalie menu checks");
