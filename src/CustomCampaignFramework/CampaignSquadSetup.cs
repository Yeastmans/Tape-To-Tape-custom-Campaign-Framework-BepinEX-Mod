using System;
using System.IO;
using CampaignCore;
using Data;
using State;
using UnityEngine;
using HarmonyLib;
using System.Collections.Generic;

namespace EndlessMode;

/// <summary>Apply author-selected pools to the custom clone, never to shared base players.</summary>
internal static class CampaignSquadSetup
{
    private static readonly Dictionary<string, string> templates = new();
    private static readonly Dictionary<string, string> revisions = new();
    private static string CacheKey(string id) => Plugin.ModFolder + "|" + id;
    internal static bool Matches(RunSquadScriptableObject squad, RunSquadScriptableObject basis)
        => templates.TryGetValue(CacheKey(squad.id), out string id) && id == basis.id
            && revisions.TryGetValue(CacheKey(squad.id), out string revision) && revision == Revision(squad.id);
    internal static void Remember(RunSquadScriptableObject squad, RunSquadScriptableObject basis)
    { templates[CacheKey(squad.id)] = basis.id; revisions[CacheKey(squad.id)] = Revision(squad.id); }
    private static string Revision(string id)
    {
        var files = new List<string> { Path.Combine(Plugin.ModFolder, "campaign.txt") };
        string squadPath = Plugin.PlayerTeamConfigs.TryGetValue(id.Substring(7), out var team)
            ? Path.Combine(Plugin.ModFolder, team.IdentityKey) : Path.Combine(Plugin.ModFolder, "player_teams", id.Substring(7));
        foreach (string path in new[] { squadPath, Path.Combine(Plugin.ModFolder, "player_teams", "draft_pool"), Path.Combine(Plugin.ModFolder, "player_teams", "superstars") })
        {
            if (Directory.Exists(path)) files.AddRange(Directory.GetFiles(path, "*", SearchOption.AllDirectories));
        }
        files.Sort(StringComparer.OrdinalIgnoreCase);
        return string.Join("|", files.ConvertAll(f => f + ":" + File.GetLastWriteTimeUtc(f).Ticks + ":" + (File.Exists(f) ? new FileInfo(f).Length : 0)));
    }
    internal static void Install(Harmony harmony)
    {
        NativeSquadBehaviorBridge.Install();
        // Native comparison hooks require a reviewed profile. Their availability
        // does not prevent custom squad cloning or managed roster/menu support.
    }
    internal static void OrderAuthoredLines(TeamData team)
    {
        if (team.lines == null || team.lines.Count < 2 || team.forwards == null) return;
        var ordered = new Il2CppSystem.Collections.Generic.List<ForwardData>();
        var used = new HashSet<string>();
        foreach (var line in team.lines)
            foreach (string id in new[] { line.leftWinger, line.rightWinger, line.center, line.leftDefensemen, line.rightDefensemen })
            {
                ForwardData player = null;
                foreach (var f in team.forwards) if (f != null && f.id == id) { player = f; break; }
                ordered.Add(player); if (player != null) used.Add(player.id);
            }
        foreach (var f in team.forwards) if (f == null || !used.Contains(f.id)) ordered.Add(f);
        team.forwards = ordered;
    }
    internal static void ApplyBench(RunSquadScriptableObject squad)
    {
        if (squad == null || !(squad.id ?? "").StartsWith("Custom_")) return;
        if (Plugin.PlayerTeamConfigs.TryGetValue(squad.id.Substring(7), out var team))
        {
            // StartNewRun reads this field directly. Keep the custom clone in
            // sync even when the audited native comparison bridge is unavailable.
            squad.m_UsesFreeAgentIntro = SquadStartingScreens.Enabled(team, "superstar_screen")
                && !SquadStartingScreens.EmptyPool("superstars", team);
            if (SquadTemplatePolicy.Identity(team.SquadTemplate) == "noeffects" && !SquadStartingScreens.Enabled(team, "bench_screen"))
            {
                squad.m_MaxBenchwarmersOnBench = 0;
                squad.m_CustomSwapPool = new Il2CppSystem.Collections.Generic.List<ForwardData>();
                return;
            }
        }
        string dir = SquadPoolRuntime.DirectoryPath(team, "draft_pool");
        if(SquadPoolRuntime.Inherited(team, "draft_pool") && (team==null || team.Fields.Get(VanillaCopyData.SquadId).Length==0
            || SquadTemplatePolicy.RuntimeIdentity(team.SquadTemplate)==team.Fields.Get(VanillaCopyData.SquadId)))return;
        var files = SquadPoolRuntime.Selected(team, "draft_pool");
        if (files == null) return;
        var pool = new Il2CppSystem.Collections.Generic.List<ForwardData>();
        squad.m_CustomSwapPool = pool; // Never retain donor candidates if a selected file fails to load.
        var sources = Resources.FindObjectsOfTypeAll<ForwardData>();
        foreach (string file in files)
        {
            if (!File.Exists(file)) throw new FileNotFoundException("Selected bench player missing", file);
            var doc = new CampaignDocument(File.ReadAllText(file));
            var config = new PlayerConfig(); Plugin.LoadPlayerFile(file, config);
            string originalName = doc.Get("Studio Base Pool Player", doc.Get("Import Player", doc.Get("Name")));
            ForwardData source = VanillaCampaignRuntime.CloneCandidate(config);
            foreach (var candidate in sources)
                if (source==null && candidate != null && !candidate.name.StartsWith("StudioBench_", StringComparison.Ordinal)
                    && ((candidate.firstName + " " + candidate.lastName).Trim().Equals(originalName, StringComparison.OrdinalIgnoreCase)
                        || candidate.name.Equals(originalName, StringComparison.OrdinalIgnoreCase))) { source = candidate; break; }
            if (source == null && squad.startingTeam?.forwards != null)
                foreach (var candidate in squad.startingTeam.forwards) if (candidate != null) { source = candidate; break; }
            if (source == null) throw new InvalidOperationException("No skater prototype available for " + originalName);
            var player = UnityEngine.Object.Instantiate(source);
            player.name = "StudioBench_" + Path.GetFileNameWithoutExtension(file);
            // Keep authored identities across repeated menu opens and save/resume.
            // Native bench identities also carry their native quests and snapshots.
            player.id = doc.Get("Studio Base Pool Player").Length > 0 || doc.Get(VanillaCopyData.PlayerId).Length>0 ? source.id
                : SelectedPlayerPool.PlayerId(dir, Path.GetFileName(file));
            ApplyFreshPlayerConfig(player, config);
            PlayerContractRuntime.StartingCandidate(squad, player);
            pool.Add(player);
        }
        squad.m_CustomSwapPool = pool;
        Plugin.Log.LogInfo($"[CustomSquad] {squad.squadName}: {pool.Count} selected bench candidates");
    }

    internal static void ApplyFreshPlayerConfig(ForwardData player, PlayerConfig config)
    {
        if (player == null || config == null) return;
        var inheritedAbility = player.ability;
        string authoredAbility = config.Ability;
        bool noAbility = string.Equals(authoredAbility?.Trim(), "none", StringComparison.OrdinalIgnoreCase);
        // Bench menus and free-agent refreshes create fresh clones. The legacy
        // talent helper clears by name only once, so reset each authored list.
        bool nativeCopy=config.Fields.Get(VanillaCopyData.PlayerId).Length>0;
        if (nativeCopy || (config.Talents?.Count ?? 0) > 0 || config.RandomTalentCount > 0)
        {
            player.powerups = new Il2CppSystem.Collections.Generic.List<Rogue.Talent>();
            player.ResetTalentCache();
        }
        // None is an authoring value, not a native ability repository entry.
        // The free-agent config is reused; restore it even if application fails.
        if (noAbility) config.Ability = "";
        try { PatchBossLaunchMatch.ApplyPlayerConfig(player, config, new UniformConfig()); }
        finally { config.Ability = authoredAbility; }
        if (noAbility || nativeCopy && string.IsNullOrEmpty(authoredAbility)) player.ability = null;
        else if (string.IsNullOrEmpty(authoredAbility)) player.ability = inheritedAbility;
    }
}


