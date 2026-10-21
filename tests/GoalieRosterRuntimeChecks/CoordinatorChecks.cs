using EndlessMode;

static class CoordinatorChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var queue = new GoalieOperationQueue();
        var order = new List<string>();
        var save = new Fake("save", queue, order);
        var load = new Fake("load", queue, order);
        var secondSave = new Fake("save2", queue, order) { ReadyNow = true };
        queue.Enqueue(save); queue.Enqueue(load); queue.Enqueue(secondSave);
        check(order.SequenceEqual(new[] { "save:start" }), "OverlappingSaveLoadSaveInvokeOnlyGateOwner");
        save.ReadyNow = true; queue.Tick();
        check(order.SequenceEqual(new[] { "save:start", "save:reconcile", "save:publish", "load:start" }), "ReconcileBeforeReleaseAndNextOriginal");
        var reentrant = new Fake("reentrant", queue, order) { ReadyNow = true };
        load.OnPublish = () => { queue.Enqueue(reentrant); queue.Tick(); };
        load.ReadyNow = true; queue.Tick(); queue.Tick();
        check(order.SequenceEqual(new[] { "save:start", "save:reconcile", "save:publish", "load:start", "load:reconcile", "load:publish",
            "save2:start", "save2:reconcile", "save2:publish", "reentrant:start", "reentrant:reconcile", "reentrant:publish" }),
            "SynchronousAndReentrantCompletionForwardExactlyOnceInOrder");
        check(queue.Current == null && save.Starts == 1 && load.Starts == 1 && secondSave.Starts == 1 && reentrant.Starts == 1,
            "NoGateOwnerOrDuplicateOriginalAfterDrain");
    }
    sealed class Fake : GoalieOperationQueue.Operation
    {
        readonly string name; readonly GoalieOperationQueue queue; readonly List<string> order;
        internal bool ReadyNow; internal int Starts; internal Action OnPublish;
        internal Fake(string name, GoalieOperationQueue queue, List<string> order) { this.name = name; this.queue = queue; this.order = order; }
        internal override void Start() { Starts++; order.Add(name + ":start"); }
        internal override bool Ready => ReadyNow;
        internal override void Reconcile() { if (queue.Current != this) throw new Exception("Lost gate before reconciliation"); order.Add(name + ":reconcile"); }
        internal override void Publish() { if (queue.Current != null) throw new Exception("Publication still owns gate"); order.Add(name + ":publish"); OnPublish?.Invoke(); }
    }
}
