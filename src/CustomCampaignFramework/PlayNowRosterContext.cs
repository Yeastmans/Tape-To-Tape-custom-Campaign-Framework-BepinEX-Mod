using System;
using System.Linq;
using Data;
using CampaignCore;
using HarmonyLib;
using Tape2Tape.Customization.UI;
using Tape2Tape.Hockey.UI;

namespace EndlessMode;

// Only the two synchronous native roster operations may consult this scope.
internal static class PlayNowRosterContext
{
    [ThreadStatic] private static Frame current;
    private sealed class Frame : IDisposable
    {
        internal Frame Parent;
        internal bool Eligible, Disposed;
        internal bool Saved;
        internal TeamData Team;
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
        var frame = new Frame { Parent = current, Team = team, Eligible = PlayNowRosterRuntime.TryGet(team, out _) };
        current = frame;
        return frame;
    }
    // Called only for sites loaded by the bridge's validated compatibility
    // profile. Addresses belong to that profile, not a second build-specific
    // allowlist here. Require the complete reviewed method contract.
    internal static bool IsRosterSite(string feature, string query, string kind, string method)
        => feature == "two_lines" && query == "lines" && kind == "query"
            && (method == "Tape2Tape.Customization.UI.TeamRosterData$$UpdateTemporaryTeamData::System.Void UpdateTemporaryTeamData()"
                || method == "Tape2Tape.Customization.UI.TeamRosterData$$ApplyChanges::System.Boolean ApplyChanges()");

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
    public static void Applied(TeamRosterData __instance, ref bool __result, IDisposable __state, bool __runOriginal)
    {
        if (!__runOriginal || !__result || __state is not Frame frame || !frame.Eligible || frame.Disposed || frame.Saved) return;
        try
        {
            if (__instance?.OriginalTeamData?.Pointer != frame.Team?.Pointer || !Valid(__instance))
                throw new InvalidOperationException("The exported lineup identity changed while applying the edit.");
            PlayNowRosterSave.SaveApplied(__instance.OriginalTeamData);
            frame.Saved = true;
        }
        catch (Exception ex)
        {
            __result = false;
            Plugin.Log.LogWarning("[PlayNow] Lineup was not saved; keep the editor open and retry: " + ex.Message);
        }
    }
    public static bool Leave(LineupMenu __instance)
    {
        var roster = __instance?.Team;
        if (roster == null || __instance.m_AreSkatersLocked || !roster.HasChanged
            || !PlayNowRosterRuntime.IsRegistered(roster.OriginalTeamData)) return true;
        try
        {
            // Invalid rosters retain the native discard/return prompt. Valid
            // edits have no confirmation prompt: native immediately applies.
            if (!roster.CheckRoster(false)) return true;
            if (NativeSquadBehaviorBridge.Ready && Valid(roster) && roster.ApplyChanges()) return true;
        }
        catch (Exception ex) { Plugin.Log.LogWarning("[PlayNow] Lineup save failed: " + ex.Message); }
        // Gate before native sets m_IsLeaving. IL2CPP catches managed patch
        // exceptions, and native itself closes even on a false ApplyChanges.
        if (__instance.m_Header?.m_Title != null)
            __instance.m_Header.m_Title.text = "Lineup not saved — retry or restore the previous roster";
        return false;
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
        roster.m_LW = Player(active[PlayNowPositions.LeftWing]); roster.m_RW = Player(active[PlayNowPositions.RightWing]);
        roster.m_C = Player(active[PlayNowPositions.Center]); roster.m_LD = Player(active[PlayNowPositions.LeftDefense]);
        roster.m_RD = Player(active[PlayNowPositions.RightDefense]);
        roster.m_BenchForwards.Clear();
        // Native UpdateMultiLineTeam consumes bench indexes C,LW,RW,LD,RD.
        foreach (string id in PlayNowPositions.NativeBench(bench)) roster.m_BenchForwards.Add(Player(id));
    }
    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(LineupMenu), "TryLeaveMenu"),
            prefix: new HarmonyMethod(typeof(PlayNowRosterContext), nameof(Leave)));
        harmony.Patch(AccessTools.GetDeclaredConstructors(typeof(TeamRosterData)).Single(c => c.GetParameters().Length == 4),
            postfix: new HarmonyMethod(typeof(PlayNowRosterContext), nameof(Constructed)));
        foreach (string name in new[] { "UpdateTemporaryTeamData", "ApplyChanges" })
            harmony.Patch(AccessTools.Method(typeof(TeamRosterData), name),
                prefix: new HarmonyMethod(typeof(PlayNowRosterContext), nameof(Prefix)),
                postfix: name == "ApplyChanges" ? new HarmonyMethod(typeof(PlayNowRosterContext), nameof(Applied)) : null,
                finalizer: new HarmonyMethod(typeof(PlayNowRosterContext), nameof(Finalizer)));
    }
}
