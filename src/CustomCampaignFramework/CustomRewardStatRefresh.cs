using System;
using System.Collections.Generic;
using System.Linq;
using CampaignCore;
using Skaters;
using UnityEngine;

namespace EndlessMode;

internal static partial class CustomRewardRuntime
{
    internal static class StatRefresh
    {
        [Flags]
        private enum Attributes { None = 0, Power = 1, Accuracy = 2, Speed = 4, Checking = 8, All = 15 }

        private sealed class Recipient
        {
            internal string Player, Team, Scoped;
            internal CustomRewardDefinition[] Loadout;
            internal Attributes Previous;
        }

        private static readonly Dictionary<IntPtr, Recipient> recipients = new();
        private static CustomRewardDefinition[] planFor, transforms, locks;
        private static bool temperature, chargeAccuracy, chargePower;

        internal static void Clear()
        {
            recipients.Clear();
            planFor = null;
        }

        private static Attributes Attribute(string name) => name switch {
            "Shot Power" => Attributes.Power, "Accuracy" => Attributes.Accuracy,
            "Speed" => Attributes.Speed, "Checking" => Attributes.Checking,
            "Overall" => Attributes.All, _ => Attributes.None
        };

        private static void Prepare()
        {
            var rules = InPlay;
            if (ReferenceEquals(planFor, rules)) return;
            temperature = rules.Any(d => !d.NeedsRedesign && CustomRewardEffects.IsTemperature(d.Effect));
            chargeAccuracy = rules.Any(d => !d.NeedsRedesign && d.Effect == CustomRewardEffects.ChargeAccuracy);
            chargePower = rules.Any(d => !d.NeedsRedesign && d.Effect == CustomRewardEffects.ChargePower);
            transforms = rules.Where(d => !d.NeedsRedesign && CustomRewardTriggers.IsCondition(d.Trigger)
                && (d.Effect == CustomRewardEffects.StatChange && Attribute(d.Stat) != Attributes.None
                    || d.Effect == CustomRewardEffects.BalanceAttributes || d.Effect == CustomRewardEffects.BonusMultiplier
                    || d.Effect == CustomRewardEffects.RandomShotAccuracy || d.Effect == CustomRewardEffects.RandomShotPower)).ToArray();
            locks = rules.Where(d => !d.NeedsRedesign && d.Effect == CustomRewardEffects.LockAttribute).ToArray();
            planFor = rules;
        }

        private static Attributes LedgerAttributes(CustomRewardProgress ledger, Recipient recipient, float now)
        {
            var attributes = Attributes.None;
            if (ledger == null) return attributes;
            foreach (var e in ledger.Effects) {
                if (e.Duration == "Seconds" && e.Expires <= now) continue;
                if (e.Recipient == recipient.Player || e.Recipient == recipient.Team || e.Recipient == recipient.Scoped)
                    attributes |= Attribute(e.Stat);
            }
            return attributes;
        }

        private static Attributes NativeAttributes(Forward f)
        {
            var attributes = Attributes.None;
            foreach (var d in ActiveConditions(f, transforms)) {
                if (d.Effect == CustomRewardEffects.BalanceAttributes) return Attributes.All;
                attributes |= d.Effect == CustomRewardEffects.RandomShotAccuracy ? Attributes.Accuracy
                    : d.Effect == CustomRewardEffects.RandomShotPower ? Attributes.Power : Attribute(d.Stat);
            }
            foreach (var rule in locks) if (Has(f._skaterData, rule.Id)) attributes |= Attribute(rule.Stat);
            return attributes;
        }

        private static bool NativeCacheChanged(ForwardStats stats, Attributes attributes)
        {
            // Native Balanced can feed any attribute into speed and accuracy.
            if (stats._isBalanced) attributes = Attributes.All;
            if ((attributes & Attributes.Accuracy) != 0 && stats.GetAccuracy() != stats.GetAccuracyCached()) return true;
            // The public max-speed cache getters additionally multiply by slowmoSpeedScale.
            // UpdateCachedStats stores the unscaled values in these two fields.
            return (attributes & Attributes.Speed) != 0 && (stats.GetSpeed() != stats.GetSpeedCached()
                || stats.GetSpeed(false) != stats.GetSpeedCached(false) || !stats.GetMaxSpeed().Equals(stats._maxSpeedCached)
                || !stats.GetMaxSpeedAtSupernova().Equals(stats._maxSpeedAtSupernovaCached));
        }

        internal static void CheckAll()
        {
            foreach (var f in forwards.Values) if (f != null) Check(f);
        }

        internal static void Check(Forward f, bool bind = false, bool periodic = false)
        {
            // Forward.OnFixedUpdate writes all five native cached values before doing its
            // simulation. The slower framework tick is only needed for disabled forwards.
            if (periodic && f.isActiveAndEnabled) return;
            var key = f.Pointer;
            if (bind || !recipients.TryGetValue(key, out var state)) {
                recipients[key] = state = new Recipient();
                bind = true;
            }
            Prepare();
            if (!ReferenceEquals(state.Loadout, planFor)) {
                if (f._skaterData == null || f.Team?._teamData == null || f.stats == null) return;
                state.Player = Player(f);
                state.Team = Team(f);
                state.Scoped = CustomRewardProgress.ScopedRecipient(state.Player, state.Team);
                state.Loadout = planFor;
            }
            var attributes = Attributes.None;
            if (progress.Effects.Count != 0) attributes |= LedgerAttributes(progress, state, Time.time);
            if (temperature && CustomRewardTemperature.Bonus(f, "Speed") != 0) attributes |= Attributes.All;
            if (chargeAccuracy && CustomRewardChargedShots.Bonus(f, "Accuracy") != 0) attributes |= Attributes.Accuracy;
            if (chargePower && CustomRewardChargedShots.Bonus(f, "Shot Power") != 0) attributes |= Attributes.Power;
            if (transforms.Length != 0 || locks.Length != 0) attributes |= NativeAttributes(f);
            // Include the previous target when its rule disappeared, expired, or stopped
            // applying. Comparing the native result also handles a renewed, already-active
            // debuff, run bonuses and suppression without reimplementing their arithmetic.
            var checkNative = attributes | state.Previous;
            if (bind || checkNative != Attributes.None) {
                var stats = f.stats;
                if (stats == null) return;
                if (bind || NativeCacheChanged(stats, checkNative)) stats.UpdateCachedStats();
            }
            state.Previous = attributes;
        }
    }
}
