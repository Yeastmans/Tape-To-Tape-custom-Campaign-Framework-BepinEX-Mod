using System;
using System.Collections.Generic;
using System.Linq;
using Data;
using HarmonyLib;
using Skaters;

namespace EndlessMode;

// Supply native lineups; Team.ChangeLine/ApplyLineForFaceoff retain native timing,
// notifications and bench handling. No team-ID spoofing or player-squad changes.
internal static class OpponentLineBehavior
{
    private static readonly Dictionary<string, bool> EnabledByTeam = new();
    private static readonly SkaterType[] Positions = { SkaterType.LeftWinger, SkaterType.RightWinger,
        SkaterType.Center, SkaterType.LeftDef, SkaterType.RightDef };
    internal static void Clear() => EnabledByTeam.Clear();
    internal static bool Disabled(TeamData team) => !Plugin.IsDefaultMode && team != null &&
        EnabledByTeam.TryGetValue(team.id ?? "", out bool enabled) && !enabled;

    // Authored slots are applied by index, so donor lineup IDs must never survive.
    // In particular, a Tycoons first line can reference players outside slots 0-4.
    private static void SetLines(TeamData team, int count)
    {
        var lines = new Il2CppSystem.Collections.Generic.List<Lineup>();
        var ids = new HashSet<string>();
        for (int line = 0; line < count; line++)
        {
            int start = line * 5;
            for (int slot = 0; slot < 5; slot++)
            {
                var player = team.forwards[start + slot];
                if (string.IsNullOrEmpty(player.id) || !ids.Add(player.id))
                {
                    player.id = Guid.NewGuid().ToString();
                    ids.Add(player.id);
                }
                player.defaultSkaterType = Positions[slot];
            }
            lines.Add(new Lineup(team) { leftWinger = team.forwards[start].id,
                rightWinger = team.forwards[start + 1].id, center = team.forwards[start + 2].id,
                leftDefensemen = team.forwards[start + 3].id, rightDefensemen = team.forwards[start + 4].id });
        }
        team.lines = lines;
        team.SetActiveLine(1);
    }

    internal static void EnforceSingleLine(TeamData team)
    {
        if (!Disabled(team)) return;
        if (CustomTeamRules.IsCustom(team) && team.forwards != null && team.forwards.Count >= 5 &&
            Enumerable.Range(0, 5).All(i => team.forwards[i] != null))
        {
            // Only the match clone is trimmed. Authored Line 2 files remain editable.
            var starters = new Il2CppSystem.Collections.Generic.List<ForwardData>();
            for (int i = 0; i < 5; i++) starters.Add(team.forwards[i]);
            team.forwards = starters;
            SetLines(team, 1);
        }
        else
        {
            // Copied native campaigns can have intentional reserves (e.g. Golfers).
            if (team.lines != null) while (team.lines.Count > 1) team.lines.RemoveAt(team.lines.Count - 1);
            team.SetActiveLine(1);
        }
    }

    internal static void Apply(TeamData team, TeamConfig cfg)
    {
        if (Plugin.IsDefaultMode || (!CustomTeamRules.IsCustom(team) && !VanillaCampaignRuntime.Owns(team)) || Plugin.PlayerTeamConfigs.Values.Any(p => ReferenceEquals(p,cfg))) return;
        bool enabled = string.Equals(cfg.OpponentTwoLines?.Trim(), "yes", StringComparison.OrdinalIgnoreCase);
        EnabledByTeam[team.id] = enabled;
        if (!enabled)
        {
            EnforceSingleLine(team);
            Plugin.Log.LogInfo("[OpponentLines] Two-line behavior disabled for '" + team.teamName + "'.");
            return;
        }
        PlayerConfig[] slots = { cfg.L2_LW,cfg.L2_RW,cfg.L2_C,cfg.L2_LD,cfg.L2_RD };
        if (slots.Any(p=>!PatchChooseMetaUI.SlotIsConfigured(p)) || team.forwards == null || team.forwards.Count < 5)
        {
            Plugin.Log.LogWarning("[OpponentLines] Fill five starting skaters and all five Line 2 positions for '"+team.teamName+"'.");
            EnabledByTeam[team.id] = false;
            EnforceSingleLine(team);
            return;
        }
        var forwards=team.forwards;
        for(int i=0;i<5;i++)
        {
            if(forwards[i]==null) { Plugin.Log.LogWarning("[OpponentLines] Missing starter; two-line behavior disabled."); EnabledByTeam[team.id] = false; EnforceSingleLine(team); return; }
        }
        while(forwards.Count<10)
        {
            int slot=forwards.Count%5;
            var clone=UnityEngine.Object.Instantiate(forwards[slot]);
            clone.id=Guid.NewGuid().ToString(); forwards.Add(clone);
        }
        for(int i=0;i<5;i++)
        {
            var player=forwards[i+5];
            if(player==null) { player=UnityEngine.Object.Instantiate(forwards[i]); player.id=Guid.NewGuid().ToString(); forwards[i+5]=player; }
            var pc=slots[i];
            if(!string.IsNullOrWhiteSpace(pc.ImportPlayer))
            {
                var imported=PatchBossLaunchMatch.FindPlayerByName(pc.ImportPlayer);
                if(imported!=null) PatchBossLaunchMatch.CopyPlayerData(imported,player);
            }
            PatchBossLaunchMatch.ApplyPlayerConfig(player,pc,cfg.Uniform);
            PatchBossLaunchMatch.ApplyTeamEquipmentColors(player,cfg,team,useAway:true);
            PatchBossLaunchMatch.ApplyPlayerColorOverrides(player,pc,useAway:true);
        }
        SetLines(team, 2);
        Plugin.Log.LogInfo("[OpponentLines] Native two-line roster prepared for '"+team.teamName+"'.");
    }
}

// Reapply the policy before native match objects are spawned, including resumed runs.
[HarmonyPatch(typeof(Tape2Tape.Team), nameof(Tape2Tape.Team.Initialize), new[] { typeof(TeamData), typeof(bool), typeof(bool) })]
internal static class OpponentSingleLineInitialize
{
    [HarmonyPrefix] private static void Prefix(TeamData __0) => OpponentLineBehavior.EnforceSingleLine(__0);
}

[HarmonyPatch(typeof(Tape2Tape.Team), nameof(Tape2Tape.Team.SwitchLineIfNeeded))]
internal static class OpponentSingleLineSwitch
{
    [HarmonyPrefix] private static bool Prefix(Tape2Tape.Team __instance) => !OpponentLineBehavior.Disabled(__instance._teamData);
}

[HarmonyPatch(typeof(Tape2Tape.Team), "ChangeLine")]
internal static class OpponentSingleLineChange
{
    [HarmonyPrefix] private static bool Prefix(Tape2Tape.Team __instance, bool __0) =>
        __0 || !OpponentLineBehavior.Disabled(__instance._teamData);
}

[HarmonyPatch]
internal static class OpponentSingleLineSelection
{
    private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(Tape2Tape.Team), nameof(Tape2Tape.Team.ApplyLineForFaceoff));
        yield return AccessTools.Method(typeof(Tape2Tape.Team), nameof(Tape2Tape.Team.ToggleSelectedLine));
    }
    [HarmonyPrefix] private static bool Prefix(Tape2Tape.Team __instance, ref bool __result)
    {
        if (!OpponentLineBehavior.Disabled(__instance._teamData)) return true;
        __instance.selectedLineNumber = 1;
        __instance.isFirstLinePlaying = true;
        __result = false;
        return false;
    }
}
