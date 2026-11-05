using EndlessMode;

internal static class ConditionStateChecks
{
    internal static void Run(Action<bool, string> check)
    {
        int reads = 0;
        double step = 1;
        bool hasPuck = true;
        var cache = new CustomRewardConditionCache<int>((_, skater) => { reads++; return skater == 1 && hasPuck; }, () => step);
        bool Query(string condition, int owner = 1) => cache.Read(condition, (IntPtr)owner, owner);

        for (int i = 0; i < 500; i++) { step++; Query("While holding puck"); }
        check(reads == 1, "Possession is read once across 500 unchanged physics steps");
        cache.Changed(RewardConditionState.Score);
        Query("While holding puck");
        check(reads == 1, "An unrelated score change leaves the possession result usable");
        hasPuck = false;
        cache.Changed(RewardConditionState.Possession);
        check(!Query("While holding puck") && reads == 2, "A lost puck is visible on the very next read in the same step");
        hasPuck = true;
        cache.Changed(RewardConditionState.Possession);
        check(Query("While holding puck") && !Query("While holding puck", 2) && reads == 4,
            "Successive same-step possession changes and distinct skaters retain their own results");
        reads = 0;
        for (int i = 0; i < 200; i++) Query("While in defensive zone");
        cache.Changed(RewardConditionState.Zone);
        Query("While in defensive zone");
        check(reads == 2, "Repeated zone queries share a reading and a blue-line crossing invalidates it immediately");
        step++; Query("While in defensive zone");
        check(reads == 3, "A teleport or moving goalie is rechecked on the next physics step even without a forward crossing");
        reads = 0;
        for (int i = 0; i < 100; i++) Query("While team has an injured skater");
        cache.Changed(RewardConditionState.Injury);
        Query("While team has an injured skater");
        cache.Changed(RewardConditionState.Lineup);
        Query("While team has an injured skater");
        check(reads == 3, "A team injury result refreshes on both healing and line changes");
        reads = 0;
        Query("While Shot Power is at least 50"); Query("While Shot Power is at least 50");
        check(reads == 2, "A derived shot-power calculation sees new modifiers even within the same callback");
        Query("While score tied"); cache.Clear(); Query("While score tied");
        check(reads == 4, "A fresh game cannot inherit a cached value when native pointers are reused");
    }
}
