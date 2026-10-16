using CampaignCore;
using Data;

namespace EndlessMode;

// A team default distributes talents to individual players; it never creates a
// team-owned talent. Match the Studio export rule, including native cross-role
// assignments, without invoking GiveTalentToPlayer's loadout-clearing behavior.
internal static class CustomTeamTalentAssignments
{
    internal static void Apply(TeamData team, TeamConfig config)
    {
        if (team == null || config?.TeamTalents == null || config.TeamTalents.Count == 0) return;
        foreach (string value in config.TeamTalents)
        {
            var talent = PatchBossLaunchMatch.FindTalent(value);
            if (talent == null) {
                Plugin.Log.LogWarning("[TeamTalents] Talent not found: " + value);
                continue;
            }
            CustomRewardRuntime.Definitions.TryGetValue(talent.id, out var custom);
            if (custom != null) CustomRewardAssignments.ValidateField(custom, "Team Talents");
            foreach (var player in team.GetAllSkaters())
            {
                if (player == null || custom != null && !CustomRewardAssignments.FitsPlayer(custom, player.TryCast<GoaltenderData>() != null)) continue;
                player.powerups ??= new Il2CppSystem.Collections.Generic.List<Rogue.Talent>();
                if (player.powerups.HasMatch(t => t != null && t.id == talent.id)) continue;
                player.powerups.Add(UnityEngine.Object.Instantiate(talent));
                player.ResetTalentCache();
            }
        }
    }
}
