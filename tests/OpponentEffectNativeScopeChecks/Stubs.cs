using System.Reflection;

// Record the real installer's decisions without modifying native code or loading
// the game runtime. Types/methods come from the installed interop via metadata.
namespace HarmonyLib
{
    public sealed class Harmony
    {
        public readonly List<MethodInfo> Patched = new();
        public void Patch(MethodInfo method, HarmonyMethod prefix = null, HarmonyMethod finalizer = null) => Patched.Add(method);
    }
    public sealed class HarmonyMethod { public HarmonyMethod(Type type, string method) { } }
    public sealed class HarmonyPriority : Attribute { public HarmonyPriority(int priority) { } }
    public static class Priority { public const int First = 800; }
    public static class AccessTools
    {
        public static Assembly Game, Other;
        public static Type TypeByName(string name) => Game.GetType(name) ?? Other.GetType(name) ?? Game.GetTypes().Concat(Other.GetTypes()).SingleOrDefault(t => t.Name == name);
    }
}
namespace EndlessMode
{
    internal static class JerseyLogoNativeHooks
    {
        internal static NativeOpponentScopes Scopes;
        internal static Func<MethodInfo, IntPtr> Address;
        internal static OpponentScopeRoute RegisterOpponentScope(string consumer, MethodInfo method, out string reason)
            => Scopes.Register(consumer, method, Address, out reason);
    }
    internal static class Plugin
    {
        internal static readonly Logger Log = new();
        internal sealed class Logger
        {
            internal readonly List<string> Warnings = new();
            internal void LogWarning(string text) => Warnings.Add(text);
            internal void LogInfo(string text) { }
        }
    }
}
