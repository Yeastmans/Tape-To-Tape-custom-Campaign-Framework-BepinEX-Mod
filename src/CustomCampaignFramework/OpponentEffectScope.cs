using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CampaignCore;
using HarmonyLib;

namespace EndlessMode;

// Synchronous scopes prevent a selected rule from enabling unrelated identity
// branches (unlocks, completion, skins, sounds). Restore even on native errors.
internal static class OpponentEffectScope
{
    [ThreadStatic] internal static string Current;
    internal struct ScopeState { internal bool Entered; internal string Previous; }
    private static readonly Dictionary<MethodBase, string> Consumers = new();
    internal static void Install(Harmony harmony)
    {
        var consumers = new HashSet<string>();
        foreach (var effect in OpponentEffects.All) foreach (string consumer in effect.Consumers) consumers.Add(consumer);
        foreach (string consumer in consumers)
        {
            int dot = consumer.LastIndexOf('.');
            string typeName = consumer.Substring(0, dot);
            int nested = typeName.IndexOf('/');
            var type = AccessTools.TypeByName(nested < 0 ? typeName : typeName.Substring(0, nested));
            if (nested >= 0 && type != null)
            {
                // Il2CppInterop escapes compiler-generated names, e.g.
                // <ReapplySkin>d__88 becomes _ReapplySkin_d__88.
                string name = typeName.Substring(nested + 1).Replace('<', '_').Replace('>', '_');
                var matches = type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(t => NativeSymbolNames.Stable(t.Name) == NativeSymbolNames.Stable(name)).ToArray();
                type = matches.Length == 1 ? matches[0] : null;
            }
            var methods = type?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(m => m.Name == consumer.Substring(dot + 1)).ToArray() ?? Array.Empty<MethodInfo>();
            if (methods.Length == 0) { Plugin.Log.LogWarning("[Opponent] Effect unavailable: " + consumer); continue; }
            foreach (var method in methods)
                try
                {
                    var route = JerseyLogoNativeHooks.RegisterOpponentScope(consumer, method, out string reason);
                    if (route == OpponentScopeRoute.Raw)
                    {
                        Plugin.Log.LogInfo("[Opponent] Shared native effect scope: " + reason + " -> " + consumer);
                        continue;
                    }
                    if (route == OpponentScopeRoute.Blocked)
                    {
                        Plugin.Log.LogWarning("[Opponent] Effect hook unavailable: " + consumer + ": " + reason);
                        continue;
                    }
                    Consumers[method] = consumer;
                    harmony.Patch(method, prefix: new HarmonyMethod(typeof(OpponentEffectScope), nameof(Enter)),
                        finalizer: new HarmonyMethod(typeof(OpponentEffectScope), nameof(Leave)));
                }
                catch (Exception ex) { Plugin.Log.LogWarning("[Opponent] Effect hook unavailable: " + consumer + ": " + ex.Message); }
        }
    }
    // Each native MoveNext slice owns a synchronous scope; it never leaks across
    // coroutine suspension, and an unregistered raw target preserves its caller.
    internal static void RunNative(string consumer, Action original)
    {
        if (consumer == null) { original(); return; }
        string previous = Current;
        try { Current = consumer; original(); }
        finally { Current = previous; }
    }
    [HarmonyPriority(Priority.First)]
    private static void Enter(MethodBase __originalMethod, out ScopeState __state)
    {
        __state = new ScopeState { Entered = true, Previous = Current };
        Current = Consumers[__originalMethod];
    }
    private static Exception Leave(ScopeState __state, Exception __exception)
    {
        if (__state.Entered) Current = __state.Previous;
        return __exception;
    }
}
