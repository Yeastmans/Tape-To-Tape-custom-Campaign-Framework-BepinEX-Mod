using System;
using System.Collections.Generic;
namespace EndlessMode;

// All capability flags stay false until subscriptions, every hook and ordering
// audit succeed. Cleanup covers the failing mutation itself, not just completed calls.
internal static class GoalieActivationTransaction
{
    internal static void Run(Action preflight, Action subscribe, Action patch, Action audit, Action enable,
        Action disable, Action unsubscribe, Action unpatch)
    {
        try { preflight(); subscribe(); patch(); audit(); enable(); }
        catch (Exception failure) {
            var errors = new List<Exception> { failure };
            foreach (var cleanup in new[] { disable, unsubscribe, unpatch })
                try { cleanup(); } catch (Exception error) { errors.Add(error); }
            if (errors.Count > 1) throw new AggregateException("Goalie activation failed; rollback incomplete and capability disabled.", errors);
            throw;
        }
    }
}
