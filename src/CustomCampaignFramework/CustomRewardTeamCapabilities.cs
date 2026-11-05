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
using Tape2Tape;
using RelicList = Il2CppSystem.Collections.Generic.List<Rogue.Relic>;

namespace EndlessMode;

// Generated field getters use the interop object pool. A fresh wrapper simply
// misses this weak cache. Replacing a native list, mutating even a single slot,
// or publishing new reward definitions all invalidate the corresponding index.
internal sealed class NativeRewardListIndex<T, TIndex> where TIndex : class
{
    private sealed class Entry
    {
        internal int Version;
        internal long Generation;
        internal TIndex Index;
    }
    private readonly ConditionalWeakTable<Il2CppSystem.Collections.Generic.List<T>, Entry> entries = new();
    private readonly Func<Il2CppSystem.Collections.Generic.List<T>, TIndex> build;
    internal NativeRewardListIndex(Func<Il2CppSystem.Collections.Generic.List<T>, TIndex> build) => this.build = build;
    internal TIndex Get(Il2CppSystem.Collections.Generic.List<T> list)
    {
        var entry = entries.GetOrCreateValue(list);
        int version = list._version;
        long generation = CustomRewardRuntime.DefinitionQueryGeneration;
        if (entry.Index == null || entry.Version != version || entry.Generation != generation) {
            entry.Index = build(list);
            entry.Version = version;
            entry.Generation = generation;
        }
        return entry.Index;
    }
}

// The live and saved-team queries are deliberately separate. Quantum Sextant
// consults the former for live bonuses and the latter only in training code.
internal static class CustomRewardTeamCapabilities
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate byte HasRelic(IntPtr team, IntPtr method);
    private static INativeDetour liveDetour, dataDetour, dataDirectDetour;
    private static HasRelic liveOriginal, dataOriginal, dataDirectOriginal;
    private static readonly HasRelic liveCallback = Live, dataCallback = Data, dataDirectCallback = DataDirect;
    private static readonly Dictionary<IntPtr, string> liveEffects = new();
    private static IntPtr dataMethod, helmetMethod, trainingMethod, attributeRewardMethod, liveBenchMethod, dataBenchMethod, yipsMethod;
    [ThreadStatic] private static bool resolving;
    private static readonly NativeRewardListIndex<Relic, OwnedRelics> loadouts = new(list => new OwnedRelics(list));
    private sealed class OwnedRelics
    {
        private readonly Dictionary<(string Effect, bool Opposing), List<Relic>> effects = new();
        internal readonly List<Relic> NativeCushions = new(), NativeYips = new();
        internal OwnedRelics(RelicList relics)
        {
            int count = relics.Count;
            for (int i = 0; i < count; i++) {
                var relic = relics[i];
                if (relic == null) continue;
                string id = relic.id;
                bool custom = CustomRewardRuntime.Definitions.TryGetValue(id ?? "", out var rule);
                if (custom && rule.IsRelic && !rule.NeedsRedesign) {
                    var key = (rule.Effect, CustomRewardEffects.IsTeamLevel(rule.Effect) && rule.Target == CustomRewardEffects.OpposingTeam);
                    if (!effects.TryGetValue(key, out var candidates)) effects[key] = candidates = new();
                    candidates.Add(relic);
                }
                if (!custom && relic.TryCast<Rogue.Relics.ChaosRelics.YipsRelic>() != null) NativeYips.Add(relic);
                if ((!custom || rule.NeedsRedesign || rule.Effect != CustomRewardEffects.BenchCapacity)
                    && relic.TryCast<RandomTalentBenchBonus>() != null) NativeCushions.Add(relic);
            }
        }
        internal bool Owns(string effect, bool opposing) => effects.TryGetValue((effect, opposing && CustomRewardEffects.IsTeamLevel(effect)), out var candidates) && AnyLive(candidates);
        // A Unity asset can be destroyed while its slot remains in the list.
        internal static bool AnyLive(List<Relic> candidates)
        {
            foreach (var relic in candidates) if (relic != null) return true;
            return false;
        }
    }
    private static byte Live(IntPtr team, IntPtr method)
    {
        byte native = liveOriginal(team, method);
        if (team == IntPtr.Zero || resolving) return native;
        try {
            resolving = true;
            if (method == liveBenchMethod && !Plugin.IsDefaultMode) return BenchReward(team, native, live: true);
            if (method == yipsMethod && !Plugin.IsDefaultMode) {
                bool penalty = CustomRewardRuntime.HasDefinedEffect(CustomRewardEffects.BreakawayShotPenalty);
                var relics = Il2CppObjectPool.Get<Team>(team)._teamData?.relics;
                if (relics == null) return native;
                var index = loadouts.Get(relics);
                return OwnedRelics.AnyLive(index.NativeYips) || penalty && index.Owns(CustomRewardEffects.BreakawayShotPenalty, false) ? (byte)1 : (byte)0;
            }
            if (native != 0 || !CustomRewardRuntime.InMatch || !liveEffects.TryGetValue(method, out var effect)
                || !CustomRewardRuntime.HasDefinedEffect(effect)) return native;
            var live = Il2CppObjectPool.Get<Team>(team);
            // Read the current teams' actual loadouts, including a rotated opponent.
            return Owns(live._teamData, effect) || Owns(live.otherTeam?._teamData, effect, opposing: true) ? (byte)1 : native;
        } catch (Exception ex) { Plugin.Log.LogError("[CustomRewards] Native live team query: " + ex); return native; }
        finally { resolving = false; }
    }
    private static byte Data(IntPtr team, IntPtr method)
    {
        byte native = dataOriginal(team, method);
        if (team == IntPtr.Zero || resolving) return native;
        if (method == trainingMethod && CustomRewardTrainingGrowth.Current is { Used: false } && !CustomRewardTrainingGrowth.Preview)
            CustomRewardTrainingGrowth.Query(Il2CppObjectPool.Get<TeamData>(team));
        try {
            resolving = true;
            if (method == dataBenchMethod && !Plugin.IsDefaultMode) return BenchReward(team, native, live: false);
            if (native != 0 || method != dataMethod || !CustomRewardRuntime.HasDefinedEffect(CustomRewardEffects.ConvertSpeedTraining)) return native;
            return Owns(Il2CppObjectPool.Get<TeamData>(team), CustomRewardEffects.ConvertSpeedTraining) ? (byte)1 : native;
        } catch (Exception ex) { Plugin.Log.LogError("[CustomRewards] Native saved team query: " + ex); return native; }
        finally { resolving = false; }
    }
    private static byte DataDirect(IntPtr team, IntPtr method)
    {
        byte native = dataDirectOriginal(team, method);
        if (native != 0 || (method != helmetMethod && method != attributeRewardMethod) || team == IntPtr.Zero || resolving) return native;
        try {
            resolving = true;
            string effect = method == helmetMethod ? CustomRewardEffects.SecondScorerHelmet : CustomRewardEffects.AttributeRewardChoice;
            return CustomRewardRuntime.HasDefinedEffect(effect) && Owns(Il2CppObjectPool.Get<TeamData>(team), effect) ? (byte)1 : native;
        } catch (Exception ex) { Plugin.Log.LogError("[CustomRewards] Native saved team query: " + ex); return native; }
        finally { resolving = false; }
    }
    private static byte BenchReward(IntPtr team, byte native, bool live)
    {
        bool grant = CustomRewardRuntime.HasDefinedEffect(CustomRewardEffects.BenchTalentSlot);
        if (!grant && native == 0) return native;
        var data = live ? Il2CppObjectPool.Get<Team>(team)._teamData : Il2CppObjectPool.Get<TeamData>(team);
        var relics = data?.relics;
        if (relics == null) return native;
        var index = loadouts.Get(relics);
        if (grant && index.Owns(CustomRewardEffects.BenchTalentSlot, false)) return 1;
        // The capacity-only carrier uses OnPickup. A genuine Comfy Cushion keeps its native reward.
        return native != 0 && OwnedRelics.AnyLive(index.NativeCushions) ? native : (byte)0;
    }
    private static bool Owns(TeamData team, string effect, bool opposing = false)
    {
        var relics = team?.relics;
        return relics != null && loadouts.Get(relics).Owns(effect, opposing);
    }
    internal static void Install()
    {
        if (liveDetour != null) return;
        try {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || IntPtr.Size != 8) throw new PlatformNotSupportedException("Team queries require the audited Windows x64 build.");
            var module = Process.GetCurrentProcess().Modules.Cast<ProcessModule>().First(m => m.ModuleName.Equals("GameAssembly.dll", StringComparison.OrdinalIgnoreCase));
            if (NativeCompatibility.Load(module.FileName).GameHash != "F756BCB8564C2D95C60A121C7A5EBB21DC1B0418421FAF03F4925139944E6142")
                throw new InvalidOperationException("Team queries have not been audited for this game build.");
            var direct = module.BaseAddress + 0xed8de0;
            Verify(direct, "48895C241848896C2420564883EC20");
            var live = module.BaseAddress + 0xed8b10; var data = module.BaseAddress + 0xed8f00;
            Verify(live, "48895C2418554883EC2048837A380048");
            Verify(data, "48895C241048896C2418565741574883");
            liveEffects.Add(Method(typeof(Team), typeof(SorestLoser), live), CustomRewardEffects.CelebrationInjuries);
            liveEffects.Add(Method(typeof(Team), typeof(Needle), live), CustomRewardEffects.SpeedOneTimerAccuracy);
            liveEffects.Add(Method(typeof(Team), typeof(QuantumSextant), live), CustomRewardEffects.ConvertSpeedBonuses);
            liveEffects.Add(Method(typeof(Team), typeof(LastStand), live), CustomRewardEffects.PossessionOvertime);
            liveEffects.Add(Method(typeof(Team), typeof(TieDomi), live), CustomRewardEffects.WinByTwo);
            liveEffects.Add(Method(typeof(Team), typeof(RiggedFaceoff), live), CustomRewardEffects.RiggedFaceoffs);
            yipsMethod = Method(typeof(Team), typeof(Rogue.Relics.ChaosRelics.YipsRelic), live);
            liveBenchMethod = Method(typeof(Team), typeof(RandomTalentBenchBonus), live);
            dataBenchMethod = Method(typeof(TeamData), typeof(RandomTalentBenchBonus), data);
            dataMethod = Method(typeof(TeamData), typeof(QuantumSextant), data);
            trainingMethod = Method(typeof(TeamData), typeof(Rogue.Relics.TrainingWhistle), data);
            attributeRewardMethod = Method(typeof(TeamData), typeof(StatRewardRelic), direct, "HaveRelic");
            helmetMethod = Method(typeof(TeamData), typeof(GoldenHelmetSecondScorerRelic), direct, "HaveRelic");
            liveDetour = INativeDetour.CreateAndApply(live, liveCallback, out liveOriginal);
            dataDetour = INativeDetour.CreateAndApply(data, dataCallback, out dataOriginal);
            dataDirectDetour = INativeDetour.CreateAndApply(direct, dataDirectCallback, out dataDirectOriginal);
            Plugin.Log.LogInfo("[CustomRewards] Native live and training conversion queries installed.");
        } catch (Exception ex) {
            liveDetour?.Dispose(); liveDetour = null; dataDetour?.Dispose(); dataDetour = null; dataDirectDetour?.Dispose(); dataDirectDetour = null; liveEffects.Clear(); dataMethod = helmetMethod = trainingMethod = attributeRewardMethod = liveBenchMethod = dataBenchMethod = yipsMethod = IntPtr.Zero;
            Plugin.Log.LogError("[CustomRewards] Team conversion rules unavailable: " + ex);
        }
    }
    private static IntPtr Method(Type owner, Type relic, IntPtr expected, string query = "HaveRelicCached")
    {
        var storage = owner.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .Single(t => t.Name.StartsWith("MethodInfoStoreGeneric_" + query + "_", StringComparison.Ordinal)).MakeGenericType(relic);
        RuntimeHelpers.RunClassConstructor(storage.TypeHandle);
        var method = (IntPtr)storage.GetField("Pointer", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetValue(null);
        if (method == IntPtr.Zero || Marshal.ReadIntPtr(method) != expected) throw new InvalidOperationException("Unexpected native team query for " + owner.Name);
        return method;
    }
    private static void Verify(IntPtr address, string hex)
    {
        byte[] expected = Convert.FromHexString(hex), actual = new byte[expected.Length]; Marshal.Copy(address, actual, 0, actual.Length);
        if (!actual.SequenceEqual(expected)) throw new InvalidOperationException("A native team query was changed by another patch.");
    }
}
