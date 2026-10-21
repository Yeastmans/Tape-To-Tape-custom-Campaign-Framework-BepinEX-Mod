using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CampaignCore;
using Data;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using State;
using Tape2Tape;

namespace EndlessMode;

// Plugin startup installs only after the complete paired compatibility gate.
// Deliberately has no HarmonyPatch class attribute: PatchAll must not enable it.
internal static class GoaliePersistenceBridge
{
    internal static bool Enabled { get; private set; }
    internal static readonly GoalieRosterRuntime Roster = new();
    private static readonly GoalieOperationQueue Queue = new();
    private static readonly Dictionary<IntPtr, GoalieNativeCheckpoint> Selections = new();
    private static object owner;
    [ThreadStatic] private static object bypass;
    [ThreadStatic] private static SaveRequest serializing;
    [ThreadStatic] private static bool readingSelection;
    [ThreadStatic] private static SaveCaller caller;
    private sealed class SaveCaller { internal CampaignState Campaign; internal object Token; internal bool Claimed; }
    internal static void SaveCampaign(CampaignState campaign, object token)
    {
        var previous = caller; caller = new SaveCaller { Campaign = campaign, Token = token };
        try { campaign.SaveRunData(); } finally { caller = previous; }
    }

    internal sealed class SaveReceipt
    {
        internal string OperationId, AttemptToken, Failure;
        internal Il2CppSystem.Threading.Tasks.Task<bool> Proxy, Original;
        internal Il2CppSystem.Exception NativeError;
        internal bool NativeResult, Reconciled;
        internal GoalieRosterState Checkpoint, Admission;
        internal IntPtr RunPointer, RepositoryPointer;
        internal bool NoLocalWrite;
        internal object CallerToken;
        // Local persisted-pair proof only; never a Steam/cloud replication claim.
        internal bool Durable => NativeError == null && NativeResult && Reconciled;
    }
    internal static event Action<SaveReceipt> SaveAdmitted;
    internal static event Action<SaveReceipt> SaveCompleted;
    private sealed class SaveRequest
    {
        internal RunDataRepositoryV2 Repository;
        internal GoalieNativeAdmission.Frozen Frozen;
        internal string Folder, Path;
        internal GoalieNativeTask<bool> Operation;
        internal GoalieCheckpointTransaction Companion;
        internal readonly SaveReceipt Receipt = new();
        internal ES3Settings PersistedSettings;
        internal GoalieNativeCheckpoint Cached;
        internal int Serialized;
        internal bool UnexpectedReplacement, StoreAttempted;
        internal GoalieNativeCheckpoint Before;
    }
    private sealed class LoadRequest
    {
        internal RunDataRepositoryV2 Repository;
        internal string Folder, Path;
        internal GoalieNativeTask<RunDataModelV2> Operation;
        internal GoalieNativeCheckpoint Selected;
        internal bool UnexpectedReplacement;
    }
    private static void Notify(Action<SaveReceipt> handlers, SaveReceipt receipt)
    {
        if (handlers == null) return;
        foreach (Action<SaveReceipt> handler in handlers.GetInvocationList())
            try { handler(receipt); } catch (Exception ex) { Plugin.Log.LogError("[GoalieCheckpoint] Completion consumer failed: " + ex); }
    }
    private static void Warn(string error) => Plugin.Log.LogWarning("[GoalieCheckpoint] " + error);
    private static void BindDeserializer(RunDataRepositoryV2 repository)
    {
        // Exactly the native LoadRunData construction, including its null logo
        // repository, using this actual injected instance even on a fresh run.
        if (repository._talentRepository == null || repository._relicRepository == null || repository._abilityRepository == null)
        { GoalieNativeSnapshot.Deserializer = null; return; }
        GoalieNativeSnapshot.Deserializer = new DeserializerV2(repository._talentRepository, repository._relicRepository, repository._abilityRepository, null);
    }
    internal static void Tick() { if (Enabled) Queue.Tick(); }

    internal static void SetEnabled(bool enabled) => Enabled = enabled;
    internal static void Preflight()
    {
        GoalieNativeCheckpoint.RequireProfile(ES3Settings.defaultSettings);
        GoalieNativeAdmission.Preflight();
        PreflightPromises();
    }
    private static void PreflightPromises()
    {
        void Check<T>(T value)
        {
            var success = Il2CppSystem.Runtime.CompilerServices.AsyncTaskMethodBuilder<T>.Create();
            var successTask = success.Task;
            if (successTask == null || successTask.IsCompleted) throw new InvalidOperationException("Invalid pending native builder.");
            success.SetResult(value);
            if (!successTask.IsCompleted || successTask.IsFaulted || successTask.IsCanceled) throw new InvalidOperationException("Native result publication failed.");
            successTask.GetAwaiter().GetResult();
            foreach (Il2CppSystem.Exception error in new Il2CppSystem.Exception[] { new Il2CppSystem.Exception("CCF promise preflight"), new Il2CppSystem.OperationCanceledException() })
            {
                var failure = Il2CppSystem.Runtime.CompilerServices.AsyncTaskMethodBuilder<T>.Create(); var task = failure.Task;
                failure.SetException(error);
                var returned = task.IsCanceled ? task.GetCancellationExceptionDispatchInfo()?.SourceException : task.GetExceptionDispatchInfos()[0].SourceException;
                if (!task.IsCompleted || returned?.Pointer != error.Pointer) throw new InvalidOperationException("Native exception publication lost provenance.");
            }
        }
        Check(false); Check(true); Check<RunDataModelV2>(null); Check(new RunDataModelV2());
    }
    private static bool SavePrefix(RunDataRepositoryV2 __instance, RunDataModelV2 __0, ref Il2CppSystem.Threading.Tasks.Task<bool> __result)
    {
        if (bypass is SaveRequest bypassed && bypassed.Repository.Pointer == __instance.Pointer && bypassed.Frozen.Model?.Pointer == __0?.Pointer)
        { bypass = null; return true; }
        if (!Enabled) return true;
        BindDeserializer(__instance);
        var request = new SaveRequest { Repository = __instance, Path = __instance._fileName, Folder = System.IO.Path.GetFullPath(Plugin.ModFolder) };
        request.Operation = new GoalieNativeTask<bool>(() => StartSave(request), op => FinishSave(request));
        request.Receipt.OperationId = request.Operation.Id; request.Receipt.Proxy = request.Operation.Proxy;
        request.Receipt.RepositoryPointer = __instance.Pointer;
        var callingRun = caller?.Campaign?.runData;
        if (caller != null && !caller.Claimed && caller.Campaign._runDataRepository?.Pointer == __instance.Pointer
            && callingRun?.runID == __0?.runID && callingRun?.squadId == __0?.squadId)
        {
            caller.Claimed = true; request.Receipt.CallerToken = caller.Token; request.Receipt.RunPointer = callingRun.Pointer;
        }
        request.Frozen = GoalieNativeAdmission.Freeze(__0, request.Path, out var nativeError);
        if (nativeError != null)
        {
            __result = request.Operation.Proxy;
            request.Operation.RejectNativeSerialization(nativeError);
            Warn("Native serialization failed before admission; no cache write was performed."); return false;
        }
        try
        {
            var run = CampaignUtil.CampaignState?.runData;
            if (!Plugin.IsDefaultMode && __0 != null && run?.runID == __0.runID && run.squadId == __0.squadId)
            {
                var frozenRoster = Roster.Capture(run, request.Folder);
                var models = new List<GoaltenderDataModelV2>();
                if (__0.team?.skaters != null) foreach (var skater in __0.team.skaters)
                { var goalie = skater?.TryCast<GoaltenderDataModelV2>(); if (goalie != null) models.Add(goalie); }
                if (models.Count != 1 || !GoalieNativeSnapshot.Matches(frozenRoster.Active, models[0], __0.teamState?.goaltenderState))
                    throw new InvalidDataException("Admitted native model and goalie snapshot differ.");
                request.Companion = new GoalieCheckpointTransaction(request.Operation.Id, request.Folder, frozenRoster);
                request.Receipt.AttemptToken = request.Companion.AttemptToken;
                request.Receipt.RunPointer = run.Pointer; request.Receipt.Admission = frozenRoster.Clone();
            }
        }
        catch (Exception ex) { request.Receipt.Failure = "Companion capture unavailable: " + ex.Message; Warn(request.Receipt.Failure); }
        __result = request.Operation.Proxy;
        Queue.Enqueue(request.Operation, false);
        Notify(SaveAdmitted, request.Receipt);
        Queue.Tick();
        return false;
    }
    private static Il2CppSystem.Threading.Tasks.Task<bool> StartSave(SaveRequest request)
    {
        owner = request; bypass = request; serializing = request;
        try {
            // Read immediately before this queued operation starts; an earlier
            // queued save may have replaced the physical container since admission.
            try { request.Before = GoalieNativeCheckpoint.Persisted(request.Folder, request.Path, new ES3Settings(request.Path, ES3Settings.defaultSettings)); }
            catch { /* No proven previous checkpoint means failure stays unresolved. */ }
            return request.Repository.SaveToPersistentStorage(request.Frozen.Model);
        }
        finally { bypass = null; serializing = null; }
    }
    private static bool SerializePrefix(Il2CppSystem.Object __0, ES3Types.ES3Type __1, ES3Settings __2, ref Il2CppStructArray<byte> __result)
    {
        var request = serializing;
        if (request == null || __0?.Pointer != request.Frozen.Model?.Pointer || __1?.type?.FullName != "State.RunDataModelV2") return true;
        if (__1.Pointer != request.Frozen.Type.Pointer || !request.Frozen.MatchesProfile(__2))
        {
            request.Receipt.Failure = "External native serialization type/profile drift: native pass-through is outside the immutable checkpoint guarantee.";
            Warn(request.Receipt.Failure);
            return true;
        }
        request.Serialized++;
        __result = request.Frozen.NativeBytes;
        return false;
    }
    private static void StorePrefix(string __0)
    {
        if (owner is not SaveRequest request || __0 != request.Path) return;
        request.StoreAttempted = true;
        if (request.Companion == null || request.Receipt.Failure != null) return;
        try
        {
            var cached = GoalieNativeCheckpoint.Cached(request.Folder, request.Path);
            if (request.Serialized != 1 || request.UnexpectedReplacement || cached.Entry.type.type.FullName != request.Frozen.Type.type.FullName
                || !cached.Bytes.SequenceEqual(request.Frozen.Bytes)) throw new InvalidDataException("Store did not select the exact admitted native serialization.");
            if (request.Cached == null)
            {
                request.PersistedSettings = new ES3Settings(request.Path, ES3Settings.defaultSettings);
                GoalieNativeCheckpoint.RequireProfile(request.PersistedSettings);
                var marker = request.Companion.Prepare(cached.Fingerprint);
                cached.Inject(marker); request.Cached = cached;
            }
            else if (cached.File.Pointer != request.Cached.File.Pointer || cached.Marker != request.Companion.Marker)
                throw new InvalidDataException("Platform store selected a different cache/marker.");
        }
        catch (Exception ex) { request.Receipt.Failure = "Companion checkpoint unavailable: " + ex.Message; Warn(request.Receipt.Failure); }
    }
    private static void FinishSave(SaveRequest request)
    {
        var op = request.Operation;
        request.Receipt.Original = op.Original; request.Receipt.NativeResult = op.Result; request.Receipt.NativeError = op.Error;
        try
        {
            if (request.Companion?.Marker != null && request.PersistedSettings != null && !request.UnexpectedReplacement)
            {
                var persisted = GoalieNativeCheckpoint.Persisted(request.Folder, request.Path, request.PersistedSettings);
                request.Receipt.Checkpoint = request.Companion.Reconcile(persisted.Marker, persisted.Fingerprint);
                request.Receipt.Reconciled = request.Receipt.Checkpoint != null && request.Receipt.Failure == null;
            }
        }
        catch (Exception ex) { request.Receipt.Failure = "Persisted checkpoint reconciliation failed: " + ex.Message; Warn(request.Receipt.Failure); }
        request.Receipt.NoLocalWrite = !request.StoreAttempted;
        if (!request.Receipt.Reconciled && request.Before != null && !request.UnexpectedReplacement)
            try {
                var after = GoalieNativeCheckpoint.Persisted(request.Folder, request.Path, new ES3Settings(request.Path, ES3Settings.defaultSettings));
                request.Receipt.NoLocalWrite = after.Fingerprint == request.Before.Fingerprint && after.Marker == request.Before.Marker;
            } catch { /* Unknown readback cannot authorize rollback/reuse. */ }
        // Retain all prepared records on false/fault/cancel or ambiguous readback.
        owner = null;
        op.Published = _ => Notify(SaveCompleted, request.Receipt);
    }
    private static bool LoadPrefix(RunDataRepositoryV2 __instance, ref Il2CppSystem.Threading.Tasks.Task<RunDataModelV2> __result)
    {
        if (bypass is LoadRequest bypassed && bypassed.Repository.Pointer == __instance.Pointer) { bypass = null; return true; }
        if (!Enabled) return true;
        BindDeserializer(__instance);
        var request = new LoadRequest { Repository = __instance, Path = __instance._fileName, Folder = System.IO.Path.GetFullPath(Plugin.ModFolder) };
        request.Operation = new GoalieNativeTask<RunDataModelV2>(() => {
            owner = request; bypass = request;
            try { return request.Repository.LoadFromPersistentStorage(); } finally { bypass = null; }
        }, op => {
            if (op.Error == null && op.Result != null && !request.UnexpectedReplacement && request.Selected?.Model?.Pointer == op.Result.Pointer)
                Selections[op.Result.Pointer] = request.Selected;
            if (op.Error == null && op.Result == null) Roster.NoSelectedRun();
            owner = null;
        });
        __result = request.Operation.Proxy; Queue.Enqueue(request.Operation); return false;
    }
    private static void CachePrefix(ES3Settings __0)
    {
        if (owner is SaveRequest save && __0?.path == save.Path) save.UnexpectedReplacement = true;
        else if (owner is LoadRequest load && __0?.path == load.Path && load.Selected != null) load.UnexpectedReplacement = true;
    }
    private static void DeserializePrefix(ES3Types.ES3Type __0, Il2CppStructArray<byte> __1, ref GoalieNativeCheckpoint __state)
    {
        __state = null;
        if (readingSelection || owner is not LoadRequest request || request.Selected != null || __0?.type?.FullName != "State.RunDataModelV2") return;
        readingSelection = true;
        try
        {
            var candidate = GoalieNativeCheckpoint.Cached(request.Folder, request.Path);
            if (candidate.MatchesInput(__0, __1)) __state = candidate;
        }
        catch (Exception ex) { Warn("Selected load has no usable companion: " + ex.Message); }
        finally { readingSelection = false; }
    }
    private static void DeserializePostfix(Il2CppSystem.Object __result, GoalieNativeCheckpoint __state)
    {
        if (__state == null || owner is not LoadRequest request || request.Selected != null) return;
        try
        {
            var model = __result?.TryCast<RunDataModelV2>();
            if (model == null) return;
            __state.Associate(model); request.Selected = __state;
        }
        catch (Exception ex) { Warn("Native load association rejected: " + ex.Message); }
    }
    internal static void Hydrated(DeserializerV2 deserializer, RunData run, RunDataModelV2 model)
    {
        Selections.Remove(model.Pointer, out var selected);
        if (!Roster.Load(run, model, deserializer, selected, Plugin.ModFolder))
        {
            Warn(Roster.UnavailableReason);
            // This identity-checked legacy sidecar restores only omitted
            // appearance on the native active. It does not infer a reserve,
            // contracts or an earned-state/checkpoint generation. Resolve it
            // here after the full-pair result, independent of postfix order.
            GoalieRecruitment.Restore(run);
        }
    }
    private static void HydratedPostfix(DeserializerV2 __instance, RunData __0, RunDataModelV2 __1) => Hydrated(__instance, __0, __1);
    private static void FreshPostfix(CampaignState __instance)
    {
        if (!Enabled || Plugin.IsDefaultMode || __instance?.runData?.teamData?.goalie == null) return;
        try {
            if (Roster.Owns(__instance.runData, Plugin.ModFolder)) return;
            Roster.Fresh(__instance.runData, Plugin.ModFolder,
                SquadBehaviorRuntime.Enabled(__instance.runData.squadId, GoalieFeatureOptions.ReserveBehaviorId, false),
                GoalieContractRuntime.Sign(SquadPoolRuntime.Team(__instance.runData.squadId)?.Goalie?.Fields, __instance.runData.teamData.goalie.id));
        }
        catch (Exception ex) { Warn("Fresh goalie roster unavailable: " + ex.Message); }
    }
}
