using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;

namespace CampaignCore;

public enum GoalieSlot { Active, Reserve }

[DataContract]
public sealed class GoalieContractState
{
    [DataMember(Order = 1)] public int Duration { get; set; }
    [DataMember(Order = 2)] public int PlayedMatches { get; set; }
    [DataMember(Order = 3)] public string LastMatchKey { get; set; } = "";
    [DataMember(Order = 4)] public bool AwaitingReplacement { get; set; }
    public GoalieContractState Clone() => (GoalieContractState)MemberwiseClone();
}

[DataContract]
public sealed class GoalieRecord
{
    [DataMember(Order = 1)] public string PlayerId { get; set; } = "";
    [DataMember(Order = 2)] public string NativePayload { get; set; } = "";
    [DataMember(Order = 3)] public string Supplemental { get; set; } = "";
    [DataMember(Order = 4)] public GoalieContractState Contract { get; set; }
    public GoalieRecord Clone() => new GoalieRecord { PlayerId = PlayerId, NativePayload = NativePayload,
        Supplemental = Supplemental, Contract = Contract?.Clone() };
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(PlayerId) || string.IsNullOrWhiteSpace(NativePayload) || Supplemental == null)
            throw new InvalidDataException("Incomplete goalie snapshot.");
        if (Contract != null && (Contract.Duration <= 0 || Contract.PlayedMatches < 0 || Contract.LastMatchKey == null))
            throw new InvalidDataException("Invalid signed goalie contract.");
    }
}

// Optional in V1 checkpoints written before goalie menu integration. Missing
// means no known consumed goalie signing; it does not infer a reserve roster.
[DataContract]
public sealed class GoalieRecruitmentVisitState
{
    [DataMember(Order = 1)] public string Witness { get; set; } = "";
    [DataMember(Order = 2)] public string Generation { get; set; } = "";
    [DataMember(Order = 3)] public int Quota { get; set; }
    [DataMember(Order = 4)] public string[] ConsumedOfferIds { get; set; } = Array.Empty<string>();
    [DataMember(Order = 5)] public string[] OfferedPoolIds { get; set; } = Array.Empty<string>();
    public GoalieRecruitmentVisitState Clone() => new() { Witness = Witness, Generation = Generation, Quota = Quota,
        ConsumedOfferIds = ConsumedOfferIds?.ToArray(), OfferedPoolIds = OfferedPoolIds?.ToArray() };
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Witness) || !Guid.TryParseExact(Generation, "N", out _) || Quota < 1
            || ConsumedOfferIds == null || OfferedPoolIds == null || ConsumedOfferIds.Length > Quota
            || ConsumedOfferIds.Any(string.IsNullOrWhiteSpace) || OfferedPoolIds.Any(string.IsNullOrWhiteSpace)
            || ConsumedOfferIds.Distinct(StringComparer.Ordinal).Count() != ConsumedOfferIds.Length
            || OfferedPoolIds.Distinct(StringComparer.Ordinal).Count() != OfferedPoolIds.Length)
            throw new InvalidDataException("Invalid goalie recruitment visit checkpoint.");
    }
}

[DataContract]
public sealed class GoalieRosterState
{
    [DataMember(Order = 1)] public int Version { get; set; } = 1;
    [DataMember(Order = 2)] public string CampaignId { get; set; } = "";
    [DataMember(Order = 3)] public string RunId { get; set; } = "";
    [DataMember(Order = 4)] public string SquadId { get; set; } = "";
    [DataMember(Order = 5)] public bool ReserveEnabled { get; set; }
    [DataMember(Order = 6)] public string NativeFingerprint { get; set; } = "";
    [DataMember(Order = 7)] public long Revision { get; set; }
    [DataMember(Order = 8)] public string CheckpointId { get; set; } = "";
    [DataMember(Order = 9)] public GoalieRecord Active { get; set; }
    [DataMember(Order = 10)] public GoalieRecord Reserve { get; set; }
    // Task 4 owns these transitions. Task 2 freezes and round-trips the ledger.
    [DataMember(Order = 11)] public string ArmedAttemptToken { get; set; } = "";
    [DataMember(Order = 12)] public string[] ProcessedAttemptTokens { get; set; } = Array.Empty<string>();

    [DataMember(Order = 13)] public GoalieRecruitmentVisitState RecruitmentVisit { get; set; }
    [DataMember(Order = 14)] public GoalieMatchAttempt Attempt { get; set; }

    public GoalieRosterState Clone()
    {
        var result = (GoalieRosterState)MemberwiseClone();
        result.Active = Active?.Clone(); result.Reserve = Reserve?.Clone();
        result.ProcessedAttemptTokens = ProcessedAttemptTokens?.ToArray();
        result.RecruitmentVisit = RecruitmentVisit?.Clone();
        result.Attempt = Attempt?.Clone();
        return result;
    }
    public void Validate()
    {
        if (Version != 1 || string.IsNullOrWhiteSpace(CampaignId) || string.IsNullOrWhiteSpace(RunId)
            || string.IsNullOrWhiteSpace(SquadId) || string.IsNullOrWhiteSpace(NativeFingerprint) || Revision < 0 || Active == null
            || ArmedAttemptToken == null || ProcessedAttemptTokens == null
            || ProcessedAttemptTokens.Any(string.IsNullOrWhiteSpace)
            || ProcessedAttemptTokens.Distinct(StringComparer.Ordinal).Count() != ProcessedAttemptTokens.Length
            || ProcessedAttemptTokens.Contains(ArmedAttemptToken, StringComparer.Ordinal))
            throw new InvalidDataException("Invalid or unsupported goalie roster checkpoint.");
        Active.Validate(); Reserve?.Validate(); RecruitmentVisit?.Validate(); Attempt?.Validate();
        if (Attempt != null && (Attempt.Token != ArmedAttemptToken || Attempt.CampaignId != CampaignId
            || Attempt.RunId != RunId || Attempt.SquadId != SquadId || Attempt.SourceRevision > Revision))
            throw new InvalidDataException("Goalie attempt does not own its roster checkpoint.");
        if (Reserve != null && (!ReserveEnabled || Active.PlayerId == Reserve.PlayerId))
            throw new InvalidDataException("Invalid goalie slot ownership or capacity.");
    }
}
