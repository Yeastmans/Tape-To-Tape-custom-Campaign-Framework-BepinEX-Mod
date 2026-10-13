using System;
using System.Linq;
using CampaignCore;
using Data;
using State;
using UnityEngine;

namespace EndlessMode;

internal static class SquadBehaviorRoster
{
    internal static void BenchChosenStarters(TeamData team, TeamConfig cfg)
    {
        string[] positions = { "Left Wing", "Right Wing", "Center", "Left Defense", "Right Defense" };
        var benched = cfg.StartingBenchPositions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (int i = 0; i < positions.Length; i++)
        {
            if (!benched.Contains(positions[i], StringComparer.OrdinalIgnoreCase) || team.forwards == null || i >= team.forwards.Count) continue;
            var player = team.forwards[i]; if (player == null) continue;
            while (team.forwards.Count < 5) team.forwards.Add(null);
            team.forwards.Add(player); team.forwards[i] = null;
            if (team.lines != null) foreach (var line in team.lines)
            {
                if (line.leftWinger == player.id) line.leftWinger = "";
                if (line.rightWinger == player.id) line.rightWinger = "";
                if (line.center == player.id) line.center = "";
                if (line.leftDefensemen == player.id) line.leftDefensemen = "";
                if (line.rightDefensemen == player.id) line.rightDefensemen = "";
            }
        }
    }
    private static bool Enabled(TeamConfig cfg, string id) => SquadBehaviors.Enabled(cfg.SquadTemplate, id,
        key => cfg.SquadBehaviors.TryGetValue(key, out string value) ? value : "");

    // Only the detached new-run clone is changed. Existing run rosters and the
    // template assets remain untouched. Native Double Decker starts with ten
    // supplied skaters; use those same defaults when opting another template in.
    internal static void Prepare(TeamData team, TeamConfig cfg)
    {
        bool two = Enabled(cfg, "two_lines");
        if (two && (team.lines == null || team.lines.Count < 2))
        {
            var basis = Resources.FindObjectsOfTypeAll<RunSquadScriptableObject>().FirstOrDefault(s => s != null && s.id == "lines");
            if (basis?.startingTeam == null) throw new InvalidOperationException("Double Decker roster asset is unavailable.");
            var donor = UnityEngine.Object.Instantiate(basis.startingTeam);
            try
            {
                // Order a detached list without changing the native template.
                CampaignSquadSetup.OrderAuthoredLines(donor);
                if (team.forwards == null) team.forwards = new Il2CppSystem.Collections.Generic.List<ForwardData>();
                while (team.forwards.Count < 10) team.forwards.Add(null);
                for (int i = 0; i < 10; i++)
                    if (team.forwards[i] == null)
                    {
                        var source = donor.forwards[i] ?? throw new InvalidOperationException("Missing Double Decker starter " + i);
                        var player = UnityEngine.Object.Instantiate(source); player.id = Guid.NewGuid().ToString(); team.forwards[i] = player;
                    }
            }
            finally { UnityEngine.Object.Destroy(donor); }
            UpdateLines(team, cfg);
        }
        else if (!two && team.lines != null && team.lines.Count > 1)
        {
            while (team.lines.Count > 1) team.lines.RemoveAt(team.lines.Count - 1);
            // The removed second line becomes ordinary bench space. Never erase
            // the skaters or saved authored files while switching a setting.
            team.SetActiveLine(1);
        }
        // Trio's two closed slots are asset flags in addition to its ID-based
        // active-count limit. Clear them when its limit is explicitly disabled.
        if (SquadTemplatePolicy.Identity(cfg.SquadTemplate) == "trio" && !Enabled(cfg, "three_skaters"))
        {
            team.leftWingerLineUpCreationLocked = false;
            team.rightDefensemenLineUpCreationLocked = false;
        }
    }
    internal static void UpdateLines(TeamData team, TeamConfig cfg)
    {
        if (!Enabled(cfg, "two_lines") || team.forwards == null || team.forwards.Count < 10) return;
        var lines = new Il2CppSystem.Collections.Generic.List<Lineup>();
        for (int line = 0; line < 2; line++)
        {
            int i = line * 5;
            lines.Add(new Lineup(team) { leftWinger = team.forwards[i]?.id ?? "", rightWinger = team.forwards[i + 1]?.id ?? "",
                center = team.forwards[i + 2]?.id ?? "", leftDefensemen = team.forwards[i + 3]?.id ?? "", rightDefensemen = team.forwards[i + 4]?.id ?? "" });
        }
        team.lines = lines; team.SetActiveLine(1);
    }
}
