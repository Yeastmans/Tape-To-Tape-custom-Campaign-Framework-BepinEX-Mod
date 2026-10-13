using System;
using Data;

namespace EndlessMode;

internal static class OpponentPlayerConfig
{
    internal static PlayerConfig Find(TeamData team, ForwardData player, TeamConfig cfg)
    {
        if (team?.forwards == null || player == null || cfg == null) return null;
        var slots = new[] { cfg.LW, cfg.RW, cfg.C, cfg.LD, cfg.RD, cfg.L2_LW, cfg.L2_RW, cfg.L2_C, cfg.L2_LD, cfg.L2_RD };
        PlayerConfig byId = null;
        int matches = 0;
        for (int i = 0; i < team.forwards.Count && i < slots.Length; i++)
        {
            var forward = team.forwards[i];
            if (forward == null) continue;
            if (forward.Pointer == player.Pointer) return slots[i];
            // Match copies retain the roster ID. OriginalId is deliberately not
            // used: separate custom players can share the same native donor.
            if (!string.IsNullOrEmpty(player.id) && string.Equals(forward.id, player.id, StringComparison.Ordinal))
            { byId = slots[i]; matches++; }
        }
        return matches == 1 ? byId : null;
    }
}
