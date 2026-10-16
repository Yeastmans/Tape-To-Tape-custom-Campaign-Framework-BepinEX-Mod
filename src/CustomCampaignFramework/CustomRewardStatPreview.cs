using System;
using System.Collections.Generic;
using System.Reflection;
using Data;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Rogue;

namespace EndlessMode;

// Use the native reward stat-preview contract; gameplay modifiers remain on
// ForwardStats. These neutral templates otherwise report a zero stat change.
[HarmonyPatch]
internal static class CustomRewardStatPreview
{
    private static IEnumerable<MethodBase> TargetMethods() {
        yield return AccessTools.Method(typeof(BasicStatsBonus), "AddStatBonuses");
        yield return AccessTools.Method(typeof(BasicStatsUpgradeRelic), "AddStatBonuses");
    }
    [HarmonyPrefix] private static bool Prefix(Il2CppObjectBase __instance,
        Il2CppSystem.Collections.Generic.IList<StatBonus> __0, SkaterData __1, TeamData __2)
    {
        string id = __instance.TryCast<Talent>()?.id ?? __instance.TryCast<Relic>()?.id;
        if (!CustomRewardRuntime.IsCustom(id)) return true;
        if (__0 == null) return false;
        try {
            foreach (var effect in CustomRewardRuntime.Preview(id, __1, __2)) {
                if (effect.Stat.StartsWith("Goalie ", StringComparison.Ordinal)) {
                    if (__1 != null && __1.TryCast<GoaltenderData>() == null) continue;
                    EGoalieStat goalieStat = effect.Stat switch { "Goalie recovery" => EGoalieStat.Recovery,
                        "Goalie standing speed" => EGoalieStat.Standing, "Goalie butterfly speed" => EGoalieStat.Butterfly,
                        "Goalie depth" => EGoalieStat.Depth, _ => EGoalieStat.Overall };
                    Add(__0, new StatBonus(goalieStat, effect.Amount, effect.Conditional));
                    continue;
                }
                if (__1 != null && __1.TryCast<ForwardData>() == null) continue;
                // Native rating badges cannot represent crit, pass-speed or size.
                // Their exact values remain in the item's full rule description.
                if (effect.Stat != "Overall" && !CampaignCore.CustomRewardDefinition.IsOverallStat(effect.Stat)) continue;
                EForwardStat stat = effect.Stat switch { "Speed" => EForwardStat.Speed, "Accuracy" => EForwardStat.Accuracy,
                    "Checking" => EForwardStat.Checking, "Shot Power" => EForwardStat.ShotPower, _ => EForwardStat.Overall };
                Add(__0, new StatBonus(stat, effect.Amount, effect.Conditional));
            }
        } catch (Exception ex) { Plugin.Log.LogWarning("[CustomRewards] Stat preview: " + ex.Message); }
        return false;
    }
    private static unsafe void Add(Il2CppSystem.Collections.Generic.IList<StatBonus> target, StatBonus bonus)
    {
        // StatBonus is a blittable generated struct. Pass its value payload;
        // unlike EventChoice it does not contain managed object references.
        IntPtr klass = IL2CPP.il2cpp_object_get_class(target.Pointer);
        IntPtr method = IL2CPP.il2cpp_class_get_method_from_name(klass, "Add", 1);
        if (method == IntPtr.Zero) throw new MissingMethodException("Stat preview collection.Add");
        void** arguments = stackalloc void*[1]; arguments[0] = &bonus;
        IntPtr exception = IntPtr.Zero;
        IL2CPP.il2cpp_runtime_invoke(method, target.Pointer, arguments, ref exception);
        Il2CppException.RaiseExceptionIfNecessary(exception);
        GC.KeepAlive(bonus); GC.KeepAlive(target);
    }
}
