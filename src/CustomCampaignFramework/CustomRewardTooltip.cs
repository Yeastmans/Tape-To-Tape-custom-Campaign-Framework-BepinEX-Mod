using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BishopGames.UI;
using HarmonyLib;
using Rogue;
using TMPro;

namespace EndlessMode;

// Native tooltip instances are reused for native rewards. Restore their exact
// text settings on the next ordinary item instead of changing the shared prefab.
internal static class CustomRewardTooltip
{
    private sealed class Settings {
        internal TMP_Text Text;
        internal bool Auto;
        internal float Minimum, Maximum;
        internal int Characters, Lines;
        internal TextOverflowModes Overflow;
    }
    private static readonly Dictionary<int, Settings> changed = new();
    internal static void Fit(TMP_Text text, bool custom)
    {
        if (text == null) return;
        int key = text.GetInstanceID();
        if (!custom) {
            if (!changed.TryGetValue(key, out var prior)) return;
            text.enableAutoSizing = prior.Auto; text.fontSizeMin = prior.Minimum; text.fontSizeMax = prior.Maximum;
            text.maxVisibleCharacters = prior.Characters; text.maxVisibleLines = prior.Lines; text.overflowMode = prior.Overflow;
            changed.Remove(key); return;
        }
        foreach (var old in changed.Where(p => p.Value.Text == null).Select(p => p.Key).ToArray()) changed.Remove(old);
        if (!changed.ContainsKey(key)) changed[key] = new Settings { Text = text, Auto = text.enableAutoSizing, Minimum = text.fontSizeMin,
            Maximum = text.fontSizeMax, Characters = text.maxVisibleCharacters, Lines = text.maxVisibleLines, Overflow = text.overflowMode };
        text.fontSizeMax = Math.Max(text.fontSize, changed[key].Maximum);
        text.fontSizeMin = Math.Min(12, text.fontSizeMax); text.enableAutoSizing = true;
        text.maxVisibleCharacters = int.MaxValue; text.maxVisibleLines = int.MaxValue; text.overflowMode = TextOverflowModes.Overflow;
        text.SetAllDirty();
    }
}
[HarmonyPatch(typeof(StaticTooltipOnHighlight), "SetDescription")]
internal static class CustomRewardStaticTooltip
{
    [HarmonyPostfix] private static void Postfix(StaticTooltipOnHighlight __instance, string __0) {
        bool custom = CustomRewardRuntime.Definitions.Values.Any(d => __0 == d.FullDescription);
        CustomRewardTooltip.Fit(__instance.m_Description, custom);
    }
}
[HarmonyPatch]
internal static class CustomRewardCardTooltip
{
    private static IEnumerable<MethodBase> TargetMethods() => typeof(TalentTooltip).GetMethods().Where(m => m.Name == "Initialize");
    [HarmonyPostfix] private static void Postfix(TalentTooltip __instance, UnityEngine.Object __0) {
        string id = __0?.TryCast<PowerUp>()?.id ?? __0?.TryCast<Relic>()?.id;
        CustomRewardTooltip.Fit(__instance._descriptionText, CustomRewardRuntime.IsCustom(id));
    }
}
