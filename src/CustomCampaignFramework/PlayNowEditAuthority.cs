using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using CampaignCore;

namespace EndlessMode;

// Only native editor-confirmed cosmetic choices own personal appearance. The
// native writer's other skin fields can be preview paint, even on explicit Save.
internal sealed class PlayNowEditAuthority
{
    internal readonly string Id, Role, NativeHash, CompanionHash, NativeJson, CompanionText;
    private readonly Dictionary<string, JsonNode> Cosmetics = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> Supplemental = new(StringComparer.Ordinal);
    internal PlayNowEditAuthority(string role, string id, string nativeJson, string companionText, string nativeHash, string companionHash)
    {
        if (role != "forward" && role != "goalie" || !PlayNowSaveJournal.SafeId(id)) throw new InvalidDataException("Invalid player edit identity.");
        Role = role; Id = id; NativeJson = nativeJson; CompanionText = companionText; NativeHash = nativeHash; CompanionHash = companionHash;
        if (JsonNode.Parse(nativeJson)?["id"]?.GetValue<string>() != id) throw new InvalidDataException("Player edit native identity mismatch.");
    }
    internal void ConfirmSkin(string field, string value)
    {
        if (field != "headSkin" && !(Role == "forward" && field == "stickSkin")) throw new InvalidDataException("Unreviewed editor appearance field.");
        Cosmetics[field] = JsonValue.Create(value ?? "");
    }
    internal void ConfirmColor(int item, int channel, float[] rgba)
    {
        if (item < 0 || item > 1 || channel < 0 || channel > 2 || rgba == null || rgba.Length != 4 || rgba.Any(v => !float.IsFinite(v)))
            throw new InvalidDataException("Invalid personal color choice.");
        string key = (item == 0 ? PlayerColorModel.StickKeys : PlayerColorModel.SkatesKeys)[channel];
        Supplemental[key] = string.Join(",", rgba.Take(3).Select(v => Math.Clamp((int)Math.Round(v * 255), 0, 255)));
        Supplemental["Export Personal " + item + " " + channel] = JsonSerializer.Serialize(rgba);
    }
    internal (string Native, CampaignDocument Companion) Reconcile(string finalNativeJson)
    {
        var before = JsonNode.Parse(NativeJson) as JsonObject ?? throw new InvalidDataException("Invalid prior player model.");
        var after = JsonNode.Parse(finalNativeJson) as JsonObject ?? throw new InvalidDataException("Invalid edited player model.");
        if (after["id"]?.GetValue<string>() != Id) throw new InvalidDataException("Editor changed player identity.");
        foreach (var p in before.Where(p => !after.ContainsKey(p.Key))) after[p.Key] = JsonNode.Parse(p.Value?.ToJsonString() ?? "null");
        // Both native schemas enumerate personal skins by these names; preserve
        // all of them unless the reviewed editor action confirmed that field.
        foreach (string key in before.Select(p => p.Key).Where(k => k.EndsWith("Skin", StringComparison.Ordinal) || k == "skin" || k == "awaySkin"))
            after[key] = JsonNode.Parse(before[key]?.ToJsonString() ?? "null");
        foreach (var pair in Cosmetics) after[pair.Key] = JsonNode.Parse(pair.Value.ToJsonString());
        var doc = new CampaignDocument(CompanionText);
        foreach (var pair in Supplemental) doc.Set(pair.Key, pair.Value);
        doc.Set(PlayerColorModel.VersionKey, PlayerColorModel.CurrentVersion);
        doc.Set("Export Model", after.ToJsonString());
        // Empty is a real selection. Never fall back to the original loadout.
        doc.Set("Export Ability ID", after["abilityId"]?.GetValue<string>() ?? "");
        var talents = after["talentIds"] as JsonArray;
        doc.Set("Export Talent IDs", talents == null ? "" : string.Join(",", talents.Select(t => t?.GetValue<string>() ?? "")));
        doc.Set("Name", ((after["firstName"]?.GetValue<string>() ?? "") + " " + (after["lastName"]?.GetValue<string>() ?? "")).Trim());
        foreach (var p in new[] { ("number", "Number"), ("headSkin", "Face"), ("stickSkin", Role == "goalie" ? "Stick Skin" : "Stick"), ("speed", "Speed"),
            ("checking", "Checking"), ("shotPower", "Shot Power"), ("shotAccuracy", "Accuracy"), ("skill", "Skill"), ("catchingSkill", "Catching"),
            ("gloveSkill", "Glove"), ("blockerSkill", "Blocker"), ("fiveHoleSkill", "Five Hole"), ("standingSpeed", "Standing Speed"),
            ("butterflySpeed", "Butterfly Speed"), ("recoverySkill", "Recovery"), ("passPower", "Pass Power"), ("pokecheckSkill", "Poke Check"), ("depth", "Depth"), ("controlSkill", "Control") })
            if (after[p.Item1] != null) doc.Set(p.Item2, after[p.Item1].ToString());
        return (after.ToJsonString(), doc);
    }
}
