using System;
using System.Collections.Generic;
using System.Linq;
using CampaignCore;
using Data;
using Tape2Tape.Customization;

namespace EndlessMode;

// Ownership belongs to completed exported team objects, never merely a team ID.
internal static class PlayNowRosterRuntime
{
    private sealed class Owner
    {
        internal TeamData Team;
        internal int InstanceId;
        internal string TeamId;
        internal bool Stamina;
        internal Dictionary<string, string> SourceIds;
        internal IntPtr ParentPointer;
        internal int ParentInstanceId;
    }
    private static readonly Dictionary<IntPtr, Owner> Owners = new();
    private static TeamRepository Repository;
    private static Il2CppSystem.Collections.Generic.List<TeamData> Cache;

    internal static void Clear()
    {
        Owners.Clear(); Repository = null; Cache = null;
        PlayNowRosterSave.Clear();
    }

    internal static void RefreshSavedPlayer(SkaterData player)
    {
        if (player == null) return;
        foreach (var owner in Owners.Values)
        {
            var team = owner.Team;
            if (team == null || team.GetInstanceID() != owner.InstanceId) continue;
            // Generated match identities remain independent. Only exact persisted
            // references, including the second line, receive this shared edit.
            var forward = player.TryCast<ForwardData>();
            if (forward != null && team.forwards != null)
                for (int i = 0; i < team.forwards.Count; i++)
                    if (team.forwards[i] != null && team.forwards[i].id == player.id) team.forwards[i] = forward;
            var goalie = player.TryCast<GoaltenderData>();
            if (goalie != null && team.goalie != null && team.goalie.id == player.id) team.goalie = goalie;
        }
    }

    private static bool Owned(TeamData team, out Owner owner)
    {
        owner = null;
        if (team == null || !Owners.TryGetValue(team.Pointer, out var found)) return false;
        if (found.Team == null || found.Team.Pointer != team.Pointer || found.InstanceId != team.GetInstanceID()
            || found.InstanceId != found.Team.GetInstanceID() || found.TeamId != team.id) return false;
        owner = found; return true;
    }

    internal static bool TryGet(TeamData team, out PlayNowRoster roster)
    {
        roster = null;
        return Owned(team, out var owner) && Snapshot(team, owner.Stamina, out roster);
    }

    internal static bool IsRegistered(TeamData team) => Owned(team, out _);
    internal static bool IsCloneOf(TeamData clone, TeamData source)
        => Owned(clone, out var owner) && Owned(source, out _)
            && owner.ParentPointer == source.Pointer && owner.ParentInstanceId == source.GetInstanceID();
    internal static void Forget(TeamData team)
    {
        if (Owned(team, out _)) Owners.Remove(team.Pointer);
    }

    // Native match IDs remain untouched. Persistence can translate them separately.
    internal static bool TryGetSourceId(TeamData team, string id, out string sourceId)
    {
        sourceId = null;
        return Owned(team, out var owner) && owner.SourceIds.TryGetValue(id, out sourceId);
    }

    // Interop lists expose native enumeration, not managed IEnumerable<T>.
    private static IEnumerable<T> Items<T>(Il2CppSystem.Collections.Generic.List<T> items)
    {
        if (items == null) yield break;
        for (int i = 0; i < items.Count; i++) yield return items[i];
    }

    private static string[] Ids(Lineup line) => new[] { line.leftWinger, line.rightWinger, line.center, line.leftDefensemen, line.rightDefensemen };
    private static Lineup Line(TeamData team, string[] ids) => new(team)
    {
        leftWinger = ids[0], rightWinger = ids[1], center = ids[2], leftDefensemen = ids[3], rightDefensemen = ids[4]
    };
    private static bool Snapshot(TeamData team, bool stamina, out PlayNowRoster roster)
    {
        roster = null;
        if (team.lines == null || team.lines.Count != 2 || team.lines[0] == null || team.lines[1] == null || team.goalie == null || team.forwards == null) return false;
        var candidate = new PlayNowRoster(Ids(team.lines[0]), Ids(team.lines[1]), team.goalie.id, stamina);
        // Reuse the codec's validation, including unsafe and duplicate IDs.
        try
        {
            PlayNowRosterCodec.PlayerIds(candidate);
            foreach (string id in candidate.Line1.Concat(candidate.Line2))
                if (Items(team.forwards).Count(p => p != null && p.id == id) != 1) return false;
        }
        catch (ArgumentException) { return false; }
        roster = candidate; return true;
    }
    private static Owner Registration(TeamData team, bool stamina, Dictionary<string, string> sourceIds) => new()
    {
        Team = team, InstanceId = team.GetInstanceID(), TeamId = team.id, Stamina = stamina, SourceIds = sourceIds
    };

    internal static bool TryHydrate(TeamRepository repository, TeamDataModel model, TeamData team)
    {
        if (repository == null || model == null || team == null || model.Id != team.id) return false;
        // Once accepted, the live team's edits are authoritative until lifecycle reset.
        if (Owned(team, out _)) return true;
        var created = new List<ForwardData>();
        var oldForwards = team.forwards; var oldLines = team.lines; int oldActive = team.activeLineNumber;
        bool committed = false;
        try
        {
            var document = PlayNowTranslation.Read(model.Id, true);
            if (document == null || !PlayNowRosterCodec.TryRead(document, out var roster, out _)) return false;
            var fallback = new[] { model.leftWingerId, model.rightWingerId, model.centerId, model.leftDefensemenId, model.rightDefensemenId };
            if (!fallback.SequenceEqual(roster.Line1) || model.goalieId != roster.GoalieId || team.goalie == null || team.goalie.id != roster.GoalieId)
            {
                Plugin.Log.LogWarning($"[PlayNow] Roster/native fallback mismatch for '{model.Id}'; keeping native roster.");
                return false;
            }
            var all = repository.m_forwardRepository.LoadAllForwards();
            var forwards = new Il2CppSystem.Collections.Generic.List<ForwardData>();
            foreach (string id in roster.Line1.Concat(roster.Line2))
            {
                var existing = Items(oldForwards).Where(p => p != null && p.id == id).ToArray();
                if (existing.Length > 1) return false;
                var player = existing.FirstOrDefault();
                if (player == null)
                {
                    if (all == null || Items(all).Count(p => p != null && p.id == id) != 1) return false;
                    player = repository.InstantiateForwardWithId(all, id);
                    if (player != null) created.Add(player);
                }
                if (player == null || player.id != id) return false;
                forwards.Add(player);
            }
            var lines = new Il2CppSystem.Collections.Generic.List<Lineup>();
            lines.Add(Line(team, roster.Line1)); lines.Add(Line(team, roster.Line2));
            var sourceIds = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string id in PlayNowRosterCodec.PlayerIds(roster)) sourceIds.Add(id, id);
            var owner = Registration(team, roster.StaminaEnabled, sourceIds);
            team.forwards = forwards; team.lines = lines; team.activeLineNumber = 1;
            PlayNowRosterSave.AcceptLoaded(repository, model, team);
            Owners[team.Pointer] = owner;
            committed = true;
            return true;
        }
        catch (Exception ex)
        {
            team.forwards = oldForwards; team.lines = oldLines; team.activeLineNumber = oldActive;
            Plugin.Log.LogWarning($"[PlayNow] Roster hydration failed for '{model.Id}': {ex.Message}");
            return false;
        }
        finally
        {
            if (!committed) foreach (var player in created) UnityEngine.Object.Destroy(player);
        }
    }

    internal static void Clone(TeamData source, TeamData clone)
    {
        if (source == null || clone == null || source.Pointer == clone.Pointer || !Owned(source, out var owner)) return;
        try
        {
            // The native clone has already generated and remapped its forward IDs.
            if (!Snapshot(clone, owner.Stamina, out var roster)) return;
            var sourceIds = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var player in clone.forwards)
            {
                if (player == null) continue;
                var original = Items(source.forwards).Where(p => p != null &&
                    (p.id == player.id || p.id == player.OriginalId || !string.IsNullOrEmpty(p.OriginalId) && p.OriginalId == player.OriginalId)).ToArray();
                if (original.Length != 1 || !owner.SourceIds.TryGetValue(original[0].id, out string persisted)) return;
                sourceIds.Add(player.id, persisted);
            }
            if (!owner.SourceIds.TryGetValue(source.goalie.id, out string goalieId)) return;
            sourceIds.Add(clone.goalie.id, goalieId);
            var lines = new Il2CppSystem.Collections.Generic.List<Lineup>();
            lines.Add(Line(clone, roster.Line1)); lines.Add(Line(clone, roster.Line2));
            var registration = Registration(clone, owner.Stamina, sourceIds);
            registration.ParentPointer = source.Pointer;
            registration.ParentInstanceId = source.GetInstanceID();
            var forwards = new Il2CppSystem.Collections.Generic.List<ForwardData>();
            foreach (var player in clone.forwards) forwards.Add(player);
            clone.forwards = forwards;
            clone.lines = lines;
            Owners[clone.Pointer] = registration;
            PlayNowRosterSave.Clone(source, clone);
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[PlayNow] Clone roster registration failed: {ex.Message}"); }
    }

    public static void ClonePostfix(TeamData __instance, TeamData __result) => Clone(__instance, __result);

    public static void CustomTeamsPostfix(TeamRepository __instance, Il2CppSystem.Collections.Generic.List<TeamData> __result)
    {
        if (__instance == null || __result == null) return;
        try
        {
            if (Repository == null || Repository.Pointer != __instance.Pointer || Cache == null || Cache.Pointer != __result.Pointer)
            {
                Clear(); Repository = __instance; Cache = __result;
            }
            var models = __instance.m_customTeamRepository.LoadCustomTeams();
            if (models == null) return;
            foreach (var team in __result)
            {
                if (team == null || Owned(team, out _)) continue;
                var matching = Items(models).Where(m => m != null && m.Id == team.id).ToArray();
                if (matching.Length == 1) TryHydrate(__instance, matching[0], team);
            }
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[PlayNow] Completed roster load failed: {ex.Message}"); }
    }
}
