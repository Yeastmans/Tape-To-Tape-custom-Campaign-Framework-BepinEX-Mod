using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CampaignCore;

namespace EndlessMode;

internal enum OpponentScopeRoute { Harmony, Raw, Blocked }

// One native owner per entry point. Reservations survive a blocked install;
// ownership survives a failed rollback, independently of appearance Enabled.
internal sealed class NativeOpponentScopes
{
    private readonly RawNativeHookContract[] reserved;
    private readonly HashSet<string> owned = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> consumers = new(StringComparer.Ordinal);
    internal IntPtr Module { get; set; }
    internal NativeOpponentScopes(IEnumerable<RawNativeHookContract> contracts) => reserved = contracts.ToArray();
    internal static string Identity(string value) => NativeSymbolNames.Stable(value.Replace("$$", ".")
        .Replace('+', '.').Replace('<', '_').Replace('>', '_'));
    internal void Own(RawNativeHookContract contract) => owned.Add(contract.Id);
    internal void Release(RawNativeHookContract contract) { owned.Remove(contract.Id); consumers.Remove(contract.Id); }
    internal string Consumer(string id) => consumers.TryGetValue(id, out var consumer) ? consumer : null;

    internal OpponentScopeRoute Register(string consumer, MethodInfo method, Func<MethodInfo, IntPtr> nativeAddress, out string reason)
    {
        reason = "";
        var named = reserved.Where(c => Identity(c.Identity) == Identity(consumer)).ToArray();
        IntPtr address;
        try { address = nativeAddress(method); }
        catch (Exception ex) { reason = "Native entry could not be verified: " + ex.Message; return OpponentScopeRoute.Blocked; }
        if (address == IntPtr.Zero || Module == IntPtr.Zero)
        { reason = "Native entry or module is unavailable."; return OpponentScopeRoute.Blocked; }
        var addressed = reserved.Where(c => Module + c.Rva == address).ToArray();
        if (named.Length == 0 && addressed.Length == 0) return OpponentScopeRoute.Harmony;
        if (named.Length != 1 || addressed.Length != 1 || named[0] != addressed[0]
            || Identity(method.DeclaringType.FullName + "." + method.Name) != Identity(consumer)
            || method.IsStatic || method.ReturnType.FullName != "System.Void" || method.GetParameters().Length != 0
            || method.ContainsGenericParameters || named[0].Kind != "move" || method.Name != "MoveNext")
        { reason = "Reserved native entry has an unverified identity or signature."; return OpponentScopeRoute.Blocked; }
        var target = named[0];
        if (!owned.Contains(target.Id))
        { reason = "Reserved raw owner is unavailable: " + target.Id; return OpponentScopeRoute.Blocked; }
        if (consumers.TryGetValue(target.Id, out var previous) && previous != consumer)
        { reason = "Raw entry already carries another opponent scope: " + target.Id; return OpponentScopeRoute.Blocked; }
        consumers[target.Id] = consumer;
        reason = target.Id;
        return OpponentScopeRoute.Raw;
    }
}
