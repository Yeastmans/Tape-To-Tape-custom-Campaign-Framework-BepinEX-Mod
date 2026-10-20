using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using CampaignCore;
using Data;
using HarmonyLib;
using Tape2Tape.Customization;
using Tape2Tape.Customization.UI;
using UnityEngine;

namespace EndlessMode;

internal static class PlayNowRosterSave
{
    internal sealed class Revision
    {
        internal string Root, Kind, Id, NativeJson, CompanionText, NativeHash, CompanionHash;
    }
    private sealed class TeamRevision
    {
        internal TeamData Team;
        internal int Instance;
        internal Revision Revision;
    }
    internal sealed class SaveFrame
    {
        internal SaveFrame Parent;
        internal TeamData Team;
        internal Revision Revision;
        internal PlayNowRoster Roster;
        internal CampaignDocument Document;
        internal TeamRepository Repository;
        internal bool Committed;
    }
    private static readonly Dictionary<IntPtr, TeamRevision> Teams = new();
    private sealed class ModelRevision { internal TeamDataModel Model; internal Revision Revision; }
    private static readonly Dictionary<IntPtr, ModelRevision> Models = new();
    [ThreadStatic] private static SaveFrame Current;
    internal static string Overrides => Path.Combine(Plugin.ModContentRoot, "play_now_overrides");
    internal static IEnumerable<T> Items<T>(Il2CppSystem.Collections.Generic.List<T> list)
    { if (list != null) for (int i = 0; i < list.Count; i++) yield return list[i]; }
    internal static Revision Capture(string root, string kind, string id)
    {
        string native = PlayNowSaveJournal.NativePath(root, kind, id), companion = PlayNowSaveJournal.CompanionPath(Overrides, kind, id);
        byte[] n = PlayNowSaveJournal.Read(native), c = PlayNowSaveJournal.Read(companion);
        if (n == null || c == null) throw new IOException("The exported record is not a committed native/companion pair.");
        using var model = JsonDocument.Parse(n);
        if (model.RootElement.GetProperty(kind == "team" ? "Id" : "id").GetString() != id) throw new InvalidDataException("Native record identity mismatch.");
        return new Revision { Root = root, Kind = kind, Id = id, NativeJson = Encoding.UTF8.GetString(n), CompanionText = Encoding.UTF8.GetString(c), NativeHash = PlayNowSaveJournal.Hash(n), CompanionHash = PlayNowSaveJournal.Hash(c) };
    }
    internal static void Check(Revision r)
    {
        if (PlayNowSaveJournal.Hash(PlayNowSaveJournal.Read(PlayNowSaveJournal.NativePath(r.Root, r.Kind, r.Id))) != r.NativeHash
            || PlayNowSaveJournal.Hash(PlayNowSaveJournal.Read(PlayNowSaveJournal.CompanionPath(Overrides, r.Kind, r.Id))) != r.CompanionHash)
            throw new IOException("This exported record changed while the editor was open. Reload before saving.");
    }
    internal static void AcceptLoaded(TeamRepository repository, TeamDataModel model, TeamData team)
    {
        var disk = repository.m_customTeamRepository.TryCast<PersistentDataCustomTeamsRepository>();
        if (disk == null) return;
        if (!Models.TryGetValue(model.Pointer, out var loaded) || loaded.Revision.Id != model.Id || loaded.Revision.Root != disk._path)
            throw new IOException("The native team's raw load revision was not captured.");
        var revision = loaded.Revision;
        Check(revision);
        using var json = JsonDocument.Parse(revision.NativeJson);
        var fields = new[] { ("leftWingerId", model.leftWingerId), ("rightWingerId", model.rightWingerId), ("centerId", model.centerId),
            ("leftDefensemenId", model.leftDefensemenId), ("rightDefensemenId", model.rightDefensemenId), ("goalieId", model.goalieId) };
        if (fields.Any(p => json.RootElement.GetProperty(p.Item1).GetString() != p.Item2)) throw new IOException("Loaded native roster is stale.");
        Teams[team.Pointer] = new TeamRevision { Team = team, Instance = team.GetInstanceID(), Revision = revision };
        foreach (var p in Items(team.forwards)) PlayNowPlayerSave.BindLoaded(p);
        PlayNowPlayerSave.BindLoaded(team.goalie);
    }
    internal static void Clone(TeamData source, TeamData clone)
    {
        if (source == null || clone == null || !Teams.TryGetValue(source.Pointer, out var old) || old.Instance != source.GetInstanceID()) return;
        Teams[clone.Pointer] = new TeamRevision { Team = clone, Instance = clone.GetInstanceID(), Revision = old.Revision };
        foreach (var p in Items(clone.forwards)) PlayNowPlayerSave.BindClone(source, p);
        PlayNowPlayerSave.BindClone(source, clone.goalie);
    }
    internal static void Clear() => Teams.Clear();
    public static void TeamModelsPostfix(PersistentDataCustomTeamsRepository __instance, Il2CppSystem.Collections.Generic.List<TeamDataModel> __result)
    {
        foreach (var model in Items(__result))
        {
            if (model == null || !PlayNowSaveJournal.SafeId(model.Id)) continue;
            string path = PlayNowSaveJournal.CompanionPath(Overrides, "team", model.Id);
            if (!File.Exists(path) || !PlayNowRosterCodec.TryRead(new CampaignDocument(File.ReadAllText(path)), out _, out _)) continue;
            // A warm native cache can share palette objects with an adapted team.
            // Keep its original immutable revision instead of legitimizing those
            // in-memory mutations using a newly read file.
            if (Models.TryGetValue(model.Pointer, out var old) && old.Revision.Id == model.Id && old.Revision.Root == __instance._path) continue;
            var revision = Capture(__instance._path, "team", model.Id);
            PlayNowPlayerSave.MatchLoadedModel(revision.NativeJson, JsonUtility.ToJson(model), typeof(TeamDataModel));
            Models[model.Pointer] = new ModelRevision { Model = model, Revision = revision };
        }
    }
    internal static CampaignDocument ResolveDocument(TeamDataModel model, CampaignDocument original)
    {
        if (!Models.TryGetValue(model.Pointer, out var loaded) || loaded.Revision.Id != model.Id) return original;
        var revision = loaded.Revision;
        var doc = new CampaignDocument(original.ToString());
        using var native = JsonDocument.Parse(revision.NativeJson);
        var root = native.RootElement;
        foreach (var pair in new[] { ("homeColors", "homeGoalieColors"), ("awayColors", "awayGoalieColors") })
            if (root.TryGetProperty(pair.Item1, out var colors) && colors.ValueKind == JsonValueKind.Object)
            {
                doc.Set("Export Saved Palette " + pair.Item1, colors.GetRawText());
                if (doc.Get("Export Saved Palette " + pair.Item2).Length == 0) doc.Set("Export Saved Palette " + pair.Item2, colors.GetRawText());
            }
        if (root.TryGetProperty("relics", out var relics) && relics.ValueKind == JsonValueKind.Array)
            doc.Set("Export Relic IDs", string.Join(",", relics.EnumerateArray().Select(v => v.GetString())));
        if (root.TryGetProperty("logoId", out var logo) && (logo.GetString() ?? "") != doc.Get("Export Logo ID")) doc.Set("Export Native Logo", "yes");
        return doc;
    }
    internal static void Save(TeamRepository repository, TeamData team) => repository.SaveCustomTeam(team);
    public static void TeamPrefix(TeamRepository __instance, TeamData customTeam, out SaveFrame __state)
    {
        __state = new SaveFrame { Parent = Current };
        Current = __state;
        if (!PlayNowRosterRuntime.IsRegistered(customTeam)) return;
        if (!PlayNowRosterRuntime.TryGet(customTeam, out var roster)) throw new InvalidDataException("The two-line roster is incomplete.");
        if (!Teams.TryGetValue(customTeam.Pointer, out var accepted) || accepted.Instance != customTeam.GetInstanceID()
            || accepted.Team.id != customTeam.id || accepted.Revision.Id != customTeam.id)
            throw new IOException("This exported team has no accepted persistent identity.");
        Check(accepted.Revision);
        roster = PlayNowRosterCodec.Remap(roster, id => PlayNowRosterRuntime.TryGetSourceId(customTeam, id, out string source) ? source : id);
        ValidateReferences(__instance, accepted.Revision.Root, roster);
        var doc = new CampaignDocument(accepted.Revision.CompanionText);
        PlayNowRosterCodec.Write(doc, roster);
        foreach (var p in new[] { ("homeColors", customTeam.homeColors), ("awayColors", customTeam.awayColors),
            ("homeGoalieColors", customTeam.homeGoalieColors), ("awayGoalieColors", customTeam.awayGoalieColors) })
        {
            if (p.Item2 == null) throw new InvalidDataException("A saved team palette is missing.");
            doc.Set("Export Saved Palette " + p.Item1, JsonUtility.ToJson(p.Item2));
        }
        __state.Team = customTeam; __state.Revision = accepted.Revision; __state.Roster = roster; __state.Document = doc; __state.Repository = __instance;
    }
    public static Exception TeamFinalizer(Exception __exception, SaveFrame __state)
    {
        if (__state != null) Current = __state.Parent;
        return __exception;
    }
    private static void ValidateReferences(TeamRepository repository, string root, PlayNowRoster roster)
    {
        foreach (string id in PlayNowRosterCodec.PlayerIds(roster))
        {
            bool goalie = id == roster.GoalieId;
            string kind = goalie ? "goalie" : "forward", path = PlayNowSaveJournal.NativePath(root, kind, id);
            if (File.Exists(path))
            {
                using var model = JsonDocument.Parse(File.ReadAllBytes(path));
                if (model.RootElement.GetProperty("id").GetString() != id) throw new InvalidDataException("Referenced player's native ID differs.");
                continue;
            }
            int matches = goalie ? Items(repository.m_goalieRepository.LoadBuiltInGoalies()).Count(p => p != null && p.id == id)
                : Items(repository.m_forwardRepository.LoadBuiltInForwards()).Count(p => p != null && p.id == id);
            if (matches != 1) throw new IOException("Save the new custom player explicitly before assigning it to this team: " + id);
        }
    }
    public static bool TeamWriterPrefix(PersistentDataCustomTeamsRepository __instance, TeamDataModel teamDataModel)
    {
        var f = Current;
        if (f?.Revision == null) return true;
        if (teamDataModel == null || teamDataModel.Id != f.Revision.Id || __instance._path != f.Revision.Root)
            throw new InvalidDataException("The native writer does not match the exported edit.");
        var r = f.Roster;
        teamDataModel.leftWingerId = r.Line1[0]; teamDataModel.rightWingerId = r.Line1[1]; teamDataModel.centerId = r.Line1[2];
        teamDataModel.leftDefensemenId = r.Line1[3]; teamDataModel.rightDefensemenId = r.Line1[4]; teamDataModel.goalieId = r.GoalieId;
        f.Document.Set("Export Relic IDs", string.Join(",", Items(teamDataModel.relics)));
        using (var old = JsonDocument.Parse(f.Revision.NativeJson))
            if (!old.RootElement.TryGetProperty("logoId", out var logo) || logo.GetString() != teamDataModel.logoId)
                f.Document.Set("Export Native Logo", "yes");
        PlayNowSaveJournal.Commit(f.Revision.Root, Overrides, "team", f.Revision.Id, JsonUtility.ToJson(teamDataModel), f.Document,
            f.Revision.NativeHash, f.Revision.CompanionHash, () => ValidateReferences(f.Repository, f.Revision.Root, r));
        f.Committed = true; __instance.m_customTeams = null;
        PlayNowTranslation.Invalidate(f.Revision.Id, true);
        Teams[f.Team.Pointer].Revision = Capture(f.Revision.Root, "team", f.Revision.Id);
        return false;
    }
    public static void TeamLoadPrefix(PersistentDataCustomTeamsRepository __instance)
    {
        if (!PlayNowSaveJournal.Recover(__instance._path, Overrides, "team")) return;
        __instance.m_customTeams = null; PlayNowTranslation.InvalidateAll();
    }
    internal static void RestorePalettes(TeamData team, CampaignDocument doc)
    {
        foreach (string name in new[] { "homeColors", "awayColors", "homeGoalieColors", "awayGoalieColors" })
        {
            string json = doc.Get("Export Saved Palette " + name);
            if (json.Length == 0) throw new InvalidDataException("Incomplete saved team palette set.");
            var palette = JsonUtility.FromJson<TeamColorsData>(json);
            typeof(TeamData).GetProperty(name).SetValue(team, palette);
            PlayNowTranslation.Register(palette, name.StartsWith("away", StringComparison.Ordinal));
        }
    }
    internal static void Install(Harmony h)
    {
        h.Patch(AccessTools.Method(typeof(TeamRepository), "SaveCustomTeam"), prefix: new HarmonyMethod(typeof(PlayNowRosterSave), nameof(TeamPrefix)), finalizer: new HarmonyMethod(typeof(PlayNowRosterSave), nameof(TeamFinalizer)));
        h.Patch(AccessTools.Method(typeof(PersistentDataCustomTeamsRepository), "SaveCustomTeam"), prefix: new HarmonyMethod(typeof(PlayNowRosterSave), nameof(TeamWriterPrefix)));
        foreach (string name in new[] { "LoadCustomTeams", "Preload" })
            h.Patch(AccessTools.Method(typeof(PersistentDataCustomTeamsRepository), name), prefix: new HarmonyMethod(typeof(PlayNowRosterSave), nameof(TeamLoadPrefix)),
                postfix: name == "LoadCustomTeams" ? new HarmonyMethod(typeof(PlayNowRosterSave), nameof(TeamModelsPostfix)) : null);
        PlayNowPlayerSave.Install(h);
    }
}
