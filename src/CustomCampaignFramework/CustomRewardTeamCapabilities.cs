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
using Rogue;
using Tape2Tape;

namespace EndlessMode;

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
    private static byte Live(IntPtr team, IntPtr method) {
        byte native = liveOriginal(team, method);
        if (team != IntPtr.Zero && method == liveBenchMethod && !resolving && !Plugin.IsDefaultMode) return BenchReward(new Team(team)._teamData, native);
        if (team != IntPtr.Zero && method == yipsMethod && !resolving && !Plugin.IsDefaultMode) {
            var data = new Team(team)._teamData;
            if (data?.relics != null) {
                bool real = data.relics.HasMatch(r => r != null && r.TryCast<Rogue.Relics.ChaosRelics.YipsRelic>() != null && !CustomRewardRuntime.IsCustom(r.id));
                return real || Owns(data, CustomRewardEffects.BreakawayShotPenalty) ? (byte)1 : (byte)0;
            }
        }
        if (native != 0 || team == IntPtr.Zero || !CustomRewardRuntime.InMatch || resolving || !liveEffects.TryGetValue(method, out var effect)) return native;
        return Owns(new Team(team)._teamData, effect) ? (byte)1 : native;
    }
    private static byte Data(IntPtr team, IntPtr method) {
        byte native = dataOriginal(team, method);
        if (team != IntPtr.Zero && method == dataBenchMethod && !resolving && !Plugin.IsDefaultMode) return BenchReward(new TeamData(team), native);
        if (team != IntPtr.Zero && method == trainingMethod && !resolving) CustomRewardTrainingGrowth.Query(new TeamData(team));
        if (native != 0 || method != dataMethod || team == IntPtr.Zero || resolving) return native;
        return Owns(new TeamData(team), CustomRewardEffects.ConvertSpeedTraining) ? (byte)1 : native;
    }
    private static byte DataDirect(IntPtr team, IntPtr method)
    {
        byte native = dataDirectOriginal(team, method);
        if (native != 0 || (method != helmetMethod && method != attributeRewardMethod) || team == IntPtr.Zero || resolving) return native;
        return Owns(new TeamData(team), method == helmetMethod ? CustomRewardEffects.SecondScorerHelmet : CustomRewardEffects.AttributeRewardChoice) ? (byte)1 : native;
    }
    private static byte BenchReward(TeamData team, byte native)
    {
        if (Owns(team, CustomRewardEffects.BenchTalentSlot)) return 1;
        if (native == 0 || team?.relics == null) return native;
        // The custom capacity carrier uses the native OnPickup increment only.
        // Preserve every real native Comfy Cushion; its reward is still active.
        foreach (var relic in team.relics)
            if (relic != null && relic.TryCast<RandomTalentBenchBonus>() != null
                && !CustomRewardLifecycle.Rule(relic.id, CustomRewardEffects.BenchCapacity, out _)) return native;
        return 0;
    }
    private static bool Owns(TeamData team, string effect)
    {
        try {
            resolving = true; CustomRewardRuntime.EnsureAssets();
            var relics = team?.relics;
            for (int i = 0; relics != null && i < relics.Count; i++) {
                var relic = relics[i];
                if (relic != null && CustomRewardRuntime.Definitions.TryGetValue(relic.id, out var d)
                    && d.IsRelic && !d.NeedsRedesign && d.Effect == effect) return true;
            }
            return false;
        } catch (Exception ex) { Plugin.Log.LogError("[CustomRewards] Native team conversion query: " + ex); return false; }
        finally { resolving = false; }
    }
    internal static void Install()
    {
        if (liveDetour != null) return;
        try {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || IntPtr.Size != 8) throw new PlatformNotSupportedException("Team queries require the audited Windows x64 build.");
            var module = Process.GetCurrentProcess().Modules.Cast<ProcessModule>().First(m => m.ModuleName.Equals("GameAssembly.dll", StringComparison.OrdinalIgnoreCase));
            if (NativeCompatibility.Load(module.FileName).GameHash != "3E8BF7879BF160C0DBCDDE2E12069E1E12689D3D8B327BFAE165DCE607FE294E")
                throw new InvalidOperationException("Team queries have not been audited for this game build.");
            var direct = module.BaseAddress + 0xC82320;
            Verify(direct, "48895C24104889742418574883EC20");
            var live = module.BaseAddress + 0xC826C0; var data = module.BaseAddress + 0xC82200;
            Verify(live, "40555741564883EC2048837A3800488B");
            Verify(data, "48895C240848896C2410488974241857");
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
