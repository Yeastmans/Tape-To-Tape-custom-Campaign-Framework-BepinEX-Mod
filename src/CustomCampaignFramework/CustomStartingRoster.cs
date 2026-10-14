using System;
using System.Collections.Generic;
using Data;
using State;

namespace EndlessMode;

internal static class CustomStartingRoster
{
    [ThreadStatic] private static TeamConfig creating;
    [ThreadStatic] private static int loadingSave;
    [ThreadStatic] private static string[] startingIds;
    [ThreadStatic] private static Stack<string[]> previousIds;

    // Only synchronous native starting-team construction is guarded.
    internal static TeamConfig Begin(RunSquadScriptableObject squad)
    {
        var previous = creating;
        (previousIds ??= new Stack<string[]>()).Push(startingIds);
        startingIds = null;
        creating = null;
        string id = squad?.id ?? "";
        if (id.StartsWith("Custom_", StringComparison.Ordinal))
            Plugin.PlayerTeamConfigs.TryGetValue(id.Substring(7), out creating);
        if(VanillaCampaignRuntime.InheritedSquad(creating))creating=null;
        if (creating != null && squad.startingTeam?.forwards != null)
        {
            startingIds = new string[squad.startingTeam.forwards.Count];
            for (int i = 0; i < startingIds.Length; i++) startingIds[i] = squad.startingTeam.forwards[i]?.id;
        }
        return previous;
    }
    internal static void End(TeamConfig previous)
    {
        creating = previous;
        startingIds = previousIds?.Count > 0 ? previousIds.Pop() : null;
    }
    private static TeamConfig Config(TeamData team)
    {
        // DeserializerV2 clears the native list before adding saved skaters.
        // Those additions are authoritative, not the new-run fallback pool.
        if (team == null || loadingSave > 0) return null;
        return creating;
    }
    internal static void BeginLoad() => loadingSave++;
    internal static void EndLoad() => loadingSave--;

    internal static void RemoveTemplateStarters(TeamData team, TeamConfig cfg)
    {
        if (team?.forwards == null || cfg == null) return;
        if (!PatchChooseMetaUI.SlotIsConfigured(cfg.Goalie)) team.goalie = null;
        var slots = new[] { cfg.LW, cfg.RW, cfg.C, cfg.LD, cfg.RD, cfg.L2_LW, cfg.L2_RW, cfg.L2_C, cfg.L2_LD, cfg.L2_RD };
        for (int i = 0; i < team.forwards.Count; i++)
            if (i >= slots.Length || !PatchChooseMetaUI.SlotIsConfigured(slots[i])) team.forwards[i] = null;
        if (team.lines != null)
            foreach (var line in team.lines)
            {
                bool Present(string id)
                {
                    foreach (var player in team.forwards) if (player != null && player.id == id) return true;
                    return false;
                }
                if (!Present(line.leftWinger)) line.leftWinger = "";
                if (!Present(line.rightWinger)) line.rightWinger = "";
                if (!Present(line.center)) line.center = "";
                if (!Present(line.leftDefensemen)) line.leftDefensemen = "";
                if (!Present(line.rightDefensemen)) line.rightDefensemen = "";
            }
    }

    internal static void LockAuthoredSkaters(TeamData team, TeamConfig cfg)
    {
        if (team == null || cfg == null) return;
        team.leftWingerLineUpCreationLocked |= PatchChooseMetaUI.SlotIsConfigured(cfg.LW);
        team.rightWingerLineUpCreationLocked |= PatchChooseMetaUI.SlotIsConfigured(cfg.RW);
        team.centerLineUpCreationLocked |= PatchChooseMetaUI.SlotIsConfigured(cfg.C);
        team.leftDefensemenLineUpCreationLocked |= PatchChooseMetaUI.SlotIsConfigured(cfg.LD);
        team.rightDefensemenLineUpCreationLocked |= PatchChooseMetaUI.SlotIsConfigured(cfg.RD);
    }
    internal static void RemoveUnselectedConstructedPlayers(TeamData team, RunSquadScriptableObject squad)
    {
        if (team?.forwards == null || squad?.startingTeam?.forwards == null) return;
        var selected = new HashSet<string>();
        foreach (var player in squad.startingTeam.forwards)
            if (player != null && !string.IsNullOrEmpty(player.id)) selected.Add(player.id);
        // Also cover native list writes which bypass the intercepted setters.
        for (int i = 0; i < team.forwards.Count; i++)
            if (team.forwards[i] != null && !selected.Contains(team.forwards[i].id)) team.forwards[i] = null;
        if (squad.startingTeam.goalie == null) team.goalie = null;
    }
    internal static bool AllowSet(TeamData team, int index, ForwardData incoming)
    {
        var cfg = Config(team);
        if (cfg == null) return true;
        if (startingIds != null && index >= 0 && index < startingIds.Length && !string.IsNullOrEmpty(startingIds[index]))
        {
            // Capture the selected source roster before cloning. This also preserves
            // authored starters explicitly moved onto the bench, and copying into an
            // initially empty target list. Native deep clones retain these IDs.
            return incoming != null && startingIds[index] == incoming.id;
        }
        // Draft and superstar selections happen outside starting-team cloning.
        // An empty authored position must stay empty during this native fallback.
        return incoming == null;
    }
}

