using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BepInEx.Unity.IL2CPP.Hook;
using CampaignCore;
using Data;
using Il2CppInterop.Runtime.Runtime;
using Rogue;
using Rogue.Powerups.Chaos;
using UnityEngine;
using TalentList = Il2CppSystem.Collections.Generic.List<Rogue.Talent>;

namespace EndlessMode;

// Attribute locks are queried on saved SkaterData as well as live skaters.
// Patching only ForwardStats would let campaign gains bypass the native lock;
// patching only TryGetStatLock misses its inlined setter/preview callers.
// Return a typed value at the shared native query, without editing loadouts.
internal static class CustomRewardDataCapabilities
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate byte HasTalent(IntPtr data, IntPtr method);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr GetTalent(IntPtr data, IntPtr method);
    private static INativeDetour cachedDetour, directDetour, getDetour, listGetDetour;
    private static HasTalent cachedOriginal, directOriginal;
    private static GetTalent getOriginal, listGetOriginal;
    private static readonly HasTalent cachedCallback = Has, directCallback = Direct;
    private static readonly GetTalent getCallback = Get, listGetCallback = ListGet;
    private static readonly Dictionary<IntPtr, string> attributes = new();
    private static readonly Dictionary<IntPtr, string> markers = new();
    private static readonly Dictionary<IntPtr, string> listAttributes = new();
    private static readonly Dictionary<string, Talent> proxies = new();
    private static readonly NativeRewardListIndex<Talent, OwnedTalents> loadouts = new(list => new OwnedTalents(list));
    [ThreadStatic] private static bool resolving;
    [ThreadStatic] private static IntPtr fillingDataCache;

    private sealed class OwnedTalents
    {
        private readonly Dictionary<(string Effect, string Stat), List<(Talent Talent, CustomRewardDefinition Rule)>> effects = new();
        internal OwnedTalents(TalentList talents)
        {
            int count = talents.Count;
            for (int i = 0; i < count; i++) {
                var talent = talents[i];
                if (talent == null || !CustomRewardRuntime.Definitions.TryGetValue(talent.id ?? "", out var rule) || rule.NeedsRedesign
                    || (rule.Effect != CustomRewardEffects.LockAttribute && rule.Effect != CustomRewardEffects.GoldenHelmet)) continue;
                var key = (rule.Effect, rule.Effect == CustomRewardEffects.LockAttribute ? rule.Stat : null);
                if (!effects.TryGetValue(key, out var candidates)) effects[key] = candidates = new();
                candidates.Add((talent, rule));
            }
        }
        internal CustomRewardDefinition First(string effect, string stat)
        {
            if (effects.TryGetValue((effect, stat), out var candidates))
                foreach (var candidate in candidates)
                    if (candidate.Talent != null && !candidate.Talent.PayoffSuppressed) return candidate.Rule;
            return null;
        }
    }

    private static Talent Resolve(IntPtr data, IntPtr method)
    {
        if (data == IntPtr.Zero || resolving) return null;
        string effect = CustomRewardEffects.LockAttribute;
        if (!attributes.TryGetValue(method, out var stat) && !markers.TryGetValue(method, out effect)) return null;
        try {
            resolving = true;
            return CustomRewardRuntime.HasDefinedEffect(effect)
                ? ResolveOwned(Il2CppObjectPool.Get<SkaterData>(data).powerups, effect, stat) : null;
        } catch (Exception ex) { Plugin.Log.LogError("[CustomRewards] Attribute lock query: " + ex); }
        finally { resolving = false; }
        return null;
    }
    private static Talent ResolveOwned(TalentList owned, string effect, string stat)
    {
        // Native loadout order supplies a deterministic value for duplicate
        // custom locks. Suppression can change without mutating the list.
        var rule = owned == null ? null : loadouts.Get(owned).First(effect, stat);
        if (rule == null) return null;
        if (!proxies.TryGetValue(rule.Id, out var proxy)) {
            proxy = effect != CustomRewardEffects.LockAttribute ? ScriptableObject.CreateInstance<GoldenHelmetTalent>() : stat == "Shot Power" ? ScriptableObject.CreateInstance<Railgun>()
                : stat == "Speed" ? ScriptableObject.CreateInstance<TopGear>() : ScriptableObject.CreateInstance<Bedrock>();
            proxy.id = rule.Id; proxy.name = "CCF_DataCapability_" + rule.Id; proxy.level = 1;
            if (effect == CustomRewardEffects.LockAttribute) proxy.Cast<StatLockTalent>().m_LockedValue = rule.Amount;
            proxy.powerupName = rule.Name; proxy.description = rule.FullDescription;
            proxy.hideFlags = HideFlags.DontUnloadUnusedAsset; proxies.Add(rule.Id, proxy);
        }
        return proxy;
    }
    // Attribute getter bodies are inlined into stat setters and previews in
    // 26085704. This static list query keeps their SAME saved-loadout semantics;
    // no live conditions or actor lookup are applied here.
    private static IntPtr ListGet(IntPtr owned, IntPtr method)
    {
        var native = listGetOriginal(owned, method);
        if (native != IntPtr.Zero || owned == IntPtr.Zero || resolving || !listAttributes.TryGetValue(method, out var stat)) return native;
        try {
            resolving = true;
            return CustomRewardRuntime.HasDefinedEffect(CustomRewardEffects.LockAttribute)
                ? ResolveOwned(Il2CppObjectPool.Get<TalentList>(owned), CustomRewardEffects.LockAttribute, stat)?.Pointer ?? IntPtr.Zero : native;
        } catch (Exception ex) { Plugin.Log.LogError("[CustomRewards] Inlined attribute value: " + ex); return native; }
        finally { resolving = false; }
    }
    private static byte Has(IntPtr data, IntPtr method) {
        byte native;
        // HaveTalentCached populates its cache by calling HaveTalent. Never
        // write a temporary custom capability into that native cache.
        IntPtr previous = fillingDataCache;
        fillingDataCache = data;
        CustomRewardCapabilities.NativeQueryDepth++;
        try { native = cachedOriginal(data, method); }
        finally { CustomRewardCapabilities.NativeQueryDepth--; fillingDataCache = previous; }
        native = CustomRewardCapabilities.FromData(native, data, method);
        return native != 0 || Resolve(data, method) == null ? native : (byte)1;
    }
    private static byte Direct(IntPtr data, IntPtr method) {
        byte native = directOriginal(data, method);
        if (data == fillingDataCache) return native;
        native = CustomRewardCapabilities.FromData(native, data, method);
        return native != 0 || Resolve(data, method) == null ? native : (byte)1;
    }
    private static IntPtr Get(IntPtr data, IntPtr method) {
        var native = getOriginal(data, method);
        return native != IntPtr.Zero ? native : Resolve(data, method)?.Pointer ?? IntPtr.Zero;
    }
    internal static void Install()
    {
        if (cachedDetour != null) return;
        try {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || IntPtr.Size != 8) throw new PlatformNotSupportedException("Attribute queries require the audited Windows x64 build.");
            var module = Process.GetCurrentProcess().Modules.Cast<ProcessModule>().First(m => m.ModuleName.Equals("GameAssembly.dll", StringComparison.OrdinalIgnoreCase));
            if (NativeCompatibility.Load(module.FileName).GameHash != "F756BCB8564C2D95C60A121C7A5EBB21DC1B0418421FAF03F4925139944E6142")
                throw new InvalidOperationException("Attribute queries have not been audited for this game build.");
            var cached = module.BaseAddress + 0xe902e0; var get = module.BaseAddress + 0xe90120;
            var direct = module.BaseAddress + 0xe901c0;
            Verify(cached, "48895C241048896C2418565741574883");
            Verify(get, "48895C2408574883EC2048837A380048");
            Verify(direct, "48895C241848896C2420564883EC20");
            var listGet = module.BaseAddress + 0xeb92c0;
            Verify(listGet, "48895C241848896C2420574883EC20");
            RegisterList<Railgun>("Shot Power", listGet); RegisterList<TopGear>("Speed", listGet); RegisterList<Bedrock>("Checking", listGet);
            Register<Railgun>("Shot Power", cached, get); Register<TopGear>("Speed", cached, get); Register<Bedrock>("Checking", cached, get);
            RegisterMarker<GoldenHelmetTalent>(CustomRewardEffects.GoldenHelmet, "HaveTalentCached", cached);
            RegisterMarker<GoldenHelmetTalent>(CustomRewardEffects.GoldenHelmet, "HaveTalent", direct);
            listGetDetour = INativeDetour.CreateAndApply(listGet, listGetCallback, out listGetOriginal);
            getDetour = INativeDetour.CreateAndApply(get, getCallback, out getOriginal);
            cachedDetour = INativeDetour.CreateAndApply(cached, cachedCallback, out cachedOriginal);
            directDetour = INativeDetour.CreateAndApply(direct, directCallback, out directOriginal);
            Plugin.Log.LogInfo("[CustomRewards] Native attribute-lock queries installed.");
        } catch (Exception ex) {
            listGetDetour?.Dispose(); listGetDetour = null; listAttributes.Clear();
            directDetour?.Dispose(); directDetour = null; cachedDetour?.Dispose(); cachedDetour = null; getDetour?.Dispose(); getDetour = null; attributes.Clear(); markers.Clear();
            Plugin.Log.LogError("[CustomRewards] Attribute-lock rules unavailable: " + ex);
        }
    }
    private static void Verify(IntPtr address, string hex) {
        byte[] expected = Convert.FromHexString(hex), actual = new byte[expected.Length]; Marshal.Copy(address, actual, 0, actual.Length);
        if (!actual.SequenceEqual(expected)) throw new InvalidOperationException("A native attribute query was changed by another patch.");
    }
    private static void Register<T>(string stat, IntPtr cached, IntPtr get) {
        foreach (var entry in new[] { ("HaveTalentCached", cached), ("GetTalent", get) }) {
            var storage = typeof(SkaterData).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                .Single(t => t.Name.StartsWith("MethodInfoStoreGeneric_" + entry.Item1 + "_", StringComparison.Ordinal)).MakeGenericType(typeof(T));
            RuntimeHelpers.RunClassConstructor(storage.TypeHandle);
            var method = (IntPtr)storage.GetField("Pointer", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            if (method == IntPtr.Zero || Marshal.ReadIntPtr(method) != entry.Item2) throw new InvalidOperationException("Unexpected native query for " + typeof(T).Name + "." + entry.Item1);
            attributes.Add(method, stat);
        }
    }
    private static void RegisterList<T>(string stat, IntPtr entry)
    {
        var storage = typeof(Talent).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .Single(t => t.Name.StartsWith("MethodInfoStoreGeneric_Get_", StringComparison.Ordinal)).MakeGenericType(typeof(T));
        RuntimeHelpers.RunClassConstructor(storage.TypeHandle);
        var method = (IntPtr)storage.GetField("Pointer", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        if (method == IntPtr.Zero || Marshal.ReadIntPtr(method) != entry) throw new InvalidOperationException("Unexpected native attribute list query for " + typeof(T).Name);
        listAttributes.Add(method, stat);
    }
    private static void RegisterMarker<T>(string effect, string name, IntPtr entry) {
        var storage = typeof(SkaterData).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .Single(t => t.Name.StartsWith("MethodInfoStoreGeneric_" + name + "_", StringComparison.Ordinal)).MakeGenericType(typeof(T));
        RuntimeHelpers.RunClassConstructor(storage.TypeHandle);
        var method = (IntPtr)storage.GetField("Pointer", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        if (method == IntPtr.Zero || Marshal.ReadIntPtr(method) != entry) throw new InvalidOperationException("Unexpected native query for " + typeof(T).Name + "." + name);
        markers.Add(method, effect);
    }
}
