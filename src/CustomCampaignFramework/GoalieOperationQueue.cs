using System;
using System.Collections.Generic;

namespace EndlessMode;

// Unity-thread coordinator. Native adapters own their typed builders, original
// tasks and error provenance; this class owns ordering and publication reentry.
internal sealed class GoalieOperationQueue
{
    internal abstract class Operation
    {
        internal readonly string Id = Guid.NewGuid().ToString("N");
        internal abstract void Start();
        internal abstract bool Ready { get; }
        internal abstract void Reconcile();
        internal abstract void Publish();
    }
    private readonly Queue<Operation> waiting = new();
    private bool draining;
    internal Operation Current { get; private set; }
    internal void Enqueue(Operation operation, bool start = true)
    {
        if (operation == null) throw new ArgumentNullException(nameof(operation));
        waiting.Enqueue(operation);
        if (start) Tick();
    }
    internal void Tick()
    {
        if (draining) return;
        draining = true;
        try
        {
            while (true)
            {
                if (Current == null)
                {
                    if (waiting.Count == 0) return;
                    Current = waiting.Dequeue();
                    Current.Start();
                }
                if (!Current.Ready) return;
                var complete = Current;
                complete.Reconcile();
                Current = null;
                // Completion can synchronously run native continuations. A new
                // admission joins the FIFO; it cannot overwrite this selection.
                complete.Publish();
            }
        }
        finally { draining = false; }
    }
}
