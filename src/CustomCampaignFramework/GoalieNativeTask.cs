using System;
using Il2CppSystem.Runtime.CompilerServices;


namespace EndlessMode;

// Instantiated only for the two proven AOT types: bool and RunDataModelV2.
// The same owned native boxed builder supplies and completes the proxy promise.
internal sealed class GoalieNativeTask<T> : GoalieOperationQueue.Operation
{
    private readonly AsyncTaskMethodBuilder<T> builder;
    private readonly Func<Il2CppSystem.Threading.Tasks.Task<T>> invoke;
    private readonly Action<GoalieNativeTask<T>> reconcile;
    internal Il2CppSystem.Threading.Tasks.Task<T> Proxy { get; }
    internal Il2CppSystem.Threading.Tasks.Task<T> Original { get; private set; }
    internal T Result { get; private set; }
    internal Il2CppSystem.Exception Error { get; private set; }
    internal Action<GoalieNativeTask<T>> Published { get; set; }
    internal GoalieNativeTask(Func<Il2CppSystem.Threading.Tasks.Task<T>> original, Action<GoalieNativeTask<T>> reconcile)
    {
        builder = AsyncTaskMethodBuilder<T>.Create();
        Proxy = builder.Task;
        if (Proxy == null || Proxy.IsCompleted) throw new InvalidOperationException("Native pending proxy contract failed.");
        invoke = original; this.reconcile = reconcile;
    }
    internal override void Start() => Original = invoke() ?? throw new InvalidOperationException("Native original returned no Task.");
    internal override bool Ready => Original?.IsCompleted == true;
    internal override void Reconcile()
    {
        if (Original.IsCanceled) Error = Original.GetCancellationExceptionDispatchInfo()?.SourceException
            ?? throw new InvalidOperationException("Native canceled task lost its original exception.");
        else if (Original.IsFaulted) Error = Original.GetExceptionDispatchInfos()[0].SourceException
            ?? throw new InvalidOperationException("Native faulted task lost its original exception.");
        else Result = Original.GetAwaiter().GetResult();
        reconcile(this);
    }
    internal override void Publish()
    {
        if (Error != null) builder.SetException(Error); else builder.SetResult(Result);
        Published?.Invoke(this);
    }
    // Failed native serialization admission is not an accepted queued operation;
    // it publishes only the actual retained native exception and writes nothing.
    internal void RejectNativeSerialization(Il2CppSystem.Exception error)
    {
        if (Original != null || error == null) throw new InvalidOperationException("Invalid failed-admission completion.");
        Error = error; builder.SetException(error);
    }
}
