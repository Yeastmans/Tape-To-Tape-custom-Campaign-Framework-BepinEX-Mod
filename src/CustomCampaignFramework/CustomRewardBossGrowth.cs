using System;
using System.Linq;
using CampaignCore;
using Data;
using HarmonyLib;
using Tape2Tape;
using Tape2Tape.Hockey.UI;

namespace EndlessMode;

[HarmonyPatch(typeof(ForwardData), nameof(ForwardData.OnBossWin))]
internal static class CustomRewardBossGrowth
{
    [HarmonyPostfix] private static void Postfix(ForwardData __instance, int act) => CustomRewardRuntime.Safe(() => {
        if (Plugin.IsDefaultMode || __instance == null) return;
        CustomRewardRuntime.EnsureAssets();
        foreach (var d in CustomRewardRuntime.Definitions.Values.Where(d => !d.NeedsRedesign && !d.IsRelic
            && (d.Effect == CustomRewardEffects.BossAttribute || d.Effect == CustomRewardEffects.BossSize)
            && (d.Trigger == "Boss won" || d.Trigger == "Act 1 boss won" && act == 1 || d.Trigger == "Act 2 boss won" && act == 2)
            && CustomRewardRuntime.Has(__instance, d.Id)
            && __instance.powerups.HasMatch(t => t != null && t.id == d.Id && !t.PayoffSuppressed)).ToArray()) {
            if (d.Effect == CustomRewardEffects.BossSize) {
                // Six native sizes: five steps reach either end, so a larger
                // amount is clamped instead of looping once per step.
                for (int i = 0; i < Math.Min(5, Math.Abs((long)d.Amount)); i++) {
                    if (d.Amount > 0) __instance.IncreaseSize(); else __instance.ReduceSize();
                }
            } else if (d.Stat == "Overall") __instance.IncrementOverallStats(d.Amount);
            else __instance.IncrementStats(d.Stat switch {
                "Speed" => BasicStats.Speed, "Accuracy" => BasicStats.ShotAccuracy,
                "Checking" => BasicStats.Checking, _ => BasicStats.ShotPower
            }, d.Amount, true, true);
            NotificationQueue.Instance?.Queue(new NotificationData {
                Type = ENotificationType.Generic, Timing = ENotificationTiming.WorldMap,
                Title = d.Name, TitleIcon = CustomRewardRuntime.FindTalent(d.Id, false)?.icon,
                Description = d.RulesText, SourceSkater = __instance,
                SourceTeam = CampaignUtil.CampaignState?.runData?.teamData,
                PowerUpOrRelic = CustomRewardRuntime.FindTalent(d.Id, false)
            });
        }
    });
}
