using System;
using CampaignCore;
using State;
using Tape2Tape;

namespace EndlessMode;

// Signing is separate from match accounting. The later launch/completion
// adapters must establish durable attempt authority before invoking policy.
internal static class GoalieContractRuntime
{
    internal static GoalieContractState Sign(CampaignDocument fields, string playerId)
    {
        int? duration = PlayerContractOptions.Duration(fields);
        if (duration is int matches) return GoalieContractPolicy.Sign(matches);
        if (PlayerContractOptions.Enabled(fields))
            Plugin.Log.LogWarning("[GoalieContracts] Invalid or missing Contract Matches for " + playerId
                + "; expected a positive whole number. This new goalie remains permanent until replaced.");
        return null;
    }

    internal static string OfferText(GoalieContractState contract) => contract == null ? "permanent until replaced"
        : contract.Duration + " played-match contract";

    // Shared with the later scoped native departure observer. Failure is
    // unknown, never an unprotected result. Do not detour the shared generic.
    internal static bool TryDepartureProtection(CampaignState campaign, out bool prevented)
    {
        prevented = false;
        try {
            if (!GoaliePersistenceBridge.Enabled || Plugin.IsDefaultMode
                || campaign?.Pointer != CampaignUtil.CampaignState?.Pointer || campaign?.runData?.teamData == null
                || !GoaliePersistenceBridge.Roster.Owns(campaign.runData, Plugin.ModFolder)) return false;
            var team = campaign.runData.teamData;
            prevented = CustomRewardContractRetention.Prevents(team) || team.HaveRelicCached<Rogue.Handcuffs>();
            return true;
        } catch (Exception ex) {
            Plugin.Log.LogWarning("[GoalieContracts] Departure protection could not be verified: " + ex.Message);
            return false;
        }
    }
}
