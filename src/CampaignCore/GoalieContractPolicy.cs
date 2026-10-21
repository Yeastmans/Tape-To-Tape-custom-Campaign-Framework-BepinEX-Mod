using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CampaignCore;

// Native adapters supply a durably armed attempt and proven participation.
// This policy never invents match identity, native ownership or protection.
public static class GoalieContractPolicy
{
    public static GoalieContractState Sign(int duration)
    {
        if (duration <= 0) throw new ArgumentOutOfRangeException(nameof(duration));
        return new GoalieContractState { Duration = duration };
    }

    public static bool Expired(GoalieRecord record) => record?.Contract != null
        && record.Contract.PlayedMatches >= record.Contract.Duration;

    public static GoalieRosterState CompleteMatch(GoalieRosterState state, string matchKey,
        IReadOnlyCollection<string> participantIds, bool departurePrevented)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        state.Validate();
        if (string.IsNullOrWhiteSpace(matchKey)) throw new ArgumentException("A saved attempt is required.", nameof(matchKey));
        // Check the run ledger before current-attempt authority. A delayed old
        // callback cannot consume the new attempt or debit a second time.
        if (state.ProcessedAttemptTokens.Contains(matchKey, StringComparer.Ordinal)) return state.Clone();
        if (state.ArmedAttemptToken != matchKey) throw new InvalidDataException("Completion does not own the armed attempt.");
        if (participantIds == null) throw new ArgumentNullException(nameof(participantIds));
        if (participantIds.Any(id => string.IsNullOrWhiteSpace(id) || id != state.Active.PlayerId && id != state.Reserve?.PlayerId))
            throw new InvalidDataException("Completion contains an unknown goalie participant.");
        var next = state.Clone();
        foreach (var goalie in new[] { next.Active, next.Reserve })
        {
            var contract = goalie?.Contract;
            if (contract == null || !participantIds.Contains(goalie.PlayerId, StringComparer.Ordinal)) continue;
            // Protected expired contracts can continue playing indefinitely.
            if (contract.PlayedMatches < int.MaxValue) contract.PlayedMatches++;
            contract.LastMatchKey = matchKey;
        }
        next.ArmedAttemptToken = "";
        next.ProcessedAttemptTokens = next.ProcessedAttemptTokens.Concat(new[] { matchKey }).ToArray();
        return ReconcileDeparture(next, departurePrevented);
    }

    public static GoalieRosterState ReconcileDeparture(GoalieRosterState state, bool departurePrevented)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        state.Validate();
        var next = state.Clone();
        if (!departurePrevented)
        {
            if (Expired(next.Reserve)) next.Reserve = null;
            if (Expired(next.Active))
            {
                if (next.Reserve != null) { next.Active = next.Reserve; next.Reserve = null; }
                else next.Active.Contract.AwaitingReplacement = true;
            }
        }
        next.Validate();
        return next;
    }
}
