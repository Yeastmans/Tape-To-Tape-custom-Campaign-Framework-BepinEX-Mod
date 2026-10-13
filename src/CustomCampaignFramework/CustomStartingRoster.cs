using System;
using Data;
using State;

namespace EndlessMode;

internal static class CustomStartingRoster
{
    [ThreadStatic] private static TeamConfig creating;
    [ThreadStatic] private static int loadingSave;

    // Only synchronous native starting-team construction is guarded.
    internal static TeamConfig Begin(RunSquadScriptableObject squad)
    {
        var previous = creating;
        creating = null;
        string id = squad?.id ?? "";
        if (id.StartsWith("Custom_", StringComparison.Ordinal))
            Plugin.PlayerTeamConfigs.TryGetValue(id.Substring(7), out creating);
        if(VanillaCampaignRuntime.InheritedSquad(creating))creating=null;
        return previous;
    }
    internal static void End(TeamConfig previous) => creating = previous;
    private static TeamConfig Config(TeamData team)
    {
        // DeserializerV2 clears the native list before adding saved skaters.
        // Those additions are authoritative, not the new-run fallback pool.
        if (team == null || loadingSave > 0) return null;
        return creating;
    }
    internal static void BeginLoad() => loadingSave++;
    internal static void EndLoad() => loadingSave--;

    internal static void LockAuthoredSkaters(TeamData team, TeamConfig cfg)
    {
        if (team == null || cfg == null) return;
        team.leftWingerLineUpCreationLocked |= PatchChooseMetaUI.SlotIsConfigured(cfg.LW);
        team.rightWingerLineUpCreationLocked |= PatchChooseMetaUI.SlotIsConfigured(cfg.RW);
        team.centerLineUpCreationLocked |= PatchChooseMetaUI.SlotIsConfigured(cfg.C);
        team.leftDefensemenLineUpCreationLocked |= PatchChooseMetaUI.SlotIsConfigured(cfg.LD);
        team.rightDefensemenLineUpCreationLocked |= PatchChooseMetaUI.SlotIsConfigured(cfg.RD);
    }
    internal static bool AllowSet(TeamData team, int index, ForwardData incoming)
    {
        var cfg = Config(team);
        if (cfg == null) return true;
        var slots = new[] { cfg.LW, cfg.RW, cfg.C, cfg.LD, cfg.RD, cfg.L2_LW, cfg.L2_RW, cfg.L2_C, cfg.L2_LD, cfg.L2_RD };
        if (index >= 0 && index < slots.Length && PatchChooseMetaUI.SlotIsConfigured(slots[index])
            && team.forwards != null && index < team.forwards.Count && team.forwards[index] != null)
        {
            var current = team.forwards[index];
            // Native deep cloning keeps the ID. Allow that copy, block a different player or clear.
            return incoming != null && !string.IsNullOrEmpty(current.id) && current.id == incoming.id;
        }
        return true;
    }
}

