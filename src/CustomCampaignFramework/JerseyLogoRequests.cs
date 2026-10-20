using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace EndlessMode;

// Policy shared by native entry adapters and source-linked tests. No native state
// address survives a synchronous scope; pooled runners map to exact operations.
internal static class JerseyLogoRequests
{
    internal sealed class Request
    {
        internal long Generation, Context;
        internal IntPtr Renderer, Composite, Player, Owner;
        internal bool Goalie;
        internal JerseyLogoRenderChoice Choice;
        internal object Roots;
    }
    internal sealed class Operation
    {
        internal readonly int Kind;
        internal readonly Request Request;
        internal IntPtr Runner;
        internal object RunnerRoot { get; set; }
        internal bool Bridged, Completed, Faulted, SyncEligible;
        internal bool PublicationObserved = true;
        internal Operation(int kind, Request request) { Kind = kind; Request = request; }
    }
    internal sealed class Scope : IDisposable
    {
        internal readonly Operation Operation;
        internal readonly IntPtr State, Builder;
        internal readonly Scope Previous;
        private bool disposed;
        internal Scope(Operation operation, IntPtr state)
        { Operation = operation; State = state; Builder = state + 8; Previous = current; current = this; }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            current = Previous;
            if (!Operation.Bridged) Complete(Operation, Operation.Faulted);
        }
    }
    [ThreadStatic] private static Scope current;
    internal static Scope Current => current;
    private static long generation;
    private static long context;
    private static readonly Dictionary<IntPtr, Request> latest = new();
    private static readonly Dictionary<(int, IntPtr), Operation> runners = new();
    private static readonly HashSet<Operation> pending = new();
    private static readonly Dictionary<IntPtr, HashSet<Texture>> displayed = new();
    internal static int PendingCount => pending.Count;

    [MethodImpl(MethodImplOptions.Synchronized)]
    internal static Request Begin(IntPtr renderer, IntPtr composite, IntPtr player, IntPtr owner, bool goalie, long requestContext, JerseyLogoRenderChoice choice, object roots, Request parent = null)
    {
        // A directly nested world request is the same owner generation; all other
        // initial requests, including pass-through, supersede the previous one.
        if (parent != null && parent.Renderer == renderer && parent.Player == player && parent.Owner == owner && parent.Goalie == goalie)
        { parent.Composite = composite; parent.Roots = roots; return parent; }
        var request = new Request { Generation = ++generation, Context = requestContext, Renderer = renderer, Composite = composite, Player = player, Owner = owner, Goalie = goalie, Choice = choice, Roots = roots };
        if (renderer != IntPtr.Zero) latest[renderer] = request;
        return request;
    }
    [MethodImpl(MethodImplOptions.Synchronized)]
    internal static Request Find(IntPtr renderer, IntPtr owner, IntPtr player, IntPtr skin)
        => latest.TryGetValue(renderer, out var request) && IsCurrent(request) && request.Owner == owner && request.Player == player && request.Composite == skin ? request : null;
    [MethodImpl(MethodImplOptions.Synchronized)]
    internal static bool IsCurrent(Request request) => request != null && request.Context == context && latest.TryGetValue(request.Renderer, out var value) && ReferenceEquals(value, request);
    [MethodImpl(MethodImplOptions.Synchronized)]
    internal static Operation Start(int kind, Request request)
    { var operation = new Operation(kind, request); pending.Add(operation); return operation; }
    internal static Scope Enter(Operation operation, IntPtr state) => new(operation, state);
    [MethodImpl(MethodImplOptions.Synchronized)]
    internal static Operation Resume(int kind, IntPtr runner)
        => runners.TryGetValue((kind, runner), out var operation) ? operation : null;
    [MethodImpl(MethodImplOptions.Synchronized)]
    internal static void Bridge(Operation operation, IntPtr runner)
    {
        if (operation == null || operation.Completed || runner == IntPtr.Zero) return;
        operation.Runner = runner; operation.Bridged = true;
        runners[(operation.Kind, runner)] = operation;
    }
    [MethodImpl(MethodImplOptions.Synchronized)]
    internal static Operation Terminal(int kind, IntPtr runner, bool faulted)
    {
        var operation = Resume(kind, runner);
        if (operation != null) Complete(operation, faulted);
        return operation;
    }
    [MethodImpl(MethodImplOptions.Synchronized)]
    internal static void Complete(Operation operation, bool faulted)
    {
        if (operation.Completed) return;
        operation.Completed = true; operation.Faulted |= faulted;
        if (runners.TryGetValue((operation.Kind, operation.Runner), out var saved) && ReferenceEquals(saved, operation)) runners.Remove((operation.Kind, operation.Runner));
        pending.Remove(operation);
    }
    private static void Report(Action<Exception> error, Exception exception)
    { try { error(exception); } catch { /* Logging must never prevent the native original. */ } }
    internal static void RunSlice(Func<Operation> capture, IntPtr state, Action original, Action<Request> apply, Action<Exception> error)
    {
        Operation operation = null; Scope scope = null; bool returned = false;
        try { operation = capture(); if (operation != null) scope = Enter(operation, state); }
        catch (Exception ex) { Report(error, ex); }
        try { original(); returned = true; }
        finally
        {
            try
            {
                if (returned && operation != null && operation.SyncEligible && operation.PublicationObserved && !operation.Bridged && !operation.Faulted) apply(operation.Request);
            }
            catch (Exception ex) { Report(error, ex); }
            finally { scope?.Dispose(); GC.KeepAlive(operation); }
        }
    }
    internal static void Publish(Operation operation, Action original, Func<IntPtr> runner, Action<Exception> error)
    {
        original();
        try
        {
            if (operation != null)
            {
                IntPtr value = runner();
                if (value == IntPtr.Zero) Indeterminate(operation);
                else Bridge(operation, value);
            }
        }
        catch (Exception ex) { if (operation != null) Indeterminate(operation); Report(error, ex); }
    }
    [MethodImpl(MethodImplOptions.Synchronized)]
    private static void Indeterminate(Operation operation)
    {
        operation.PublicationObserved = false;
        if (latest.TryGetValue(operation.Request.Renderer, out var request) && ReferenceEquals(request, operation.Request)) latest.Remove(operation.Request.Renderer);
    }
    internal static void Finish(int kind, IntPtr runner, bool faulted, Action<Request> apply, Action original, Action<Exception> error)
    {
        try { var operation = Terminal(kind, runner, faulted); if (!faulted && operation != null) apply(operation.Request); }
        catch (Exception ex) { Report(error, ex); }
        original();
    }
    internal static void MarkBuilderFault(IntPtr builder)
    {
        // Nested operations may invoke another builder. Never poison the parent
        // simply because the shared helper ran on this thread.
        for (var scope = current; scope != null; scope = scope.Previous)
            if (scope.Builder == builder) { scope.Operation.Faulted = true; break; }
    }
    [MethodImpl(MethodImplOptions.Synchronized)]
    internal static bool FinalLogo(Request request, IntPtr renderer, IntPtr composite, bool goalie, ref Texture texture)
    {
        if (request == null || request.Renderer != renderer || request.Goalie != goalie) return true;
        if (!IsCurrent(request) || current?.Operation.Faulted == true) return false;
        // A stale native SetSkin can still change the skin; don't paint the newer
        // texture onto that unrelated skin. This does not repair native ordering.
        if (composite == IntPtr.Zero || request.Composite != composite) return true;
        if (request.Choice.Override) texture = request.Choice.Texture;
        return true;
    }
    [MethodImpl(MethodImplOptions.Synchronized)]
    internal static bool CanApply(Request request, IntPtr composite)
        => composite != IntPtr.Zero && IsCurrent(request) && request.Composite == composite && request.Choice.Override && current?.Operation.Faulted != true;
    [MethodImpl(MethodImplOptions.Synchronized)]
    internal static void DidDisplay(IntPtr renderer, Texture texture)
    {
        if (renderer == IntPtr.Zero) return;
        if (texture == null) return;
        if (!displayed.TryGetValue(renderer, out var textures)) displayed[renderer] = textures = new();
        textures.Add(texture);
    }
    [MethodImpl(MethodImplOptions.Synchronized)]
    internal static void ForgetRenderer(IntPtr renderer)
    { latest.Remove(renderer); displayed.Remove(renderer); }
    [MethodImpl(MethodImplOptions.Synchronized)]
    internal static void RetireContext(long value)
    {
        context = value; latest.Clear();
        // Pending operations still need their exact saved scope to reject late
        // writes, and displayed resources are still in use by native materials.
    }
    [MethodImpl(MethodImplOptions.Synchronized)]
    internal static IEnumerable<Texture> RetainedTextures()
    {
        var result = new HashSet<Texture>();
        foreach (var operation in pending) if (operation.Request.Choice.Texture != null) result.Add(operation.Request.Choice.Texture);
        foreach (var request in latest.Values) if (request.Choice.Texture != null) result.Add(request.Choice.Texture);
        foreach (var set in displayed.Values) foreach (var texture in set) result.Add(texture);
        return result;
    }
}
