namespace Il2CppSystem {
    public class Exception : Object { public string Message; public Exception(string message) { Message = message; } public Exception(IntPtr pointer) : base(pointer) { Message = ((Exception)NativeRegistry.Values[pointer]).Message; } }
    public class OperationCanceledException : Exception { public OperationCanceledException() : base("cancel") {} }
}
namespace Il2CppSystem.Runtime.ExceptionServices { public class ExceptionDispatchInfo { public Il2CppSystem.Exception SourceException { get; init; } } }
namespace Il2CppSystem.Threading.Tasks {
    public class Task<T> : Il2CppSystem.Object {
        internal int ResultReads; internal T Result; internal Il2CppSystem.Exception Error;
        public bool IsCompleted { get; internal set; } public bool IsCanceled => Error is Il2CppSystem.OperationCanceledException;
        public bool IsFaulted => Error != null && !IsCanceled;
        public Runtime.ExceptionServices.ExceptionDispatchInfo GetCancellationExceptionDispatchInfo() => Error == null ? null : new() { SourceException = Error };
        public List<Runtime.ExceptionServices.ExceptionDispatchInfo> GetExceptionDispatchInfos() => new() { new() { SourceException = Error } };
        public Awaiter GetAwaiter() => new(this);
        public class Awaiter { readonly Task<T> task; public Awaiter(Task<T> task) { this.task = task; } public T GetResult() { task.ResultReads++; if (!task.IsCompleted || task.Error != null) throw new System.Exception("Native failure"); return task.Result; } }
    }
}
namespace Il2CppSystem.Runtime.CompilerServices {
    public class AsyncTaskMethodBuilder<T> {
        readonly Threading.Tasks.Task<T> task = new();
        public static AsyncTaskMethodBuilder<T> Create() => new();
        public Threading.Tasks.Task<T> Task => task;
        public void SetResult(T result) { if (task.IsCompleted) throw new System.Exception("duplicate completion"); task.Result = result; task.IsCompleted = true; }
        public void SetException(Il2CppSystem.Exception error) { if (task.IsCompleted) throw new System.Exception("duplicate completion"); task.Error = error; task.IsCompleted = true; }
    }
}
namespace State { public class RunDataModelV2 : Il2CppSystem.Object { public string runID, squadId; public TeamDataModelV2 team; public TeamState teamState; } }
