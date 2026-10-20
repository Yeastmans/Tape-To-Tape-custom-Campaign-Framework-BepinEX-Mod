using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Il2CppInterop.Runtime;

namespace EndlessMode;

// IL2CPP inlines many squad-ID comparisons. Harmony's managed transpilers cannot
// change those native instructions. Redirect only the audited comparison calls,
// never String.Equals globally and never the squad's actual ID. Each site keeps
// its native branch, mode check and gameplay implementation.
//
// A full binary hash AND all original call bytes must match before installation.
// Unknown builds or another mod editing a site leave the entire bridge untouched.
internal static class NativeSquadBehaviorBridge
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte Query(IntPtr instance, IntPtr id, byte checkMode, IntPtr method);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte Compare(IntPtr left, IntPtr right, IntPtr method);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte InlineCompare(IntPtr left, IntPtr right);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte StartingFlow(IntPtr state, byte intro, IntPtr title);
    private sealed class Site
    {
        internal string Feature, Query, Kind, Name;
        internal int Rva, Target;
        internal byte[] Original;
        internal IntPtr Address, Stub;
        internal CampaignCore.NativeInlineComparison Inline;
    }
    private static readonly List<Delegate> callbacks = new();
    private static readonly List<Site> installed = new();
    private static IntPtr arena;
    private static bool reportedCallbackFailure;
    internal static bool Ready { get; private set; }
    internal static string Status { get; private set; } = "Not initialized";

    internal static void Install()
    {
        if (Ready) return;
        try
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || IntPtr.Size != 8)
                throw new PlatformNotSupportedException("Squad behavior routing requires the audited Windows x64 game build (including Proton).");
            var module = Process.GetCurrentProcess().Modules.Cast<ProcessModule>()
                .FirstOrDefault(m => m.ModuleName.Equals("GameAssembly.dll", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("GameAssembly.dll is not loaded.");
            var profile = NativeCompatibility.Load(module.FileName);
            var sites = new List<Site>();
            foreach (var entry in profile.Sites)
            {
                var site = new Site { Feature = entry.Feature, Query = entry.Query, Kind = entry.Kind,
                    Rva = entry.Rva, Target = entry.Target, Original = Convert.FromHexString(entry.Bytes), Name = entry.Method, Inline=entry.Inline };
                site.Address = module.BaseAddress + site.Rva;
                var current = new byte[site.Original.Length]; Marshal.Copy(site.Address, current, 0, current.Length);
                if (!current.SequenceEqual(site.Original))
                    throw new InvalidOperationException("Squad comparison site changed: " + site.Name);
                if(site.Kind=="inline_eq")
                {
                    var expected=Convert.FromHexString(site.Inline.BlockBytes);var actual=new byte[expected.Length];
                    Marshal.Copy(site.Address,actual,0,actual.Length);
                    if(!actual.SequenceEqual(expected)) throw new InvalidOperationException("Inline squad comparison changed: "+site.Name);
                }
                sites.Add(site);
            }
            var flow = new Site { Feature = "starting_screens", Query = "", Kind = "flow", Rva = profile.FlowRva,
                Target = profile.FlowRva + 7, Original = Convert.FromHexString(profile.FlowBytes), Name = "TitleScreen.StartNewRun starting screen flags" };
            flow.Address = module.BaseAddress + flow.Rva;
            var flowBytes = new byte[flow.Original.Length]; Marshal.Copy(flow.Address, flowBytes, 0, flowBytes.Length);
            if (!flowBytes.SequenceEqual(flow.Original)) throw new InvalidOperationException("Starting screen site changed.");
            sites.Add(flow);
            arena = AllocateNear(module.BaseAddress, module.ModuleMemorySize);
            var stubs = new Dictionary<string, IntPtr>();
            int offset = 0;
            foreach (var site in sites)
            {
                if(site.Kind=="inline_eq")
                {
                    InlineCompare callback=(left,right)=>
                    {
                        try
                        {
                            if(Plugin.IsDefaultMode) return 0;
                            string a=IL2CPP.Il2CppStringToManaged(left),b=IL2CPP.Il2CppStringToManaged(right);
                            string customId=b==site.Query?a:a==site.Query?b:null;
                            return SquadBehaviorRuntime.TryEnabled(customId,site.Feature,out bool enabled)?(byte)(enabled?2:1):(byte)0;
                        }
                        catch(Exception ex){ReportCallback(ex);return 0;}
                    };
                    callbacks.Add(callback);site.Stub=arena+offset;offset+=512;
                    var code=NativeInlineComparisonStub.BuildInlineComparisonStub(Marshal.GetFunctionPointerForDelegate(callback),
                        site.Address+site.Original.Length,module.BaseAddress+site.Inline.EqualBranchRva,module.BaseAddress+site.Target,
                        site.Original.Take(3).ToArray(),site.Inline.LeftRegister);
                    if(code.Length>512) throw new InvalidOperationException("Inline squad stub exceeds reserved space.");
                    Marshal.Copy(code,0,site.Stub,code.Length);
                    continue;
                }
                if (site.Kind == "flow")
                {
                    StartingFlow callback = SquadStartingScreens.Apply; callbacks.Add(callback);
                    site.Stub = arena + offset; offset += 256;
                    byte[] code = BuildStartingFlowStub(Marshal.GetFunctionPointerForDelegate(callback), module.BaseAddress + site.Target, site.Original);
                    Marshal.Copy(code, 0, site.Stub, code.Length);
                    continue;
                }
                string key = site.Feature + "/" + site.Query + "/" + site.Kind;
                if (!stubs.TryGetValue(key, out site.Stub))
                {
                    IntPtr target = module.BaseAddress + site.Target;
                    Delegate callback;
                    if (site.Kind == "query")
                    {
                        var original = Marshal.GetDelegateForFunctionPointer<Query>(target);
                        Query routed = (instance, id, checkMode, method) =>
                        {
                            try
                            {
                                if (Plugin.IsDefaultMode || instance == IntPtr.Zero) return original(instance, id, checkMode, method);
                                var util = new Tape2Tape.CampaignUtil(instance);
                                string customId = util._campaignState?.GetCurrentSquad()?.id;
                                if (SquadBehaviorRuntime.TryEnabled(customId, site.Feature, out bool enabled))
                                    return enabled ? original(instance, IL2CPP.ManagedStringToIl2Cpp(customId), checkMode, method) : (byte)0;
                            }
                            catch (Exception ex) { ReportCallback(ex); }
                            return original(instance, id, checkMode, method);
                        };
                        callback = routed;
                    }
                    else
                    {
                        var original = Marshal.GetDelegateForFunctionPointer<Compare>(target);
                        Compare routed = (left, right, method) =>
                        {
                            try
                            {
                                if (Plugin.IsDefaultMode) return original(left, right, method);
                                string a = IL2CPP.Il2CppStringToManaged(left), b = IL2CPP.Il2CppStringToManaged(right);
                                string customId = b == site.Query ? a : a == site.Query ? b : null;
                                if (SquadBehaviorRuntime.TryEnabled(customId, site.Feature, out bool enabled))
                                    return (byte)((site.Kind == "neq" ? !enabled : enabled) ? 1 : 0);
                            }
                            catch (Exception ex) { ReportCallback(ex); }
                            return original(left, right, method);
                        };
                        callback = routed;
                    }
                    callbacks.Add(callback);
                    site.Stub = arena + offset; offset += 16;
                    // mov rax, destination; jmp rax. The call-site ABI is unchanged,
                    // including native tail calls and their original return address.
                    byte[] stub = new byte[12]; stub[0] = 0x48; stub[1] = 0xb8;
                    BitConverter.GetBytes(Marshal.GetFunctionPointerForDelegate(callback).ToInt64()).CopyTo(stub, 2);
                    stub[10] = 0xff; stub[11] = 0xe0;
                    Marshal.Copy(stub, 0, site.Stub, stub.Length); stubs[key] = site.Stub;
                }
                long displacement = site.Stub.ToInt64() - site.Address.ToInt64() - 5;
                if (displacement < int.MinValue || displacement > int.MaxValue) throw new InvalidOperationException("Squad bridge is out of call range.");
            }
            if (!VirtualProtect(arena, (UIntPtr)65536, 0x20, out _)) throw new System.ComponentModel.Win32Exception();
            FlushInstructionCache(GetCurrentProcess(), arena, (UIntPtr)65536);
            foreach (var site in sites)
            {
                byte[] call = (byte[])site.Original.Clone();
                if (site.Kind == "flow" || site.Kind=="inline_eq") { Array.Fill(call, (byte)0x90); call[0] = 0xe9; }
                BitConverter.GetBytes(checked((int)(site.Stub.ToInt64() - site.Address.ToInt64() - 5))).CopyTo(call, 1);
                WriteCode(site.Address, call); installed.Add(site);
            }
            Ready = true; Status = $"{installed.Count} audited squad checks routed on build {profile.Build}";
            Plugin.Log.LogInfo("[SquadBehaviors] " + Status);
        }
        catch (Exception ex)
        {
            foreach (var site in installed.AsEnumerable().Reverse()) WriteCode(site.Address, site.Original);
            installed.Clear();
            // Keep callback roots/arena alive even after rollback: a callback may
            // already be returning on another native thread.
            Ready = false; Status = ex.Message;
            Plugin.Log.LogError("[SquadBehaviors] Behavior controls unavailable: " + Status);
        }
    }
    internal static byte[] BuildStartingFlowStub(IntPtr callback, IntPtr resume, byte[] displaced)
    {
        var code = new List<byte>();
        void Emit(params byte[] bytes) => code.AddRange(bytes);
        // Preserve every volatile integer/vector register. RSP is 16-byte
        // aligned here in the native method; 7 pushes plus 0x88 retain ABI
        // alignment and reserve the required 32-byte callback shadow space.
        Emit(0x50,0x51,0x52,0x41,0x50,0x41,0x51,0x41,0x52,0x41,0x53);
        Emit(0x48,0x81,0xec,0x88,0,0,0);
        for (int i=0;i<6;i++) Emit(0xf3,0x0f,0x7f,(byte)(0x44+i*8),0x24,(byte)(0x20+i*16));
        Emit(0x48,0x89,0xf9,0x41,0x0f,0xb6,0xd6,0x49,0x89,0xf0,0x48,0xb8); code.AddRange(BitConverter.GetBytes(callback.ToInt64()));
        Emit(0xff,0xd0,0x41,0x88,0xc6);
        for (int i=0;i<6;i++) Emit(0xf3,0x0f,0x6f,(byte)(0x44+i*8),0x24,(byte)(0x20+i*16));
        Emit(0x48,0x81,0xc4,0x88,0,0,0,0x41,0x5b,0x41,0x5a,0x41,0x59,0x41,0x58,0x5a,0x59,0x58);
        code.AddRange(displaced); Emit(0xff,0x25,0,0,0,0); code.AddRange(BitConverter.GetBytes(resume.ToInt64()));
        return code.ToArray();
    }
    private static void ReportCallback(Exception ex)
    {
        if (reportedCallbackFailure) return; reportedCallbackFailure = true;
        Plugin.Log.LogError("[SquadBehaviors] Native comparison routing failed; retaining native result: " + ex);
    }
    private static void WriteCode(IntPtr address, byte[] code)
    {
        if (!VirtualProtect(address, (UIntPtr)code.Length, 0x40, out uint previous)) throw new System.ComponentModel.Win32Exception();
        try { Marshal.Copy(code, 0, address, code.Length); FlushInstructionCache(GetCurrentProcess(), address, (UIntPtr)code.Length); }
        finally { VirtualProtect(address, (UIntPtr)code.Length, previous, out _); }
    }
    private static IntPtr AllocateNear(IntPtr module, int length)
    {
        long end = (module.ToInt64() + length + 65535) & ~65535L;
        for (long delta = 0; delta < 0x30000000; delta += 65536)
        {
            var result = VirtualAlloc(new IntPtr(end + delta), (UIntPtr)65536, 0x3000, 0x04);
            if (result != IntPtr.Zero) return result;
        }
        throw new InvalidOperationException("Could not allocate a nearby squad behavior bridge.");
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr VirtualAlloc(IntPtr address, UIntPtr size, uint type, uint protection);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool VirtualProtect(IntPtr address, UIntPtr size, uint protection, out uint previous);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] private static extern bool FlushInstructionCache(IntPtr process, IntPtr address, UIntPtr size);
}
