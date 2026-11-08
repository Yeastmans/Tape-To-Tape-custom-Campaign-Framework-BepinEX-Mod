namespace Il2CppSystem.Collections.Generic
{
    public interface IReadOnlyCollection<T> { int Count { get; } }
    public interface IReadOnlyList<T> : System.Collections.Generic.IReadOnlyList<T> { U Cast<U>(); }
    public class List<T> : System.Collections.Generic.List<T>, IReadOnlyList<T>, IReadOnlyCollection<T>
    {
        public U Cast<U>() => (U)(object)this;
    }
}
namespace HarmonyLib
{
    public class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string method) { } }
    public class HarmonyPrefix : Attribute { }
}
namespace Tape2Tape.Hockey.UI
{
    public class RunTeamStats { }
    public class RunMatchData(object team, MatchScore score)
    {
        public object OpponentTeam { get; } = team;
        public MatchScore Score { get; } = score;
    }
}
public class MatchScore : Il2CppSystem.Object
{
    public int act, SelfScore, OtherScore;
    public bool IsBoss, Win, Shootout;
    public string teamId, score;
    public (int, int) MapPoint;
    public object scorersStats, goalieStats;
}

namespace Il2CppSystem
{
    public class Object
    {
        private static long next;
        public IntPtr Pointer { get; } = new(++next);
        public new Object MemberwiseClone() => (Object)base.MemberwiseClone();
        public U Cast<U>() => (U)(object)this;
    }
}
namespace Tape2Tape
{
    public static class CampaignUtil { public static readonly CampaignState CampaignState = new(); }
    public class CampaignState { public readonly Stats runStatsState = new(); }
    public class Stats { public readonly Il2CppSystem.Collections.Generic.List<MatchScore> runMatchScores = new(); }
}
namespace EndlessMode
{
    internal static class Plugin
    {
        internal static int[] ActSequence = [1, 2, 3];
        internal static bool IsDefaultMode = false;
    }
}
