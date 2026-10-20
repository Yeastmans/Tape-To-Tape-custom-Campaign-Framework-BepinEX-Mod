using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CampaignCore;
using Data;
using HarmonyLib;
using Tape2Tape.Customization;
using Tape2Tape.Customization.UI;
using UnityEngine;

namespace EndlessMode;

internal static class PlayNowPlayerSave
{
    private sealed class Binding
    {
        internal SkaterData Player;
        internal int Instance;
        internal PlayNowRosterSave.Revision Revision;
    }
    private sealed class Editor
    {
        internal SkaterData Player;
        internal int Instance;
        internal CustomizeSkaterData Staged;
        internal PlayNowRosterSave.Revision Revision;
        internal PlayNowEditAuthority Authority;
    }
    internal sealed class Frame
    {
        internal Frame Parent;
        internal SkaterData Player;
        internal PlayNowRosterSave.Revision Revision;
        internal PlayNowEditAuthority Authority;
    }
    private static readonly Dictionary<string, PlayNowRosterSave.Revision> Loaded = new(StringComparer.Ordinal);
    private static readonly Dictionary<IntPtr, Binding> Players = new();
    private static readonly Dictionary<IntPtr, Editor> Editors = new();
    [ThreadStatic] private static Frame Current;
    private static bool Member(string id)
    {
        if (!PlayNowSaveJournal.SafeId(id) || !Directory.Exists(PlayNowRosterSave.Overrides)) return false;
        foreach (string path in Directory.EnumerateFiles(PlayNowRosterSave.Overrides, "*.team.txt"))
            if (PlayNowRosterCodec.TryRead(new CampaignDocument(File.ReadAllText(path)), out var roster, out _)
                && PlayNowRosterCodec.PlayerIds(roster).Contains(id, StringComparer.Ordinal)) return true;
        return false;
    }
    internal static void Observe(string root, string kind, SkaterData player, string modelJson)
    {
        if (player == null || !Member(player.id) || PlayNowTranslation.Read(player.id) == null) return;
        var r = PlayNowRosterSave.Capture(root, kind, player.id);
        // Native cached models must still describe the file that establishes the
        // accepted revision. A newer file cannot legitimize an older object.
        MatchLoadedModel(r.NativeJson, modelJson);
        Loaded[kind + ":" + player.id] = r;
        Players[player.Pointer] = new Binding { Player = player, Instance = player.GetInstanceID(), Revision = r };
        // Native-representable fields are authoritative even for an edit made
        // before this persistence extension was installed. Preserve the omitted
        // supplemental data while preventing old Export Model/loadout replay.
        var authority = new PlayNowEditAuthority(kind, player.id, r.NativeJson, r.CompanionText, r.NativeHash, r.CompanionHash);
        PlayNowTranslation.RegisterPreview(player.id, authority.Reconcile(modelJson).Companion);
    }
    internal static void MatchLoadedModel(string persisted, string serialized)
    {
        using var disk = JsonDocument.Parse(persisted); using var model = JsonDocument.Parse(serialized);
        bool Same(JsonElement a, JsonElement b)
        {
            if (a.ValueKind != b.ValueKind) return false;
            if (a.ValueKind == JsonValueKind.Object)
                // Studio includes a few role-independent fields (for example
                // name/isLefty on goalies) that native JsonUtility ignores.
                return a.EnumerateObject().All(p => !b.TryGetProperty(p.Name, out var value) || Same(p.Value, value));
            if (a.ValueKind == JsonValueKind.Array) return a.GetArrayLength() == b.GetArrayLength() && a.EnumerateArray().Zip(b.EnumerateArray(), Same).All(v => v);
            if (a.ValueKind == JsonValueKind.Number) return a.GetDouble() == b.GetDouble();
            if (a.ValueKind == JsonValueKind.String) return a.GetString() == b.GetString();
            return a.GetRawText() == b.GetRawText();
        }
        if (!Same(disk.RootElement, model.RootElement)) throw new IOException("Loaded native player/team model is older than its saved file.");
    }
    internal static void BindLoaded(SkaterData player)
    {
        if (player == null) return;
        string kind = player.TryCast<GoaltenderData>() != null ? "goalie" : "forward";
        if (Loaded.TryGetValue(kind + ":" + player.id, out var r))
            Players[player.Pointer] = new Binding { Player = player, Instance = player.GetInstanceID(), Revision = r };
    }
    internal static void BindClone(TeamData source, SkaterData player)
    {
        if (player == null || !PlayNowRosterRuntime.TryGetSourceId(source, string.IsNullOrEmpty(player.OriginalId) ? player.id : player.OriginalId, out string id)) return;
        var original = PlayNowRosterSave.Items(source.forwards).Cast<SkaterData>().Append(source.goalie).FirstOrDefault(p => p != null && (p.id == id || p.id == player.OriginalId));
        if (original != null && Players.TryGetValue(original.Pointer, out var b))
            Players[player.Pointer] = new Binding { Player = player, Instance = player.GetInstanceID(), Revision = b.Revision };
    }
    private static Editor Begin(SkaterData player, CustomizeSkaterData staged = null, SkaterData source = null)
    {
        if (player == null || !Member(player.id) || PlayNowTranslation.Read(player.id) == null) return null;
        source ??= player;
        if (!Players.TryGetValue(source.Pointer, out var accepted) || accepted.Instance != source.GetInstanceID()
            || accepted.Player.id != accepted.Revision.Id || player.id != accepted.Revision.Id)
            throw new IOException("Reload this exported player before editing; its source revision is unavailable.");
        PlayNowRosterSave.Check(accepted.Revision);
        var r = accepted.Revision;
        var e = new Editor { Player = player, Instance = player.GetInstanceID(), Staged = staged, Revision = r,
            Authority = new PlayNowEditAuthority(r.Kind, r.Id, r.NativeJson, r.CompanionText, r.NativeHash, r.CompanionHash) };
        Editors[player.Pointer] = e;
        return e;
    }
    public static void ForwardEditorPostfix(CustomizeForwardData __instance, ForwardData data) => Begin(data, __instance);
    public static void GoalieEditorPostfix(CustomizeGoalieData __instance, GoaltenderData data) => Begin(data, __instance);
    public static void LegacyForwardLoadPostfix(ForwardCustomizationHandler __instance, ForwardData pickedForward)
        => Begin(__instance._forwardData, source: pickedForward);
    public static void LegacyGoalieLoadPostfix(GoalieCustomizationHandler __instance, GoaltenderData pickedGoalie)
        => Begin(__instance._goalieData, source: pickedGoalie);
    private static Editor For(SkaterCustomizationMenu menu)
    {
        var stage = menu?.CurrentSkater; var player = stage?.SkaterData;
        if (player == null || !Editors.TryGetValue(player.Pointer, out var editor) || editor.Instance != player.GetInstanceID()
            || editor.Staged == null || editor.Staged.Pointer != stage.Pointer || editor.Revision.Id != player.id) return null;
        return editor;
    }
    public static void HeadConfirmed(SkaterCustomizationMenu __instance)
    {
        var e = For(__instance); if (e == null) return;
        string value = e.Staged.TryCast<CustomizeForwardData>()?.headSkin ?? e.Staged.TryCast<CustomizeGoalieData>()?.headSkin;
        e.Authority.ConfirmSkin("headSkin", value);
    }
    public static void StickConfirmed(SkaterCustomizationMenu __instance)
    {
        var e = For(__instance); var data = e?.Staged.TryCast<CustomizeForwardData>();
        if (data != null) e.Authority.ConfirmSkin("stickSkin", data.stickSkin);
    }
    public static void ColorConfirmed(SkaterCustomizationMenu __instance, Color c)
    {
        var e = For(__instance); if (e == null) return;
        var selection = __instance.m_CurrentColorBeingPicked;
        e.Authority.ConfirmColor((int)selection.Item, selection.Index, new[] { c.r, c.g, c.b, c.a });
    }
    // Preview and Back-from-color-panel deliberately do not touch authority.
    // Hide invalidates the exact staged owner, so delayed callbacks cannot reuse it.
    public static void HidePrefix(SkaterCustomizationMenu __instance)
    {
        var e = For(__instance); if (e != null) Editors.Remove(e.Player.Pointer);
    }
    public static void LegacyHeadConfirmed(ForwardCustomizationHandler __instance)
    {
        var p = __instance._forwardData;
        if (p != null && Editors.TryGetValue(p.Pointer, out var e) && e.Instance == p.GetInstanceID()) e.Authority.ConfirmSkin("headSkin", p.headSkin);
    }
    public static void LegacyStickConfirmed(ForwardCustomizationHandler __instance)
    {
        var p = __instance._forwardData;
        if (p != null && Editors.TryGetValue(p.Pointer, out var e) && e.Instance == p.GetInstanceID()) e.Authority.ConfirmSkin("stickSkin", p.stickSkin);
    }
    private static void Start(SkaterData player, out Frame frame)
    {
        frame = new Frame { Parent = Current };
        Current = frame;
        if (player == null) return;
        if (!Editors.TryGetValue(player.Pointer, out var e))
        {
            if (!Member(player.id) || PlayNowTranslation.Read(player.id) == null) return;
            throw new IOException("An exported player save requires its explicit editor session.");
        }
        if (e.Instance != player.GetInstanceID() || e.Revision.Id != player.id)
            throw new IOException("The player editor identity changed during this edit.");
        PlayNowRosterSave.Check(e.Revision);
        frame.Player = player; frame.Revision = e.Revision; frame.Authority = e.Authority;
    }
    public static void ForwardPrefix(ForwardData skater, out Frame __state) => Start(skater, out __state);
    public static void GoaliePrefix(GoaltenderData customGoalie, out Frame __state) => Start(customGoalie, out __state);
    public static Exception Finalizer(Exception __exception, Frame __state)
    { if (__state != null) Current = __state.Parent; return __exception; }
    private static bool Write(string root, string kind, string id, string json)
    {
        var f = Current;
        if (f?.Revision == null) return true;
        if (root != f.Revision.Root || kind != f.Revision.Kind || id != f.Revision.Id) throw new InvalidDataException("Player writer identity differs from its editor.");
        var pair = f.Authority.Reconcile(json);
        PlayNowSaveJournal.Commit(root, PlayNowRosterSave.Overrides, kind, id, pair.Native, pair.Companion, f.Revision.NativeHash, f.Revision.CompanionHash);
        PlayNowTranslation.Invalidate(id);
        var next = PlayNowRosterSave.Capture(root, kind, id);
        Loaded[kind + ":" + id] = next;
        Players[f.Player.Pointer] = new Binding { Player = f.Player, Instance = f.Player.GetInstanceID(), Revision = next };
        // The outer native repository caches this object after its writer returns.
        // Restore the committed personal data before that cache update/RefreshSkater.
        PlayNowTranslation.Player(f.Player, id);
        PlayNowRosterRuntime.RefreshSavedPlayer(f.Player);
        Editors.Remove(f.Player.Pointer);
        return false;
    }
    public static bool ForwardWriterPrefix(PersistentDataCustomForwardRepository __instance, ForwardDataModel data)
    {
        bool native = Write(__instance._persistentDataPath, "forward", data.id, JsonUtility.ToJson(data));
        if (!native) __instance._customForwardCache = null;
        return native;
    }
    public static bool GoalieWriterPrefix(PersistentDataCustomGoalieRepository __instance, GoalieDataModel goalieDataModel)
    {
        bool native = Write(__instance._persistentDataPath, "goalie", goalieDataModel.id, JsonUtility.ToJson(goalieDataModel));
        if (!native) __instance._customGoalieCache = null;
        return native;
    }
    public static void ForwardLoadPrefix(PersistentDataCustomForwardRepository __instance)
    {
        if (!PlayNowSaveJournal.Recover(__instance._persistentDataPath, PlayNowRosterSave.Overrides, "forward")) return;
        __instance._customForwardCache = null; PlayNowTranslation.InvalidateAll();
    }
    public static void GoalieLoadPrefix(PersistentDataCustomGoalieRepository __instance)
    {
        if (!PlayNowSaveJournal.Recover(__instance._persistentDataPath, PlayNowRosterSave.Overrides, "goalie")) return;
        __instance._customGoalieCache = null; PlayNowTranslation.InvalidateAll();
    }
    internal static void RestorePersonal(SkaterData player, CampaignDocument doc)
    {
        for (int item = 0; item < 2; item++) for (int channel = 0; channel < 3; channel++)
        {
            string text = doc.Get("Export Personal " + item + " " + channel); if (text.Length == 0) continue;
            var rgba = JsonSerializer.Deserialize<float[]>(text);
            if (rgba == null || rgba.Length != 4 || rgba.Any(v => !float.IsFinite(v))) throw new InvalidDataException("Invalid saved personal color.");
            var scheme = item == 0 ? player.StickColor : player.SkatesColor;
            var color = new Color(rgba[0], rgba[1], rgba[2], rgba[3]);
            if (channel == 0) scheme.primaryColor = color; else if (channel == 1) scheme.secondaryColor = color; else scheme.tertiaryColor = color;
            var palette = player.TryCast<ForwardData>()?.colorSchemes ?? player.TryCast<GoaltenderData>()?.colorSchemes;
            var rendered = item == 0 ? palette?.stickScheme : palette?.skatesScheme;
            if (rendered != null) { if (channel == 0) rendered.primaryColor = color; else if (channel == 1) rendered.secondaryColor = color; else rendered.tertiaryColor = color; }
        }
    }
    internal static void Install(Harmony h)
    {
        void Patch(Type t, string name, string prefix = null, string postfix = null, string finalizer = null)
            => h.Patch(AccessTools.Method(t, name), prefix: prefix == null ? null : new HarmonyMethod(typeof(PlayNowPlayerSave), prefix),
                postfix: postfix == null ? null : new HarmonyMethod(typeof(PlayNowPlayerSave), postfix), finalizer: finalizer == null ? null : new HarmonyMethod(typeof(PlayNowPlayerSave), finalizer));
        Patch(typeof(ForwardRepository), "SaveCustomForward", nameof(ForwardPrefix), finalizer: nameof(Finalizer));
        Patch(typeof(GoalieRepository), "SaveCustomGoalie", nameof(GoaliePrefix), finalizer: nameof(Finalizer));
        Patch(typeof(PersistentDataCustomForwardRepository), "SaveCustomForward", nameof(ForwardWriterPrefix));
        Patch(typeof(PersistentDataCustomGoalieRepository), "SaveCustomGoalie", nameof(GoalieWriterPrefix));
        foreach (string n in new[] { "LoadCustomForwards", "Preload" }) Patch(typeof(PersistentDataCustomForwardRepository), n, nameof(ForwardLoadPrefix));
        foreach (string n in new[] { "LoadCustomGoalies", "Preload" }) Patch(typeof(PersistentDataCustomGoalieRepository), n, nameof(GoalieLoadPrefix));
        h.Patch(AccessTools.Constructor(typeof(CustomizeForwardData), new[] { typeof(ForwardData) }), postfix: new HarmonyMethod(typeof(PlayNowPlayerSave), nameof(ForwardEditorPostfix)));
        h.Patch(AccessTools.Constructor(typeof(CustomizeGoalieData), new[] { typeof(GoaltenderData) }), postfix: new HarmonyMethod(typeof(PlayNowPlayerSave), nameof(GoalieEditorPostfix)));
        Patch(typeof(ForwardCustomizationHandler), "LoadForward", postfix: nameof(LegacyForwardLoadPostfix));
        Patch(typeof(GoalieCustomizationHandler), "LoadGoalie", postfix: nameof(LegacyGoalieLoadPostfix));
        Patch(typeof(SkaterCustomizationMenu), "OnPanelHeadClick", postfix: nameof(HeadConfirmed));
        Patch(typeof(SkaterCustomizationMenu), "OnPanelStickClick", postfix: nameof(StickConfirmed));
        Patch(typeof(SkaterCustomizationMenu), "OnColorPicked", postfix: nameof(ColorConfirmed));
        Patch(typeof(SkaterCustomizationMenu), "Hide", prefix: nameof(HidePrefix));
        foreach (string n in new[] { "NextFaceSkin", "PrevFaceSkin" }) Patch(typeof(ForwardCustomizationHandler), n, postfix: nameof(LegacyHeadConfirmed));
        foreach (string n in new[] { "NextStickSkin", "PrevStickSkin" }) Patch(typeof(ForwardCustomizationHandler), n, postfix: nameof(LegacyStickConfirmed));
    }
}
