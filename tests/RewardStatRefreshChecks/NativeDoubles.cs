using CampaignCore;
using Skaters;

namespace UnityEngine
{
    internal static class Time { internal static float time; }
}

namespace Skaters
{
    internal sealed class Forward
    {
        private static int next;
        internal readonly IntPtr Pointer = new(++next);
        internal readonly string Id, TeamId;
        internal readonly object _skaterData = new();
        internal readonly Team Team = new();
        internal readonly ForwardStats stats;
        internal int DynamicSpeed, NativeAdjustment, CapturedAccuracy, Temperature;
        internal bool Transform, isActiveAndEnabled = true;

        internal Forward(string id, string team)
        {
            Id = id;
            TeamId = team;
            stats = new ForwardStats(this);
        }
    }

    internal sealed class Team
    {
        internal readonly object _teamData = new();
    }

    internal sealed class ForwardStats
    {
        private readonly Forward f;
        private int speed, accuracy;
        internal int Updates, Reads;
        internal bool _isBalanced;
        internal float slowmoSpeedScale = 1;
        internal float _maxSpeedCached, _maxSpeedAtSupernovaCached;
        internal ForwardStats(Forward forward) => f = forward;
        internal int GetSpeed(bool injury = true) { Reads++; return 80 + EndlessMode.CustomRewardRuntime.NativeBonus(f, "Speed") + (f.Transform ? f.NativeAdjustment : 0); }
        internal int GetSpeedCached(bool injury = true) => speed;
        internal int GetAccuracy() { Reads++; return 80 + EndlessMode.CustomRewardRuntime.NativeBonus(f, "Accuracy"); }
        internal int GetAccuracyCached() => accuracy;
        internal float GetMaxSpeed() => GetSpeed();
        internal float GetMaxSpeedCached() => _maxSpeedCached * slowmoSpeedScale;
        internal float GetMaxSpeedAtSupernova() => GetSpeed();
        internal float GetMaxSpeedAtSupernovaCached() => _maxSpeedAtSupernovaCached * slowmoSpeedScale;
        internal void FixedUpdate() { _maxSpeedCached = GetMaxSpeed(); _maxSpeedAtSupernovaCached = GetMaxSpeedAtSupernova(); speed = GetSpeed(); accuracy = GetAccuracy(); }
        internal void UpdateCachedStats() { Updates++; FixedUpdate(); }
    }
}

namespace EndlessMode
{
    internal static partial class CustomRewardRuntime
    {
        private static readonly Dictionary<IntPtr, Forward> forwards = new();
        private static readonly CustomRewardProgress progress = new();
        internal static bool Owned = true;
        internal static CustomRewardDefinition[] InPlay = Array.Empty<CustomRewardDefinition>();
        internal static CustomRewardProgress Ledger => progress;

        private static string Team(Forward f) => "team:" + f.TeamId;
        private static string Player(Forward f) => "player:" + f.Id;
        internal static void SweepForTest() => StatRefresh.CheckAll();
        internal static void PeriodicForTest(Forward f) => StatRefresh.Check(f, periodic: true);
        internal static void BindForTest(Forward f) { forwards[f.Pointer] = f; StatRefresh.Check(f, bind: true); }
        internal static int NativeBonus(Forward f, string stat) => progress.Bonus(Player(f), Team(f), stat, UnityEngine.Time.time, (_, _) => Owned)
            + (stat == "Speed" ? f.DynamicSpeed : stat == "Accuracy" ? f.CapturedAccuracy : 0) + f.Temperature;
        private static IEnumerable<CustomRewardDefinition> ActiveConditions(Forward f, CustomRewardDefinition[] rules)
            => rules.Where(d => d.Effect == CustomRewardEffects.StatChange ? f.DynamicSpeed != 0 : f.Transform);
        private static bool Has(object data, string id) => false;
    }
    internal static class CustomRewardTemperature { internal static int Bonus(Forward f, string stat) => f.Temperature; }
    internal static class CustomRewardChargedShots { internal static int Bonus(Forward f, string stat) => f.CapturedAccuracy; }
}
