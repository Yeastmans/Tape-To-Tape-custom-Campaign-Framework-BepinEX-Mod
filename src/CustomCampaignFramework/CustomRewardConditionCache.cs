using System;
using System.Collections.Generic;

namespace EndlessMode;

internal enum RewardConditionState { Live, Match, Possession, Score, Period, Zone, Breakaway, Lineup, Injury, Equipment, Motion }

internal sealed class CustomRewardConditionCache<T>
{
    private readonly Func<string, T, bool> evaluate;
    private readonly Func<double> physicsStep;
    private readonly Dictionary<(IntPtr, string), (long Revision, double Step, bool Result)> answers = new();
    private readonly long[] revisions = new long[(int)RewardConditionState.Motion + 1];
    internal long ScoreOrPeriodRevision { get; private set; }

    internal CustomRewardConditionCache(Func<string, T, bool> evaluate, Func<double> physicsStep)
    { this.evaluate = evaluate; this.physicsStep = physicsStep; }

    internal bool Read(string condition, IntPtr actor, T value)
    {
        if (condition == "While owned") return true;
        var group = StateFor(condition);
        // Derived stats can change or recurse within the calculation that is asking for them.
        if (group == RewardConditionState.Live || actor == IntPtr.Zero) return evaluate(condition, value);
        long revision = revisions[(int)group];
        // Geometry may also change without a forward crossing a trigger (e.g. a teleport).
        double step = group is RewardConditionState.Motion or RewardConditionState.Zone ? physicsStep() : 0;
        var key = (actor, condition);
        if (answers.TryGetValue(key, out var previous) && previous.Revision == revision && previous.Step == step)
            return previous.Result;
        bool result = evaluate(condition, value);
        answers[key] = (revision, step, result);
        return result;
    }

    internal void Changed(RewardConditionState group)
    {
        revisions[(int)group]++;
        if (group is RewardConditionState.Score or RewardConditionState.Period) ScoreOrPeriodRevision++;
        if (group == RewardConditionState.Lineup) revisions[(int)RewardConditionState.Injury]++;
    }

    internal void Clear()
    {
        answers.Clear();
        ScoreOrPeriodRevision++;
        for (int i = 0; i < revisions.Length; i++) revisions[i]++;
    }

    private static RewardConditionState StateFor(string condition) => condition switch {
        "While holding puck" or "While without puck" or "While team has puck" or "While opponent has puck" => RewardConditionState.Possession,
        "While score tied" or "While trailing" or "While leading" or "While scoreless" or "While having scored" => RewardConditionState.Score,
        "While in period 1" or "While in period 2" or "While in period 3" or "While outside period 1" => RewardConditionState.Period,
        "While in defensive zone" or "While in blue-line shot range" => RewardConditionState.Zone,
        "While on a breakaway" => RewardConditionState.Breakaway,
        "While playing" or "While playing centre" or "While playing wing" or "While playing defence"
            or "While team has three or fewer skaters" => RewardConditionState.Lineup,
        "While injured" or "While eye injured" or "While shoulder injured" or "While arm injured" or "While leg injured"
            or "While team has an injured skater" => RewardConditionState.Injury,
        "While wearing helmet" or "While without helmet" or "While wearing Golden Helmet" or "While wearing a special helmet"
            or "While wearing Golden Helmet against a boss" => RewardConditionState.Equipment,
        "While playing against Elite" or "While playing against a boss" => RewardConditionState.Match,
        "While skating backward" or "While moving toward own end" or "While moving toward opposing end" => RewardConditionState.Motion,
        _ => RewardConditionState.Live
    };
}
