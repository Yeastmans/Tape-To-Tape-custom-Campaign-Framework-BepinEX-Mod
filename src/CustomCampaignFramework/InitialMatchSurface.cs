using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Arena;
using BepInEx.Unity.IL2CPP.Hook;

namespace EndlessMode;

internal static class InitialMatchSurface
{
    // Verified Windows x64 ABI. UniTask is a 16-byte value:
    // RCX=caller's return buffer, RDX=IceSetup*, R8D=type, R9=MethodInfo*.
    // Pass that buffer through untouched instead of marshalling a UniTask
    // through Harmony. The native caller still awaits the original operation.
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr SwitchIce(IntPtr result, IntPtr instance, int type, IntPtr method);
    private static INativeDetour detour;
    private static SwitchIce original;
    private static readonly SwitchIce callback = LoadSelectedSurface;
    private static bool reportedFailure;

    internal static void Install()
    {
        if (detour != null || Plugin.IsDefaultMode) return;
        try
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || IntPtr.Size != 8)
                throw new PlatformNotSupportedException("Initial rink selection requires the audited Windows x64 build.");
            var module = Process.GetCurrentProcess().Modules.Cast<ProcessModule>()
                .FirstOrDefault(m => m.ModuleName.Equals("GameAssembly.dll", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("GameAssembly.dll is not loaded.");
            var profile = NativeCompatibility.Load(module.FileName);
            var address = module.BaseAddress + profile.SurfaceRva;
            byte[] expected = Convert.FromHexString(profile.SurfaceBytes);
            var actual = new byte[expected.Length];
            Marshal.Copy(address, actual, 0, actual.Length);
            if (!actual.SequenceEqual(expected))
                throw new InvalidOperationException("The native rink loader entry was changed by another patch.");
            detour = INativeDetour.CreateAndApply(address, callback, out original);
            Plugin.Log.LogInfo($"[Surface] Initial rink selection installed for build {profile.Build}; no post-load swap.");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError("[Surface] Initial rink selection unavailable; retaining native surface: " + ex);
        }
    }

    private static IntPtr LoadSelectedSurface(IntPtr result, IntPtr instance, int type, IntPtr method)
    {
        int selected = type;
        try
        {
            if (instance != IntPtr.Zero && !Plugin.IsDefaultMode)
            {
                var setup = new IceSetup(instance);
                bool live = IceSetup._instance != null && IceSetup._instance.Pointer == instance;
                selected = InitialSurfaceChoice.Resolve(type, Plugin.IsDefaultMode, live,
                    setup._isInitialized, setup._isSwitchingIceType, (int)setup._currentIceType,
                    OpponentMatch.Surface, Plugin.SoccerThisMatch);
                if (live && !setup._isInitialized && !setup._isSwitchingIceType && (int)setup._currentIceType == 0)
                    Plugin.Log.LogInfo($"[Surface] Initial load: native={type}, selected={selected}, authored='{OpponentMatch.Surface}', current=0, initialized=False.");
            }
        }
        catch (Exception ex)
        {
            selected = type;
            if (!reportedFailure)
            {
                reportedFailure = true;
                Plugin.Log.LogWarning("[Surface] Selection failed; retaining native load: " + ex);
            }
        }
        // Exactly one native operation. Never start another switch from inside
        // Initialize, discard its task, or retry after the rink becomes visible.
        return original(result, instance, selected, method);
    }
}
