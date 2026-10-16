using System;
using System.Collections.Generic;
using System.Linq;
using CampaignCore;
using Rogue;
using Rogue.Relics.FinalSquadRelics;
using Skaters;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EndlessMode;

internal static class CustomRewardBorrowing
{
    private sealed class Loan { internal Forward Player; internal Talent Talent; }
    private static readonly Dictionary<IntPtr, Loan> loans = new();
    internal static void Refresh(IEnumerable<Forward> players)
    {
        var all = players.Where(p => p != null).ToArray();
        foreach (var pair in loans.ToArray()) {
            var player = pair.Value.Player;
            if (player == null || !all.Any(p => p.Pointer == pair.Key) || !Eligible(player)) Return(pair.Key);
        }
        foreach (var player in all) {
            if (loans.ContainsKey(player.Pointer) || !Eligible(player)) continue;
            var candidates = new List<Talent>();
            foreach (var teammate in player.Team.AllForwards) {
                if (teammate == null || teammate == player || !teammate.IsBenched()) continue;
                foreach (var talent in teammate._forwardData.powerups) {
                    if (talent == null || talent.TryCast<ICurse>() != null || talent.TryCast<BenchBorrower>() != null) continue;
                    bool custom = CustomRewardRuntime.Definitions.TryGetValue(talent.id, out var rule);
                    if (custom && (rule.NeedsRedesign || !CustomRewardAssignments.FitsPlayer(rule, false) || rule.Effect == CustomRewardEffects.BorrowTalent)) continue;
                    // Native identity is type-based; custom neutral carriers
                    // use their stable IDs so different rules remain distinct.
                    if (player._forwardData.powerups.HasMatch(p => p != null && (p.id == talent.id
                        || !custom && !CustomRewardRuntime.IsCustom(p.id) && p.GetIl2CppType() == talent.GetIl2CppType()))) continue;
                    candidates.Add(talent);
                }
            }
            if (candidates.Count == 0) continue;
            var copy = Object.Instantiate(candidates[UnityEngine.Random.Range(0, candidates.Count)]);
            copy.name += " [BorrowedFromBench]";
            var loan = new Loan { Player = player, Talent = copy };
            loans.Add(player.Pointer, loan);
            try {
                if (!player.TryAddTalent(copy, CustomRewardRuntime.IsCustom(copy.id))) { Return(player.Pointer); continue; }
                player.RefreshSkaterUI();
            } catch { Return(player.Pointer); throw; }
        }
    }
    private static bool Eligible(Forward player) => !player.IsBenched() && !player.HaveTalentCached<BenchBorrower>()
        && CustomRewardCapabilities.Active(player, CustomRewardEffects.BorrowTalent);
    private static void Return(IntPtr key)
    {
        if (!loans.TryGetValue(key, out var loan)) return;
        loans.Remove(key);
        try {
            // Remove only the exact loan; a subsequently acquired native
            // reward with the same ID/type remains owned.
            if (loan.Player != null && loan.Talent != null && loan.Player._forwardData?.powerups?.HasMatch(t => t?.Pointer == loan.Talent.Pointer) == true) {
                loan.Player.RemoveTalent(loan.Talent, false); loan.Player.RefreshSkaterUI();
            }
        } finally { if (loan.Talent != null) Object.Destroy(loan.Talent); }
    }
    internal static void Clear() { foreach (var key in loans.Keys.ToArray()) Return(key); }
}
