using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using BepInEx.Unity.IL2CPP.Hook;
using CampaignCore;
using Data;
using HarmonyLib;
using Spine;
using Spine.Unity;
using Skaters;
using UnityEngine;
using NativeList = Il2CppSystem.Collections.Generic.List<Spine.Skin>;

namespace EndlessMode;

// Raw value-type callbacks NEVER construct a state wrapper. Only entry fields
// proven to be actual objects are wrapped; the native state is read-only.
internal static class JerseyLogoNativeHooks
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void MoveFn(IntPtr state, IntPtr method);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void BridgeFn(IntPtr state, IntPtr runnerRef, IntPtr method);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void FaultFn(IntPtr runner, IntPtr exception, IntPtr method);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void BuilderFaultFn(IntPtr builder, IntPtr exception);
    private const int ForwardOwner = 1, GoalieOwner = 2, World = 3, Graphic = 4, Bench = 5, Penalty = 6, Knockout = 7, SkaterSkin = 8;
    private static readonly List<INativeDetour> detours = new();
    private static readonly List<Delegate> callbacks = new();
    private static readonly List<RawNativeHookContract> installed = new();
    private static Harmony harmony;
    internal static bool Enabled { get; private set; }
    private static long context = -1;
    private sealed class Selection
    {
        internal SkaterData Player;
        internal bool Home;
        internal long Context;
        internal JerseyLogoRenderChoice Choice;
        internal object Root;
    }
    private sealed class Target
    {
        internal SkeletonRenderer World;
        internal SkeletonAnimation Animation;
        internal SkeletonGraphic Graphic;
        internal IntPtr Pointer => World != null ? World.Pointer : Graphic != null ? Graphic.Pointer : IntPtr.Zero;
        internal Skeleton Skeleton => World != null ? World.Skeleton : Graphic?.Skeleton;
        internal Skin Skin => Skeleton?.Skin;
        internal bool Alive => World != null || Graphic != null;
    }
    private sealed class Roots
    {
        internal Target Target;
        internal Selection Selection;
        internal object Owner;
        internal Skin Composite;
    }
    private sealed class Handoff
    {
        internal object Root;
        internal SkaterData[] Candidates;
        internal IntPtr Renderer, Owner;
        internal Selection Selection;
    }
    [ThreadStatic] private static Handoff handoff;
    private static readonly Dictionary<IntPtr, Selection> lists = new(), composites = new();
    private static readonly Dictionary<IntPtr, Target> targets = new();
    private static IntPtr Ptr(IntPtr state, int offset) => Marshal.ReadIntPtr(state, offset);
    private static bool Flag(IntPtr state, int offset) => Marshal.ReadByte(state, offset) != 0;
    private static Target WorldTarget(SkeletonAnimation animation)
        => new Target { Animation = animation, World = animation?.Renderer?.TryCast<SkeletonRenderer>() };
    private static IntPtr Pointer(Skin skin) => skin?.Pointer ?? IntPtr.Zero;
    private static void Warn(Exception ex)
    { try { Plugin.Log.LogWarning("[JerseyLogo] Native binding kept fail-closed: " + ex.Message); } catch { } }
    private static void ObserveContext()
    {
        JerseyLogoRuntime.EnsureContext();
        if (context != JerseyLogoRuntime.Context) { context = JerseyLogoRuntime.Context; lists.Clear(); composites.Clear(); }
        foreach (var pair in targets.Where(p => !p.Value.Alive).ToArray()) { targets.Remove(pair.Key); JerseyLogoRequests.ForgetRenderer(pair.Key); }
        JerseyLogoRuntime.Collect();
    }
    private static Selection Select(SkaterData player, bool home, object root)
    {
        ObserveContext();
        if (player == null) return null;
        if (!JerseyLogoRuntime.Bound(player) && handoff?.Candidates != null)
        {
            if (JerseyLogoRuntime.CopyUniqueClone(player, handoff.Candidates))
            {
                var source = handoff.Candidates.Single(p => p != null && p.id == player.id);
                CampaignAppearanceRuntime.Copy(source, player);
            }
        }
        return new Selection { Player = player, Home = home, Context = context, Choice = JerseyLogoRuntime.Resolve(player, !home), Root = root };
    }
    internal static void CaptureList(SkaterData player, bool home, NativeList skins)
    {
        if (!Enabled || skins == null) return;
        try
        {
            var selection = Select(player, home, skins);
            if (selection == null) return;
            lists[skins.Pointer] = selection;
            if (handoff != null && handoff.Selection?.Player?.Pointer == player?.Pointer) handoff.Selection = selection;
        }
        catch (Exception ex) { Warn(ex); }
    }
    private static void ForwardComposite(ForwardData data, bool useHomeUniform, Skin __result)
        => CaptureComposite(data, useHomeUniform, __result);
    private static void GoalieComposite(GoaltenderData data, bool useHomeUniform, Skin __result)
        => CaptureComposite(data, useHomeUniform, __result);
    private static void CaptureComposite(SkaterData player, bool home, Skin skin)
    {
        if (!Enabled || skin == null) return;
        try { var selected = Select(player, home, skin); if (selected != null) composites[skin.Pointer] = selected; }
        catch (Exception ex) { Warn(ex); }
    }
    private static JerseyLogoRequests.Operation Capture(int kind, IntPtr state)
    {
        ObserveContext();
        int entryState = Marshal.ReadInt32(state);
        if (entryState >= 0)
        {
            var runner = Ptr(state, 8);
            var resumed = JerseyLogoRequests.Resume(kind, runner);
            // Unknown resumed state cannot acquire ownership from a new global
            // selection. A pass-through generation still supersedes old work.
            if (resumed != null) return resumed;
            Target unknown = null;
            if (kind == World) unknown = WorldTarget(new SkeletonAnimation(Ptr(state, 0x18)));
            else if (kind == Graphic) unknown = new Target { Graphic = new SkeletonGraphic(Ptr(state, 0x18)) };
            else if (kind == ForwardOwner) unknown = WorldTarget(new Forward(Ptr(state, 0x18)).skeletonAnimation);
            else if (kind == GoalieOwner) unknown = WorldTarget(new Goaltender.Goaltender(Ptr(state, 0x18)).skeletonAnimation);
            if (unknown?.Alive != true) return null;
            return JerseyLogoRequests.Start(kind, new JerseyLogoRequests.Request { Renderer = unknown.Pointer, Context = -1, Goalie = kind == GoalieOwner || (kind == World && Flag(state, 0x44)) || (kind == Graphic && Flag(state, 0x40)), Roots = new Roots { Target = unknown } });
        }
        Target target; Selection selected = null; object owner = null; IntPtr ownerPtr = IntPtr.Zero; Skin skin;
        bool goalie;
        if (kind == ForwardOwner)
        {
            ownerPtr = Ptr(state, 0x18); if (ownerPtr == IntPtr.Zero) return null;
            var forward = new Forward(ownerPtr); owner = forward;
            target = WorldTarget(forward.skeletonAnimation);
            selected = Select(forward._forwardData, Flag(state, 0x20), forward);
            goalie = false; skin = target.Skin;
        }
        else if (kind == GoalieOwner)
        {
            ownerPtr = Ptr(state, 0x18); if (ownerPtr == IntPtr.Zero) return null;
            var g = new Goaltender.Goaltender(ownerPtr); owner = g;
            target = WorldTarget(g.skeletonAnimation); goalie = true; skin = target.Skin;
            var list = Ptr(state, 0x20);
            if (lists.TryGetValue(list, out var found) && found.Player?.Pointer == g.goaltenderData?.Pointer && found.Context == context) selected = found;
        }
        else
        {
            IntPtr renderer = Ptr(state, 0x18), composite = Ptr(state, kind == World ? 0x20 : 0x28);
            if (renderer == IntPtr.Zero) return null;
            target = kind == World ? WorldTarget(new SkeletonAnimation(renderer)) : new Target { Graphic = new SkeletonGraphic(renderer) };
            skin = composite == IntPtr.Zero ? null : new Skin(composite);
            goalie = Flag(state, kind == World ? 0x44 : 0x40);
            var parent = JerseyLogoRequests.Current?.Operation.Request;
            if (parent != null && parent.Renderer == target.Pointer && parent.Roots is Roots saved && parent.Goalie == goalie)
            { selected = saved.Selection; owner = saved.Owner; ownerPtr = parent.Owner; }
            else if (composites.TryGetValue(composite, out var found) && found.Context == context) selected = found;
            else if (handoff?.Renderer == target.Pointer && handoff.Selection?.Context == context)
            { selected = handoff.Selection; owner = handoff.Root; ownerPtr = handoff.Owner; }
        }
        if (!target.Alive) return null;
        var roots = new Roots { Target = target, Selection = selected, Owner = owner, Composite = skin };
        var player = selected?.Player?.Pointer ?? IntPtr.Zero;
        var inherited = kind is World or Graphic ? JerseyLogoRequests.Current?.Operation.Request : null;
        var request = JerseyLogoRequests.Begin(target.Pointer, Pointer(skin), player, ownerPtr, goalie, context, selected?.Choice ?? default, roots, inherited);
        // Keep the exact newly constructed composite rooted across suspension.
        targets[target.Pointer] = target;
        bool syncEligible = kind == ForwardOwner && Ptr(state, 8) == IntPtr.Zero;
        var initial = JerseyLogoRequests.Start(kind, request);
        initial.SyncEligible = syncEligible;
        return initial;
    }
    private static void Move(int kind, MoveFn original, IntPtr state, IntPtr method)
    {
        JerseyLogoRequests.RunSlice(() => Enabled ? Capture(kind, state) : null, state,
            () => original(state, method), Apply, Warn);
    }
    private static void Bridge(int kind, BridgeFn original, IntPtr state, IntPtr runnerRef, IntPtr method)
    {
        var scope = JerseyLogoRequests.Current;
        var operation = scope != null && scope.State == state && scope.Operation.Kind == kind ? scope.Operation : null;
        JerseyLogoRequests.Publish(operation, () => original(state, runnerRef, method), () => {
                IntPtr runner = Marshal.ReadIntPtr(runnerRef);
                if (runner != IntPtr.Zero) operation.RunnerRoot = new Il2CppSystem.Object(runner);
                return runner;
            }, Warn);
        GC.KeepAlive(operation);
    }
    private static void Apply(JerseyLogoRequests.Request request)
    {
        if (request?.Roots is not Roots roots || !roots.Target.Alive || !JerseyLogoRequests.CanApply(request, Pointer(roots.Target.Skin))) return;
        if (roots.Target.World != null && request.Choice.Texture is Texture2D texture)
            T2TSkeletonExtensions.SetLogo(roots.Target.World, texture, request.Goalie);
        else if (roots.Target.Graphic != null) T2TSkeletonExtensions.SetLogo(roots.Target.Graphic, request.Choice.Texture, request.Goalie);
    }
    private static Handoff CaptureHandoff(int kind, IntPtr state)
    {
        ObserveContext();
        if (kind == Bench)
        {
            var pointer = Ptr(state, 0x20); if (pointer == IntPtr.Zero) return null;
            // Owner and Team are genuine object pointers, never state wrappers.
            var owner = new Il2CppSystem.Object(pointer);
            var teamPointer = Ptr(pointer, 0x38); if (teamPointer == IntPtr.Zero) return null;
            var team = new Tape2Tape.Team(teamPointer);
            var players = team.AllForwards?.ToArray().Where(p => p != null).Select(p => (SkaterData)p._forwardData).ToArray() ?? Array.Empty<SkaterData>();
            return new Handoff { Root = new object[] { owner, team }, Candidates = players };
        }
        if (kind == Penalty)
        {
            var pointer = Ptr(state, 0x10); if (pointer == IntPtr.Zero) return null;
            var forward = new Forward(pointer);
            return new Handoff { Root = forward, Candidates = new SkaterData[] { forward._forwardData } };
        }
        if (kind == Knockout)
        {
            var pointer = Ptr(state, 0x10); if (pointer == IntPtr.Zero) return null;
            var owner = new Il2CppSystem.Object(pointer); var manager = Ptr(pointer, 0x10); if (manager == IntPtr.Zero) return null;
            var skater = Ptr(manager, 0x1c8); if (skater == IntPtr.Zero) return null;
            var forward = new Forward(skater);
            return new Handoff { Root = new object[] { owner, new Il2CppSystem.Object(manager), forward }, Owner = skater,
                Renderer = WorldTarget(forward.skeletonAnimation).Pointer,
                Selection = new Selection { Player = forward._forwardData, Context = context } };
        }
        var playerPointer = Ptr(state, 0x20); if (playerPointer == IntPtr.Zero) return null;
        var player = new SkaterData(playerPointer); bool home = Flag(state, 0x40);
        var composite = Ptr(state, home ? 0x28 : 0x30);
        if (composite != IntPtr.Zero) CaptureComposite(player, home, new Skin(composite));
        return new Handoff { Root = player };
    }
    private static void Transient(int kind, MoveFn original, IntPtr state, IntPtr method)
    {
        var previous = handoff; Handoff snapshot = null;
        try { if (Enabled) snapshot = CaptureHandoff(kind, state); } catch (Exception ex) { Warn(ex); }
        try { handoff = snapshot; original(state, method); }
        finally { handoff = previous; GC.KeepAlive(snapshot); }
    }
    private static void SlotsPostfix(Skeleton skeleton)
    {
        try
        {
            var request = JerseyLogoRequests.Current?.Operation.Request;
            if (request?.Roots is Roots roots && roots.Target.Skeleton?.Pointer == skeleton?.Pointer) Apply(request);
        }
        catch (Exception ex) { Warn(ex); }
    }
    private static void MaterialsPostfix(SkeletonRenderer skeletonAnim)
    {
        try { var request = JerseyLogoRequests.Current?.Operation.Request; if (request?.Renderer == skeletonAnim?.Pointer) Apply(request); }
        catch (Exception ex) { Warn(ex); }
    }
    private static void GraphicMaterialPostfix(SkeletonGraphic skeletonGraphic)
    {
        try { var request = JerseyLogoRequests.Current?.Operation.Request; if (request?.Renderer == skeletonGraphic?.Pointer) Apply(request); }
        catch (Exception ex) { Warn(ex); }
    }
    private static bool Logo(Target target, bool goalie, ref Texture texture)
    {
        if (!Enabled || !target.Alive) return true;
        ObserveContext();
        var request = JerseyLogoRequests.Current?.Operation.Request;
        if (request == null || request.Renderer != target.Pointer)
        {
            // A native UI clear/identity update is a real new native write. It
            // invalidates older authored work; it never acquires its override.
            JerseyLogoRequests.Begin(target.Pointer, Pointer(target.Skin), IntPtr.Zero, IntPtr.Zero, goalie, context, default, new Roots { Target = target });
        }
        return JerseyLogoRequests.FinalLogo(request, target.Pointer, Pointer(target.Skin), goalie, ref texture);
    }
    private static bool WorldLogo(SkeletonRenderer skeletonAnimation, bool isGoalie, ref Texture2D logo, out bool __state)
    {
        __state = true;
        try { Texture value = logo; __state = Logo(new Target { World = skeletonAnimation }, isGoalie, ref value); logo = value as Texture2D; }
        catch (Exception ex) { Warn(ex); }
        return __state;
    }
    private static bool GraphicLogo(SkeletonGraphic skeletonGraphic, bool isGoalie, ref Texture logo, out bool __state)
    {
        __state = true;
        try { __state = Logo(new Target { Graphic = skeletonGraphic }, isGoalie, ref logo); }
        catch (Exception ex) { Warn(ex); }
        return __state;
    }
    private static void WorldLogoDone(SkeletonRenderer skeletonAnimation, Texture2D logo, bool __state)
    { if (__state && skeletonAnimation != null) JerseyLogoRequests.DidDisplay(skeletonAnimation.Pointer, logo); }
    private static void GraphicLogoDone(SkeletonGraphic skeletonGraphic, Texture logo, bool __state)
    { if (__state && skeletonGraphic != null) JerseyLogoRequests.DidDisplay(skeletonGraphic.Pointer, logo); }

    private static void OwnerLogoScope(Target target, IntPtr owner, SkaterData player, out JerseyLogoRequests.Scope scope)
    {
        scope = null;
        if (!Enabled || target?.Alive != true || JerseyLogoRequests.Current != null) return;
        try
        {
            ObserveContext();
            var request = JerseyLogoRequests.Find(target.Pointer, owner, player?.Pointer ?? IntPtr.Zero, Pointer(target.Skin));
            if (request != null) scope = JerseyLogoRequests.Enter(JerseyLogoRequests.Start(0, request), IntPtr.Zero);
        }
        catch (Exception ex) { Warn(ex); }
    }
    private static void ForwardLogoScope(Forward __instance, out JerseyLogoRequests.Scope __state)
        => OwnerLogoScope(WorldTarget(__instance.skeletonAnimation), __instance.Pointer, __instance._forwardData, out __state);
    private static void GoalieLogoScope(Goaltender.Goaltender __instance, out JerseyLogoRequests.Scope __state)
        => OwnerLogoScope(WorldTarget(__instance.skeletonAnimation), __instance.Pointer, __instance.goaltenderData, out __state);
    private static void OwnerLogoFinalizer(JerseyLogoRequests.Scope __state) => __state?.Dispose();

    internal static void Install()
    {
        if (Enabled || detours.Count > 0) return;
        try
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || IntPtr.Size != 8) throw new PlatformNotSupportedException("Audited Windows x64 required.");
            var module = Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Single(m => m.ModuleName.Equals("GameAssembly.dll", StringComparison.OrdinalIgnoreCase));
            var profile = NativeCompatibility.Load(module.FileName);
            using var stream = typeof(JerseyLogoNativeHooks).Assembly.GetManifestResourceStream("JerseyLogoTargets.json");
            var contracts = JsonSerializer.Deserialize<RawNativeHookContract[]>(new StreamReader(stream).ReadToEnd());
            if (profile.GameHash != "92E73E6D0BE549DD8C695302B7AAE6A77CBBF4DE27F27D26ED01F4B3DABC5B8D" || profile.MetadataHash != "1009F63178E260153CBBF224338BDC9124F9468D0F462F7A58327F638188914A")
                throw new InvalidDataException("Jersey adapters require pinned build 26085704.");
            ValidateContracts(contracts, profile.RawHooks);
            foreach (var dependency in new[] {
                ("MainScriptsAssembly", "7F83636ADC1951E9BE9CE73D3DE3D2BEC7EC707A2AC17A72A0745E733EEF51F3"),
                ("spine-csharp", "895B13B3EC6AF4B9D27B389C7188321770750B516371F2B2A442BBFDB7253973"),
                ("Il2CppInterop.HarmonySupport", "F7612EA2E5A4D473B771F34EE3B20CF32DC0C4ECEA0185416474999A17FBCC07"),
                ("Il2CppInterop.Runtime", "65AB051A681C2C1E52DF7596FF738BE401AAE72CFF882FF18241CF54A3EA38A4") })
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == dependency.Item1);
                if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))) != dependency.Item2) throw new InvalidDataException("Jersey bridge dependency changed: " + dependency.Item1);
            }
            foreach (var contract in contracts)
            {
                var bytes = new byte[contract.Size]; Marshal.Copy(module.BaseAddress + contract.Rva, bytes, 0, bytes.Length);
                if (Convert.ToHexString(SHA256.HashData(bytes)) != contract.Sha256 || !bytes.Take(contract.Prologue.Length / 2).SequenceEqual(Convert.FromHexString(contract.Prologue)))
                    throw new InvalidDataException("Native jersey target changed: " + contract.Id);
            }
            // No patch is installed until every actual target has passed.
            foreach (var contract in contracts) InstallRaw(module.BaseAddress, contract);
            harmony = new Harmony("EndlessMode.JerseyLogo"); InstallManaged(harmony);
            Enabled = true; Receipt("installed", "");
        }
        catch (Exception ex)
        {
            Enabled = false;
            bool rollbackFailed = false;
            try { harmony?.UnpatchSelf(); } catch (Exception rollback) { rollbackFailed = true; Warn(rollback); }
            for (int i = detours.Count - 1; i >= 0; --i)
            {
                try { detours[i].Dispose(); detours.RemoveAt(i); installed.RemoveAt(i); }
                catch (Exception rollback) { rollbackFailed = true; Warn(rollback); }
            }
            // Keep callback/original delegates rooted if a provider refused removal.
            if (!rollbackFailed) callbacks.Clear();
            Receipt(rollbackFailed ? "rollback-incomplete-disabled" : "blocked", ex.Message); Warn(ex);
        }
    }
    internal static void ValidateContracts(RawNativeHookContract[] expected, IEnumerable<RawNativeHookContract> approved)
    {
        RawNativeHookContract.RequireExactSet(expected, approved, "jersey-logo");
    }
    private static void InstallRaw(IntPtr module, RawNativeHookContract contract)
    {
        var address = module + contract.Rva; int kind = contract.Scope;
        if (contract.Kind == "move")
        {
            MoveFn original = null;
            MoveFn callback = (s, m) => { if (kind <= Graphic) Move(kind, original, s, m); else Transient(kind, original, s, m); };
            callbacks.Add(callback); detours.Add(INativeDetour.CreateAndApply(address, callback, out original));
        }
        else if (contract.Kind == "bridge")
        {
            BridgeFn original = null; BridgeFn callback = (s, r, m) => Bridge(kind, original, s, r, m);
            callbacks.Add(callback); detours.Add(INativeDetour.CreateAndApply(address, callback, out original));
        }
        else if (contract.Kind == "result")
        {
            MoveFn original = null; MoveFn callback = (r, m) => { JerseyLogoRequests.Finish(kind, r, false, Apply, () => original(r, m), Warn); };
            callbacks.Add(callback); detours.Add(INativeDetour.CreateAndApply(address, callback, out original));
        }
        else if (contract.Kind == "fault")
        {
            FaultFn original = null; FaultFn callback = (r, e, m) => { JerseyLogoRequests.Finish(kind, r, true, Apply, () => original(r, e, m), Warn); };
            callbacks.Add(callback); detours.Add(INativeDetour.CreateAndApply(address, callback, out original));
        }
        else if (contract.Kind == "builder-fault")
        {
            BuilderFaultFn original = null; BuilderFaultFn callback = (b, e) => { JerseyLogoRequests.MarkBuilderFault(b); original(b, e); };
            callbacks.Add(callback); detours.Add(INativeDetour.CreateAndApply(address, callback, out original));
        }
        else throw new InvalidDataException("Unknown jersey callback kind.");
        installed.Add(contract);
    }
    private static void InstallManaged(Harmony h)
    {
        void Patch(string name, Type[] args, string prefix = null, string postfix = null)
        {
            var target = typeof(T2TSkeletonExtensions).GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly, null, args, null) ?? throw new MissingMethodException(name);
            h.Patch(target, prefix == null ? null : new HarmonyMethod(typeof(JerseyLogoNativeHooks), prefix), postfix == null ? null : new HarmonyMethod(typeof(JerseyLogoNativeHooks), postfix));
        }
        h.Patch(AccessTools.DeclaredMethod(typeof(SkinHelper), "GetSkinForForward", new[] { typeof(SkeletonData), typeof(ForwardData), typeof(bool), typeof(bool), typeof(bool), typeof(bool) }), postfix: new HarmonyMethod(typeof(JerseyLogoNativeHooks), nameof(ForwardComposite)));
        h.Patch(AccessTools.DeclaredMethod(typeof(SkinHelper), "GetSkinForGoalie", new[] { typeof(SkeletonData), typeof(GoaltenderData), typeof(bool) }), postfix: new HarmonyMethod(typeof(JerseyLogoNativeHooks), nameof(GoalieComposite)));
        Patch("SetLogo", new[] { typeof(SkeletonRenderer), typeof(Texture2D), typeof(bool) }, nameof(WorldLogo), nameof(WorldLogoDone));
        Patch("SetLogo", new[] { typeof(SkeletonGraphic), typeof(Texture), typeof(bool) }, nameof(GraphicLogo), nameof(GraphicLogoDone));
        Patch("OverrideSlotWithCustomizationMaterial", new[] { typeof(SkeletonGraphic), typeof(Skin), typeof(TeamColorsData), typeof(string), typeof(Il2CppSystem.Collections.Generic.Dictionary<Texture, Material>) }, postfix: nameof(GraphicMaterialPostfix));
        Patch("SetupSkeletonSlots", new[] { typeof(Skeleton), typeof(T2TSkeletonExtensions.CustomizationFlags), typeof(bool) }, postfix: nameof(SlotsPostfix));
        foreach (var owner in new[] { (typeof(Forward), nameof(ForwardLogoScope)), (typeof(Goaltender.Goaltender), nameof(GoalieLogoScope)) })
            h.Patch(AccessTools.DeclaredMethod(owner.Item1, "InitializeLogo", new[] { typeof(TeamData) }),
                prefix: new HarmonyMethod(typeof(JerseyLogoNativeHooks), owner.Item2), finalizer: new HarmonyMethod(typeof(JerseyLogoNativeHooks), nameof(OwnerLogoFinalizer)));
        Patch("SetupSkeletonSlotsMaterials", new[] { typeof(bool), typeof(SkeletonRenderer), typeof(Material), typeof(T2TSkeletonExtensions.CustomizationFlags) }, postfix: nameof(MaterialsPostfix));
    }
    private static void Receipt(string status, string reason)
    {
        try
        {
            string path = Path.Combine(Path.GetDirectoryName(typeof(JerseyLogoNativeHooks).Assembly.Location), "jersey-logo-installed-targets.json");
            File.WriteAllText(path, JsonSerializer.Serialize(new { status, reason, nativeExecutionValidated = false, installed = installed.ToArray(), managed = harmony?.GetPatchedMethods().Select(m => m.DeclaringType.FullName + "." + m).ToArray() ?? Array.Empty<string>() }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { Warn(ex); }
    }
}
