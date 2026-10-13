using System;
using System.Globalization;
using Data;
using HarmonyLib;
using Spine.Unity;
using Tape2Tape.Customization;
using UnityEngine;

namespace EndlessMode;

// PRIVATE PROPOSAL ONLY. Not part of the production project or installed mod.
// The current native goalie branch passes decimal text even for Roman/Prison.
// Keep that branch, names, textures, caches and material calls intact; correct
// only its unchanged decimal string during the exact authored goalie call.
internal static class GoalieNumberDisplay
{
    internal sealed class Scope
    {
        internal Scope Previous;
        internal GoaltenderData Goalie;
        internal IntPtr Skeleton;
        internal int Number;
        internal NumberFormat Format;
        internal string Decimal;
    }

    [ThreadStatic] private static Scope active;
    private static int warnings;

    internal static Scope Enter(SkaterData data, SkeletonAnimation skeleton)
    {
        // Every nested call masks the parent, including native forwards and
        // unbound goalies. A direct/nested controller call cannot borrow its scope.
        var scope = new Scope { Previous = active };
        active = scope;
        try
        {
            if (Plugin.IsDefaultMode || data == null || skeleton == null) return scope;
            var goalie = data.TryCast<GoaltenderData>();
            if (goalie == null || CampaignAppearanceRuntime.Read(goalie) == null) return scope;
            var format = goalie.numberFormat;
            if (format is not (NumberFormat.RomanNumerals or NumberFormat.Prison)) return scope;
            // Existing authoring/export validation permits numeric fields0..999.
            // Invalid or third-party out-of-contract numbers retain native behavior.
            int number = goalie.number;
            if (number < 0 || number > 999) return scope;
            scope.Goalie = goalie;
            scope.Skeleton = skeleton.Pointer;
            scope.Number = number;
            scope.Format = format;
            scope.Decimal = number.ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception ex) { scope.Goalie = null; Report(ex); }
        return scope;
    }

    internal static void Exit(Scope scope)
    {
        if (scope != null && ReferenceEquals(active, scope)) active = scope.Previous;
    }

    internal static void Format(ref string backNumber, SkeletonAnimation skeleton, NumberFormat format)
    {
        var scope = active;
        try
        {
            if (Plugin.IsDefaultMode || scope?.Goalie == null || skeleton == null
                || skeleton.Pointer != scope.Skeleton || format != scope.Format
                || scope.Goalie.number != scope.Number || scope.Goalie.numberFormat != scope.Format
                || !string.Equals(backNumber, scope.Decimal, StringComparison.Ordinal)) return;
            // Preserve text already rewritten by another prefix, including an
            // already-padded value. The renderer's other arguments are untouched.
            string replacement = format == NumberFormat.Prison ? backNumber.PadLeft(5, '0')
                : CustomNumberApplier.ToRomanNumeral(scope.Number);
            if (replacement != null) backNumber = replacement;
        }
        catch (Exception ex) { Report(ex); }
    }

    private static void Report(Exception ex)
    {
        if (warnings++ < 4) Plugin.Log.LogWarning("[GoalieNumber] Kept native text: " + ex.Message);
    }
}

[HarmonyPatch(typeof(CustomNumberApplier), "ApplyNumber", new Type[] {
    typeof(SkaterData), typeof(SkeletonAnimation), typeof(bool), typeof(bool), typeof(Texture) })]
internal static class GoalieNumberContextPatch
{
    [HarmonyPrefix, HarmonyPriority(Priority.Last)]
    internal static void Prefix(SkaterData __0, SkeletonAnimation __1, out GoalieNumberDisplay.Scope __state)
        => __state = GoalieNumberDisplay.Enter(__0, __1);

    [HarmonyFinalizer]
    internal static Exception Finalizer(Exception __exception, GoalieNumberDisplay.Scope __state)
    {
        GoalieNumberDisplay.Exit(__state);
        return __exception;
    }
}

[HarmonyPatch(typeof(CustomNumberController), "SetCustomNumberGoalie", new Type[] {
    typeof(string), typeof(string), typeof(SkeletonAnimation), typeof(NumberFormat), typeof(Texture) })]
internal static class GoalieNumberControllerPatch
{
    [HarmonyPrefix, HarmonyPriority(Priority.Last)]
    internal static void Prefix(ref string __0, SkeletonAnimation __2, NumberFormat __3)
        => GoalieNumberDisplay.Format(ref __0, __2, __3);
}
