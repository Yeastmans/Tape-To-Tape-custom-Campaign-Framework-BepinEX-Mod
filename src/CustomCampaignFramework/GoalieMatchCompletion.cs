using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CampaignCore;
using Cysharp.Threading.Tasks;
using Data;
using HarmonyLib;
using State;
using State.EndOfGame;
using State.GameModeControllers;
using State.GameModeControllers.Shootout;
using STS.Map;
using Tape2Tape;

namespace EndlessMode;

// Resolve-only hooks. Subscribe only as part of the complete reviewed installer,
// undo with Unsubscribe on any partial installation failure. No PatchAll entry.
internal static class GoalieMatchCompletion
{
    internal const string Feature = "goalie-match-completion";
    internal static readonly NamedManagedHookContract[] Required = {
        Hook("boss", "STS.Map.BossMapNode", "LaunchMatch", "System.Void", Array.Empty<string>(), "NodePrefix", "NodeFinalizer"),
        Hook("challenge", "STS.Map.ChallengeMapNode", "LaunchMatch", "System.Void", Array.Empty<string>(), "NodePrefix", "NodeFinalizer"),
        Hook("elite", "STS.Map.EliteMapNode", "LaunchMatch", "System.Void", Array.Empty<string>(), "NodePrefix", "NodeFinalizer"),
        Hook("initialize", "State.GameModeControllers.GameControllerBase", "InitializeGoaltenders", "System.Void", Array.Empty<string>(), "InitializePrefix", "InitializeFinalizer"),
        Hook("side", "Tape2Tape.Team", "SwitchSide", "System.Void", new[] { "System.Boolean" }, "SidePrefix", "SideFinalizer"),
        Hook("north-goalie", "Goaltender.GoaltenderNorth", "Initialize", "System.Void", new[] { "Data.GoaltenderData", "System.Boolean" }, "NorthPrefix", "NorthFinalizer"),
        Hook("goalie", "Goaltender.Goaltender", "Initialize", "System.Void", new[] { "Data.GoaltenderData", "System.Boolean" }, "GoaliePrefix", "GoalieFinalizer"),
        Hook("faceoff", "GameManager", "OnFaceoff", "System.Void", new[] { "Events.FaceoffArgs" }, "ExecutionPrefix", "NormalPlayFinalizer"),
        Hook("puckdrop", "GameManager", "OnPuckDropLaunch", "System.Void", Array.Empty<string>(), "ExecutionPrefix", "NormalPlayFinalizer"),
        Hook("shootout", "State.GameModeControllers.Shootout.InRoundState", "OnEnter", "System.Void", Array.Empty<string>(), "ExecutionPrefix", "ShootoutPlayFinalizer"),
        Hook("normal-restart", "State.GameModeControllers.NormalMatch.NormalMatchGameModeController", "OnRestart", "System.Void", Array.Empty<string>(), "ExecutionPrefix", "RestartFinalizer"),
        Hook("shootout-restart", "State.GameModeControllers.Shootout.ShootoutGameModeController", "OnRestart", "System.Void", Array.Empty<string>(), "ExecutionPrefix", "RestartFinalizer"),
        Hook("overtime", "GameLoader", "SwitchToShootout", "System.Void", new[] { "System.Int32" }, "SwitchPrefix", "SwitchFinalizer"),
        Hook("departure", "Rogue.FreeAgents.FreeAgentManager", "HandleFreeAgentsLeaving", "System.Void", Array.Empty<string>(), "DeparturePrefix", "DepartureFinalizer"),
        Hook("context", "State.EndOfGame.EndOfGameFlowController", "Initialize", "System.Void", Array.Empty<string>(), "ExecutionPrefix", "FlowFinalizer"),
        Hook("update", "State.EndOfGame.Steps.CampaignUpdateStep", "Execute", "Cysharp.Threading.Tasks.UniTask", new[] { "State.EndOfGame.EndOfGameContext" }, "UpdatePrefix", "UpdateFinalizer")
    };
    private static NamedManagedHookContract Hook(string id, string type, string method, string result, string[] args, string prefix, string finalizer)
        => new() { Feature = Feature, Id = "goalie-completion-" + id, Assembly = "MainScriptsAssembly", DeclaringType = type,
            Method = method, ReturnType = result, Parameters = args, Prefix = prefix, Finalizer = finalizer };
    internal static (MethodInfo Native, MethodInfo Prefix, MethodInfo Finalizer)[] Resolve()
        => Required.Select(c => (JerseyLogoManagedTargets.Resolve(c, AppDomain.CurrentDomain.GetAssemblies()),
            c.Prefix.Length == 0 ? null : AccessTools.Method(typeof(GoalieMatchCompletion), c.Prefix) ?? throw new MissingMethodException(c.Prefix),
            AccessTools.Method(typeof(GoalieMatchCompletion), c.Finalizer) ?? throw new MissingMethodException(c.Finalizer))).ToArray();

    // The shipped HarmonyX allocates __runOriginal only while writing a prefix
    // or postfix. These otherwise-finalizer-only entries need a real prefix so
    // finalizers can observe native completion and foreign skips. HarmonyX ANDs
    // prefix results, so returning true never revives a foreign-skipped body.
    private static bool ExecutionPrefix() => true;

    private sealed class Session
    {
        internal GoalieMatchLaunchGate.LaunchOrigin Origin;
        internal IntPtr Blueprint;
        internal string Participant;
        internal bool Invalid, Completing;
    }
    private sealed class Binding
    {
        internal Session Session;
        internal GameControllerBase Controller;
        internal GameManager Manager;
        internal Goaltender.Goaltender Goalie;
        internal GoaltenderData Live;
    }
    private sealed class Initialization
    {
        internal Initialization Previous;
        internal Binding Binding;
        internal bool Observed;
    }
    private sealed class SideHandoff
    {
        internal SideHandoff Previous;
        internal Binding Before, Candidate;
        internal Team Team;
        internal GoaltenderData Source;
        internal bool North, Failed;
        internal string FailureStage, Entry, InitializeEntry, InitializeExit;
        internal int InitializeCalls;
    }
    private sealed class NorthInitialization
    {
        internal NorthInitialization Previous;
        internal Binding Binding;
        internal SideHandoff Side;
        internal Initialization Scope;
        internal Goaltender.GoaltenderNorth Receiver;
        internal GoaltenderData Input;
        internal bool Observed, Failed;
    }
    private sealed class CloneObservation
    {
        internal Initialization Scope;
        internal SideHandoff Side;
        internal GoaltenderData First;
        internal NorthInitialization North;
    }
    private sealed class Update
    {
        internal Update Previous;
        internal Binding Binding;
        internal EndOfGameContext Context;
        internal bool DepartureSeen, ProtectionKnown, Prevented;
    }
    private sealed class OriginContext
    {
        internal EndOfGameContext Context;
        internal Session Session;
        internal IntPtr Controller;
    }
    private sealed class Departure
    {
        internal Departure Previous;
        internal Update Update;
        internal Rogue.FreeAgents.FreeAgentManager Manager;
        internal bool SeenCustom, Custom, OriginalAllowed, Known, Prevented;
    }
    private static readonly Dictionary<IntPtr, Binding> Controllers = new();
    private static readonly Dictionary<IntPtr, Binding> Managers = new();
    private static readonly Dictionary<IntPtr, OriginContext> Contexts = new();
    private static GoalieMatchLaunchGate.LaunchOrigin previewOrigin;
    private static Session pendingScene;
    private static Binding pendingOvertime;
    private static bool subscribed;
    [ThreadStatic] private static Initialization initialization;
    [ThreadStatic] private static SideHandoff sideHandoff;
    [ThreadStatic] private static NorthInitialization northInitialization;
    [ThreadStatic] private static Update update;
    [ThreadStatic] private static Departure departure;

    internal static void Subscribe()
    {
        if (subscribed) return;
        GoalieMatchLaunchGate.LaunchAuthorized += ObserveOrigin;
        GoalieContractRuntime.RetentionEvaluated += ObserveRetention;
        subscribed = true;
    }
    internal static void Unsubscribe()
    {
        GoalieMatchLaunchGate.LaunchAuthorized -= ObserveOrigin;
        GoalieContractRuntime.RetentionEvaluated -= ObserveRetention;
        subscribed = false; previewOrigin = null; pendingScene = null; pendingOvertime = null;
        Controllers.Clear(); Managers.Clear(); Contexts.Clear(); initialization = null; sideHandoff = null; northInitialization = null; update = null; departure = null;
    }
    private static void ObserveOrigin(GoalieMatchLaunchGate.LaunchOrigin origin) => previewOrigin = origin;
    private static bool Owned(Session session) => session != null && !session.Invalid && !session.Completing
        && GoalieMatchLaunchGate.TryReuseOrigin(session.Origin, session.Origin.Campaign, session.Origin.HomeTeam);
    private static bool Bound(Binding binding) => BindingFailure(binding) == null;
    private static string BindingFailure(Binding binding)
    {
        if (binding == null || !Owned(binding.Session)) return "origin";
        if (GameManager.Instance?.Pointer != binding.Manager.Pointer) return "manager";
        if (binding.Controller.campaignState?.Pointer != binding.Session.Origin.Campaign.Pointer) return "controller-campaign";
        if (binding.Controller.teamSelectionState?.gameType != ScriptableStates.GameType.Rogue) return "game-type";
        if (binding.Controller.teamSelectionState.homeTeam?.Pointer != binding.Session.Origin.HomeTeam.Pointer) return "selection-team";
        if (binding.Manager._homeTeam?.goaltender?.Pointer != binding.Goalie?.Pointer) return "goalie-receiver";
        if (binding.Live == null || binding.Goalie.goaltenderData?.Pointer != binding.Live.Pointer) return "live-data";
        if (binding.Goalie._skaterData?.Pointer != binding.Live.Pointer) return "skater-data";
        if (binding.Live.id != binding.Session.Origin.Attempt.GoalieId) return "live-id";
        return null;
    }
    private static void ReportBindingFailure(Binding binding, string reason)
    {
        // Read-only evidence before invalidation. Keep pointer identity strict:
        // a same-ID clone is not proof of an authorized native handoff.
        static string Pointer(IntPtr? value) => value?.ToString("X") ?? "null";
        var origin = binding.Session.Origin;
        Plugin.Log.LogWarning("[GoalieIdentity] reason=" + reason
            + "; run=" + origin.Attempt.RunId + "; token=" + origin.Attempt.Token
            + "; expectedId=" + origin.Attempt.GoalieId + "; observedId=" + binding.Goalie?.goaltenderData?.id
            + "; participant=" + binding.Session.Participant
            + "; expectedManager=" + Pointer(binding.Manager?.Pointer) + "; manager=" + Pointer(GameManager.Instance?.Pointer)
            + "; controller=" + Pointer(binding.Controller?.Pointer)
            + "; expectedCampaign=" + Pointer(origin.Campaign?.Pointer) + "; campaign=" + Pointer(binding.Controller?.campaignState?.Pointer)
            + "; expectedTeam=" + Pointer(origin.HomeTeam?.Pointer) + "; selectionTeam=" + Pointer(binding.Controller?.teamSelectionState?.homeTeam?.Pointer)
            + "; gameType=" + binding.Controller?.teamSelectionState?.gameType
            + "; expectedReceiver=" + Pointer(binding.Goalie?.Pointer) + "; receiver=" + Pointer(binding.Manager?._homeTeam?.goaltender?.Pointer)
            + "; expectedLive=" + Pointer(binding.Live?.Pointer) + "; goalieData=" + Pointer(binding.Goalie?.goaltenderData?.Pointer)
            + "; skaterData=" + Pointer(binding.Goalie?._skaterData?.Pointer));
    }
    private static void NodePrefix(MapNode __instance, out GoalieMatchLaunchGate.LaunchOrigin __state)
    {
        __state = previewOrigin?.Destination?.Pointer == __instance?.Pointer ? previewOrigin : null;
    }
    private static Exception NodeFinalizer(MapNode __instance, GoalieMatchLaunchGate.LaunchOrigin __state, bool __runOriginal, Exception __exception)
    {
        try {
            if (__state != null && __instance?.Blueprint != null && __exception == null && __runOriginal
                && GoalieMatchLaunchGate.MatchesDestination(__state, __state.Campaign, __instance, __state.HomeTeam)) {
                pendingScene = new Session { Origin = __state, Blueprint = __instance.Blueprint.Pointer };
                pendingOvertime = null;
                // Old contexts are never rebound on demand. Removing them is a
                // permanent rejection of delayed callbacks, not token borrowing.
                Controllers.Clear(); Managers.Clear(); Contexts.Clear();
                if (ReferenceEquals(previewOrigin, __state)) previewOrigin = null;
            }
        } catch (Exception ex) { Warn(ex.Message); }
        return __exception;
    }
    private static void InitializePrefix(GameControllerBase __instance, out Initialization __state)
    {
        __state = new Initialization { Previous = initialization }; initialization = __state;
        try {
            Session session = pendingScene ?? (Controllers.TryGetValue(__instance.Pointer, out var existing) ? existing.Session : null);
            var manager = GameManager.Instance;
            if (!Owned(session) || manager?._homeTeam?.goaltender == null
                || __instance.campaignState?.Pointer != session.Origin.Campaign.Pointer
                || __instance.teamSelectionState?.gameType != ScriptableStates.GameType.Rogue
                || __instance.teamSelectionState.homeTeam?.Pointer != session.Origin.HomeTeam.Pointer
                || __instance.teamSelectionState.homeTeam.goalie?.id != session.Origin.Attempt.GoalieId) return;
            if (ReferenceEquals(session, pendingScene)) pendingScene = null;
            __state.Binding = new Binding { Session = session, Controller = __instance, Manager = manager, Goalie = manager._homeTeam.goaltender };
        } catch (Exception ex) { Warn(ex.Message); }
    }
    private static bool SideOwned(SideHandoff scope)
    {
        var binding = scope?.Before;
        return binding != null && Owned(binding.Session)
            && GameManager.Instance?.Pointer == binding.Manager.Pointer
            && binding.Manager._homeTeam?.Pointer == scope.Team.Pointer && scope.Team.isHomeTeam
            && scope.Team.teamSelection?.Pointer == binding.Controller.teamSelectionState?.Pointer
            && binding.Controller.campaignState?.Pointer == binding.Session.Origin.Campaign.Pointer
            && scope.Team.teamSelection?.gameType == ScriptableStates.GameType.Rogue
            && scope.Team.teamSelection.homeTeam?.Pointer == binding.Session.Origin.HomeTeam.Pointer
            && scope.Team.teamSelection.homeTeam.goalie?.Pointer == scope.Source.Pointer
            && scope.Source.id == binding.Session.Origin.Attempt.GoalieId;
    }
    private static string CaptureSide(Func<string> capture)
    {
        try { return capture(); }
        catch (Exception ex) { return "snapshot-error:" + ex.GetType().Name; }
    }
    private static string SideSnapshot(SideHandoff scope)
    {
        static string P(IntPtr? value) => value?.ToString("X") ?? "null";
        var before = scope.Before; var origin = before?.Session.Origin;
        return "owned=" + SideOwned(scope) + "; manager=" + P(GameManager.Instance?.Pointer)
            + "; expectedManager=" + P(before?.Manager?.Pointer) + "; team=" + P(scope.Team?.Pointer)
            + "; managerHome=" + P(before?.Manager?._homeTeam?.Pointer) + "; isHome=" + scope.Team?.isHomeTeam
            + "; selection=" + P(scope.Team?.teamSelection?.Pointer) + "; controllerSelection=" + P(before?.Controller?.teamSelectionState?.Pointer)
            + "; campaign=" + P(before?.Controller?.campaignState?.Pointer) + "; expectedCampaign=" + P(origin?.Campaign?.Pointer)
            + "; selectedHome=" + P(scope.Team?.teamSelection?.homeTeam?.Pointer) + "; expectedHome=" + P(origin?.HomeTeam?.Pointer)
            + "; gameType=" + scope.Team?.teamSelection?.gameType + "; source=" + P(scope.Source?.Pointer)
            + "; currentSource=" + P(scope.Team?.teamSelection?.homeTeam?.goalie?.Pointer)
            + "; sourceId=" + scope.Source?.id + "; expectedId=" + origin?.Attempt.GoalieId
            + "; requestedNorth=" + scope.North + "; actualNorth=" + scope.Team?.isNorth
            + "; receiver=" + P(scope.Team?.goaltender?.Pointer) + "; receiverTeam=" + P(scope.Team?.goaltender?.Team?.Pointer);
    }
    private static void ReportSideFailure(SideHandoff scope, bool original, Exception error)
    {
        Plugin.Log.LogWarning("[GoalieSide] rejected; utc=" + DateTime.UtcNow.ToString("O")
            + "; token=" + scope.Before.Session.Origin.Attempt.Token + "; stage=" + (scope.FailureStage ?? "outer-finalizer")
            + "; original=" + original + "; exception=" + (error?.GetType().FullName ?? "none")
            + "; currentScope=" + ReferenceEquals(sideHandoff, scope) + "; failed=" + scope.Failed
            + "; initialized=" + scope.InitializeCalls + "; candidate=" + (scope.Candidate != null)
            + "; candidateBinding=" + (scope.Candidate == null ? "missing" : BindingFailure(scope.Candidate) ?? "valid")
            + "; beforeBinding=" + BindingFailure(scope.Before) + "; final={" + SideSnapshot(scope) + "}"
            + "; entry={" + scope.Entry + "}; initializeEntry={" + scope.InitializeEntry + "}; initializeExit={" + scope.InitializeExit + "}");
    }
    private static void SidePrefix(Team __instance, bool __0, out SideHandoff __state)
    {
        __state = new SideHandoff { Previous = sideHandoff, Team = __instance, North = __0 }; sideHandoff = __state;
        try {
            var manager = GameManager.Instance;
            if (manager == null || !Managers.TryGetValue(manager.Pointer, out var before)
                || manager._homeTeam?.Pointer != __instance?.Pointer || !Bound(before)) return;
            __state.Before = before; __state.Source = before.Session.Origin.HomeTeam.goalie;
            var evidenceScope = __state;
            __state.Entry = CaptureSide(() => SideSnapshot(evidenceScope));
            if (!SideOwned(__state) || __state.Previous?.Before != null) { __state.Failed = true; __state.FailureStage = "side-prefix"; }
        } catch (Exception ex) { __state.Failed = true; __state.FailureStage = "side-prefix-exception"; Warn(ex.Message); }
    }
    private static Exception SideFinalizer(SideHandoff __state, bool __runOriginal, Exception __exception)
    {
        try {
            if (__state?.Before == null) return __exception;
            var candidate = __state.Candidate;
            if (__exception == null && __runOriginal && ReferenceEquals(sideHandoff, __state) && !__state.Failed
                && SideOwned(__state) && __state.Team.isNorth == __state.North && candidate != null && Bound(candidate)
                && candidate.Goalie.Team?.Pointer == __state.Team.Pointer) {
                Controllers[candidate.Controller.Pointer] = candidate; Managers[candidate.Manager.Pointer] = candidate;
                if (ReferenceEquals(pendingOvertime, __state.Before)) pendingOvertime = candidate;
                Plugin.Log.LogWarning("[GoalieIdentity] handoff=side; token=" + candidate.Session.Origin.Attempt.Token
                    + "; id=" + candidate.Live.id + "; oldReceiver=" + __state.Before.Goalie.Pointer.ToString("X")
                    + "; receiver=" + candidate.Goalie.Pointer.ToString("X") + "; oldLive=" + __state.Before.Live.Pointer.ToString("X")
                    + "; live=" + candidate.Live.Pointer.ToString("X"));
            } else {
                try { ReportSideFailure(__state, __runOriginal, __exception); }
                finally { Unavailable(__state.Before.Session, "Native goalie side switching did not preserve the originating home goalie."); }
            }
        } catch (Exception ex) { if (__state?.Before != null) Unavailable(__state.Before.Session, ex.Message); }
        finally { if (__state != null && ReferenceEquals(sideHandoff, __state)) sideHandoff = __state.Previous; }
        return __exception;
    }
    // The reviewed North override discards its true caller campaign flag and
    // invokes the base Initialize with false. Witness that exact owned call;
    // never infer authority from receiver type or a matching goalie ID alone.
    private static void NorthPrefix(Goaltender.GoaltenderNorth __instance, GoaltenderData __0, bool __1, out NorthInitialization __state)
    {
        __state = new NorthInitialization { Previous = northInitialization }; northInitialization = __state;
        try {
            var side = sideHandoff?.Before != null ? sideHandoff : null;
            var scope = side == null ? initialization : null;
            var binding = side?.Before ?? scope?.Binding;
            if (!__1 || !Owned(binding?.Session) || __0 == null || __0.id != binding.Session.Origin.Attempt.GoalieId
                || __instance?.Pointer != (side?.Team.goaltender ?? binding.Goalie)?.Pointer) return;
            if (side != null ? !SideOwned(side) || __0.Pointer != side.Source.Pointer
                : __0.Pointer == binding.Session.Origin.HomeTeam.goalie.Pointer) return;
            __state.Binding = binding; __state.Side = side; __state.Scope = scope;
            __state.Receiver = __instance; __state.Input = __0;
            if (__state.Previous?.Binding != null) FailNorth(__state);
        } catch (Exception ex) { FailNorth(__state); Warn(ex.Message); }
    }
    private static bool NorthBaseWitness(NorthInitialization scope, Goaltender.Goaltender receiver, GoaltenderData input, bool campaign)
        => !campaign && !scope.Failed && !scope.Observed && Owned(scope.Binding.Session)
            && receiver?.Pointer == scope.Receiver.Pointer && input?.Pointer == scope.Input.Pointer
            && (scope.Side != null ? ReferenceEquals(sideHandoff, scope.Side) && SideOwned(scope.Side)
                : ReferenceEquals(initialization, scope.Scope));
    private static void FailNorth(NorthInitialization scope)
    {
        scope.Failed = true;
        if (scope.Side != null) { scope.Side.Failed = true; scope.Side.FailureStage ??= "north-override"; }
        else if (scope.Binding != null) scope.Binding.Session.Invalid = true;
    }
    private static Exception NorthFinalizer(NorthInitialization __state, bool __runOriginal, Exception __exception)
    {
        try {
            if (__state?.Binding != null && (__exception != null || !__runOriginal || __state.Failed || !__state.Observed
                || !ReferenceEquals(northInitialization, __state) || !Owned(__state.Binding.Session)
                || (__state.Side != null ? !ReferenceEquals(sideHandoff, __state.Side) : !ReferenceEquals(initialization, __state.Scope))))
                FailNorth(__state);
        } catch (Exception ex) { if (__state != null) FailNorth(__state); Warn(ex.Message); }
        finally { if (__state != null && ReferenceEquals(northInitialization, __state)) northInitialization = __state.Previous; }
        return __exception;
    }
    private static void GoaliePrefix(Goaltender.Goaltender __instance, GoaltenderData __0, bool __1, out CloneObservation __state)
    {
        __state = null;
        try {
            var north = northInitialization?.Binding != null ? northInitialization : null;
            bool campaign = north == null ? __1 : NorthBaseWitness(north, __instance, __0, __1);
            if (north != null && !campaign) FailNorth(north);
            var side = sideHandoff;
            if (side?.Before != null) {
                side.InitializeCalls++;
                side.InitializeEntry = CaptureSide(() => "receiver=" + __instance?.Pointer.ToString("X") + "; receiverTeam=" + __instance?.Team?.Pointer.ToString("X")
                    + "; input=" + __0?.Pointer.ToString("X") + "; inputId=" + __0?.id + "; campaign=" + __1
                    + "; candidate=" + (side.Candidate != null) + "; failed=" + side.Failed + "; " + SideSnapshot(side));
                if (side.Failed || side.Candidate != null || !SideOwned(side) || !campaign || __0?.Pointer != side.Source.Pointer
                    || __instance?.Pointer != side.Team.goaltender?.Pointer || __instance.Team?.Pointer != side.Team.Pointer) {
                    side.Failed = true; side.FailureStage ??= "goalie-prefix"; return;
                }
                __state = new CloneObservation { Side = side, First = __0, North = north }; return;
            }
            var scope = initialization; var binding = scope?.Binding;
            if (binding == null || !campaign || __instance?.Pointer != binding.Goalie.Pointer) return;
            if (__0 == null || __0.id != binding.Session.Origin.Attempt.GoalieId
                || __0.Pointer == binding.Session.Origin.HomeTeam.goalie.Pointer) { binding.Session.Invalid = true; return; }
            __state = new CloneObservation { Scope = scope, First = __0, North = north };
        } catch (Exception ex) { Warn(ex.Message); }
    }
    private static Exception GoalieFinalizer(Goaltender.Goaltender __instance, CloneObservation __state, bool __runOriginal, Exception __exception)
    {
        try {
            if (__state == null) return __exception;
            if (__state.Side is SideHandoff side) {
                var output = __instance.goaltenderData;
                side.InitializeExit = CaptureSide(() => "original=" + __runOriginal + "; exception=" + (__exception?.GetType().FullName ?? "none")
                    + "; currentScope=" + ReferenceEquals(sideHandoff, side) + "; candidate=" + (side.Candidate != null)
                    + "; output=" + output?.Pointer.ToString("X") + "; outputId=" + output?.id
                    + "; input=" + __state.First?.Pointer.ToString("X") + "; skater=" + __instance._skaterData?.Pointer.ToString("X")
                    + "; receiver=" + __instance.Pointer.ToString("X") + "; receiverTeam=" + __instance.Team?.Pointer.ToString("X")
                    + "; " + SideSnapshot(side));
                if (__exception != null || !__runOriginal || !ReferenceEquals(sideHandoff, side) || side.Candidate != null
                    || !SideOwned(side) || output == null || output.Pointer == __state.First.Pointer
                    || output.id != side.Before.Session.Origin.Attempt.GoalieId || __instance._skaterData?.Pointer != output.Pointer
                    || __instance.Pointer != side.Team.goaltender?.Pointer || __instance.Team?.Pointer != side.Team.Pointer) {
                    side.Failed = true; side.FailureStage ??= "goalie-finalizer"; return __exception;
                }
                if (__state.North != null) __state.North.Observed = true;
                side.Candidate = new Binding { Session = side.Before.Session, Controller = side.Before.Controller,
                    Manager = side.Before.Manager, Goalie = __instance, Live = output };
                return __exception;
            }
            var binding = __state.Scope.Binding; var live = __instance.goaltenderData;
            if (__exception != null || !__runOriginal || !ReferenceEquals(initialization, __state.Scope)
                || !Owned(binding.Session) || live == null || live.Pointer == __state.First.Pointer
                || live.id != __state.First.id || __instance._skaterData?.Pointer != live.Pointer) { binding.Session.Invalid = true; return __exception; }
            if (__state.North != null) __state.North.Observed = true;
            binding.Live = live; __state.Scope.Observed = true;
        } catch (Exception ex) { if (__state?.Side != null) { __state.Side.Failed = true; __state.Side.FailureStage ??= "goalie-finalizer-exception"; } if (__state?.Scope?.Binding != null) __state.Scope.Binding.Session.Invalid = true; Warn(ex.Message); }
        return __exception;
    }
    private static Exception InitializeFinalizer(Initialization __state, bool __runOriginal, Exception __exception)
    {
        try {
            if (__state?.Binding is not Binding binding) return __exception;
            if (__exception == null && __runOriginal && __state.Observed && Bound(binding)) {
                Controllers[binding.Controller.Pointer] = binding; Managers[binding.Manager.Pointer] = binding;
            } else Unavailable(binding.Session, "Native goalie initialization did not preserve the originating home goalie.");
        } catch (Exception ex) { if (__state?.Binding != null) __state.Binding.Session.Invalid = true; Warn(ex.Message); }
        finally { if (__state != null && ReferenceEquals(initialization, __state)) initialization = __state.Previous; }
        return __exception;
    }
    private static void Played(Binding binding)
    {
        // A destroyed scene's delayed event is not a live identity mutation.
        if (binding?.Manager?.Pointer != GameManager.Instance?.Pointer) return;
        string failure = BindingFailure(binding);
        if (failure != null) {
            if (Owned(binding?.Session)) {
                try { ReportBindingFailure(binding, failure); }
                finally { Unavailable(binding.Session, "The live goalie identity changed during this match."); }
            }
            return;
        }
        if (!binding.Manager.isGamePlaying) return;
        string id = binding.Live.id;
        if (binding.Session.Participant != null && binding.Session.Participant != id) binding.Session.Invalid = true;
        else binding.Session.Participant = id;
    }
    private static Exception NormalPlayFinalizer(GameManager __instance, bool __runOriginal, Exception __exception)
    {
        try {
            if (__exception == null && __runOriginal && Managers.TryGetValue(__instance.Pointer, out var binding)
                && binding.Controller.GetIl2CppType().FullName == "State.GameModeControllers.NormalMatch.NormalMatchGameModeController") Played(binding);
        } catch (Exception ex) { Warn(ex.Message); }
        return __exception;
    }
    private static Exception ShootoutPlayFinalizer(InRoundState __instance, bool __runOriginal, Exception __exception)
    {
        try {
            if (__exception == null && __runOriginal && __instance?._gameModeController != null
                && Controllers.TryGetValue(__instance._gameModeController.Pointer, out var binding)) Played(binding);
        } catch (Exception ex) { Warn(ex.Message); }
        return __exception;
    }
    private static Exception RestartFinalizer(GameControllerBase __instance, bool __runOriginal, Exception __exception)
    {
        try {
            if (__exception == null && __runOriginal && Controllers.TryGetValue(__instance.Pointer, out var binding) && Bound(binding)) pendingScene = binding.Session;
        } catch (Exception ex) { Warn(ex.Message); }
        return __exception;
    }
    private static void SwitchPrefix(GameLoader __instance, out Session __state)
    {
        __state = null;
        try {
            // Explicit native overtime handoff, before the async wrapper starts
            // reinjecting scene controllers. Never obtain an origin from saved
            // intent or whichever token is current at the new initialization.
            if (__instance?._gameModeController == null || !Controllers.TryGetValue(__instance._gameModeController.Pointer, out var binding)
                || !Bound(binding) || __instance.campaignState?.Pointer != binding.Session.Origin.Campaign.Pointer
                || __instance.teamSelectionState?.homeTeam?.Pointer != binding.Session.Origin.HomeTeam.Pointer) return;
            __state = binding.Session; pendingScene = __state; pendingOvertime = binding;
        } catch (Exception ex) { Warn(ex.Message); }
    }
    private static Exception SwitchFinalizer(Session __state, bool __runOriginal, Exception __exception)
    {
        if (__state != null && (!__runOriginal || __exception != null)) {
            if (ReferenceEquals(pendingScene, __state)) pendingScene = null;
            if (ReferenceEquals(pendingOvertime?.Session, __state)) pendingOvertime = null;
            Unavailable(__state, "The native overtime transition did not complete its entry.");
        }
        return __exception;
    }
    private static Exception FlowFinalizer(EndOfGameFlowController __instance, bool __runOriginal, Exception __exception)
    {
        try {
            var context = __instance?._context;
            if (__exception != null || !__runOriginal || context == null || Contexts.ContainsKey(context.Pointer)) return __exception;
            Session session = pendingScene ?? (__instance._gameModeController != null
                && Controllers.TryGetValue(__instance._gameModeController.Pointer, out var binding) ? binding.Session : null);
            if (!Owned(session) || __instance._campaignState?.Pointer != session.Origin.Campaign.Pointer
                || __instance._teamSelection?.homeTeam?.Pointer != session.Origin.HomeTeam.Pointer
                || context.CampaignState?.Pointer != session.Origin.Campaign.Pointer
                || context.GameModeController?.Pointer != __instance._gameModeController?.Pointer) return __exception;
            Contexts.Add(context.Pointer, new OriginContext { Context = context, Session = session, Controller = context.GameModeController.Pointer });
            // Native overtime reinjects controllers in the existing scene. It
            // can keep the already-proven live goalie instead of cloning again.
            // Only the explicit SwitchToShootout origin may transfer that exact
            // receiver/data/manager triple to the new shootout controller.
            var shootout = context.GameModeController.TryCast<ShootoutGameModeController>();
            if (ReferenceEquals(pendingOvertime?.Session, session) && Bound(pendingOvertime) && shootout != null) {
                var inherited = new Binding { Session = session, Controller = shootout,
                    Manager = pendingOvertime.Manager, Goalie = pendingOvertime.Goalie, Live = pendingOvertime.Live };
                if (inherited.Controller != null && Bound(inherited)) {
                    Controllers[inherited.Controller.Pointer] = inherited; Managers[inherited.Manager.Pointer] = inherited;
                    pendingOvertime = null; if (ReferenceEquals(pendingScene, session)) pendingScene = null;
                }
            }
        } catch (Exception ex) { Warn(ex.Message); }
        return __exception;
    }
    private static void UpdatePrefix(EndOfGameContext __0, out Update __state)
    {
        __state = new Update { Previous = update, Context = __0 }; update = __state;
        try {
            if (__0?.GameType == ScriptableStates.GameType.Rogue && __0.GameModeController != null
                && Contexts.TryGetValue(__0.Pointer, out var context) && context.Controller == __0.GameModeController.Pointer
                && Controllers.TryGetValue(context.Controller, out var binding) && ReferenceEquals(context.Session, binding.Session)
                && __0.CampaignState?.Pointer == binding.Session.Origin.Campaign.Pointer
                && __0.MatchNode?.Pointer == binding.Session.Blueprint)
                __state.Binding = binding;
        } catch (Exception ex) { Warn(ex.Message); }
    }
    // Install with Priority.First. A missing prefix/foreign skip is unknown.
    // The actual owned custom prefix reports its decision into this scope.
    private static void DeparturePrefix(Rogue.FreeAgents.FreeAgentManager __instance, out Departure __state)
    {
        __state = new Departure { Previous = departure, Update = update, Manager = __instance }; departure = __state;
    }
    private static void ObserveRetention(Rogue.FreeAgents.FreeAgentManager manager, bool prevented, bool originalAllowed)
    {
        try {
            var scope = departure; var binding = scope?.Update?.Binding;
            if (binding == null || !ReferenceEquals(update, scope.Update) || scope.Manager?.Pointer != manager?.Pointer
                || manager._campaignState?.Pointer != binding.Session.Origin.Campaign.Pointer) return;
            scope.SeenCustom = true; scope.Custom = prevented; scope.OriginalAllowed = originalAllowed;
            if (!Bound(binding) || !CustomRewardContractRetention.TryPrevents(binding.Session.Origin.HomeTeam, out bool currentCustom)
                || currentCustom != prevented) return;
            // A second, typed native query is necessary when no forward expires.
            // Never detour the generic RVA or interpret no native call as false.
            bool native = binding.Session.Origin.HomeTeam.HaveRelicCached<Rogue.Handcuffs>();
            scope.Prevented = prevented || native; scope.Known = true;
        } catch (Exception ex) { Warn(ex.Message); }
    }
    private static Exception DepartureFinalizer(Rogue.FreeAgents.FreeAgentManager __instance, Departure __state, bool __runOriginal, Exception __exception)
    {
        try {
            var owner = __state?.Update;
            if (owner?.Binding == null || !ReferenceEquals(update, owner) || __instance?.Pointer != __state.Manager?.Pointer) return __exception;
            // Multiple/conflicting observations never upgrade an unknown result.
            bool known = __exception == null && __state.SeenCustom && __state.Known && __state.OriginalAllowed
                && (__runOriginal ? !__state.Custom : __state.Custom);
            owner.ProtectionKnown = !owner.DepartureSeen && known;
            owner.DepartureSeen = true; owner.Prevented = __state.Prevented;
        } catch (Exception ex) { Warn(ex.Message); }
        finally { if (__state != null && ReferenceEquals(departure, __state)) departure = __state.Previous; }
        return __exception;
    }
    private static Exception UpdateFinalizer(EndOfGameContext __0, Update __state, UniTask __result, bool __runOriginal, Exception __exception)
    {
        try {
            var binding = __state?.Binding; var session = binding?.Session;
            if (session == null || session.Completing || !Owned(session)) return __exception;
            if (__exception != null || !__runOriginal || __result.Status != UniTaskStatus.Succeeded
                || __0?.Pointer != __state.Context.Pointer || !Bound(binding) || session.Participant == null
                || !__state.DepartureSeen || !__state.ProtectionKnown) { Unavailable(session, "Native match completion or departure protection could not be verified."); return __exception; }
            // Native copy, base event, boss awards and bench/run bookkeeping have
            // all returned. Only now may Stage publish a promoted reserve.
            session.Completing = true;
            if (!GoaliePersistenceBridge.Roster.TryCompleteMatch(__0, session.Origin.Campaign, session.Origin.Attempt, session.Participant, __state.Prevented, result => {
                if (result.Outcome != GoalieRosterRuntime.ChangeOutcome.Committed) Unavailable(session, result.Message);
                else if (result.Receipt?.Durable != true) Warn(result.Message);
            })) Unavailable(session, "Goalie completion save was not accepted.");
        } catch (Exception ex) { if (__state?.Binding != null) Unavailable(__state.Binding.Session, ex.Message); }
        finally { if (__state != null && ReferenceEquals(update, __state)) update = __state.Previous; }
        return __exception;
    }
    private static void Unavailable(Session session, string detail)
    {
        session.Invalid = true;
        string message = detail + " Return to the main menu and Continue to recover the exact saved goalie roster before starting another match.";
        GoaliePersistenceBridge.Roster.CompletionUnavailable(session.Origin.Campaign, session.Origin.Attempt, message);
        Warn(message);
    }
    private static void Warn(string message) => Plugin.Log.LogWarning("[GoalieContracts] " + message);
}
