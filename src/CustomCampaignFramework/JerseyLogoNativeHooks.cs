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
using Il2CppInterop.Common;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Runtime;
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
    private static NativeOpponentScopes opponentScopes;
    private static Harmony harmony;
    internal static bool Enabled { get; private set; }
    private static RawNativeHookContract[] Contracts()
    {
        using var stream = typeof(JerseyLogoNativeHooks).Assembly.GetManifestResourceStream("JerseyLogoTargets.json");
        return JsonSerializer.Deserialize<RawNativeHookContract[]>(new StreamReader(stream).ReadToEnd());
    }
    internal static OpponentScopeRoute RegisterOpponentScope(string consumer, MethodInfo method, out string reason)
    {
        // Reserve targets even when native validation/installation was blocked.
        opponentScopes ??= new NativeOpponentScopes(Contracts());
        if (opponentScopes.Module == IntPtr.Zero)
            opponentScopes.Module = Process.GetCurrentProcess().Modules.Cast<ProcessModule>()
                .Single(m => m.ModuleName.Equals("GameAssembly.dll", StringComparison.OrdinalIgnoreCase)).BaseAddress;
        return opponentScopes.Register(consumer, method, NativeAddress, out reason);
    }
    private static unsafe IntPtr NativeAddress(MethodInfo method)
    {
        var field = Il2CppInteropUtils.GetIl2CppMethodInfoPointerFieldForGeneratedMethod(method)
            ?? throw new MissingFieldException("Generated native MethodInfo pointer.");
        var pointer = (IntPtr)field.GetValue(null);
        if (pointer == IntPtr.Zero) return IntPtr.Zero;
        return UnityVersionHandler.Wrap((Il2CppMethodInfo*)pointer).MethodPointer;
    }
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
        internal bool StartsPreview;
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
            targets[unknown.Pointer] = unknown;
            var native = JerseyLogoRequests.BeginNative(unknown.Pointer, Pointer(unknown.Skin),
                kind == GoalieOwner || (kind == World && Flag(state, 0x44)) || (kind == Graphic && Flag(state, 0x40)),
                context, new Roots { Target = unknown });
            return JerseyLogoRequests.Start(kind, native);
        }
        Target target; Selection selected = null; object owner = null; IntPtr ownerPtr = IntPtr.Zero; Skin skin;
        bool goalie;
        bool newPreview = false;
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
            newPreview = kind == Graphic && handoff?.StartsPreview == true && handoff.Renderer == target.Pointer && handoff.Selection?.Context == context;
            if (newPreview)
            { selected = handoff.Selection; owner = handoff.Root; ownerPtr = handoff.Owner; }
            else if (parent != null && parent.Renderer == target.Pointer && parent.Roots is Roots saved && parent.Goalie == goalie)
            { selected = saved.Selection; owner = saved.Owner; ownerPtr = parent.Owner; }
            else if (handoff?.Renderer == target.Pointer && handoff.Selection?.Context == context)
            { selected = handoff.Selection; owner = handoff.Root; ownerPtr = handoff.Owner; }
            else if (composites.TryGetValue(composite, out var found) && found.Context == context) selected = found;
        }
        if (!target.Alive) return null;
        var roots = new Roots { Target = target, Selection = selected, Owner = owner, Composite = skin };
        var player = selected?.Player?.Pointer ?? IntPtr.Zero;
        var inherited = !newPreview && (kind is World or Graphic) ? JerseyLogoRequests.Current?.Operation.Request : null;
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
            JerseyLogoRequests.BeginNative(target.Pointer, Pointer(target.Skin), goalie, context, new Roots { Target = target });
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

    // Pinned UpdatePlayerSkeleton passes home=true and onlyHead=false into
    // GetForwardSkin, then the same player/composite into graphic ApplySkin.
    private static void SelectedPlayerScope(SelectedPlayerUI __instance, ForwardData forwardData, out Handoff __state)
    {
        __state = handoff;
        if (!Enabled) return;
        try
        {
            ObserveContext();
            var target = new Target { Graphic = __instance.skeletonGraphic };
            if (!target.Alive) { handoff = null; return; }
            // Selection change retires the previous preview even if native setup fails.
            JerseyLogoRequests.BeginNative(target.Pointer, Pointer(target.Skin), false, context, new Roots { Target = target });
            if (forwardData == null) { handoff = null; return; }
            handoff = new Handoff { Root = __instance, Owner = __instance.Pointer, Renderer = target.Pointer,
                Selection = Select(forwardData, true, __instance), StartsPreview = true };
        }
        catch (Exception ex) { handoff = null; Warn(ex); }
    }
    private static void SelectedPlayerFinalizer(Handoff __state) => handoff = __state;
    private static void SelectedPlayerLogoScope(SelectedPlayerUI __instance, ForwardData _forwardData, out JerseyLogoRequests.Scope __state)
    {
        __state = null;
        if (!Enabled || _forwardData == null) return;
        try
        {
            ObserveContext();
            var target = new Target { Graphic = __instance.skeletonGraphic };
            if (!target.Alive || __instance.currentForwardData?.Pointer != _forwardData.Pointer) return;
            var active = JerseyLogoRequests.Current?.Operation.Request;
            // An already executing stale owner must not acquire a newer generation.
            var request = active?.Renderer == target.Pointer && active.Owner == __instance.Pointer && active.Player == _forwardData.Pointer
                ? active : JerseyLogoRequests.FindOwner(target.Pointer, __instance.Pointer, _forwardData.Pointer);
            if (request?.Roots is not Roots roots || roots.Selection?.Home != true || request.Goalie) return;
            if (ReferenceEquals(active, request)) return;
            // No skin equality here: the exact request may still be waiting for its
            // composite. FinalLogo checks that equality before replacing the texture.
            __state = JerseyLogoRequests.Enter(JerseyLogoRequests.Start(0, request), IntPtr.Zero);
        }
        catch (Exception ex) { Warn(ex); }
    }

    internal static void Install()
    {
        if (Enabled || detours.Count > 0) return;
        try
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || IntPtr.Size != 8) throw new PlatformNotSupportedException("Audited Windows x64 required.");
            var module = Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Single(m => m.ModuleName.Equals("GameAssembly.dll", StringComparison.OrdinalIgnoreCase));
            var profile = NativeCompatibility.Load(module.FileName);
            var contracts = Contracts();
            opponentScopes = new NativeOpponentScopes(contracts) { Module = module.BaseAddress };
            if (profile.GameHash != "FD2E6FDF3900272C2E9B201F3280AF15C44D330D28E131E00FE6C99D007C4B93" || profile.MetadataHash != "22E6F00B8CE5F83A3D167410FE6B7C76086B201BC48CB83CFAA3BD4E577CAC37")
                throw new InvalidDataException("Jersey adapters require pinned build 26150666.");
            JerseyLogoManagedTargets.ValidateBeforeInstall(contracts, profile.RawHooks, profile.NamedHooks);
            // Resolve the complete named plan (including patch callbacks) before any
            // native mutation. Installation consumes these same resolved targets.
            var managed = ResolveManaged();
            foreach (var dependency in new[] {
                ("MainScriptsAssembly", "83FAB7CBC1625A290AA5B72699F78BD2C300D3245414F6C3976184F8D2A5C9AF"),
                ("spine-csharp", "DEC561B73C5328C01275C395E0180A75A4355C0AD87960BDEEDBBDD78713EBAD"),
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
            harmony = new Harmony("EndlessMode.JerseyLogo"); InstallManaged(harmony, managed);
            Enabled = true; Receipt("installed", "");
        }
        catch (Exception ex)
        {
            Enabled = false;
            bool rollbackFailed = false;
            try { harmony?.UnpatchSelf(); } catch (Exception rollback) { rollbackFailed = true; Warn(rollback); }
            for (int i = detours.Count - 1; i >= 0; --i)
            {
                try { detours[i].Dispose(); opponentScopes.Release(installed[i]); detours.RemoveAt(i); installed.RemoveAt(i); }
                catch (Exception rollback) { rollbackFailed = true; Warn(rollback); }
            }
            // Keep callback/original delegates rooted if a provider refused removal.
            if (!rollbackFailed) callbacks.Clear();
            Receipt(rollbackFailed ? "rollback-incomplete-disabled" : "blocked", ex.Message); Warn(ex);
        }
    }
    private static void InstallRaw(IntPtr module, RawNativeHookContract contract)
    {
        var address = module + contract.Rva; int kind = contract.Scope;
        if (contract.Kind == "move")
        {
            MoveFn original = null;
            MoveFn scopedOriginal = (s, m) => OpponentEffectScope.RunNative(opponentScopes.Consumer(contract.Id), () => original(s, m));
            MoveFn callback = (s, m) => { if (kind <= Graphic) Move(kind, scopedOriginal, s, m); else Transient(kind, scopedOriginal, s, m); };
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
        opponentScopes.Own(contract);
    }
    private sealed class ManagedPatch
    {
        internal MethodInfo Target;
        internal HarmonyMethod Prefix, Postfix, Finalizer;
    }
    private static ManagedPatch[] ResolveManaged()
    {
        HarmonyMethod Callback(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            return new HarmonyMethod(typeof(JerseyLogoNativeHooks).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException("Jersey patch callback: " + name));
        }
        return JerseyLogoManagedTargets.Required.Select(c => new ManagedPatch {
            Target = JerseyLogoManagedTargets.Resolve(c, AppDomain.CurrentDomain.GetAssemblies()),
            Prefix = Callback(c.Prefix), Postfix = Callback(c.Postfix), Finalizer = Callback(c.Finalizer)
        }).ToArray();
    }
    private static void InstallManaged(Harmony h, ManagedPatch[] plan)
    {
        foreach (var patch in plan) h.Patch(patch.Target, prefix: patch.Prefix, postfix: patch.Postfix, finalizer: patch.Finalizer);
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
