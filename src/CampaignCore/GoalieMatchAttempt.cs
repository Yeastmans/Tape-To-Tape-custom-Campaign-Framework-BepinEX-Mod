using System;
using System.IO;
using System.Runtime.Serialization;

namespace CampaignCore;

// Persisted intent, never proof of live participation. Identity is minted only
// before preview launch and survives Continue/retries; no native pointer persists.
[DataContract]
public sealed class GoalieMatchAttempt
{
    [DataMember(Order = 1)] public int Version { get; private set; }
    [DataMember(Order = 2)] public string Token { get; private set; }
    [DataMember(Order = 3)] public string CampaignId { get; private set; }
    [DataMember(Order = 4)] public string RunId { get; private set; }
    [DataMember(Order = 5)] public string SquadId { get; private set; }
    [DataMember(Order = 6)] public string HomeTeamId { get; private set; }
    [DataMember(Order = 7)] public string GoalieId { get; private set; }
    [DataMember(Order = 8)] public long SourceRevision { get; private set; }
    [DataMember(Order = 9)] public string SourceCheckpointId { get; private set; }
    [DataMember(Order = 10)] public string RouteWitness { get; private set; }
    [DataMember(Order = 11)] public int Act { get; private set; }
    [DataMember(Order = 12)] public int NodeIndex { get; private set; }
    [DataMember(Order = 13)] public int LayerIndex { get; private set; }
    [DataMember(Order = 14)] public string NodeType { get; private set; }
    [DataMember(Order = 15)] public string Lifecycle { get; private set; }

    public GoalieMatchAttempt(GoalieRosterState state, string homeTeamId, string routeWitness,
        int act, int nodeIndex, int layerIndex, string nodeType)
    {
        Version = 1; Token = Guid.NewGuid().ToString("N"); CampaignId = state.CampaignId;
        RunId = state.RunId; SquadId = state.SquadId; HomeTeamId = homeTeamId; GoalieId = state.Active.PlayerId;
        SourceRevision = state.Revision; SourceCheckpointId = state.CheckpointId; RouteWitness = routeWitness;
        Act = act; NodeIndex = nodeIndex; LayerIndex = layerIndex; NodeType = nodeType; Lifecycle = "Armed";
        Validate();
    }
    public GoalieMatchAttempt Clone() => (GoalieMatchAttempt)MemberwiseClone();
    public void Validate()
    {
        if (Version != 1 || !Guid.TryParseExact(Token, "N", out _) || string.IsNullOrWhiteSpace(CampaignId)
            || string.IsNullOrWhiteSpace(RunId) || string.IsNullOrWhiteSpace(SquadId) || string.IsNullOrWhiteSpace(HomeTeamId)
            || string.IsNullOrWhiteSpace(GoalieId) || SourceRevision < 0 || SourceCheckpointId == null
            || SourceRevision > 0 && !Guid.TryParseExact(SourceCheckpointId, "N", out _)
            || string.IsNullOrWhiteSpace(RouteWitness) || string.IsNullOrWhiteSpace(NodeType) || (Lifecycle != "Armed" && Lifecycle != "CanceledBeforeLaunch"))
            throw new InvalidDataException("Invalid goalie match attempt context.");
    }
    public bool Matches(GoalieRosterState state, string homeTeamId, string routeWitness,
        int act, int nodeIndex, int layerIndex, string nodeType)
        => state != null && CampaignId == state.CampaignId && RunId == state.RunId && SquadId == state.SquadId
            && GoalieId == state.Active?.PlayerId && HomeTeamId == homeTeamId && RouteWitness == routeWitness
            && Act == act && NodeIndex == nodeIndex && LayerIndex == layerIndex && NodeType == nodeType;
    public GoalieMatchAttempt Rearm() { var result = Clone(); result.Lifecycle = "Armed"; return result; }
    public GoalieMatchAttempt CanceledBeforeLaunch() { var result = Clone(); result.Lifecycle = "CanceledBeforeLaunch"; return result; }
    public bool Same(GoalieMatchAttempt other) => SameIntent(other) && Lifecycle == other.Lifecycle;
    public bool SameIntent(GoalieMatchAttempt other) => other != null && Version == other.Version && Token == other.Token
        && CampaignId == other.CampaignId && RunId == other.RunId && SquadId == other.SquadId
        && HomeTeamId == other.HomeTeamId && GoalieId == other.GoalieId && SourceRevision == other.SourceRevision
        && SourceCheckpointId == other.SourceCheckpointId && RouteWitness == other.RouteWitness
        && Act == other.Act && NodeIndex == other.NodeIndex && LayerIndex == other.LayerIndex
        && NodeType == other.NodeType;
}
