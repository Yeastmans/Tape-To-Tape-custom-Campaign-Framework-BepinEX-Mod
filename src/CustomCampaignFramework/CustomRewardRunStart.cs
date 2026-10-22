using System.Linq;
using CampaignCore;
using HarmonyLib;

namespace EndlessMode;

internal static class CustomRewardRunStart
{
    internal static void Complete(State.CampaignState __instance)
    {
        CustomRewardRuntime.Safe(() => Apply(__instance));
        PlayerContractRuntime.Safe(() => PlayerContractRuntime.CompleteStarting(__instance));
    }
    private static void Apply(State.CampaignState state)
    {
        if (Plugin.IsDefaultMode || state?.runData?.teamData is not { } team) return;
        var roster = team.GetForwards(true);
        foreach (var d in CustomRewardCampaignRules.Owned(team, "Run started").GroupBy(d => d.Id).Select(g => g.First()).ToArray()) {
            foreach (var player in roster) {
                if (player == null || !d.IsRelic && d.Target != CustomRewardEffects.Team
                    && !player.powerups.HasMatch(t => t != null && t.id == d.Id && !t.PayoffSuppressed)) continue;
                var source = state.GetRandomUnlockedFreeAgent(team);
                if (source == null) { Plugin.Log.LogInfo("[CustomRewards] " + d.Name + ": no eligible unlocked free agent."); continue; }
                if (d.Effect == CustomRewardEffects.CopyFreeAgentStats) {
                    player.shotPower = source.shotPower; player.shotAccuracy = source.shotAccuracy;
                    player.speed = source.speed; player.checking = source.checking;
                } else if (d.Effect == CustomRewardEffects.CopyFreeAgentLook) {
                    int power = player.shotPower, accuracy = player.shotAccuracy, speed = player.speed, checking = player.checking;
                    var position = player.defaultSkaterType; var rarity = player.forwardRarity;
                    State.CampaignState.CopyFreeAgentData(player, source);
                    player.shotPower = power; player.shotAccuracy = accuracy; player.speed = speed; player.checking = checking;
                    player.defaultSkaterType = position; player.forwardRarity = rarity;
                    team.EnsureUniqueJerseyNumber(roster.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<Data.ForwardData>>(), player);
                }
            }
        }
    }
}
