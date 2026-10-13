using System;
using System.Collections.Generic;
using System.Linq;

namespace CampaignCore;

// The base game's split, read from its customizers and TeamColorsData.MergeWithCustomSkaterColors:
// every equipment color belongs to the team's home/away kit, except that a player may own
// one stick color set (SkaterData.StickColor) and one skate color set (SkaterData.SkatesColor).
// Those two have no home/away split; when unset the team kit's stick/skates apply.
public static class PlayerColorModel
{
    public const string VersionKey = "Player Palette Version";
    public const string CurrentVersion = "1";
    public static readonly string[] StickKeys = { "Stick Color", "Stick Secondary Color", "Stick Tertiary Color" };
    public static readonly string[] SkatesKeys = { "Skates Color", "Blade Color", "Laces Color" };
    public static IEnumerable<string> PlayerKeys => StickKeys.Concat(SkatesKeys);

    public static bool IsPlayerColor(string key) => PlayerKeys.Contains(key, StringComparer.OrdinalIgnoreCase);

    // Any color key a player file might carry that the game only takes from the team kit.
    public static bool IsTeamOnlyColor(string key)
    {
        string canonical = AppearanceFields.ColorKey(key);
        if (canonical.Length == 0 && !(key.StartsWith("Bicep ", StringComparison.OrdinalIgnoreCase)
            && key.EndsWith(" Color", StringComparison.OrdinalIgnoreCase))) return false;
        return !IsPlayerColor(canonical.Length > 0 ? canonical : key);
    }

    public static bool HasStick(Func<string, string> value) => StickKeys.Any(k => value(k).Length > 0);
    public static bool HasSkates(Func<string, string> value) => SkatesKeys.Any(k => value(k).Length > 0);

    public sealed class Change
    {
        public Change(string key, string oldValue, string newValue, string action) { Key = key; OldValue = oldValue; NewValue = newValue; Action = action; }
        public string Key { get; }
        public string OldValue { get; }
        public string NewValue { get; }
        public string Action { get; }
    }

    // Rewrites a player file to the game model. Stick/skates keep the home value (or the away
    // value when only that was set), but only for players with their own look: a team-look
    // player's colors were never used in game, so they must not start applying now.
    public static List<Change> Migrate(CampaignDocument player)
    {
        var changes = new List<Change>();
        // Only legacy files tied personal colors to the equipment toggle. Once
        // migrated, a player's own stick/skate colors remain independent of it.
        bool ownLook = AppearanceFields.OverridesTeam(player) || player.Get(VersionKey) == CurrentVersion;
        foreach (var group in new[] { StickKeys, SkatesKeys })
            foreach (string home in group)
            {
                string awayKey = AwayKey(home);
                string current = Read(player, home), away = Read(player, awayKey);
                string target = ownLook ? (current.Length > 0 ? current : away) : "";
                if (target != current)
                    changes.Add(new Change(home, current, target, target.Length == 0 ? "removed (team look: never used in game)" : "stick/skates now single player value (from away)"));
                if (target.Length > 0) player.Set(home, target); else player.Remove(home);
            }
        foreach (string key in player.Fields.Keys.ToArray())
        {
            if (IsPlayerColor(key)) continue;
            string canonical = AppearanceFields.ColorKey(key);
            // Aliases such as "Stick Color 2" were folded into the canonical key above.
            if (IsPlayerColor(canonical)) { player.Remove(key); continue; }
            bool drop = IsTeamOnlyColor(key) || (canonical.Length > 0 && IsAwayPlayerColor(canonical)) || IsAwayPlayerColor(key);
            if (!drop) continue;
            string old = player.Get(key);
            if (old.Length > 0) changes.Add(new Change(key, old, "", IsAwayPlayerColor(canonical.Length > 0 ? canonical : key)
                ? "removed (stick/skates have no away value in game)" : "removed (team kit color; game has no per-player value)"));
            player.Remove(key);
        }
        player.Set(VersionKey, CurrentVersion);
        return changes;
    }

    // Base game: the stick (style and colors) belongs to the player; the team customizer has
    // none. Before a team loses its stick, its players adopt what they currently wear so no
    // look changes: team-look players and players without their own style take the team's
    // stick style, and players without their own stick colors take the team's home ones.
    public static List<Change> AdoptTeamStick(CampaignDocument player, CampaignDocument team)
    {
        var changes = new List<Change>();
        string teamStyle = team.Get("Stick");
        if (teamStyle.Length > 0 && (!AppearanceFields.OverridesTeam(player) || player.Get("Stick").Length == 0) && player.Get("Stick") != teamStyle)
        {
            changes.Add(new Change("Stick", player.Get("Stick"), teamStyle, "copied from team (stick style is the player's in the base game)"));
            player.Set("Stick", teamStyle);
        }
        if (!HasStick(k => player.Get(k)))
            foreach (string key in StickKeys)
            {
                string value = Read(team, key);
                if (value.Length == 0) continue;
                changes.Add(new Change(key, "", value, "copied from team home kit (stick colors are the player's in the base game)"));
                player.Set(key, value);
            }
        return changes;
    }
    // The stick style or any stick color key (home, away, alias): never a team field in the base game.
    public static bool IsStickKey(string key)
    {
        if (key.Equals("Stick", StringComparison.OrdinalIgnoreCase)) return true;
        string canonical = AppearanceFields.ColorKey(key);
        return (canonical.Length > 0 ? canonical : key).StartsWith("Stick ", StringComparison.OrdinalIgnoreCase)
            && (canonical.Length > 0 || key.EndsWith(" Color", StringComparison.OrdinalIgnoreCase));
    }
    public static List<Change> StripTeamStick(CampaignDocument team)
    {
        var changes = new List<Change>();
        foreach (string key in team.Fields.Keys.ToArray())
        {
            if (!IsStickKey(key)) continue;
            string old = team.Get(key);
            if (old.Length > 0) changes.Add(new Change(key, old, "", "removed from team (base game team has no stick)"));
            team.Remove(key);
        }
        return changes;
    }

    private static string Read(CampaignDocument doc, string canonical)
    {
        foreach (var pair in doc.Fields)
            if (pair.Value.Length > 0 && AppearanceFields.ColorKey(pair.Key).Equals(canonical, StringComparison.OrdinalIgnoreCase)) return pair.Value;
        return "";
    }
    private static string AwayKey(string home) => home switch
    {
        "Stick Color" => "Stick Away Color", "Stick Secondary Color" => "Stick Away Secondary Color", "Stick Tertiary Color" => "Stick Away Tertiary Color",
        "Skates Color" => "Skates Away Color", "Blade Color" => "Blade Away Color", "Laces Color" => "Laces Away Color", _ => home
    };
    private static bool IsAwayPlayerColor(string key) => PlayerKeys.Select(AwayKey).Contains(key, StringComparer.OrdinalIgnoreCase);
}
