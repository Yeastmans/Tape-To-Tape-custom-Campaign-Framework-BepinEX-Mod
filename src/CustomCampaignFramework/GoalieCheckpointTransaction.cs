using System;
using System.IO;
using CampaignCore;

namespace EndlessMode;

// Immutable companion intent. Only the exact coflushed marker can reconcile it;
// native completion status is deliberately a separate fact owned by the bridge.
internal sealed class GoalieCheckpointTransaction
{
    private readonly string folder;
    private readonly GoalieRosterState admission;
    private string fingerprint;
    internal string OperationId { get; }
    internal string AttemptToken => admission.ArmedAttemptToken;
    internal string Token { get; private set; }
    internal string Marker { get; private set; }
    internal GoalieCheckpointTransaction(string operationId, string folder, GoalieRosterState state)
    {
        admission = state.Clone(); admission.Validate();
        if (admission.CampaignId != GoalieRosterStore.CampaignIdentity(folder)) throw new InvalidDataException("Wrong companion folder.");
        OperationId = operationId; this.folder = Path.GetFullPath(folder);
    }
    internal string Prepare(string nativeFingerprint)
    {
        if (Token != null) throw new InvalidOperationException("A transaction may prepare only once.");
        var state = admission.Clone(); state.NativeFingerprint = nativeFingerprint;
        Token = GoalieRosterStore.Prepare(folder, state);
        fingerprint = nativeFingerprint;
        Marker = GoalieRosterStore.Marker(Token);
        return Marker;
    }
    internal GoalieRosterState Reconcile(string selectedMarker, string selectedFingerprint)
    {
        if (Marker == null || Marker != selectedMarker || fingerprint != selectedFingerprint) return null;
        var result = GoalieRosterStore.Load(folder, admission.RunId, GoalieRosterStore.Hash(selectedMarker));
        return result != null && result.NativeFingerprint == selectedFingerprint && result.SquadId == admission.SquadId
            && result.Active.PlayerId == admission.Active.PlayerId && result.ArmedAttemptToken == admission.ArmedAttemptToken ? result : null;
    }
}
