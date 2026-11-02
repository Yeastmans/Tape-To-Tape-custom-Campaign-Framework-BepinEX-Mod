using System;
using System.Collections.Generic;

namespace EndlessMode;

// Shared by the actual native callbacks and source-linked checks. Native object
// lifetime and eligibility remain the responsibility of each consumer.
internal static class BaseGamePoolSelectionRuntime
{
    internal static bool Applies(bool baseGame, bool enabled) => baseGame && enabled;

    internal static List<T> Eligible<T>(IEnumerable<T> source, Predicate<T> eligible)
    {
        var result = new List<T>();
        foreach (var candidate in source) if (eligible(candidate)) result.Add(candidate);
        return result;
    }

    internal static List<T> Sample<T>(List<T> candidates, int count, Func<int, int> random)
    {
        var remaining = new List<T>(candidates);
        var result = new List<T>();
        while (result.Count < count && remaining.Count > 0)
        {
            int index = random(remaining.Count);
            result.Add(remaining[index]); remaining.RemoveAt(index);
        }
        return result;
    }

    internal sealed class Scope : IDisposable
    {
        private readonly Stack<Action> restore = new Stack<Action>();
        internal void Replace<T>(Func<T> read, Action<T> write, T temporary)
        {
            T original = read();
            restore.Push(() => write(original));
            write(temporary);
        }
        public void Dispose()
        {
            Exception failure = null;
            while (restore.Count > 0)
                try { restore.Pop()(); } catch (Exception ex) { failure ??= ex; }
            if (failure != null) throw failure;
        }
    }
}
