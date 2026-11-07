using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using BepInEx.Unity.IL2CPP.Hook;
using HarmonyLib;
using HarmonyLib.Public.Patching;
using Il2CppInterop.Common;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Runtime;
using Il2CppInterop.Runtime.Runtime.VersionSpecific.MethodInfo;
using Movements.MovementStates;

namespace EndlessMode;

// Dobby widens ReleaseShot's initial two-byte JNE but retains a displacement
// calculated for the old length. Only this proven RET+4 form is repaired.
// Other hooks and providers are never modified or globally replaced.
internal static class CustomRewardChargedReleaseRepair
{
    private const string Owner = "com.mods.customcampaign.chargedrelease";
    private const int ReleaseShotRva = 0x3c55450;
    private const string BodyHash = "F8E630EED796F25F0827060FFAC0EAE87318755BD5A7612A349E47B267BEDD81";
    private const string DobbyHash = "8015DE7D867245A1095D13947A63763878E4CF5FD3D3089B63CC39200B055DED";
    private static bool ready;

    internal static void Install()
    {
        if (ready) return;
        var harmony = new Harmony(Owner);
        var target = AccessTools.Method(typeof(SlapshotState), nameof(SlapshotState.ReleaseShot),
            new[] { typeof(UnityEngine.Vector2), typeof(float), typeof(bool), typeof(bool) });
        byte[] original = null; IntPtr entry = IntPtr.Zero; INativeMethodInfoStruct verifiedInfo = null;
        ready = ChargedReleaseTrampoline.InstallChecked(
            () =>
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || IntPtr.Size != 8 || target == null)
                    throw new PlatformNotSupportedException("Charged release requires the reviewed Windows x64 target.");
                // Do not repatch another owner's bridge or change its trampoline.
                if (Harmony.GetPatchInfo(target)?.Owners.Any() == true)
                    throw new InvalidOperationException("ReleaseShot already has a patch owner; preserving it unchanged.");
                CheckAssembly(typeof(Harmony).Assembly, "6C898933B52149E8BCF6722305C7E4D94ADD47C9E53B364096F91721624C0877");
                CheckAssembly(typeof(INativeDetour).Assembly, "46CF1EF802BDF8CB6587FC1E4D98F7AF1073A60487B39A64E230E6F6E4C23AEC");
                CheckAssembly(typeof(IL2CPP).Assembly, "65AB051A681C2C1E52DF7596FF738BE401AAE72CFF882FF18241CF54A3EA38A4");
                CheckAssembly(typeof(Il2CppInteropUtils).Assembly, "070E04F2276A8963A6D2A7EB52D43B644F3AE9AA3816C34541124D3862F1B37F");
                if (GeneratedInteropIdentity.Hash(File.ReadAllBytes(typeof(SlapshotState).Assembly.Location))
                    != "F46652CBC6BBFAB80D67D2ADE3F77A5FC926F1BAEAC80B95754C35D92B162B7E")
                    throw new InvalidDataException("Unreviewed generated charged-release wrapper.");
                var patcherType = Type.GetType("Il2CppInterop.HarmonySupport.Il2CppDetourMethodPatcher, Il2CppInterop.HarmonySupport", true);
                CheckAssembly(patcherType.Assembly, "F7612EA2E5A4D473B771F34EE3B20CF32DC0C4ECEA0185416474999A17FBCC07");
                CheckFile(Path.Combine(Path.GetDirectoryName(typeof(INativeDetour).Assembly.Location), "dobby.dll"), DobbyHash);
                var module = Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Single(m => m.ModuleName.Equals("GameAssembly.dll", StringComparison.OrdinalIgnoreCase));
                if (NativeCompatibility.Load(module.FileName).GameHash != "0709D469AE6C47B695BBA71664BF5619982EE6C3E17EAEF6DC8B98FDA0A61CEC")
                    throw new InvalidDataException("Charged release has not been reviewed for this native build.");
                entry = module.BaseAddress + ReleaseShotRva; original = Read(entry, 96);
                if (Convert.ToHexString(SHA256.HashData(original)) != BodyHash || original[95] != 0xc3)
                    throw new InvalidDataException("ReleaseShot body or its original RET has changed.");
                // Resolve the very same generated descriptor that Harmony will
                // patch, BEFORE allowing it to touch any native function.
                verifiedInfo = ResolveMethodInfo(target);
                if (UnityVersionHandler.MethodSize() != 88 || verifiedInfo.MethodPointer != entry)
                    throw new InvalidDataException("Generated ReleaseShot descriptor points to an unexpected native method.");
            },
            () => harmony.Patch(target, prefix: new HarmonyMethod(typeof(CustomRewardChargedRelease), nameof(CustomRewardChargedRelease.Prefix))),
            () => ValidateAndRepair(target, entry, original, verifiedInfo),
            () => RemoveOwnedPrefix(harmony, target, entry, original, verifiedInfo),
            error => Plugin.Log.LogError("[CustomRewards] Charged-release capture unavailable; its hook was not retained: " + error));
    }

    private static void RemoveOwnedPrefix(Harmony harmony, MethodBase target, IntPtr entry, byte[] original, INativeMethodInfoStruct verifiedInfo)
    {
        ChargedReleaseTrampoline.RemoveOwned(
            () => harmony.Unpatch(target, HarmonyPatchType.Prefix, Owner),
            () => Harmony.GetPatchInfo(target)?.Owners.Contains(Owner) == true,
            () => Harmony.GetPatchInfo(target)?.Owners.Any() == true,
            () =>
            {
                // The reviewed HarmonyX backend rebuilds an EMPTY managed bridge
                // even after removing its last prefix. Dispose that one unowned
                // bridge as well; otherwise its bad relocation could remain active.
                // An independently added owner is never detached here.
                var patcher = PatchManager.GetMethodPatcher(target);
                RequireType(patcher, "Il2CppInterop.HarmonySupport.Il2CppDetourMethodPatcher");
                var slot = AccessTools.Field(patcher.GetType(), "nativeDetour") ?? throw new MissingFieldException("nativeDetour");
                var detour = slot.GetValue(patcher);
                if (detour != null)
                {
                    RequireType(detour, "BepInEx.Unity.IL2CPP.Hook.Il2CppInteropDetour");
                    var native = (INativeMethodInfoStruct)Field(patcher, "originalNativeMethodInfo");
                    if (native.Pointer != verifiedInfo.Pointer || native.MethodPointer != entry)
                        throw new InvalidDataException("Original charged-release descriptor changed during rollback.");
                    var copy = (INativeMethodInfoStruct)Field(patcher, "modifiedNativeMethodInfo");
                    copy.MethodPointer = native.MethodPointer;
                    ((IDisposable)detour).Dispose();
                    slot.SetValue(patcher, null);
                }
            },
            () => { if (!Read(entry, 96).SequenceEqual(original)) throw new InvalidOperationException("Native ReleaseShot was not restored after removing its refused hook."); });
    }

    private static void ValidateAndRepair(MethodBase target, IntPtr entry, byte[] original, INativeMethodInfoStruct verifiedInfo)
    {
        var patcher = PatchManager.GetMethodPatcher(target);
        RequireType(patcher, "Il2CppInterop.HarmonySupport.Il2CppDetourMethodPatcher");
        var native = Field(patcher, "nativeDetour");
        RequireType(native, "BepInEx.Unity.IL2CPP.Hook.Il2CppInteropDetour");
        var wrapper = Field(native, "detour");
        RequireType(wrapper, "BepInEx.Unity.IL2CPP.Hook.INativeDetour+CacheDetourWrapper");
        var backend = Field(wrapper, "_wrapped");
        RequireType(backend, "BepInEx.Unity.IL2CPP.Hook.Dobby.DobbyDetour");
        var dobbyModule = Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Single(m => m.ModuleName.Equals("dobby.dll", StringComparison.OrdinalIgnoreCase));
        CheckFile(dobbyModule.FileName, DobbyHash);
        var nativeInfo = (INativeMethodInfoStruct)Field(patcher, "originalNativeMethodInfo");
        var copiedInfo = (INativeMethodInfoStruct)Field(patcher, "modifiedNativeMethodInfo");
        var binding = (INativeDetour)backend;
        var trampoline = binding.TrampolinePtr;
        if (UnityVersionHandler.MethodSize() != 88 || nativeInfo.Pointer == IntPtr.Zero || copiedInfo.Pointer == IntPtr.Zero
            || nativeInfo.Pointer != verifiedInfo.Pointer
            || binding.OriginalMethodPtr != entry || nativeInfo.MethodPointer != entry || copiedInfo.MethodPointer != trampoline || trampoline == IntPtr.Zero
            || !Read(nativeInfo.Pointer, 88).Skip(8).SequenceEqual(Read(copiedInfo.Pointer, 88).Skip(8)))
            throw new InvalidDataException("Unexpected charged-release MethodInfo copy or native trampoline binding.");
        var nativeBody = Read(entry, 96);
        if (nativeBody[0] != 0xff || nativeBody[1] != 0x25 || !nativeBody.Skip(6).SequenceEqual(original.Skip(6)) || nativeBody[95] != 0xc3)
            throw new InvalidDataException("Unexpected ReleaseShot entry patch or changed native RET.");
        IntPtr entryCell = new IntPtr(checked(entry.ToInt64() + 6 + BitConverter.ToInt32(nativeBody, 2)));
        if (Marshal.ReadIntPtr(entryCell) != binding.DetourMethodPtr)
            throw new InvalidDataException("ReleaseShot entry no longer points to its owned detour.");
        bool corrected = ChargedReleaseTrampoline.Apply(trampoline, entry);
        Plugin.Log.LogInfo("[CustomRewards] Charged-release capture installed; original early-return target verified" + (corrected ? " and corrected." : "."));
    }

    private static object Field(object instance, string name) => AccessTools.Field(instance.GetType(), name)?.GetValue(instance)
        ?? throw new MissingFieldException(instance.GetType().FullName, name);
    private static unsafe INativeMethodInfoStruct ResolveMethodInfo(MethodBase target)
    {
        var field = Il2CppInteropUtils.GetIl2CppMethodInfoPointerFieldForGeneratedMethod(target)
            ?? throw new InvalidDataException("Generated ReleaseShot descriptor field is missing.");
        var pointer = (IntPtr)field.GetValue(null);
        if (pointer == IntPtr.Zero) throw new InvalidDataException("Generated ReleaseShot descriptor is null.");
        return UnityVersionHandler.Wrap((Il2CppMethodInfo*)pointer);
    }
    private static void RequireType(object instance, string name)
    { if (instance?.GetType().FullName != name) throw new InvalidDataException("Unreviewed charged-release provider: " + instance?.GetType().FullName); }
    private static byte[] Read(IntPtr address, int count) { var result = new byte[count]; Marshal.Copy(address, result, 0, count); return result; }
    private static void CheckAssembly(Assembly assembly, string hash) => CheckFile(assembly.Location, hash);
    private static void CheckFile(string file, string hash)
    { using var stream = File.OpenRead(file); using var sha = SHA256.Create(); if (Convert.ToHexString(sha.ComputeHash(stream)) != hash) throw new InvalidDataException("Unreviewed charged-release dependency: " + Path.GetFileName(file)); }
}
