using System;
using System.Linq;
using Data;

namespace EndlessMode;

// Supply native lineups; Team.ChangeLine/ApplyLineForFaceoff retain native timing,
// notifications and bench handling. No team-ID spoofing or player-squad changes.
internal static class OpponentLineBehavior
{
    internal static void Apply(TeamData team, TeamConfig cfg)
    {
        if (Plugin.IsDefaultMode || (!CustomTeamRules.IsCustom(team) && !VanillaCampaignRuntime.Owns(team)) || Plugin.PlayerTeamConfigs.Values.Any(p => ReferenceEquals(p,cfg))) return;
        if (string.IsNullOrWhiteSpace(cfg.OpponentTwoLines)) return;
        if (!cfg.OpponentTwoLines.Equals("yes",StringComparison.OrdinalIgnoreCase))
        {
            if (team.lines != null) while(team.lines.Count>1) team.lines.RemoveAt(team.lines.Count-1);
            team.SetActiveLine(1); return;
        }
        PlayerConfig[] slots = { cfg.L2_LW,cfg.L2_RW,cfg.L2_C,cfg.L2_LD,cfg.L2_RD };
        if (slots.Any(p=>!PatchChooseMetaUI.SlotIsConfigured(p)) || team.forwards == null || team.forwards.Count < 5)
        {
            Plugin.Log.LogWarning("[OpponentLines] Fill five starting skaters and all five Line 2 positions for '"+team.teamName+"'.");
            return;
        }
        var forwards=team.forwards;
        for(int i=0;i<5;i++)
        {
            if(forwards[i]==null) { Plugin.Log.LogWarning("[OpponentLines] Missing starter; line behavior not applied."); return; }
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
        var lines=new Il2CppSystem.Collections.Generic.List<Lineup>();
        for(int line=0;line<2;line++)
        {
            int start=line*5;
            var lineup=new Lineup(team) { leftWinger=forwards[start].id,rightWinger=forwards[start+1].id,
                center=forwards[start+2].id,leftDefensemen=forwards[start+3].id,rightDefensemen=forwards[start+4].id };
            lines.Add(lineup);
        }
        team.lines=lines; team.SetActiveLine(1);
        Plugin.Log.LogInfo("[OpponentLines] Native two-line roster prepared for '"+team.teamName+"'.");
    }
}
