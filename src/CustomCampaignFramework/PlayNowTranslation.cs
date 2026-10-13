using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CampaignCore;
using Data;
using HarmonyLib;
using Tape2Tape.Customization;
using Tape2Tape.Customization.UI;
using UnityEngine;

namespace EndlessMode;

// Versioned export data supplements fields omitted by the native save models.
// Resolve from all loaded assets, not the mode-specific random reward pools.
internal static class PlayNowTranslation
{
    private static readonly Dictionary<string, CampaignDocument> Documents = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<IntPtr, bool> Kits = new();

    internal static void RegisterPreview(string id, CampaignDocument doc) => Documents[id] = doc;

    // TeamData.Clone generates match IDs but retains the source OriginalId.
    // Resolve render metadata through that identity without reapplying the
    // exported model over a live match player's stats or generated ID.
    internal static CampaignDocument ReadPlayer(SkaterData player)
        => player == null ? null : Read(player.id) ?? Read(player.OriginalId);

    internal static CampaignDocument Read(string id, bool team = false)
    {
        if (string.IsNullOrEmpty(id) || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
        string key = id + (team ? ".team" : "");
        if (Documents.TryGetValue(key, out var cached)) return cached;
        string file = Path.Combine(Plugin.ModContentRoot, "play_now_overrides", key + ".txt");
        if (!File.Exists(file)) return null;
        var doc = new CampaignDocument(File.ReadAllText(file));
        if (doc.Get("Export Version") != "2" && doc.Get("Export Version") != "3") return null;
        Documents[key] = doc;
        return doc;
    }

    internal static void Player(SkaterData player, string id)
    {
        var doc = Read(id); if (doc == null || player == null) return;
        ExportAppearanceRuntime.Apply(player, doc);
        var talents = Resources.FindObjectsOfTypeAll<Rogue.Talent>();
        var resolved = new Il2CppSystem.Collections.Generic.List<Rogue.Talent>();
        foreach (string wanted in Split(doc.Get("Export Talent IDs")))
        {
            var talent = talents.FirstOrDefault(t => t != null && t.id == wanted);
            if (talent == null) throw new InvalidOperationException($"Export talent ID '{wanted}' is not loaded for {player.firstName}.");
            resolved.Add(talent);
        }
        string abilityId = doc.Get("Export Ability ID");
        var ability = abilityId.Length == 0 ? null : Resources.FindObjectsOfTypeAll<Rogue.Ability>().FirstOrDefault(a => a != null && a.id == abilityId);
        if (abilityId.Length > 0 && ability == null) throw new InvalidOperationException($"Export ability ID '{abilityId}' is not loaded.");
        player.powerups = resolved; player.ability = ability; player.ResetTalentCache();
        var goalie = player.TryCast<GoaltenderData>();
        if (goalie != null && int.TryParse(doc.Get("Pass Read"), out int passRead)) goalie.passReadSkill = passRead;
        ExportAppearanceRuntime.Receipt(player, doc);
        Plugin.Log.LogInfo($"[PlayNow] Translated {resolved.Count} talents and ability '{abilityId}' for {player.firstName} ({id})");
    }

    private static string[] Split(string value) => value.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();

    public static bool ForwardReferencesPrefix(ForwardDataModel model, ForwardData forwardData)
    {
        if (model == null || Read(model.id) == null) return true;
        try { Player(forwardData, model.id); return false; }
        catch (Exception ex) { Plugin.Log.LogError($"[PlayNow] Forward translation failed: {ex}"); return true; }
    }

    public static bool GoalieReferencesPrefix(GoalieDataModel model, GoaltenderData goalieData)
    {
        if (model == null || Read(model.id) == null) return true;
        try { Player(goalieData, model.id); return false; }
        catch (Exception ex) { Plugin.Log.LogError($"[PlayNow] Goalie translation failed: {ex}"); return true; }
    }

    public static void TeamReferencesPostfix(TeamDataModel model, TeamData teamData) => TeamPostfix(model, teamData);

    public static void TeamPostfix(TeamDataModel model, TeamData __result)
    {
        if (model == null || __result == null) return;
        try
        {
            Register(__result.homeColors, false); Register(__result.awayColors, true);
            Register(__result.homeGoalieColors, false); Register(__result.awayGoalieColors, true);
            var doc = Read(model.Id, true); if (doc == null) return;
            ExportAppearanceRuntime.ApplyTeamLogo(__result, doc);
            TeamGoalSound.Register(__result.id, doc.Get("Goal Sound Data"), doc.Get("Goal Sound Volume", "100"));
            // The native adapter derives equipment colors from the jersey. Restore
            // explicit authored channels after that normalization has finished.
            var palette = new CampaignDocument(doc.ToString());
            foreach (var pair in new[] { ("Jersey Primary", "Jersey Color"), ("Jersey Secondary", "Jersey Secondary Color"), ("Jersey Accent", "Jersey Accent Color"),
                ("Away Primary", "Jersey Away Color"), ("Away Secondary", "Jersey Away Secondary Color"), ("Away Accent", "Jersey Away Accent Color"),
                ("Number Color Home", "Number Tertiary Color"), ("Number Color Away", "Number Away Tertiary Color") })
                if (doc.Get(pair.Item1).Length > 0) palette.Set(pair.Item2, doc.Get(pair.Item1));
            Paint(__result.homeColors, palette, false); Paint(__result.awayColors, palette, true);
            Paint(__result.homeGoalieColors, palette, false); Paint(__result.awayGoalieColors, palette, true);
            var all = Resources.FindObjectsOfTypeAll<Rogue.Relic>();
            var relics = new Il2CppSystem.Collections.Generic.List<Rogue.Relic>();
            foreach (string id in Split(doc.Get("Export Relic IDs")))
            {
                var relic = all.FirstOrDefault(r => r != null && r.id == id);
                if (relic == null) throw new InvalidOperationException($"Export relic ID '{id}' is not loaded.");
                relics.Add(relic);
            }
            __result.relics = relics;
            Plugin.Log.LogInfo($"[PlayNow] Translated team '{__result.teamName}' with {relics.Count} relics");
        }
        catch (Exception ex) { Plugin.Log.LogError($"[PlayNow] Team translation failed: {ex}"); }
    }

    internal static void Register(TeamColorsData colors, bool away) { if (colors != null) Kits[colors.Pointer] = away; }

    // Which kit a team palette belongs to. TeamData.GetColors/GetGoalieColors only
    // return one of these four fields, so read the fields instead of hooking those
    // methods: the Harmony detour on the 16-byte GetColors handed the game its own
    // trampoline address as the TeamColorsData, which ApplySkinAsync stored and the
    // UnloadUnusedAssets liveness scan then crashed on (2026-09-25 first faceoff).
    private static readonly HashSet<IntPtr> NotKits = new();
    private static bool TryKit(TeamColorsData colors, out bool away)
    {
        away = false;
        if (colors == null) return false;
        if (Kits.TryGetValue(colors.Pointer, out away)) return true;
        if (NotKits.Contains(colors.Pointer)) return false;
        foreach (var team in Resources.FindObjectsOfTypeAll<TeamData>())
        {
            if (team == null) continue;
            Register(team.homeColors, false); Register(team.awayColors, true);
            Register(team.homeGoalieColors, false); Register(team.awayGoalieColors, true);
        }
        if (Kits.TryGetValue(colors.Pointer, out away)) return true;
        NotKits.Add(colors.Pointer);
        return false;
    }
    internal static bool PreviewAway(TeamColorsData colors, SkaterData skater)
        => TryKit(colors, out bool away) ? away : CampaignAppearanceRuntime.Away(skater);

    // Campaign players: paint their campaign team's kit after the native merge (the team
    // file is the source of truth), then put the player's own stick/skates back on top the
    // way the merge does. Play Now players carry no kit colors: the native merge (their Play
    // Now team's kit + their own stick/skates) is already the base-game result.
    // Copy before writing to avoid recoloring teammates.
    public static void ColorsPostfix(TeamColorsData colors, SkaterData skater, ref TeamColorsData __result)
    {
        if (skater == null || __result == null) return;
        try
        {
            var doc = CampaignAppearanceRuntime.Read(skater); if (doc == null) return;
            bool away = TryKit(colors, out bool a) ? a : CampaignAppearanceRuntime.Away(skater);
            var copy = TeamColorsData.CopyFrom(__result);
            Paint(copy, doc, away);
            if (skater.HasCustomStickColor) copy.stickScheme = ColorScheme.CopyFrom(skater.StickColor);
            if (skater.HasCustomSkatesColor) copy.skatesScheme = ColorScheme.CopyFrom(skater.SkatesColor);
            __result = copy;
        }
        catch (Exception ex) { Plugin.Log.LogError($"[PlayNow] Color translation failed: {ex}"); }
    }

    internal static void Paint(TeamColorsData copy, CampaignDocument doc, bool away)
    {
            if (copy == null) return;
            string Value(string home, string visitor) => doc.Get(away ? visitor : home);
            void Set(ColorScheme scheme, string home, string visitor, int channel)
            {
                if (scheme == null || !FieldSchema.TryRgb(Value(home, visitor), out byte r, out byte g, out byte b)) return;
                var c = new Color(r / 255f, g / 255f, b / 255f, 1);
                if (channel == 0) scheme.primaryColor = c; else if (channel == 1) scheme.secondaryColor = c; else scheme.tertiaryColor = c;
            }
            void Piece(ColorScheme scheme, string piece)
            {
                Set(scheme, piece + " Color", piece + " Away Color", 0);
                Set(scheme, piece + " Secondary Color", piece + " Away Secondary Color", 1);
                Set(scheme, piece + " Tertiary Color", piece + " Away Tertiary Color", 2);
            }
            Piece(copy.jerseyScheme, "Jersey"); Piece(copy.helmetScheme, "Helmet"); Piece(copy.glovesScheme, "Gloves");
            Piece(copy.pantsScheme, "Pants"); Piece(copy.socksScheme, "Socks"); Piece(copy.numberScheme, "Number"); Piece(copy.stickScheme, "Stick");
            Set(copy.jerseyScheme, "Jersey Accent Color", "Jersey Away Accent Color", 2);
            Set(copy.skatesScheme, "Skates Color", "Skates Away Color", 0);
            Set(copy.skatesScheme, "Blade Color", "Blade Away Color", 1);
            Set(copy.skatesScheme, "Laces Color", "Laces Away Color", 2);
            Set(copy.jerseyScheme, "Bicep Color", "Bicep Away Color", 1);
    }

    internal static void Install(Harmony harmony)
    {
        ExportAppearanceRuntime.Install(harmony);
        ExportLoadoutBadges.Install(harmony);
        // Never hook TeamData.GetColors/GetGoalieColors — see TryKit.
        harmony.Patch(AccessTools.Method(typeof(TeamDataAdapter), "ConvertToTeamData"), postfix: new HarmonyMethod(typeof(PlayNowTranslation), nameof(TeamPostfix)));
        // Repositories resolve IDs AFTER adapters. Replace their curated-pool
        // lookup for versioned exports, or they would overwrite these assignments.
        harmony.Patch(AccessTools.Method(typeof(ForwardRepository), "DeserializeAssetReferences"), prefix: new HarmonyMethod(typeof(PlayNowTranslation), nameof(ForwardReferencesPrefix)));
        harmony.Patch(AccessTools.Method(typeof(GoalieRepository), "DeserializeAssetReferences"), prefix: new HarmonyMethod(typeof(PlayNowTranslation), nameof(GoalieReferencesPrefix)));
        harmony.Patch(AccessTools.Method(typeof(TeamRepository), "DeserializeAssetReferences"), postfix: new HarmonyMethod(typeof(PlayNowTranslation), nameof(TeamReferencesPostfix)));
        harmony.Patch(AccessTools.Method(typeof(TeamColorsData), "MergeWithCustomSkaterColors"), postfix: new HarmonyMethod(typeof(PlayNowTranslation), nameof(ColorsPostfix)));
    }
}
