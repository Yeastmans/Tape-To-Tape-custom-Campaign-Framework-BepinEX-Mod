using System;
using CampaignCore;

namespace EndlessMode;

// Selection lasts for one synchronous native Initialize call. Deferred menu work
// re-enters through that event's binding; unrelated reward requests see no pool.
internal sealed class CustomCoachScope : IDisposable
{
    [ThreadStatic] internal static CustomCoachDefinition Current;
    private readonly CustomCoachDefinition previous;
    internal CustomCoachScope(CustomCoachDefinition coach) { previous = Current; Current = coach; }
    public void Dispose() { Current = previous; }
}
