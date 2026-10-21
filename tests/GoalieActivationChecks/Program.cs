using EndlessMode;
int count = 0;
void Check(bool ok, string label) { if (!ok) throw new Exception(label); count++; }
for (int failure = 0; failure < 8; failure++) {
    int stage = 0; bool enabled = false, subscribed = false, patched = false;
    var error = new InvalidOperationException("injected");
    void Step() { if (stage++ == failure) throw error; }
    try {
        GoalieActivationTransaction.Run(() => { Step(); Step(); },
            () => { subscribed = true; Step(); },
            () => { patched = true; Step(); Step(); Step(); },
            () => { Step(); },
            () => { Step(); enabled = true; },
            () => enabled = false, () => subscribed = false, () => patched = false);
        throw new Exception("Expected failure");
    } catch (InvalidOperationException ex) { Check(ReferenceEquals(ex, error), "preserve activation error"); }
    Check(!enabled && !subscribed && !patched, "rollback partial mutation stage " + failure);
}
{
    var trace = new List<string>();
    GoalieActivationTransaction.Run(() => trace.Add("preflight"), () => trace.Add("subscribe"),
        () => trace.Add("patch"), () => trace.Add("audit"), () => trace.Add("enable"),
        () => trace.Add("disable"), () => trace.Add("unsubscribe"), () => trace.Add("unpatch"));
    Check(string.Join(",", trace) == "preflight,subscribe,patch,audit,enable", "publish only after complete audit");
}
{
    bool disabled = false, unpatched = false;
    try { GoalieActivationTransaction.Run(() => {}, () => {}, () => throw new InvalidOperationException("patch"), () => {}, () => {},
        () => disabled = true, () => throw new Exception("unsubscribe"), () => unpatched = true); }
    catch (AggregateException ex) { Check(ex.InnerExceptions.Count == 2, "retain startup and cleanup errors"); }
    Check(disabled && unpatched, "cleanup continues when unsubscribe fails");
}
Console.WriteLine($"{count} activation checks passed");

CoordinatorChecks.Run();
