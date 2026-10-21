using System;
using System.IO;
using CampaignCore;
using Data;
using State;

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
        run = null; state = null; folder = null;
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
        internal Mutation(GoalieRosterRuntime roster, RunData run, GoalieRosterState proposed, DeserializerV2 deserializer)
        {
            this.roster = roster; this.run = run; this.proposed = proposed.Clone(); previous = roster.state;
            oldGoalie = run.teamData.goalie; oldOwner = run.teamState.goaltenderState;
            active = GoalieNativeSnapshot.RestorePair(this.proposed.Active, deserializer);
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
    internal void Fresh(RunData nativeRun, string campaignFolder, bool reserveEnabled)
    {
        var initial = new GoalieRosterState { CampaignId = GoalieRosterStore.CampaignIdentity(campaignFolder),
            RunId = nativeRun.runID, SquadId = nativeRun.squadId, ReserveEnabled = reserveEnabled,
            NativeFingerprint = "uncheckpointed", Active = GoalieNativeSnapshot.Capture(nativeRun.teamData.goalie) };
        initial.Validate();
        run = nativeRun; folder = System.IO.Path.GetFullPath(campaignFolder); state = initial; UnavailableReason = null;
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
        run = nativeRun; folder = System.IO.Path.GetFullPath(campaignFolder); state = null;
        UnavailableReason = "Reserve unavailable: no matching native checkpoint companion.";
        try
        {
            if (selected?.Model?.Pointer != model?.Pointer || selected.Folder != folder || selected.Marker == null) return false;
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
            state = candidate.Clone(); UnavailableReason = null;
            GC.KeepAlive(reserve);
            return true;
        }
        catch (Exception ex) { UnavailableReason = "Reserve unavailable: " + ex.Message; return false; }
    }
}
