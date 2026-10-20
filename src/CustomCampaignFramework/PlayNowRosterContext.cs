using System;
using System.Linq;
using Data;
using HarmonyLib;
using Tape2Tape.Customization.UI;

namespace EndlessMode;

// Only the two synchronous native roster operations may consult this scope.
internal static class PlayNowRosterContext
{
    [ThreadStatic] private static Frame current;
    private sealed class Frame : IDisposable
    {
        internal Frame Parent;
        internal bool Eligible, Disposed;
        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            // Out-of-order disposal must never expose a disposed outer frame.
            while (current != null && current.Disposed) current = current.Parent;
        }
    }
    internal static bool Active => current?.Eligible == true && !current.Disposed;
    internal static IDisposable Enter(TeamData team)
    {
        var frame = new Frame { Parent = current, Eligible = PlayNowRosterRuntime.TryGet(team, out _) };
        current = frame;
        return frame;
    }
    internal static bool IsRosterSite(long rva) => rva == 0x4501226 || rva == 0x45022E0;

    public static bool Prefix(TeamRosterData __instance, out IDisposable __state)
    {
        bool registered = PlayNowRosterRuntime.IsRegistered(__instance?.OriginalTeamData);
        bool valid = registered && NativeSquadBehaviorBridge.Ready && Valid(__instance);
        __state = Enter(valid ? __instance.OriginalTeamData : null);
        // Unsupported native bridge or invalid exported shape must not fall
        // through to the single-line writer and silently discard Line 2.
        if (registered && !valid)
            Plugin.Log.LogWarning("[PlayNow] Roster edit was not applied: two-line shape or reviewed native bridge is unavailable.");
        return !registered || valid;
    }
    private static bool Valid(TeamRosterData roster)
    {
        if (roster == null || !PlayNowRosterRuntime.TryGet(roster.OriginalTeamData, out _)
            || !PlayNowRosterRuntime.IsCloneOf(roster.TemporaryTeamData, roster.OriginalTeamData)
            || !PlayNowRosterRuntime.TryGet(roster.TemporaryTeamData, out _)
            || roster.m_BenchForwards == null || roster.m_BenchForwards.Count != 5) return false;
        var ids = new[] { roster.m_LW?.id, roster.m_RW?.id, roster.m_C?.id, roster.m_LD?.id, roster.m_RD?.id,
            roster.m_BenchForwards[0]?.id, roster.m_BenchForwards[1]?.id, roster.m_BenchForwards[2]?.id,
            roster.m_BenchForwards[3]?.id, roster.m_BenchForwards[4]?.id, roster.m_G?.id };
        return ids.All(id => !string.IsNullOrEmpty(id)) && ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 11;
    }
    public static Exception Finalizer(Exception __exception, IDisposable __state)
    {
        __state?.Dispose(); return __exception;
    }

    // Native constructor reads CurrentLine and limits the bench to benchSize.
    // An editable exported roster always presents absolute Line 1 and Line 2.
    // Do not change the original team's selected/active line while opening it.
    public static void Constructed(TeamRosterData __instance)
    {
        var source = __instance?.OriginalTeamData;
        if (!PlayNowRosterRuntime.TryGet(source, out var roster)) return;
        PlayNowRosterRuntime.Clone(source, __instance.TemporaryTeamData);
        if (!PlayNowRosterRuntime.TryGet(__instance.TemporaryTeamData, out _)) return;
        Fill(__instance, source, roster.Line1, roster.Line2);
        __instance.TemporaryTeamData.activeLineNumber = 1;
    }
    internal static void RefreshLocked(TeamRosterData roster)
    {
        var source = roster?.OriginalTeamData;
        if (!PlayNowRosterRuntime.TryGet(source, out var snapshot)) return;
        bool second = source.activeLineNumber == 2;
        Fill(roster, source, second ? snapshot.Line2 : snapshot.Line1, second ? snapshot.Line1 : snapshot.Line2);
        if (roster.TemporaryTeamData != null) roster.TemporaryTeamData.activeLineNumber = source.activeLineNumber;
    }
    private static void Fill(TeamRosterData roster, TeamData source, string[] active, string[] bench)
    {
        ForwardData Player(string id)
        {
            foreach (var p in source.forwards) if (p != null && p.id == id) return p;
            throw new InvalidOperationException("Validated exported roster player disappeared.");
        }
        roster.m_LW = Player(active[0]); roster.m_RW = Player(active[1]);
        roster.m_C = Player(active[2]); roster.m_LD = Player(active[3]);
        roster.m_RD = Player(active[4]);
        roster.m_BenchForwards.Clear();
        // Native UpdateMultiLineTeam consumes bench indexes C,LW,RW,LD,RD.
        foreach (int slot in new[] { 2, 0, 1, 3, 4 }) roster.m_BenchForwards.Add(Player(bench[slot]));
    }
    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.GetDeclaredConstructors(typeof(TeamRosterData)).Single(c => c.GetParameters().Length == 4),
            postfix: new HarmonyMethod(typeof(PlayNowRosterContext), nameof(Constructed)));
        foreach (string name in new[] { "UpdateTemporaryTeamData", "ApplyChanges" })
            harmony.Patch(AccessTools.Method(typeof(TeamRosterData), name),
                prefix: new HarmonyMethod(typeof(PlayNowRosterContext), nameof(Prefix)),
                finalizer: new HarmonyMethod(typeof(PlayNowRosterContext), nameof(Finalizer)));
    }
}
