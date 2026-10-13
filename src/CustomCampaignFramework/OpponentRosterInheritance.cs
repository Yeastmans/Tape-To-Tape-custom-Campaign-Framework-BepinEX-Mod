using System;
using Data;

namespace EndlessMode;

// Copied native opponents retain reserves outside the editable lineup. Custom
// teams still replace their donor's extra players so donor identities cannot leak.
internal static class OpponentRosterInheritance
{
    internal static bool ApplyAuthoredPlayer(TeamConfig cfg, int index) =>
        !VanillaCampaignRuntime.IsCopiedOpponent(cfg)
        || index < (string.Equals(cfg.OpponentTwoLines, "yes", StringComparison.OrdinalIgnoreCase) ? 10 : 5);

    internal static bool NeutralizeExtraPlayers(TeamConfig cfg) =>
        !VanillaCampaignRuntime.IsCopiedOpponent(cfg);

    // Older copied-native files exported empty helmet fields. Preserve the
    // source clone's bareheaded state before generic player defaults run.
    // An explicit authored helmet subsequently replaces this sentinel normally.
    internal static void PreserveNativeHelmetDefaults(TeamData team, TeamConfig cfg)
    {
        if (!VanillaCampaignRuntime.IsCopiedOpponent(cfg) || team?.forwards == null) return;
        foreach (var player in team.forwards)
        {
            if (player == null) continue;
            if (string.IsNullOrEmpty(player.helmetSkin)) player.helmetSkin = "__NO_HELMET__";
            if (string.IsNullOrEmpty(player.helmetAwaySkin)) player.helmetAwaySkin = "__NO_HELMET__";
        }
    }
}
