using System;
using CampaignCore;
using Data;
using HarmonyLib;
using Spine;

namespace EndlessMode;

// Resolve only configured heads. Never enumerate Skin.Attachments: its boxed
// IL2CPP value types are unsafe in this game. Named attachment lookup is enough.
internal static class HeadAppearanceRuntime
{
    internal static void Install(Harmony harmony) => harmony.Patch(
        AccessTools.Method(typeof(Skaters.SkinHelper), "GetSkins"),
        prefix: new HarmonyMethod(typeof(HeadAppearanceRuntime), nameof(BeforeSkins)),
        postfix: new HarmonyMethod(typeof(HeadAppearanceRuntime), nameof(AfterSkins)));

    private static void BeforeSkins(bool isHome, bool headOnly, ForwardData forwardData)
    {
        if (!headOnly) CampaignAppearanceRuntime.ForKit(forwardData, isHome);
    }

    internal static void Configure(ForwardData data)
    {
        // Native glasses visibility tests for a nonempty skin string. The
        // authoring sentinel must not enable the default sunglasses slot.
        if (string.Equals(data.glassesSkin, "none", StringComparison.OrdinalIgnoreCase)) data.glassesSkin = "";
        string head = HeadAppearance.Base(data.headSkin);
        if (string.IsNullOrEmpty(head)) return;
        if (string.Equals(data.helmetSkin, "none", StringComparison.OrdinalIgnoreCase)) data.helmetSkin = "__NO_HELMET__";
        if (string.Equals(data.helmetAwaySkin, "none", StringComparison.OrdinalIgnoreCase)) data.helmetAwaySkin = "__NO_HELMET__";
        if (HeadAppearance.HasNoHelmetVariant(head))
            data.helmetSkin = data.helmetAwaySkin = "__NO_HELMET__";
        // Current native SkinHelper selects the fitted head from live equipment.
        // Storing the fitted head here leaves it fitted after a helmet is lost.
        data.headSkin = head;
    }

    private static void AfterSkins(SkeletonData skeletonData, Skaters.SkaterType skType, bool isHome, bool headOnly, ForwardData forwardData,
        Skaters.IEquipmentOwner equipmentOwner,
        Il2CppSystem.Collections.Generic.List<Skin> __result)
    {
        if (forwardData == null || skeletonData == null || __result == null) return;
        if (skType == Skaters.SkaterType.Referee)
        {
            CampaignRefereeAppearance.Apply(skeletonData, forwardData, __result);
            return; // Preserve the referee decal/logo exclusion, including head-only renders.
        }
        if (!headOnly) AppearanceDecalRuntime.Forward(skeletonData, forwardData, __result, isHome);
        // Native GetSkins constructs a ForwardDataEquipmentOwner when callers
        // omit one. Preserve its complete head/helmet result in previews too:
        // physical talents, transformations and helmet stacks are native state.
    }
}
