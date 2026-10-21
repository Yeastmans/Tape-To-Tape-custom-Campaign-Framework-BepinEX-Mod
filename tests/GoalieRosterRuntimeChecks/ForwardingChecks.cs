using EndlessMode;
using Il2CppSystem.Runtime.CompilerServices;

static class ForwardingChecks
{
    internal static void Run(Action<bool, string> check)
    {
        foreach (string outcome in new[] { "true", "false", "fault", "cancel" })
        {
            var queue = new GoalieOperationQueue(); var source = AsyncTaskMethodBuilder<bool>.Create();
            var original = source.Task; int calls = 0, finishes = 0;
            var op = new GoalieNativeTask<bool>(() => { calls++; return original; }, value => { finishes++; check(value.Original == original, outcome + " exact original task retained"); });
            var proxy = op.Proxy; queue.Enqueue(op);
            check(!proxy.IsCompleted && calls == 1, outcome + " pending proxy forwards one original");
            Il2CppSystem.Exception error = outcome == "cancel" ? new Il2CppSystem.OperationCanceledException() : new Il2CppSystem.Exception("native failure");
            if (outcome is "fault" or "cancel") source.SetException(error); else source.SetResult(outcome == "true");
            queue.Tick(); queue.Tick();
            check(proxy.IsCompleted && finishes == 1 && calls == 1, outcome + " completes and reconciles exactly once");
            if (outcome is "fault" or "cancel") check(ReferenceEquals(op.Error, error) && proxy.IsCanceled == (outcome == "cancel"), outcome + " retains native exception provenance");
            else check(proxy.GetAwaiter().GetResult() == (outcome == "true"), outcome + " preserves native boolean");
        }
        var loadSource = AsyncTaskMethodBuilder<State.RunDataModelV2>.Create();
        var load = new GoalieNativeTask<State.RunDataModelV2>(() => loadSource.Task, _ => {});
        var loadQueue = new GoalieOperationQueue(); loadQueue.Enqueue(load); loadSource.SetResult((State.RunDataModelV2)null); loadQueue.Tick();
        check(load.Proxy.GetAwaiter().GetResult() == null, "NativeNullLoadRemainsNull");
    }
}
