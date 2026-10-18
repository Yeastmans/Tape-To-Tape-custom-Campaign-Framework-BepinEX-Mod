using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using BepInEx.Unity.IL2CPP.Hook;
using CampaignCore;
using NHL94;
using Skaters;

namespace EndlessMode;

internal static class CustomRewardGoalRules
{
    // The generated interop constructor allocates a new object. Detour the
    // audited native void constructor instead, keeping the caller's instance.
    // The goal is classified before ANY GoalScored subscriber sees it.
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Construct(IntPtr instance, IntPtr scorer, int goalType, float elapsed,
        int mode, int shotType, byte critical, byte enraged, byte gameEnding, IntPtr method);
    private static INativeDetour detour;
    private static Construct original;
    private static readonly Construct callback = BeforeDispatch;
    internal static void Install()
    {
        if (detour != null) return;
        try {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || IntPtr.Size != 8)
                throw new PlatformNotSupportedException("Goal verification requires the audited Windows x64 build.");
            var module = Process.GetCurrentProcess().Modules.Cast<ProcessModule>().First(m => m.ModuleName.Equals("GameAssembly.dll", StringComparison.OrdinalIgnoreCase));
            if (NativeCompatibility.Load(module.FileName).GameHash != "3E8BF7879BF160C0DBCDDE2E12069E1E12689D3D8B327BFAE165DCE607FE294E")
                throw new InvalidOperationException("Goal verification has not been audited for this game build.");
            var address = module.BaseAddress + 0x3607960;
            byte[] expected = Convert.FromHexString("48895C24084889742410574883EC30"), actual = new byte[15];
            Marshal.Copy(address, actual, 0, actual.Length);
            if (!actual.SequenceEqual(expected)) throw new InvalidOperationException("The native goal constructor was changed by another patch.");
            detour = INativeDetour.CreateAndApply(address, callback, out original);
            Plugin.Log.LogInfo("[CustomRewards] Goal verification installed before native event dispatch.");
        } catch (Exception ex) { Plugin.Log.LogError("[CustomRewards] Goal denial unavailable: " + ex); }
    }
    private static void BeforeDispatch(IntPtr instance, IntPtr scorer, int goalType, float elapsed,
        int mode, int shotType, byte critical, byte enraged, byte gameEnding, IntPtr method)
    {
        int selected = goalType; byte ending = gameEnding;
        try {
            if (mode == (int)GameMode.Normal && goalType == (int)GoalType.GOOD && scorer != IntPtr.Zero
                && ShouldDeny(new Skater(scorer))) { selected = (int)GoalType.CHEAT_NO_GOAL; ending = 0; }
        } catch (Exception ex) { Plugin.Log.LogError("[CustomRewards] Goal verification: " + ex); }
        original(instance, scorer, selected, elapsed, mode, shotType, critical, enraged, ending, method);
    }
    internal static bool ShouldDeny(Skater scorer)
    {
        if (!CustomRewardRuntime.InMatch || scorer?.Team?.otherTeam == null) return false;
        foreach (var rule in CustomRewardRuntime.Definitions.Values) {
            if (rule.NeedsRedesign || rule.Trigger != "Goal verification" || rule.Effect != CustomRewardEffects.DenyGoal) continue;
            bool Owned(Skater owner) => owner != null && owner.ArePowerUpsEnabled()
                && CustomRewardRuntime.Has(owner._skaterData, rule.Id)
                && owner._skaterData.powerups.HasMatch(t => t != null && t.id == rule.Id && !t.PayoffSuppressed);
            bool applies = rule.IsRelic ? CustomRewardRuntime.Has(scorer.Team.otherTeam._teamData, rule.Id)
                : rule.Target == CustomRewardEffects.Actor ? Owned(scorer)
                : scorer.Team.otherTeam.PlayingForwards.HasMatch(f => f != null && !f.IsBenched() && f.CurrentMovementState?.TryCast<Movements.MovementStates.KnockoutState>() == null && Owned(f));
            if (applies && UnityEngine.Random.Range(0, 100) < rule.Amount) {
                Skater owner = scorer;
                if (rule.Target == CustomRewardEffects.OpposingTeam) {
                    owner = scorer.Team.otherTeam.goaltender;
                    if (!rule.IsRelic) foreach (var player in scorer.Team.otherTeam.PlayingForwards)
                        if (Owned(player)) { owner = player; break; }
                }
                if (owner != null) CustomRewardRuntime.Safe(() => CustomRewardRuntime.Notify(rule, owner));
                return true;
            }
        }
        return false;
    }
}
