using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CampaignCore;
using Data;
using HarmonyLib;
using Rogue.NPC.GeneralManagerNPC;
using State;
using STS.Map;
using Tape2Tape;

namespace EndlessMode;

// Goalies have a single native roster slot. Keep this path separate from the
// ForwardData free-agent manager and its roster/contract lists.
internal static class GoalieRecruitment
{
    internal const string IdPrefix = "CCF_GFA_";
    private sealed class Selection
    {
        internal IntPtr State, Run, Team, Blueprint;
        internal string RunId, Folder;
        internal int Act, Layer, Index;
        internal bool Matches(CampaignState state)
        {
            var run = state?.runData;
            return !Plugin.IsDefaultMode && state?.Pointer == State && run?.Pointer == Run
                && run.teamData?.Pointer == Team && state.currentNode?.Pointer == Blueprint
                && state.currentNode.nodeType == NodeType.GeneralManager && run.runID == RunId
                && run.CurrentAct == Act && run.currentNodePoint.x == Index && run.currentNodePoint.y == Layer
                && string.Equals(Plugin.ModFolder, Folder, StringComparison.OrdinalIgnoreCase);
        }
    }
    private sealed class Visit
    {
        internal Selection Selection;
        internal IntPtr Node;
        internal HashSet<string> Offered = new(StringComparer.Ordinal);
        internal int Revision;
    }
    private static Selection selected;
    private static Visit visit;
    internal static bool IsRecruit(GoaltenderData goalie) => (goalie?.id ?? "").StartsWith(IdPrefix, StringComparison.Ordinal);
    internal static bool IsGoalieNode => selected?.Matches(CampaignUtil.CampaignState) == true;

    internal static void Selected(CampaignState state, MapNode node)
    {
        selected = null; visit = null;
        if (Plugin.IsDefaultMode || state?.runData?.teamData == null || node?.Node == null
            || state.currentNode?.nodeType != NodeType.GeneralManager) return;
        try
        {
            var run = state.runData;
            if (string.IsNullOrWhiteSpace(run.runID) || run.currentNodePoint.x != node.Node.NodeIndex
                || run.currentNodePoint.y != node.Node.LayerIndex || CustomMapLayout.RecruitRoleFor(node) != GoalieFreeAgents.Goalies) return;
            selected = new Selection { State = state.Pointer, Run = run.Pointer, Team = run.teamData.Pointer,
                Blueprint = state.currentNode.Pointer, RunId = run.runID, Folder = Plugin.ModFolder,
                Act = run.CurrentAct, Layer = node.Node.LayerIndex, Index = node.Node.NodeIndex };
        }
        catch (Exception ex) { Plugin.Log.LogWarning("[GoalieRecruit] Could not resolve saved map role: " + ex.Message); }
    }
    internal static void BeginVisit() => visit = null;

    internal static bool Choices(GeneralManagerEvent node, int choicesMade)
    {
        var cards = new List<EventChoice>();
        try
        {
            if (!IsGoalieNode) return false;
            if (visit == null || visit.Node != node.Pointer || !ReferenceEquals(visit.Selection, selected))
                visit = new Visit { Selection = selected, Node = node.Pointer };
            var current = visit;
            int revision = ++current.Revision;
            if (choicesMade >= node.NumTimesToOfferChoices) { TwoLineContracts.ReplaceChoices(node, cards); return false; }
            var state = CampaignUtil.CampaignState;
            var team = state.runData.teamData;
            var config = SquadPoolRuntime.Team(state.GetCurrentSquad());
            // Native GM offers two candidates. Short or empty authored pools stay short.
            var files = GoalieFreeAgents.SelectedFiles(selected.Folder)
                .Select(file => (File: file, Id: IdPrefix + SelectedPlayerPool.PlayerId(GoalieFreeAgents.DirectoryPath(selected.Folder), Path.GetFileName(file))))
                .Where(p => !MatchesPool(team.goalie, p.Id) && !current.Offered.Contains(p.Id))
                .OrderBy(_ => Guid.NewGuid()).Take(2).ToArray();
            foreach (var file in files)
            {
                var cfg = new PlayerConfig(); Plugin.LoadPlayerFile(file.File, cfg);
                // A prepared offer must never overwrite supplemental state for a
                // previously saved recruit made from the same authored template.
                var goalie = Candidate(cfg, config, file.Id + "_" + Guid.NewGuid().ToString("N"), state.GetCurrentSquad()?.startingTeam?.goalie);
                var snapshot = Snapshot(goalie);
                // Prepare durable supplemental state before exposing a selectable
                // card. An unwritable snapshot cannot consume a signing choice.
                GoalieRecruitSnapshots.Save(current.Selection.Folder, current.Selection.RunId, goalie.id, snapshot);
                bool taken = false;
                cards.Add(new EventChoice
                {
                    translatedButtonName = "Recruit " + goalie.FullName + " — replaces current goalie",
                    interactable = true, nodeType = NodeType.GeneralManager, ChoiceType = EEventChoiceType.Skater,
                    Skater = goalie, choicesTermParams = new Il2CppSystem.Collections.Generic.List<EventTermParam>(),
                    onSelection = (Action)(() =>
                    {
                        if (taken || !ReferenceEquals(visit, current) || current.Revision != revision
                            || !current.Selection.Matches(CampaignUtil.CampaignState)) return;
                        try
                        {
                            team.goalie = goalie; taken = true; current.Revision++;
                            Plugin.Log.LogInfo("[GoalieRecruit] Recruited " + goalie.FullName + " into the active goalie slot.");
                        }
                        catch (Exception ex) { Plugin.Log.LogError("[GoalieRecruit] Signing failed; current goalie retained: " + ex); }
                    })
                });
                current.Offered.Add(file.Id);
            }
        }
        catch (Exception ex)
        {
            cards.Clear();
            Plugin.Log.LogError("[GoalieRecruit] Authored goalie offers failed; no skater/native fallback: " + ex);
        }
        TwoLineContracts.ReplaceChoices(node, cards);
        return cards.Count > 0;
    }

    internal static bool MatchesPool(GoaltenderData goalie, string poolId)
        => goalie?.id == poolId || (goalie?.id ?? "").StartsWith(poolId + "_", StringComparison.Ordinal);

    internal static bool HasAuthoredTalents(PlayerConfig cfg)
        => cfg.Talents.Count > 0 || cfg.Fields.Fields.ContainsKey("Goalie Talents") || cfg.Fields.Fields.ContainsKey("Talents");

    internal static void NormalizeImport(PlayerConfig cfg)
    {
        // Match Studio PlayerExport.EquipmentIntent: V3 snapshots bake an own
        // outfit but separately retain the author's future-team inheritance.
        string intent = cfg.Fields.Get("Export Own Equipment");
        if (intent.Equals("yes", StringComparison.OrdinalIgnoreCase)) cfg.Fields.Set("Override Team Settings", "yes");
        else if (intent.Equals("no", StringComparison.OrdinalIgnoreCase)) cfg.Fields.Set("Override Team Settings", "no");
        foreach (var pair in cfg.Colors.Fields) cfg.Fields.Set(pair.Key, pair.Value);
        cfg.Fields.Remove("Export Version");
    }

    internal static void InheritTeamGear(PlayerConfig cfg, TeamConfig team, GoaltenderData template)
    {
        if (AppearanceFields.OverridesTeam(cfg.Fields)) return;
        // Team defaults exist independently of the starting goalie's private
        // outfit. Its config intentionally does not load this file when it owns
        // equipment, so resolve the authored team file directly for this recruit.
        string path = string.IsNullOrWhiteSpace(team?.IdentityKey) ? null
            : Path.Combine(Plugin.ModFolder, team.IdentityKey, "goalie-equipment.cfg");
        var gear = path != null && File.Exists(path) ? new CampaignDocument(File.ReadAllText(path)) : new CampaignDocument("");
        var templateLook = CampaignAppearanceRuntime.Read(template);
        string Native(string key) => key switch {
            "Goalie Pants Skin" when templateLook?.Get(key).Length > 0 => templateLook.Get(key),
            "Goalie Pants Away" when templateLook?.Get(key).Length > 0 => templateLook.Get(key),
            "Skin" or "Goalie Pants Skin" => template?.skin,
            "Skin Away" or "Goalie Pants Away" => template?.awaySkin,
            "Helmet Skin" => template?.helmetSkin,
            "Glove Skin" => template?.gloveSkin, "Glove Away" => template?.awayGloveSkin,
            "Blocker Skin" => template?.blockerSkin, "Blocker Away" => template?.awayBlockerSkin,
            "Pads Skin" => template?.padsSkin, "Pads Away" => template?.awayPadsSkin,
            "Stick Skin" => template?.stickSkin, "Stick Away" => template?.awayStickSkin, _ => null };
        foreach (string key in new[] { "Skin", "Skin Away", "Goalie Pants Skin", "Goalie Pants Away", "Helmet Skin",
            "Glove Skin", "Glove Away", "Blocker Skin", "Blocker Away", "Pads Skin", "Pads Away", "Stick Skin", "Stick Away" })
        {
            string fallback = Native(key);
            Plugin.ApplyPlayerField(cfg, key.ToLowerInvariant(), gear.Get(key, string.IsNullOrEmpty(fallback) ? "standard" : fallback));
        }
        cfg.Fields.Set("Recruit Inherited Away Helmet", gear.Fields.ContainsKey("Helmet Skin")
            ? Plugin.ResolveGoalieSkin(gear.Get("Helmet Skin"), "helmet") : template?.helmetAwaySkin ?? "");
        cfg.TeamGoalieGear = true;
    }

    internal static GoaltenderData Candidate(PlayerConfig cfg, TeamConfig team, string id, GoaltenderData template = null)
    {
        // Freeze authoring-time random colors before cards or snapshots use them.
        // A Studio import is now campaign-owned. Retaining its Play Now marker
        // would route rendering through an export ID which this recruit lacks.
        NormalizeImport(cfg);
        InheritTeamGear(cfg, team, template);
        GoaltenderData goalie;
        if (!string.IsNullOrWhiteSpace(cfg.ImportPlayer))
        {
            var source = PatchBossLaunchMatch.FindGoalieByName(cfg.ImportPlayer)
                ?? throw new InvalidDataException("Imported goalie was not found: " + cfg.ImportPlayer);
            goalie = UnityEngine.Object.Instantiate(source);
            var talents = new Il2CppSystem.Collections.Generic.List<Rogue.Talent>();
            if (source.powerups != null) foreach (var talent in source.powerups) if (talent != null) talents.Add(talent);
            goalie.powerups = talents;
            goalie.ResetTalentCache();
        }
        else
        {
            goalie = UnityEngine.ScriptableObject.CreateInstance<GoaltenderData>();
            goalie.skin = goalie.awaySkin = Plugin.ResolveGoalieSkin("standard", "body");
            goalie.gloveSkin = goalie.awayGloveSkin = Plugin.ResolveGoalieSkin("standard", "glove");
            goalie.blockerSkin = goalie.awayBlockerSkin = Plugin.ResolveGoalieSkin("standard", "blocker");
            goalie.padsSkin = goalie.awayPadsSkin = Plugin.ResolveGoalieSkin("standard", "pads");
            goalie.stickSkin = goalie.awayStickSkin = Plugin.ResolveGoalieSkin("standard", "stick");
            goalie.helmetSkin = goalie.helmetAwaySkin = Plugin.ResolveGoalieSkin("standard", "helmet");
            goalie.headSkin = ""; goalie.powerups = new Il2CppSystem.Collections.Generic.List<Rogue.Talent>();
        }
        goalie.id = id; goalie.name = id;
        if (string.IsNullOrWhiteSpace(cfg.Name) && string.IsNullOrWhiteSpace(goalie.FullName))
            throw new InvalidDataException("A goalie recruit must have a name.");
        if (HasAuthoredTalents(cfg)) { goalie.powerups = new Il2CppSystem.Collections.Generic.List<Rogue.Talent>(); goalie.ResetTalentCache(); }
        PatchBossLaunchMatch.ApplyGoalieConfig(goalie, cfg);
        if (cfg.TeamGoalieGear)
        {
            string awayHelmet = cfg.Fields.Get("Recruit Inherited Away Helmet");
            goalie.helmetAwaySkin = string.IsNullOrEmpty(awayHelmet) ? goalie.helmetSkin : awayHelmet;
        }
        if (cfg.Ability.Equals("none", StringComparison.OrdinalIgnoreCase)) goalie.ability = null;
        else if (!string.IsNullOrWhiteSpace(cfg.Ability)) goalie.ability = PatchBossLaunchMatch.FindAbility(cfg.Ability)
            ?? throw new InvalidDataException("Goalie ability was not found: " + cfg.Ability);
        goalie.numberFormat = (NumberFormat)(cfg.NumberFormat >= 0 ? cfg.NumberFormat : team?.Uniform.NumberFormat ?? 0);
        CampaignAppearanceRuntime.Team(goalie, team, null, false);
        CampaignAppearanceRuntime.Player(goalie, cfg, false);
        return goalie;
    }
    internal static CampaignDocument Snapshot(GoaltenderData goalie)
    {
        var snapshot = CampaignAppearanceRuntime.RecruitSnapshot(goalie);
        snapshot.Set("Recruit Number Format", ((int)goalie.numberFormat).ToString(CultureInfo.InvariantCulture));
        snapshot.Set("Recruit Pass Read", goalie.passReadSkill.ToString("R", CultureInfo.InvariantCulture));
        snapshot.Set("Recruit Away Helmet", goalie.helmetAwaySkin ?? "");
        return snapshot;
    }
    internal static void Restore(RunData run)
    {
        if (Plugin.IsDefaultMode || !IsRecruit(run?.teamData?.goalie) || string.IsNullOrWhiteSpace(run.runID)) return;
        try
        {
            var goalie = run.teamData.goalie;
            var snapshot = GoalieRecruitSnapshots.Read(Plugin.ModFolder, run.runID, goalie.id);
            if (snapshot == null) { Plugin.Log.LogWarning("[GoalieRecruit] Supplemental snapshot missing; retaining the native saved goalie."); return; }
            if (int.TryParse(snapshot.Get("Recruit Number Format"), out int numberFormat)
                && numberFormat >= 0 && numberFormat < FieldSchema.NumberStyles.Length) goalie.numberFormat = (NumberFormat)numberFormat;
            if (float.TryParse(snapshot.Get("Recruit Pass Read"), NumberStyles.Float, CultureInfo.InvariantCulture, out float passRead)
                && float.IsFinite(passRead)) goalie.passReadSkill = passRead;
            goalie.helmetAwaySkin = snapshot.Get("Recruit Away Helmet");
            CampaignAppearanceRuntime.RestoreRecruit(goalie, snapshot);
        }
        catch (Exception ex) { Plugin.Log.LogError("[GoalieRecruit] Supplemental restore failed; native saved goalie retained: " + ex); }
    }
}

[HarmonyPatch(typeof(CampaignState), nameof(CampaignState.SetCurrentNode))]
internal static class GoalieRecruitSelection
{
    [HarmonyPostfix] private static void Postfix(CampaignState __instance, MapNode __0) => GoalieRecruitment.Selected(__instance, __0);
}
[HarmonyPatch(typeof(GeneralManagerEvent), "Initialize")]
internal static class GoalieRecruitVisitStart
{
    [HarmonyPrefix] private static void Prefix() => GoalieRecruitment.BeginVisit();
}
[HarmonyPatch(typeof(GeneralManagerEvent), "NextChoices")]
internal static class GoalieRecruitChoices
{
    [HarmonyPrefix] private static bool Prefix(GeneralManagerEvent __instance, int __0, ref bool __result)
    {
        if (!GoalieRecruitment.IsGoalieNode) return true;
        __result = GoalieRecruitment.Choices(__instance, __0); return false;
    }
}
[HarmonyPatch(typeof(DeserializerV2), "DeserializeFromModel")]
internal static class GoalieRecruitLoaded
{
    [HarmonyPostfix] private static void Postfix(RunData __0) => GoalieRecruitment.Restore(__0);
}
