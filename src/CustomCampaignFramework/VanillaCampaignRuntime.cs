using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using CampaignCore;
using Data;
using STS.Map;
using UnityEngine;
using HarmonyLib;

namespace EndlessMode;

/// <summary>Native choices keep their identity; editable copies overlay only the chosen team.</summary>
internal static class VanillaCampaignRuntime
{
    private static readonly Dictionary<string,TeamConfig> opponents=new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<IntPtr,TeamConfig> owned=new();
    private static readonly Dictionary<TeamConfig,bool> inherited=new();
    private static readonly Dictionary<TeamConfig,CampaignDocument> baselines=new();
    internal static IEnumerable<TeamConfig> Opponents=>opponents.Values;
    internal static void Reset(){opponents.Clear();owned.Clear();inherited.Clear();baselines.Clear();}
    internal static void ForgetInstances()=>owned.Clear();
    internal static bool AddOpponent(TeamConfig cfg)
    {
        string id=cfg.Fields.Get(VanillaCopyData.OpponentId);if(id.Length==0)return false;
        if(opponents.ContainsKey(id))throw new InvalidDataException("Two copied opponents bind the same native team: "+id);
        opponents.Add(id,cfg);return true;
    }
    internal static TeamConfig FindByKey(string key)=>opponents.Values.FirstOrDefault(c=>
        Path.GetFileName(c.IdentityKey).Equals(key,StringComparison.OrdinalIgnoreCase)||c.Name.Equals(key,StringComparison.OrdinalIgnoreCase));
    internal static bool IsCopiedOpponent(TeamConfig cfg)=>cfg?.Fields.Get(VanillaCopyData.OpponentId).Length>0;
    internal static bool TeamInherited(TeamConfig cfg)
    {
        if(cfg==null)return false;
        if(!inherited.TryGetValue(cfg,out bool result)) inherited[cfg]=result=VanillaCopyData.TeamInherited(Path.Combine(Plugin.ModFolder,cfg.IdentityKey));
        return result;
    }
    internal static bool InheritedSquad(TeamConfig cfg)=>cfg?.Fields.Get(VanillaCopyData.SquadId).Length>0 && TeamInherited(cfg);
    internal static bool InheritedPool(string folder)=>VanillaCopyData.PoolInherited(Path.Combine(Plugin.ModFolder,"player_teams",folder));
    internal static TeamConfig ConfigFor(TeamData team)
    {
        if(Plugin.IsDefaultMode||team==null)return null;
        if(owned.TryGetValue(team.Pointer,out var cfg))return cfg;
        return opponents.TryGetValue(team.id??"",out cfg)?cfg:null;
    }
    internal static bool SettingChanged(TeamConfig cfg,string key)
    {
        if(cfg==null)return false;
        if(!IsCopiedOpponent(cfg) && cfg?.Fields.Get(VanillaCopyData.SquadId).Length==0)return true;
        if(!baselines.TryGetValue(cfg,out var original))
        {
            string file=Path.Combine(Plugin.ModFolder,cfg.IdentityKey,"team.txt.native");
            baselines[cfg]=original=File.Exists(file)?new CampaignDocument(File.ReadAllText(file)):null;
        }
        return original==null||!original.Get(key).Equals(cfg.Fields.Get(key),StringComparison.Ordinal);
    }
    internal static bool Owns(TeamData team)=>team!=null&&owned.ContainsKey(team.Pointer);
    internal static void RememberClone(TeamData source,TeamData clone)
    {
        if(source!=null&&clone!=null&&owned.TryGetValue(source.Pointer,out var cfg))owned[clone.Pointer]=cfg;
    }
    internal static TeamData CloneAssigned(TeamConfig cfg)
    {
        string id=cfg.Fields.Get(VanillaCopyData.OpponentId);
        var source=Resources.FindObjectsOfTypeAll<TeamData>().FirstOrDefault(t=>t!=null&&t.id==id&&!Owns(t)&&!t.name.EndsWith("(Clone)",StringComparison.Ordinal));
        if(source==null)throw new InvalidOperationException("Native opponent is unavailable: "+cfg.Name);
        var clone=UnityEngine.Object.Instantiate(source);CloneNativePlayers(clone);owned[clone.Pointer]=cfg;return clone;
    }
    internal static bool OverrideSetting(TeamData team,params string[] keys)=>CustomTeamRules.IsCustom(team)
        || Owns(team)&&keys.Any(key=>SettingChanged(ConfigFor(team),key));
    internal static bool OverrideClassifier(TeamData team,string classifier)
    {
        if(CustomTeamRules.IsCustom(team))return true;
        if(!Owns(team)||OpponentEffectScope.Current==null)return false;
        var cfg=ConfigFor(team);
        foreach(var interaction in RelicOpponentInteractions.All)
            if(interaction.Consumer==OpponentEffectScope.Current&&interaction.Classifier==classifier)
                return SettingChanged(cfg,interaction.SettingKey)||SettingChanged(cfg,"Opponent Effects");
        return OpponentEffects.All.Any(e=>e.Classifier==classifier&&e.Consumers.Contains(OpponentEffectScope.Current))
            && (SettingChanged(cfg,"Opponent Effects")||SettingChanged(cfg,"Opponent Preset"));
    }
    internal static bool PreserveStartingRelics(TeamConfig cfg)=>cfg?.Fields.Get(VanillaCopyData.SquadId).Length>0
        && !new[] {"Team Relics","Relics","Starting Relics Mode","No Bench Bonus","Squad Template"}.Any(k=>SettingChanged(cfg,k));
    internal static void CloneNativePlayers(TeamData team)
    {
        team.lines=team.CloneLines();
        if(team.forwards!=null)
        {
            var players=new Il2CppSystem.Collections.Generic.List<ForwardData>();
            foreach(var player in team.forwards)players.Add(player==null?null:UnityEngine.Object.Instantiate(player));
            team.forwards=players;
        }
        if(team.goalie!=null)team.goalie=UnityEngine.Object.Instantiate(team.goalie);
    }
    internal static void Apply(MatchMapNode node)
    {
        var team=node?.opponent;var cfg=ConfigFor(team);if(cfg==null||owned.ContainsKey(team.Pointer))return;
        if(node.Node!=null && Plugin.NodeAssignments.ContainsKey(Plugin.NodeKey(Plugin.ActsCompleted+1,node.Node.LayerIndex,node.Node.NodeIndex)))return;
        var clone=UnityEngine.Object.Instantiate(team);CloneNativePlayers(clone);
        owned[clone.Pointer]=cfg;node.opponent=clone;
        if(!TeamInherited(cfg))
        {
            PatchBossLaunchMatch.EnsureRepos();PatchBossLaunchMatch.ResetClearedPlayers();
            PatchBossLaunchMatch.ApplyTeamFromConfig(clone,cfg);
        }
        else OpponentMatch.Apply(clone,cfg);
        Plugin.Log.LogInfo($"[VanillaCopy] Native choice '{cfg.Name}' ({clone.id}); edited={!TeamInherited(cfg)}");
    }
    internal static ForwardData CloneCandidate(PlayerConfig cfg)
    {
        string id=cfg?.Fields.Get(VanillaCopyData.PlayerId)??"";if(id.Length==0)return null;
        string asset=cfg.Fields.Get(VanillaCopyData.PlayerAsset);
        var source=Resources.FindObjectsOfTypeAll<ForwardData>().FirstOrDefault(p=>p!=null && p.id==id && p.name==asset);
        if(source==null)throw new InvalidOperationException("Native candidate is unavailable: "+cfg.Name+" ("+id+")");
        return UnityEngine.Object.Instantiate(source);
    }
}

[HarmonyPatch(typeof(TeamData),nameof(TeamData.Clone))]
internal static class VanillaOpponentClone
{
    [HarmonyPostfix] private static void Postfix(TeamData __instance,TeamData __result)=>VanillaCampaignRuntime.RememberClone(__instance,__result);
}
