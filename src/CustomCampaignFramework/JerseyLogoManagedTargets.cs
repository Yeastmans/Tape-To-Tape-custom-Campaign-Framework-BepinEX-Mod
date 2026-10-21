using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CampaignCore;

namespace EndlessMode;

// Single named manifest consumed by preflight, installation, tests and final audit.
internal static class JerseyLogoManagedTargets
{
    private const string Extensions = "Skaters.T2TSkeletonExtensions";
    internal static readonly NamedManagedHookContract[] Required = {
        Target("forward-composite", "Skaters.SkinHelper", "GetSkinForForward", true, "Spine.Skin", new[] { "Spine.SkeletonData", "Data.ForwardData", "System.Boolean", "System.Boolean", "System.Boolean", "System.Boolean" }, postfix: "ForwardComposite"),
        Target("goalie-composite", "Skaters.SkinHelper", "GetSkinForGoalie", true, "Spine.Skin", new[] { "Spine.SkeletonData", "Data.GoaltenderData", "System.Boolean" }, postfix: "GoalieComposite"),
        Target("world-logo", Extensions, "SetLogo", true, "System.Void", new[] { "Spine.Unity.SkeletonRenderer", "UnityEngine.Texture2D", "System.Boolean" }, "WorldLogo", "WorldLogoDone"),
        Target("graphic-logo", Extensions, "SetLogo", true, "System.Void", new[] { "Spine.Unity.SkeletonGraphic", "UnityEngine.Texture", "System.Boolean" }, "GraphicLogo", "GraphicLogoDone"),
        Target("graphic-material", Extensions, "OverrideSlotWithCustomizationMaterial", true, "System.Void", new[] { "Spine.Unity.SkeletonGraphic", "Spine.Skin", "Data.TeamColorsData", "System.String", "Il2CppSystem.Collections.Generic.Dictionary<UnityEngine.Texture,UnityEngine.Material>" }, postfix: "GraphicMaterialPostfix"),
        Target("slots", Extensions, "SetupSkeletonSlots", true, "System.Void", new[] { "Spine.Skeleton", "Skaters.T2TSkeletonExtensions+CustomizationFlags", "System.Boolean" }, postfix: "SlotsPostfix"),
        Target("forward-logo", "Skaters.Forward", "InitializeLogo", false, "System.Void", new[] { "Data.TeamData" }, "ForwardLogoScope", finalizer: "OwnerLogoFinalizer"),
        Target("goalie-logo", "Goaltender.Goaltender", "InitializeLogo", false, "System.Void", new[] { "Data.TeamData" }, "GoalieLogoScope", finalizer: "OwnerLogoFinalizer"),
        Target("world-material", Extensions, "SetupSkeletonSlotsMaterials", true, "System.Void", new[] { "System.Boolean", "Spine.Unity.SkeletonRenderer", "UnityEngine.Material", "Skaters.T2TSkeletonExtensions+CustomizationFlags" }, postfix: "MaterialsPostfix"),
        Target("selected-player-skeleton", "SelectedPlayerUI", "UpdatePlayerSkeleton", false, "System.Void", new[] { "Data.ForwardData", "Data.TeamData", "System.Boolean" }, "SelectedPlayerScope", finalizer: "SelectedPlayerFinalizer"),
        Target("selected-player-logo", "SelectedPlayerUI", "InitializeLogo", false, "System.Void", new[] { "Data.ForwardData", "Data.TeamData" }, "SelectedPlayerLogoScope", finalizer: "OwnerLogoFinalizer")
    };
    private static NamedManagedHookContract Target(string id, string type, string method, bool isStatic, string result, string[] args, string prefix = "", string postfix = "", string finalizer = "")
        => new() { Feature = "jersey-logo", Id = id, Assembly = "MainScriptsAssembly", DeclaringType = type, Method = method,
            IsStatic = isStatic, ReturnType = result, Parameters = args, Prefix = prefix, Postfix = postfix, Finalizer = finalizer };

    internal static void ValidateBeforeInstall(IEnumerable<RawNativeHookContract> expectedRaw, IEnumerable<RawNativeHookContract> approvedRaw, IEnumerable<NamedManagedHookContract> approvedNamed)
    {
        RawNativeHookContract.RequireExactSet(expectedRaw, approvedRaw, "jersey-logo");
        NamedManagedHookContract.RequireExactSet(Required, approvedNamed, "jersey-logo");
    }
    internal static string TypeName(Type type) => type.IsGenericType
        ? type.GetGenericTypeDefinition().FullName.Split('`')[0] + "<" + string.Join(",", type.GetGenericArguments().Select(TypeName)) + ">"
        : type.FullName;
    internal static MethodInfo Resolve(NamedManagedHookContract contract, IEnumerable<Assembly> assemblies)
    {
        var assembly = assemblies.Single(a => a.GetName().Name == contract.Assembly);
        var type = assembly.GetType(contract.DeclaringType, true);
        var matches = type.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(m => m.Name == contract.Method && !m.IsGenericMethod && m.IsStatic == contract.IsStatic && TypeName(m.ReturnType) == contract.ReturnType
                && m.GetParameters().Select(p => TypeName(p.ParameterType)).SequenceEqual(contract.Parameters)).ToArray();
        if (matches.Length != 1) throw new MissingMethodException("Named jersey contract differs: " + contract.Id);
        return matches[0];
    }
}
