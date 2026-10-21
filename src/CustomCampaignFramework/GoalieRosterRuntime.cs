using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Globalization;
using CampaignCore;
using Data;
using State;
using Tape2Tape;

namespace EndlessMode;

// Owns one canonical run's roster. All restoration is detached and validated
// before publishing the active model and its late-native-LoadState owner.
internal sealed class GoalieRosterRuntime
{
    private RunData run;
    private string folder;
    private GoalieRosterState state;
    internal string UnavailableReason { get; private set; }
    internal bool Owns(RunData candidate, string campaignFolder) => candidate != null && run?.Pointer == candidate.Pointer
        && run.runID == state?.RunId && string.Equals(folder, System.IO.Path.GetFullPath(campaignFolder), StringComparison.OrdinalIgnoreCase);
    internal GoalieRosterState Selection => state?.Clone();
    internal void NoSelectedRun()
    {
        run = null; state = null; folder = null; changeOwner = null; changing = false;
        UnavailableReason = "Reserve unavailable: native load selected no saved run.";
    }
    // Task 3 owns the UI/transition policy. This handle supplies detached
    // validation, one publication and a conditional exact-occupant rollback.
    internal sealed class Mutation
    {
        private readonly GoalieRosterRuntime roster;
        private readonly RunData run;
        private readonly GoalieRosterState previous, proposed;
        private readonly GoaltenderData oldGoalie;
        private readonly SkaterState oldOwner;
        private readonly GoalieNativeSnapshot.Restored active, reserve;
        private bool published, finished;
        internal bool Complete(GoalieRosterState checkpoint)
        {
            if (!published || finished || roster.run?.Pointer != run.Pointer || roster.state != proposed
                || run.teamData.goalie?.Pointer != active.Goalie.Pointer || checkpoint == null
                || checkpoint.CampaignId != proposed.CampaignId || checkpoint.RunId != proposed.RunId || checkpoint.SquadId != proposed.SquadId
                || checkpoint.Active.PlayerId != proposed.Active.PlayerId || checkpoint.Reserve?.PlayerId != proposed.Reserve?.PlayerId
                || checkpoint.Revision <= proposed.Revision || checkpoint.ArmedAttemptToken != proposed.ArmedAttemptToken
                || (proposed.Attempt == null ? checkpoint.Attempt != null : !proposed.Attempt.Same(checkpoint.Attempt))) return false;
            roster.state = checkpoint.Clone(); finished = true; return true;
        }
        internal Mutation(GoalieRosterRuntime roster, RunData run, GoalieRosterState proposed, DeserializerV2 deserializer)
        {
            this.roster = roster; this.run = run; this.proposed = proposed.Clone(); previous = roster.state;
            oldGoalie = run.teamData.goalie; oldOwner = run.teamState.goaltenderState;
            var current = GoalieNativeSnapshot.Capture(oldGoalie);
            active = current.PlayerId == this.proposed.Active.PlayerId && current.NativePayload == this.proposed.Active.NativePayload
                && current.Supplemental == this.proposed.Active.Supplemental
                ? new GoalieNativeSnapshot.Restored(oldGoalie, oldOwner)
                : GoalieNativeSnapshot.RestorePair(this.proposed.Active, deserializer);
            reserve = this.proposed.Reserve == null ? null : GoalieNativeSnapshot.RestorePair(this.proposed.Reserve, deserializer);
        }
        internal void Publish()
        {
            if (published || finished || roster.run?.Pointer != run.Pointer || roster.state != previous || run.teamData.goalie?.Pointer != oldGoalie.Pointer
                || run.teamState.goaltenderState?.Pointer != oldOwner.Pointer) throw new InvalidOperationException("Goalie mutation lost its prior ownership.");
            run.teamData.goalie = active.Goalie; run.teamState.goaltenderState = active.State;
            roster.state = proposed; published = true; GC.KeepAlive(reserve);
        }
        internal bool Rollback()
        {
            if (!published || finished || roster.run?.Pointer != run.Pointer || roster.state != proposed
                || run.teamData.goalie?.Pointer != active.Goalie.Pointer || run.teamState?.goaltenderState?.Id != active.Goalie.id) return false;
            // Canonical saving replaces TeamState and captures a fresh goalie
            // SkaterState. The published roster generation + goalie own this
            // mutation; an expected save snapshot is not a different mutation.
            run.teamData.goalie = oldGoalie; run.teamState.goaltenderState = oldOwner; roster.state = previous; finished = true; return true;
        }
    }
    internal Mutation Stage(RunData nativeRun, GoalieRosterState proposed, DeserializerV2 deserializer)
    {
        if (!Owns(nativeRun, folder)) throw new InvalidOperationException("Goalie changes require an available matching run roster.");
        proposed.Validate();
        if (proposed.CampaignId != state.CampaignId || proposed.RunId != state.RunId || proposed.SquadId != state.SquadId || proposed.ReserveEnabled != state.ReserveEnabled)
            throw new InvalidDataException("Goalie changes cannot replace saved scope or capacity policy.");
        GoalieNativeCheckpoint.RequireProfile(GoalieNativeAdmission.SaveSettings("RunDataV2.es3"));
        return new Mutation(this, nativeRun, proposed, deserializer);
    }
    internal enum ChangeOutcome { Committed, Rejected, Unresolved }
    internal sealed class ChangeResult
    {
        internal ChangeOutcome Outcome;
        internal string Message;
        internal GoaliePersistenceBridge.SaveReceipt Receipt;
    }
    private bool changing;
    private object changeOwner;
    internal bool Busy => changing;
    internal bool CanEdit(CampaignState campaign) => GoaliePersistenceBridge.Enabled && !Plugin.IsDefaultMode
        && !T2TUtils.IsInMatch && GameManager.GameType == ScriptableStates.GameType.Rogue
        && campaign != null && campaign.Pointer == CampaignUtil.CampaignState?.Pointer
        && state != null && UnavailableReason == null && Owns(campaign.runData, Plugin.ModFolder);

    // False means rejected without publication. True means the asynchronous
    // transaction was accepted; only its completion determines consumption.
    internal bool TryRecruit(CampaignState campaign, GoaltenderData candidate, GoalieSlot slot, long expectedRevision,
        Action<ChangeResult> completed = null, GoalieContractState signedContract = null,
        GoalieRecruitmentVisitState signingVisit = null, string offerId = null, bool? expectedDeparturePrevention = null)
    {
        if (!CanEdit(campaign) || changing || state.Revision != expectedRevision || candidate == null
            || (slot != GoalieSlot.Active && slot != GoalieSlot.Reserve) || slot == GoalieSlot.Reserve && !state.ReserveEnabled) return false;
        try {
            var proposed = Capture(campaign.runData, Plugin.ModFolder);
            if (candidate.id == proposed.Active.PlayerId || candidate.id == proposed.Reserve?.PlayerId) return false;
            bool reconcile = slot == GoalieSlot.Reserve && GoalieContractPolicy.Expired(proposed.Active);
            bool prevented = false;
            if (reconcile && (!GoalieContractRuntime.TryDepartureProtection(campaign, out prevented)
                || expectedDeparturePrevention.HasValue && expectedDeparturePrevention.Value != prevented)) return false;
            if (signingVisit != null) {
                signingVisit.Validate();
                string witness = RecruitmentWitness(campaign);
                var saved = proposed.RecruitmentVisit?.Witness == witness ? proposed.RecruitmentVisit : null;
                if (witness != signingVisit.Witness || string.IsNullOrWhiteSpace(offerId)
                    || signingVisit.ConsumedOfferIds.Length >= signingVisit.Quota || signingVisit.ConsumedOfferIds.Contains(offerId)
                    || saved != null && (saved.Generation != signingVisit.Generation || saved.Quota != signingVisit.Quota
                        || !saved.ConsumedOfferIds.SequenceEqual(signingVisit.ConsumedOfferIds)
                        || saved.OfferedPoolIds.Except(signingVisit.OfferedPoolIds).Any())
                    || saved == null && signingVisit.ConsumedOfferIds.Length != 0) return false;
                proposed.RecruitmentVisit = signingVisit.Clone();
                proposed.RecruitmentVisit.ConsumedOfferIds = signingVisit.ConsumedOfferIds.Append(offerId).ToArray();
            }
            var recruit = GoalieNativeSnapshot.Capture(candidate); recruit.Contract = signedContract?.Clone();
            if (slot == GoalieSlot.Active) proposed.Active = recruit; else proposed.Reserve = recruit;
            if (reconcile) proposed = GoalieContractPolicy.ReconcileDeparture(proposed, prevented);
            return SaveChange(campaign, proposed, completed);
        } catch (Exception ex) { Plugin.Log.LogError("[GoalieRoster] Recruitment preparation failed: " + ex); return false; }
    }
    internal static bool HasRecruitmentWitness(CampaignState campaign, string witness)
    {
        try { return witness != null && RecruitmentWitness(campaign) == witness; }
        catch { return false; }
    }
    internal static string RecruitmentWitness(CampaignState campaign)
    {
        // Native SaveMap serializes Map.act and Map.path (Point.x/y). Only
        // completed node traversal appends to path. Ignore formatting, nodes,
        // reward IDs and other map fields so unrelated authoring cannot mint quota.
        var run = campaign?.runData ?? throw new InvalidOperationException("No campaign run.");
        using var map = JsonDocument.Parse(run.currentMap ?? "");
        var path = map.RootElement.GetProperty("path");
        if (path.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Saved route path unavailable.");
        var text = new StringBuilder().Append(map.RootElement.GetProperty("act").GetInt32()).Append(':')
            .Append(run.CurrentAct).Append(':').Append(run.currentNodePoint.x.ToString("R", CultureInfo.InvariantCulture)).Append(':')
            .Append(run.currentNodePoint.y.ToString("R", CultureInfo.InvariantCulture));
        foreach (var point in path.EnumerateArray()) text.Append('|').Append(point.GetProperty("x").GetInt32()).Append(',').Append(point.GetProperty("y").GetInt32());
        return GoalieRosterStore.Hash(text.ToString());
    }
    internal bool TrySwap(CampaignState campaign, long expectedRevision, Action<ChangeResult> completed = null)
    {
        if (!CanEdit(campaign) || changing || state.Revision != expectedRevision || !state.ReserveEnabled || state.Reserve == null) return false;
        try {
            var proposed = Capture(campaign.runData, Plugin.ModFolder);
            (proposed.Active, proposed.Reserve) = (proposed.Reserve, proposed.Active);
            return SaveChange(campaign, proposed, completed);
        } catch (Exception ex) { Plugin.Log.LogError("[GoalieRoster] Swap preparation failed: " + ex); return false; }
    }
    internal bool TryArmMatch(CampaignState campaign, GoalieMatchAttempt attempt, Action<ChangeResult> completed)
    {
        if (!CanEdit(campaign) || changing || attempt == null || attempt.Lifecycle != "Armed") return false;
        try {
            var proposed = Capture(campaign.runData, Plugin.ModFolder);
            attempt.Validate();
            if (attempt.CampaignId != proposed.CampaignId || attempt.RunId != proposed.RunId || attempt.SquadId != proposed.SquadId
                || attempt.GoalieId != proposed.Active.PlayerId || attempt.HomeTeamId != campaign.runData.teamData.id
                || proposed.ProcessedAttemptTokens.Contains(attempt.Token)
                || proposed.Attempt != null && proposed.Attempt.Lifecycle != "CanceledBeforeLaunch" && !proposed.Attempt.Same(attempt)
                || proposed.Attempt == null && proposed.ArmedAttemptToken.Length != 0) return false;
            if (proposed.Attempt != null && proposed.Attempt.Token != attempt.Token) {
                // Explicit prelaunch cancellation permits atomic retirement and
                // replacement, never a completion/debit or unrelated token reuse.
                if (proposed.Attempt.Lifecycle != "CanceledBeforeLaunch") return false;
                proposed.ProcessedAttemptTokens = proposed.ProcessedAttemptTokens.Append(proposed.Attempt.Token).ToArray();
            } else if (proposed.Attempt != null && !proposed.Attempt.SameIntent(attempt)) return false;
            proposed.Attempt = attempt.Clone(); proposed.ArmedAttemptToken = attempt.Token;
            return SaveChange(campaign, proposed, completed);
        } catch (Exception ex) { Plugin.Log.LogError("[GoalieLaunch] Attempt preparation failed: " + ex); return false; }
    }
    // Caller must own an observed never-permitted preview cancellation. A saved
    // Armed record alone cannot establish that provenance after process loss.
    internal bool TryCancelMatch(CampaignState campaign, GoalieMatchAttempt expected, Action<ChangeResult> completed)
    {
        if (!CanEdit(campaign) || changing || expected == null) return false;
        try {
            var proposed = Capture(campaign.runData, Plugin.ModFolder);
            if (expected.Lifecycle != "Armed" || !expected.Same(proposed.Attempt)
                || proposed.ArmedAttemptToken != expected.Token) return false;
            proposed.Attempt = expected.CanceledBeforeLaunch();
            return SaveChange(campaign, proposed, completed);
        } catch (Exception ex) { Plugin.Log.LogError("[GoalieLaunch] Cancellation preparation failed: " + ex); return false; }
    }
    private bool SaveChange(CampaignState campaign, GoalieRosterState proposed, Action<ChangeResult> completed)
    {
        var mutation = Stage(campaign.runData, proposed, GoalieNativeSnapshot.Deserializer
            ?? throw new InvalidOperationException("Native goalie restore repositories are unavailable."));
        GoaliePersistenceBridge.SaveReceipt admitted = null;
        bool invoking = false, done = false;
        object token = new();
        Action<GoaliePersistenceBridge.SaveReceipt> onAdmission = null, onCompletion = null;
        void Finish(ChangeOutcome outcome, string message, GoaliePersistenceBridge.SaveReceipt receipt)
        {
            if (done) return; done = true;
            GoaliePersistenceBridge.SaveAdmitted -= onAdmission; GoaliePersistenceBridge.SaveCompleted -= onCompletion;
            if (ReferenceEquals(changeOwner, token)) {
                changing = false; changeOwner = null;
                if (outcome == ChangeOutcome.Unresolved) UnavailableReason = message;
            }
            completed?.Invoke(new ChangeResult { Outcome = outcome, Message = message, Receipt = receipt });
        }
        onAdmission = receipt => {
            if (done || !invoking || admitted != null || !ReferenceEquals(receipt.CallerToken, token) || receipt.RunPointer != campaign.runData.Pointer
                || receipt.RepositoryPointer != campaign._runDataRepository?.Pointer) return;
            // Even an unavailable companion is our native save and may write.
            // Completion classifies it; lack of Admission is never a rollback proof.
            admitted = receipt;
        };
        // A reentrant admission subscriber may drain the queue before our
        // admission callback runs. Claim only the same scoped caller receipt.
        onCompletion = receipt => {
            if (admitted == null) onAdmission(receipt);
            if (!ReferenceEquals(receipt, admitted) || receipt.OperationId != admitted.OperationId || receipt.Proxy?.Pointer != admitted.Proxy?.Pointer) return;
            if (receipt.Reconciled) {
                if (mutation.Complete(receipt.Checkpoint)) Finish(ChangeOutcome.Committed,
                    receipt.Durable ? "Goalie roster saved." : "Goalie roster saved locally; native platform save reported a failure.", receipt);
                else Finish(ChangeOutcome.Unresolved, "Goalie save ownership changed. Return to the main menu and Continue to reload the saved roster before making more changes.", receipt);
            } else if (receipt.NoLocalWrite && mutation.Rollback()) Finish(ChangeOutcome.Rejected, "Save failed; previous goalies retained. Try again or go back.", receipt);
            else Finish(ChangeOutcome.Unresolved, "Goalie save could not be verified. Return to the main menu and Continue to reload the saved roster before making more changes.", receipt);
        };
        changing = true; changeOwner = token;
        GoaliePersistenceBridge.SaveAdmitted += onAdmission; GoaliePersistenceBridge.SaveCompleted += onCompletion;
        try {
            mutation.Publish(); invoking = true;
            GoaliePersistenceBridge.SaveCampaign(campaign, token); // native SaveState replaces TeamState before repository admission
            invoking = false;
            if (admitted == null && !done) {
                if (mutation.Rollback()) Finish(ChangeOutcome.Rejected, "Save was not admitted; previous goalies retained.", null);
                else Finish(ChangeOutcome.Unresolved, "Goalie save unavailable. Return to the main menu and Continue before making more changes.", null);
            }
        } catch (Exception ex) {
            invoking = false;
            // SaveRunData can throw after admitting/completing the roster save
            // (e.g. its separate stats save). Keep the exact admitted result.
            if (admitted == null && !done) {
                if (mutation.Rollback()) Finish(ChangeOutcome.Rejected, "Save failed before admission; previous goalies retained.", null);
                else Finish(ChangeOutcome.Unresolved, "Goalie save ownership unavailable. Reload through Continue.", null);
            }
            Plugin.Log.LogError("[GoalieRoster] Save call failed: " + ex);
        }
        return true;
    }
    // Separate proven completion boundary. CanEdit deliberately remains a
    // between-match UI guard; callers must retain the exact played origin.
    internal bool TryCompleteMatch(CampaignState campaign, GoalieMatchAttempt attempt, string participant, bool prevented,
        Action<ChangeResult> completed)
    {
        if (!GoaliePersistenceBridge.Enabled || Plugin.IsDefaultMode || GameManager.GameType != ScriptableStates.GameType.Rogue || changing || UnavailableReason != null
            || campaign?.Pointer != CampaignUtil.CampaignState?.Pointer || attempt == null || participant != attempt.GoalieId
            || !Owns(campaign.runData, Plugin.ModFolder) || !attempt.Same(state?.Attempt)
            || attempt.Lifecycle != "Armed" || state.ArmedAttemptToken != attempt.Token
            || campaign.runData.runID != attempt.RunId || campaign.runData.squadId != attempt.SquadId
            || campaign.runData.teamData?.id != attempt.HomeTeamId) return false;
        try {
            var final = Capture(campaign.runData, Plugin.ModFolder);
            if (final.Active.PlayerId != participant) return false;
            return SaveChange(campaign, GoalieContractPolicy.CompleteMatch(final, attempt.Token, new[] { participant }, prevented), completed);
        } catch (Exception ex) { Plugin.Log.LogError("[GoalieContracts] Completion preparation failed: " + ex); return false; }
    }
    internal void CompletionUnavailable(CampaignState campaign, GoalieMatchAttempt attempt, string reason)
    {
        // Never let an old callback lock a later run or attempt. A completion
        // failure does not fabricate debit authority from the replay ledger.
        if (campaign?.runData?.Pointer == run?.Pointer && attempt != null && attempt.Same(state?.Attempt))
            UnavailableReason = reason;
    }
    internal void Fresh(RunData nativeRun, string campaignFolder, bool reserveEnabled, GoalieContractState signedContract = null)
    {
        var initial = new GoalieRosterState { CampaignId = GoalieRosterStore.CampaignIdentity(campaignFolder),
            RunId = nativeRun.runID, SquadId = nativeRun.squadId, ReserveEnabled = reserveEnabled,
            NativeFingerprint = "uncheckpointed", Active = GoalieNativeSnapshot.Capture(nativeRun.teamData.goalie) };
        initial.Active.Contract = signedContract?.Clone();
        initial.Validate();
        run = nativeRun; folder = System.IO.Path.GetFullPath(campaignFolder); state = initial; changing = false; changeOwner = null; UnavailableReason = null;
    }
    internal GoalieRosterState Capture(RunData nativeRun, string campaignFolder)
    {
        if (!Owns(nativeRun, campaignFolder)) throw new InvalidOperationException("No goalie roster is bound to this exact run.");
        var frozen = state.Clone();
        var active = GoalieNativeSnapshot.Capture(nativeRun.teamData.goalie);
        if (active.PlayerId != frozen.Active.PlayerId) throw new InvalidDataException("Uncoordinated active goalie replacement.");
        active.Contract = frozen.Active.Contract?.Clone(); frozen.Active = active;
        frozen.Validate(); return frozen;
    }
    internal bool Load(RunData nativeRun, RunDataModelV2 model, DeserializerV2 deserializer, GoalieNativeCheckpoint selected, string campaignFolder)
    {
        // Bind the injected native repositories to their actual hydration call.
        GoalieNativeSnapshot.Deserializer = deserializer;
        run = nativeRun; folder = System.IO.Path.GetFullPath(campaignFolder); state = null; changing = false; changeOwner = null;
        UnavailableReason = "Reserve unavailable: no matching native checkpoint companion. Restore the matching save and companion, then Continue before editing goalies.";
        try
        {
            if (selected == null || model == null || selected.Model?.Pointer != model.Pointer || selected.Folder != folder
                || nativeRun == null || nativeRun.runID != model.runID || nativeRun.squadId != model.squadId) return false;
            if (!selected.HasMarker)
            {
                // Only positively associated marker-free R23 bytes authorize a
                // baseline. Capture the hydrated late LoadState owner, never
                // current template contracts/capacity or an inferred attempt.
                var activeModels = new System.Collections.Generic.List<GoaltenderDataModelV2>();
                if (model.team?.skaters != null) foreach (var skater in model.team.skaters) { var goalie = skater?.TryCast<GoaltenderDataModelV2>(); if (goalie != null) activeModels.Add(goalie); }
                if (activeModels.Count != 1 || nativeRun.teamData?.goalie == null
                    || nativeRun.teamState?.goaltenderState?.Id != nativeRun.teamData.goalie.id) return false;
                var native = GoalieNativeSnapshot.Capture(nativeRun.teamData.goalie);
                if (!GoalieNativeSnapshot.Matches(native, activeModels[0], model.teamState?.goaltenderState)
                    || !GoalieNativeSnapshot.Matches(native, activeModels[0], nativeRun.teamState.goaltenderState)) return false;
                // Identity-checked R23 omitted appearance precedes baseline capture.
                GoalieRecruitment.Restore(nativeRun);
                var baseline = new GoalieRosterState {
                    CampaignId = GoalieRosterStore.CampaignIdentity(folder), RunId = nativeRun.runID, SquadId = nativeRun.squadId,
                    ReserveEnabled = false, NativeFingerprint = "uncheckpointed", Active = GoalieNativeSnapshot.Capture(nativeRun.teamData.goalie)
                };
                baseline.Validate(); state = baseline; UnavailableReason = null;
                return true;
            }
            if (string.IsNullOrWhiteSpace(selected.Marker)) return false;
            var candidate = GoalieRosterStore.Load(folder, nativeRun.runID, GoalieRosterStore.Hash(selected.Marker));
            if (candidate == null || candidate.NativeFingerprint != selected.Fingerprint || candidate.SquadId != nativeRun.squadId
                || candidate.RunId != model.runID || candidate.SquadId != model.squadId
                || candidate.Active.PlayerId != nativeRun.teamData?.goalie?.id
                || nativeRun.teamState?.goaltenderState?.Id != candidate.Active.PlayerId) return false;
            var active = GoalieNativeSnapshot.RestorePair(candidate.Active, deserializer);
            var reserve = candidate.Reserve == null ? null : GoalieNativeSnapshot.RestorePair(candidate.Reserve, deserializer);
            candidate.Validate();
            // Do not replace TeamState: forward and relic state remain native.
            nativeRun.teamData.goalie = active.Goalie;
            nativeRun.teamState.goaltenderState = active.State;
            state = candidate.Clone(); changing = false; changeOwner = null; UnavailableReason = null;
            GC.KeepAlive(reserve);
            return true;
        }
        catch (Exception ex) { UnavailableReason = "Reserve unavailable: " + ex.Message; return false; }
    }
}
