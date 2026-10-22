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
    private static readonly Dictionary<IntPtr, IntPtr> dataMethods = new();
    [ThreadStatic] internal static int NativeQueryDepth;
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

    private static byte Native(HasTalent original, IntPtr player, IntPtr method)
    {
        NativeQueryDepth++;
        try { return original(player, method); }
        finally { NativeQueryDepth--; }
    }
    private static byte Cached(IntPtr player, IntPtr method) => Resolve(Native(cachedOriginal, player, method), player, method);
    private static byte Direct(IntPtr player, IntPtr method) => Resolve(Native(directOriginal, player, method), player, method);
    // Only audited inlined LIVE callers enter this scope. Data-only queries
    // (including previews of the same SkaterData) retain their native meaning.
    internal static byte FromData(byte native, IntPtr data, IntPtr method)
    {
        if (!CustomRewardLiveQueryScope.Active || NativeQueryDepth != 0 || !dataMethods.TryGetValue(method, out var liveMethod)) return native;
        try {
            var actor = CustomRewardRuntime.ActorForData(data);
            if (actor == null || !actor.ArePowerUpsEnabled()) return native;
            return Resolve(native, actor.Pointer, liveMethod);
        } catch (Exception ex) { Plugin.Log.LogError("[CustomRewards] Inlined live query: " + ex); return native; }
    }
    private static byte Resolve(byte native, IntPtr player, IntPtr method)
    {
        if (player != IntPtr.Zero && !resolving && CustomRewardRuntime.InMatch && effects.TryGetValue(method, out string goal) && goal == CustomRewardEffects.ExtraGoal)
            return GoalValue(native, player);
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
    // Hidden Ace's query in OnGoalScored / OnDeniedGoal. The native branch adds
    // or cancels one goal unit; arm the total so the same native helper runs
    // once per unit. A native Hidden Ace still supplies its own shared unit.
    private static byte GoalValue(byte native, IntPtr player)
    {
        try {
            resolving = true;
            var actor = new Skater(player);
            if (!actor.ArePowerUpsEnabled()) return native;
            var scope = CustomRewardGoalValues.ScopeFor(actor);
            if (scope == null) return native;
            // A denial reverses only what this goal's scoring query added.
            int units = scope.Denial && scope.Units < 0 ? 0 : scope.Query(native != 0);
            if (units <= 0) return native;
            if (!GoalRepeatInstalled && units > 1) units = 1;
            if (factories.ContainsKey(CustomRewardEffects.ExtraGoal)) Proxy(scope.Rule);
            CustomRewardGoalValues.Arm(scope, units);
            return 1;
        }
        catch (Exception ex) { Plugin.Log.LogError("[CustomRewards] Native goal value query: " + ex); return native; }
        finally { resolving = false; }
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void AddGoalFn(IntPtr stats, IntPtr goal, IntPtr method);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void CancelStatsFn(IntPtr stats, IntPtr method);
    private static INativeDetour addGoalDetour;
    private static AddGoalFn addGoalOriginal;
    private static readonly AddGoalFn addGoalCallback = AddGoal;
    private static CancelStatsFn cancelStats;
    internal static bool GoalRepeatInstalled => addGoalDetour != null && cancelStats != null;
    // GameStats.<OnGoalScored>g__AddGoal: one complete native goal unit (team
    // and scorer goals, per-difficulty goals, every assist with its event,
    // and the persistent goal statistics batch collected by OnGoalScored).
    private static void AddGoal(IntPtr stats, IntPtr goal, IntPtr method)
    {
        int units = 0;
        try { units = CustomRewardGoalValues.TakeArmed(stats); }
        catch (Exception ex) { Plugin.Log.LogError("[CustomRewards] Extra goals: " + ex); }
        addGoalOriginal(stats, goal, method);
        for (int i = 1; i < units; i++) addGoalOriginal(stats, goal, method);
    }
    // After native OnDeniedGoal has cancelled its own Hidden Ace unit, cancel
    // the remaining armed units through the same native helper. CancelStats
    // reads only the last scorer, assists and stat collector, which denial
    // leaves intact; it is called exactly as OnDeniedGoal calls it.
    internal static void CancelExtraGoals(IntPtr stats, bool scorerPresent)
    {
        int units = CustomRewardGoalValues.TakeArmed(stats);
        if (!scorerPresent || cancelStats == null) return;
        for (int i = 1; i < units; i++) cancelStats(stats, IntPtr.Zero);
    }
    private static void InstallGoalRepeat(ProcessModule module)
    {
        try {
            var add = module.BaseAddress + 0x403cfc0; var cancel = module.BaseAddress + 0x403cc60;
            Verify(add, "48895C2408488974241048897C241841564883EC60");
            Verify(cancel, "48895C2418574883EC40803D");
            cancelStats = Marshal.GetDelegateForFunctionPointer<CancelStatsFn>(cancel);
            addGoalDetour = INativeDetour.CreateAndApply(add, addGoalCallback, out addGoalOriginal);
            Plugin.Log.LogInfo("[CustomRewards] Native extra-goal repeat installed.");
        } catch (Exception ex) {
            addGoalDetour?.Dispose(); addGoalDetour = null; cancelStats = null;
            Plugin.Log.LogError("[CustomRewards] Extra goals limited to one per goal: " + ex);
        }
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
            if (NativeCompatibility.Load(module.FileName).GameHash != "A58718A1AF369E064DF3A7CE149129C0130C4F316BC5E47EF7E6365853B1AA42")
                throw new InvalidOperationException("Native capability queries have not been audited for this game build.");
            var cached = module.BaseAddress + 0xe8ff80; var direct = module.BaseAddress + 0xe8ff20; var get = module.BaseAddress + 0xe8fc70;
            Verify(cached, "48895C2408574883EC2048837A3800");
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
            factories[CustomRewardEffects.ExtraGoal] = factories[CustomRewardEffects.ExtraGoals] = _ => ScriptableObject.CreateInstance<GoldenPuck>();
            factories[CustomRewardEffects.DeflectPucks] = _ => ScriptableObject.CreateInstance<DefensiveDeflect>();
            factories[CustomRewardEffects.TurningDelay] = rule => { var proxy = ScriptableObject.CreateInstance<HyperDrive>(); proxy.minTimeTurningIncrease = rule.Amount / 1000f; return proxy; };
            factories[CustomRewardEffects.ImpactKnockout] = factories[CustomRewardEffects.ImpactExplosion] = rule => {
                var proxy = ScriptableObject.CreateInstance<DestroyerShot>(); proxy.explosionsRadius = rule.Radius; return proxy;
            };
            getDetour = INativeDetour.CreateAndApply(get, getCallback, out getOriginal);
            cachedDetour = INativeDetour.CreateAndApply(cached, cachedCallback, out cachedOriginal);
            directDetour = INativeDetour.CreateAndApply(direct, directCallback, out directOriginal);
            InstallFixedQueries(module);
            Plugin.Log.LogInfo("[CustomRewards] Native movement and contact capability queries installed.");
            InstallGoalRepeat(module);
        } catch (Exception ex) {
            foreach (var hook in fixedDetours) hook.Dispose(); fixedDetours.Clear(); fixedCallbacks.Clear();
            directDetour?.Dispose(); directDetour = null; cachedDetour?.Dispose(); cachedDetour = null; getDetour?.Dispose(); getDetour = null; effects.Clear(); dataMethods.Clear(); factories.Clear();
            Plugin.Log.LogError("[CustomRewards] Native capability rules unavailable: " + ex);
        }
    }

    // The optimizer emitted one-argument specializations which do not call the
    // shared Skater query entry. Their originals still own the native gates.
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate byte FixedQuery(IntPtr player);
    private static readonly List<INativeDetour> fixedDetours = new();
    private static readonly List<FixedQuery> fixedCallbacks = new();
    private static void InstallFixedQueries(ProcessModule module)
    {
        Fixed<Kick>(module, 0x636ef0, "HaveTalentCached", "48895C2408574883EC20488B1DF7FD1F08488BF948837B38");
        Fixed<Slugger>(module, 0x636f60, "HaveTalentCached", "48895C2408574883EC20488B1D6FFD1F08488BF948837B38");
        Fixed<ChargeShot>(module, 0x636fe0, "HaveTalentCached", "48895C2408574883EC20488B1D47FB1F08488BF948837B38");
        Fixed<WonderWall>(module, 0x63a330, "HaveTalentCached", "48895C2408574883EC20488B1D87E62108488BF948837B38");
        Fixed<OffensiveBodycheck>(module, 0x63e6d0, "HaveTalentCached", "48895C2408574883EC20488B1D476F1D08488BF948837B38");
        Fixed<HyperDrive>(module, 0x63e840, "HaveTalentCached", "48895C2408574883EC20488B1D5F7E1D08488BF948837B38");
        Fixed<SpringBoard>(module, 0x63eb80, "HaveTalentCached", "48895C2408574883EC20488B1D7F8D1D08488BF948837B38");
        Fixed<Rogue.Powerups.Chaos.FragileTalent>(module, 0x6406a0, "HaveTalent", "48895C2408574883EC20488B1D9F201908488BF948837B38");
        Fixed<GoldenPuck>(module, 0x646400, "HaveTalentCached", "48895C2408574883EC20488B1DCFB22308488BF948837B38");
        Fixed<PrinceAI>(module, 0x6470e0, "HaveTalentCached", "48895C2408574883EC20488B1DB7F22308488BF948837B38");
        Fixed<Rogue.Powerups.Chaos.Anchor>(module, 0x647150, "HaveTalentCached", "48895C2408574883EC20488B1D8FF22308488BF948837B38");
        Fixed<SpikedArmor>(module, 0x6471c0, "HaveTalentCached", "48895C2408574883EC20488B1D3FEF2308488BF948837B38");
        Fixed<Gambler>(module, 0x647790, "HaveTalent", "48895C2408574883EC20488B1D6F1F2408488BF948837B38");
        Fixed<Gambler>(module, 0x647950, "HaveTalentCached", "48895C2408574883EC20488B1DD71C2408488BF948837B38");
        Fixed<DestroyerShot>(module, 0x647f10, "HaveTalentCached", "48895C2408574883EC20488B1D8F222408488BF948837B38");
        Fixed<PingKing>(module, 0x648200, "HaveTalent", "48895C2408574883EC20488B1D575A2408488BF948837B38");
        Fixed<Velcro>(module, 0x6499c0, "HaveTalentCached", "48895C2408574883EC20488B1D6FC82508488BF948837B38");
        Fixed<DefensiveDeflect>(module, 0x649a30, "HaveTalentCached", "48895C2408574883EC20488B1DE7C72508488BF948837B38");
        Fixed<KarlsonBehavior>(module, 0x658370, "HaveTalentCached", "48895C2408574883EC20488B1DBFA11A08488BF948837B38");
    }
    private static void Fixed<T>(ProcessModule module, int rva, string query, string prologue)
    {
        var storage = typeof(Skater).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .Single(t => t.Name.StartsWith("MethodInfoStoreGeneric_" + query + "_", StringComparison.Ordinal)).MakeGenericType(typeof(T));
        RuntimeHelpers.RunClassConstructor(storage.TypeHandle);
        var method = (IntPtr)storage.GetField("Pointer", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        if (!effects.ContainsKey(method)) throw new InvalidOperationException("Unregistered fixed live query " + typeof(T).Name);
        Verify(module.BaseAddress + rva, prologue);
        FixedQuery original = null;
        FixedQuery callback = player => {
            byte native;
            NativeQueryDepth++;
            try { native = original(player); }
            finally { NativeQueryDepth--; }
            return Resolve(native, player, method);
        };
        fixedCallbacks.Add(callback);
        fixedDetours.Add(INativeDetour.CreateAndApply(module.BaseAddress + rva, callback, out original));
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
            if (entry.Item1 != "GetTalent") {
                var dataStorage = typeof(Data.SkaterData).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                    .Single(t => t.Name.StartsWith("MethodInfoStoreGeneric_" + entry.Item1 + "_", StringComparison.Ordinal)).MakeGenericType(typeof(T));
                RuntimeHelpers.RunClassConstructor(dataStorage.TypeHandle);
                var dataMethod = (IntPtr)dataStorage.GetField("Pointer", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
                var dataEntry = entry.Item1 == "HaveTalentCached" ? cached - 0xe8ff80 + 0xe90200 : direct - 0xe8ff20 + 0xe900e0;
                if (dataMethod == IntPtr.Zero || Marshal.ReadIntPtr(dataMethod) != dataEntry)
                    throw new InvalidOperationException("Unexpected inlined data query for " + typeof(T).Name + "." + entry.Item1);
                dataMethods.Add(dataMethod, method);
            }
        }
    }
}

// These are the 26085704 callers whose native bodies inline the Skater gate
// and call SkaterData directly. Keep this positive list separate from saved
// data consumers: BenchBonus.MoveNext, for example, contains both kinds.
[HarmonyPatch]
internal static class CustomRewardLiveQueryScope
{
    [ThreadStatic] internal static bool Active;
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(Movements.MovementManager), "CanBodycheck");
        yield return AccessTools.Method(typeof(Goaltender.GoaltenderStates.SavingState), "OnPuckCollision");
        yield return AccessTools.Method(typeof(Rogue.NoStickSoccerRelicController), "_AddEffect_b__3_0");
        yield return AccessTools.Method(typeof(Rogue.Relics.Controllers.SpikeArmorRelicController), "AddEffect");
        yield return AccessTools.Method(typeof(Skaters.ForwardStats), "GetStatAfterInjuryCheck");
        yield return AccessTools.Method(typeof(SkaterAbility), "GetPowerupsInShots");
        yield return AccessTools.Method(typeof(SkaterAbility.__Pass_d__49), "MoveNext");
        yield return AccessTools.Method(typeof(Skaters.SkaterCollision), "IsGuaranteedKnockout");
        yield return AccessTools.Method(typeof(Skaters.SkaterCollision), "HandleHitted");
        yield return AccessTools.Method(typeof(Skaters.Injuries.InjurySystem), "TryInjuryAttempt");
        yield return AccessTools.Method(typeof(Tape2Tape.Team.__c), "_HasRefBlockShotTalent_b__175_0");
        yield return AccessTools.Method(typeof(AI.AIStates.AIState), "ResetToDefaultState");
        yield return AccessTools.Method(typeof(AI.AIStates.AIState), "IsKnightOf");
    }
    [HarmonyPrefix] private static void Prefix(out bool __state) { __state = Active; Active = true; }
    [HarmonyFinalizer] private static void Finalizer(bool __state) => Active = __state;
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
        var forward = __instance.forward;
        // Native OnEnter skips recovery relic queries for teamless actors.
        // The standalone helpers assume both teams exist, including referees.
        if (__instance._isBrawlFinisher || forward?.Team == null || forward.Team.otherTeam == null
            || !CustomRewardCapabilities.Active(forward, CustomRewardEffects.QuickRecovery)) return;
        if (__instance.HasQuickerRecovery() || __instance.HasInstantRecovery()) return;
        __instance._timeKnockout *= .5f;
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
