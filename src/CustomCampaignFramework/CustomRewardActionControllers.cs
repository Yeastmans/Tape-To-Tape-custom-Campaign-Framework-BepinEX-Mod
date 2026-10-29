using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BepInEx.Unity.IL2CPP.Hook;
using CampaignCore;
using HarmonyLib;
using Movements.MovementStates;
using Rogue;
using Skaters;

namespace EndlessMode;

// A native caller explicitly requests a controller for this one immediate
// action. The proxy has no event subscriptions or secondary stat component.
internal static class CustomRewardActionControllers
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr GetController(IntPtr player, IntPtr method);
    private static INativeDetour detour;
    private static GetController original;
    private static readonly GetController callback = Get;
    private static IntPtr passMethod, randomMethod;
    private static readonly Dictionary<IntPtr, PassPuncherController> passControllers = new();
    [ThreadStatic] private static bool resolving;
    private static IntPtr Get(IntPtr player, IntPtr method)
    {
        var native = original(player, method);
        if (native != IntPtr.Zero || player == IntPtr.Zero || (method != passMethod && method != randomMethod) || resolving || !CustomRewardRuntime.InMatch) return native;
        try {
            resolving = true; var forward = new Skater(player).TryCast<Forward>();
            if (method == randomMethod) return forward != null && CustomRewardRandomShots.Rule(forward) != null ? CustomRewardRandomShots.Controller(forward).Pointer : native;
            if (forward == null || !CustomRewardCapabilities.Active(forward, CustomRewardEffects.PunchPass)) return native;
            if (!passControllers.TryGetValue(player, out var controller)) {
                controller = new PassPuncherController { skater = forward, _forward = forward };
                passControllers.Add(player, controller);
            }
            return controller.Pointer;
        } catch (Exception ex) { Plugin.Log.LogError("[CustomRewards] Native action controller: " + ex); return native; }
        finally { resolving = false; }
    }
    internal static void Clear() { passControllers.Clear(); CustomRewardRandomShots.Clear(); CustomRewardChargedShots.Clear(); }
    internal static void Install()
    {
        if (detour != null) return;
        try {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || IntPtr.Size != 8) throw new PlatformNotSupportedException("Action queries require the audited Windows x64 build.");
            var module = Process.GetCurrentProcess().Modules.Cast<ProcessModule>().First(m => m.ModuleName.Equals("GameAssembly.dll", StringComparison.OrdinalIgnoreCase));
            if (NativeCompatibility.Load(module.FileName).GameHash != "0E1B540314D591A17215BBC46FD17C2605CCFA58A7D30418D62453FFF0FEB3B6") throw new InvalidOperationException("Action queries have not been audited for this game build.");
            var get = module.BaseAddress + 0xe8fdd0;
            Verify(get, "48897C242041564883EC2048837A38");
            passMethod = Method(typeof(PassPuncherController), get);
            randomMethod = Method(typeof(Rogue.Powerups.Controllers.Talents.GamblerController), get);
            detour = INativeDetour.CreateAndApply(get, callback, out original);
        } catch (Exception ex) { detour?.Dispose(); detour = null; passMethod = randomMethod = IntPtr.Zero; Plugin.Log.LogError("[CustomRewards] Native action query unavailable: " + ex); }
    }
    private static IntPtr Method(Type controller, IntPtr expected)
    {
        var storage = typeof(Skater).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                .Single(t => t.Name.StartsWith("MethodInfoStoreGeneric_GetTalentController_", StringComparison.Ordinal)).MakeGenericType(controller);
            RuntimeHelpers.RunClassConstructor(storage.TypeHandle);
            var method = (IntPtr)storage.GetField("Pointer", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetValue(null);
            if (method == IntPtr.Zero || Marshal.ReadIntPtr(method) != expected) throw new InvalidOperationException("Unexpected native action query.");
        return method;
    }
    private static void Verify(IntPtr address, string hex)
    {
        byte[] expected = Convert.FromHexString(hex), actual = new byte[expected.Length]; Marshal.Copy(address, actual, 0, actual.Length);
        if (!actual.SequenceEqual(expected)) throw new InvalidOperationException("A native action query was changed by another patch.");
    }
}
// The two HasFaster* helpers are inlined in this build. Scope the real
// OnEnter call and patch its animation input/output instead of unused helpers.
[HarmonyPatch(typeof(SlapshotState), "OnEnter")]
internal static class CustomRewardWindupScope
{
    [ThreadStatic] internal static Forward Current;
    [HarmonyPrefix] private static void Prefix(SlapshotState __instance, out Forward __state)
    { __state = Current; Current = __instance.forward; }
    [HarmonyPostfix] private static void Postfix(SlapshotState __instance)
    {
        var player = __instance.forward;
        if (__instance._windUpAnimationTrack == null || !CustomRewardCapabilities.Active(player, CustomRewardEffects.FasterWindup)) return;
        var native = player.GetTalent<SlapshotSlowmo>();
        if (native == null || native.level < 2) __instance._windUpAnimationTrack.TimeScale *= 1.5f;
    }
    [HarmonyFinalizer] private static Exception Finalizer(Exception __exception, Forward __state)
    { Current = __state; return __exception; }
}
[HarmonyPatch(typeof(Animations.SkatingAnimationManager), "SetSlapShotWindupAnimationWithTurning")]
internal static class CustomRewardWindupTurning
{
    [HarmonyPrefix] private static void Prefix(Animations.SkatingAnimationManager __instance, ref float __2)
    {
        var source = CustomRewardWindupScope.Current;
        if (source != null && __instance._skater?.Pointer == source.Pointer && CustomRewardCapabilities.Active(source, CustomRewardEffects.FasterShotTurning))
            __2 = Math.Min(__2, .03f);
    }
}
