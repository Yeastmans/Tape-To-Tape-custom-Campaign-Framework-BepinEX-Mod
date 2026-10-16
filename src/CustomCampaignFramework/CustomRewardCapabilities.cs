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
using UnityEngine;

namespace EndlessMode;

// These talents are marker capabilities queried by native collision and
// movement code. Supply the capability at the query, without adding a talent,
// attaching its controller, changing its cache, or bypassing the caller's gates.
internal static class CustomRewardCapabilities
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte HasTalent(IntPtr instance, IntPtr method);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetTalent(IntPtr instance, IntPtr method);
    private static INativeDetour cachedDetour, directDetour, getDetour;
    private static HasTalent cachedOriginal, directOriginal;
    private static GetTalent getOriginal;
    private static readonly HasTalent cachedCallback = Cached, directCallback = Direct;
    private static readonly GetTalent getCallback = Get;
    private static readonly Dictionary<IntPtr, string> effects = new();
    private static readonly Dictionary<string, Func<CustomRewardDefinition, Talent>> factories = new();
    private static readonly Dictionary<string, Talent> proxies = new();
    [ThreadStatic] private static bool resolving;
    [ThreadStatic] internal static bool OffensiveAnchorQuery;
    [ThreadStatic] internal static bool ThrownStickQuery;
    [ThreadStatic] internal static bool ArmorInjuryQuery;
    [ThreadStatic] internal static bool PredictionQuery;

    internal static bool Active(Skater player, string effect) => player != null && player.ArePowerUpsEnabled()
        && CustomRewardRuntime.ActiveConditions(player).Any(d => d.Effect == effect);
    private static CustomRewardDefinition Rule(Skater player, string effect, bool valueQuery = false)
    {
        if (effect == CustomRewardEffects.AiPrince && PredictionQuery) effect = CustomRewardEffects.PreventPredictivePoke;
        if (effect == CustomRewardEffects.AiKarlson || effect == CustomRewardEffects.AiPrince || effect == CustomRewardEffects.AiKnight) {
            var forward = player?.TryCast<Forward>();
            return forward != null && forward.ArePowerUpsEnabled() && CustomRewardLiveRules.Behavior(forward) == effect
                ? CustomRewardRuntime.ActiveConditions(player).Where(d => d.Effect == effect).OrderByDescending(d => CustomRewardRuntime.Acquired(player, d)).FirstOrDefault() : null;
        }
        if (effect == CustomRewardEffects.SpikedRetaliation && ArmorInjuryQuery) effect = CustomRewardEffects.SpikedInjury;
        if (effect == CustomRewardEffects.BatAim) return CustomRewardBatting.Rule(player, valueQuery);
        if (effect == CustomRewardEffects.ChargePower) return CustomRewardChargedShots.Rule(player);
        if (effect == CustomRewardEffects.RandomShotPower) return CustomRewardRandomShots.Rule(player);
        if (effect == CustomRewardEffects.ExtraGoal) return CustomRewardGoalValues.RuleFor(player);
        if (effect == CustomRewardEffects.ImpactKnockout) return CustomRewardNativeEffects.ImpactRule(player);
        if (effect == CustomRewardEffects.DeflectSticks && !ThrownStickQuery) return null;
        if (effect == CustomRewardEffects.AnchorProtection && OffensiveAnchorQuery) effect = CustomRewardEffects.ForbidKnockouts;
        return player != null && player.ArePowerUpsEnabled() ? CustomRewardRuntime.ActiveConditions(player).FirstOrDefault(d => d.Effect == effect) : null;
    }
    private static Talent Proxy(CustomRewardDefinition rule)
    {
        if (!proxies.TryGetValue(rule.Id, out var proxy)) {
            proxy = factories[rule.Effect](rule);
            proxy.id = rule.Id; proxy.name = "CCF_Capability_" + rule.Id; proxy.level = rule.Effect == CustomRewardEffects.ImpactExplosion ? 2 : 1;
            proxy.powerupName = rule.Name; proxy.description = rule.FullDescription; proxy.upgradedDescription = "";
            proxy.statBonusDescription = ""; proxy.hasUpgradeText = false; proxy.icon = CustomRewardRuntime.FindTalent(rule.Id, false)?.icon ?? CustomRewardRuntime.FindRelic(rule.Id, false)?.sprite;
            proxy.hideFlags = HideFlags.DontUnloadUnusedAsset;
            proxies.Add(rule.Id, proxy);
        }
        return proxy;
    }

    private static byte Cached(IntPtr player, IntPtr method) => Resolve(cachedOriginal(player, method), player, method);
    private static byte Direct(IntPtr player, IntPtr method) => Resolve(directOriginal(player, method), player, method);
    private static byte Resolve(byte native, IntPtr player, IntPtr method)
    {
        if (native != 0 || player == IntPtr.Zero || resolving || !CustomRewardRuntime.InMatch || !effects.TryGetValue(method, out string effect)) return native;
        try {
            resolving = true;
            var rule = Rule(new Skater(player), effect);
            if (rule == null) return native;
            // A capability with a value lookup must have a valid object before
            // the caller is told it exists. The object never enters a loadout.
            if (factories.ContainsKey(effect)) Proxy(rule);
            return 1;
        }
        catch (Exception ex) { Plugin.Log.LogError("[CustomRewards] Native capability query: " + ex); return native; }
        finally { resolving = false; }
    }
    private static IntPtr Get(IntPtr player, IntPtr method)
    {
        var native = getOriginal(player, method);
        if (native != IntPtr.Zero || player == IntPtr.Zero || resolving || !CustomRewardRuntime.InMatch || !effects.TryGetValue(method, out string effect)) return native;
        try {
            resolving = true; var rule = Rule(new Skater(player), effect, true);
            return rule == null ? native : Proxy(rule).Pointer;
        } catch (Exception ex) { Plugin.Log.LogError("[CustomRewards] Native capability value: " + ex); return native; }
        finally { resolving = false; }
    }

    internal static void Install()
    {
        if (cachedDetour != null) return;
        try {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || IntPtr.Size != 8)
                throw new PlatformNotSupportedException("Native capability rules require the audited Windows x64 build.");
            var module = Process.GetCurrentProcess().Modules.Cast<ProcessModule>().First(m => m.ModuleName.Equals("GameAssembly.dll", StringComparison.OrdinalIgnoreCase));
            if (NativeCompatibility.Load(module.FileName).GameHash != "7B93D7F5B31CA164F7D5E35D1310A4F64007C8058C7B408061EDB928007D0EBA")
                throw new InvalidOperationException("Native capability queries have not been audited for this game build.");
            var cached = module.BaseAddress + 0xc40990; var direct = module.BaseAddress + 0xc40af0; var get = module.BaseAddress + 0xc40800;
            Verify(cached, "48895C2418564883EC2048837A3800");
            Verify(direct, "48895C2408574883EC2048837A3800");
            Verify(get, "48895C2408574883EC2048837A3800");
            Register<SpikedArmor>(CustomRewardEffects.SpikedRetaliation, cached, direct, get);
            Register<RefBlockShotTalent>(CustomRewardEffects.RefereeBlockTalent, cached, direct);
            Register<KarlsonBehavior>(CustomRewardEffects.AiKarlson, cached, direct);
            Register<PrinceAI>(CustomRewardEffects.AiPrince, cached, direct);
            Register<KnightAIPowerup>(CustomRewardEffects.AiKnight, cached, direct);
            Register<Rogue.Powerups.Chaos.FragileTalent>(CustomRewardEffects.FragileBody, cached, direct);
            Register<OffensiveBodycheck>(CustomRewardEffects.CarryingCheck, cached, direct);
            Register<Onepunch>(CustomRewardEffects.GuaranteedCheck, cached, direct);
            Register<FigureCheck>(CustomRewardEffects.SpinOnCheck, cached, direct);
            Register<Velcro>(CustomRewardEffects.CollectOnHit, cached, direct);
            Register<Kick>(CustomRewardEffects.KickPuck, cached, direct, get);
            Register<PingKing>(CustomRewardEffects.PostAim, cached, direct, get);
            Register<ReboundBouncer>(CustomRewardEffects.ForceRebound, cached, direct, get);
            Register<GoldenPuck>(CustomRewardEffects.ExtraGoal, cached, direct, get);
            Register<SpringBoard>(CustomRewardEffects.InstantRecovery, cached, direct);
            Register<DefensiveDeflect>(CustomRewardEffects.DeflectPucks, cached, direct, get);
            Register<WonderWall>(CustomRewardEffects.DeflectSticks, cached, direct);
            Register<HyperDrive>(CustomRewardEffects.TurningDelay, cached, direct, get);
            Register<DestroyerShot>(CustomRewardEffects.ImpactKnockout, cached, direct, get);
            Register<Slugger>(CustomRewardEffects.BatAim, cached, direct, get);
            Register<ChargeShot>(CustomRewardEffects.ChargePower, cached, direct, get);
            Register<Gambler>(CustomRewardEffects.RandomShotPower, cached, direct, get);
            Register<PassBack>(CustomRewardEffects.ReturnPass, cached, direct);
            Register<PuckPuncher>(CustomRewardEffects.PunchShot, cached, direct, get);
            Register<SadoMaso>(CustomRewardEffects.IgnoreInjuryPenalties, cached, direct);
            Register<Rogue.Powerups.Chaos.Anchor>(CustomRewardEffects.AnchorProtection, cached, direct);
            factories[CustomRewardEffects.SpikedRetaliation] = factories[CustomRewardEffects.SpikedInjury] = rule => { var proxy = ScriptableObject.CreateInstance<SpikedArmor>(); proxy.injuryMultiplier = rule.Effect == CustomRewardEffects.SpikedInjury ? rule.Amount / 100f : 1; return proxy; };
            factories[CustomRewardEffects.KickPuck] = _ => {
                var kick = ScriptableObject.CreateInstance<Kick>(); kick.baseShotPowerBonus = kick.upgradeShotPowerBonus = 0; return kick;
            };
            factories[CustomRewardEffects.BatAim] = factories[CustomRewardEffects.BatLift] = factories[CustomRewardEffects.BatKnockout] = rule => { var proxy = ScriptableObject.CreateInstance<Slugger>(); proxy.radius = rule.Effect == CustomRewardEffects.BatKnockout ? rule.Radius : 0; return proxy; };
            factories[CustomRewardEffects.ChargePower] = factories[CustomRewardEffects.ChargeAccuracy] = factories[CustomRewardEffects.ChargeCritical] = rule => {
                var proxy = ScriptableObject.CreateInstance<ChargeShot>(); proxy.powerBonus = proxy.accBonus = 0;
                proxy.chargeSpeedMulti = 1; proxy.maxChargetime = rule.Effect == CustomRewardEffects.ChargeCritical ? rule.Seconds : float.MaxValue;
                return proxy;
            };
            factories[CustomRewardEffects.RandomShotPower] = factories[CustomRewardEffects.RandomShotAccuracy] = _ => ScriptableObject.CreateInstance<Gambler>();
            factories[CustomRewardEffects.PostAim] = _ => ScriptableObject.CreateInstance<PingKing>();
            factories[CustomRewardEffects.PunchShot] = _ => ScriptableObject.CreateInstance<PuckPuncher>();
            factories[CustomRewardEffects.ForceRebound] = _ => ScriptableObject.CreateInstance<ReboundBouncer>();
            factories[CustomRewardEffects.ExtraGoal] = _ => ScriptableObject.CreateInstance<GoldenPuck>();
            factories[CustomRewardEffects.DeflectPucks] = _ => ScriptableObject.CreateInstance<DefensiveDeflect>();
            factories[CustomRewardEffects.TurningDelay] = rule => { var proxy = ScriptableObject.CreateInstance<HyperDrive>(); proxy.minTimeTurningIncrease = rule.Amount / 1000f; return proxy; };
            factories[CustomRewardEffects.ImpactKnockout] = factories[CustomRewardEffects.ImpactExplosion] = rule => {
                var proxy = ScriptableObject.CreateInstance<DestroyerShot>(); proxy.explosionsRadius = rule.Radius; return proxy;
            };
            getDetour = INativeDetour.CreateAndApply(get, getCallback, out getOriginal);
            cachedDetour = INativeDetour.CreateAndApply(cached, cachedCallback, out cachedOriginal);
            directDetour = INativeDetour.CreateAndApply(direct, directCallback, out directOriginal);
            Plugin.Log.LogInfo("[CustomRewards] Native movement and contact capability queries installed.");
        } catch (Exception ex) {
            directDetour?.Dispose(); directDetour = null; cachedDetour?.Dispose(); cachedDetour = null; getDetour?.Dispose(); getDetour = null; effects.Clear(); factories.Clear();
            Plugin.Log.LogError("[CustomRewards] Native capability rules unavailable: " + ex);
        }
    }
    private static void Verify(IntPtr address, string hex)
    {
        byte[] expected = Convert.FromHexString(hex), actual = new byte[expected.Length];
        Marshal.Copy(address, actual, 0, actual.Length);
        if (!actual.SequenceEqual(expected)) throw new InvalidOperationException("A native capability query was changed by another patch.");
    }
    private static void Register<T>(string effect, IntPtr cached, IntPtr direct, IntPtr get = default)
    {
        var methods = new List<(string, IntPtr)> { ("HaveTalentCached", cached), ("HaveTalent", direct) };
        if (get != IntPtr.Zero) methods.Add(("GetTalent", get));
        foreach (var entry in methods) {
            var storage = typeof(Skater).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                .Single(t => t.Name.StartsWith("MethodInfoStoreGeneric_" + entry.Item1 + "_", StringComparison.Ordinal)).MakeGenericType(typeof(T));
            RuntimeHelpers.RunClassConstructor(storage.TypeHandle);
            var method = (IntPtr)storage.GetField("Pointer", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            if (method == IntPtr.Zero || Marshal.ReadIntPtr(method) != entry.Item2)
                throw new InvalidOperationException("Unexpected native method pointer for " + typeof(T).Name + "." + entry.Item1);
            effects.Add(method, effect);
        }
    }
}

[HarmonyPatch(typeof(Pools.Stick), "TryHit")]
internal static class CustomRewardThrownStickScope
{
    [HarmonyPrefix] private static void Prefix(out bool __state) { __state = CustomRewardCapabilities.ThrownStickQuery; CustomRewardCapabilities.ThrownStickQuery = true; }
    [HarmonyFinalizer] private static void Finalizer(bool __state) => CustomRewardCapabilities.ThrownStickQuery = __state;
}

// This is the only native Anchor query that governs outgoing knockouts.
// The remaining audited queries govern being hit. Preserve nested scopes and
// exception paths so one half of the native bundle never grants the other.
[HarmonyPatch(typeof(Forward), "CanKnockout")]
internal static class CustomRewardAnchorAttackScope
{
    [HarmonyPrefix] private static void Prefix(out bool __state) { __state = CustomRewardCapabilities.OffensiveAnchorQuery; CustomRewardCapabilities.OffensiveAnchorQuery = true; }
    [HarmonyFinalizer] private static void Finalizer(bool __state) => CustomRewardCapabilities.OffensiveAnchorQuery = __state;
}

// HasQuickerRecovery is inlined in OnEnter. Apply its separate time modifier
// after that native path, and do not apply it twice when a native item did so.
[HarmonyPatch(typeof(KnockoutState), "OnEnter")]
internal static class CustomRewardRecovery
{
    [HarmonyPostfix] private static void Postfix(KnockoutState __instance) => CustomRewardRuntime.Safe(() => {
        if (__instance._isBrawlFinisher || __instance.HasQuickerRecovery() || __instance.HasInstantRecovery()) return;
        if (CustomRewardCapabilities.Active(__instance.forward, CustomRewardEffects.QuickRecovery)) __instance._timeKnockout *= .5f;
    });
}

// Spiked Armor's injury queries are a separate component from its contact
// capability. The scoped native query preserves both normal and fragile paths.
[HarmonyPatch]
internal static class CustomRewardArmorInjuryScope
{
    private static IEnumerable<MethodBase> TargetMethods() {
        yield return AccessTools.Method(typeof(Skaters.Injuries.InjurySystem), "HandleInjury");
        yield return AccessTools.Method(typeof(Skaters.Injuries.InjurySystem), "HandleFragileInjury");
    }
    [HarmonyPrefix] private static void Prefix(out bool __state) { __state = CustomRewardCapabilities.ArmorInjuryQuery; CustomRewardCapabilities.ArmorInjuryQuery = true; }
    [HarmonyFinalizer] private static void Finalizer(bool __state) => CustomRewardCapabilities.ArmorInjuryQuery = __state;
}
