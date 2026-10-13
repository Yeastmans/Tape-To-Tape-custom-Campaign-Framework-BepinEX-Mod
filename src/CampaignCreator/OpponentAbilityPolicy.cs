namespace CampaignCreator;

/// <summary>Normal hockey AI support for build 25899138; see release/docs/OPPONENTS_AND_EQUIPMENT.md.</summary>
public static class OpponentAbilityPolicy
{
    // Native asset identities, not localized labels. Upgrades share the same AI path.
    private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase)
    {
        "Headshot Redirect", "Headshot Redirect LVL2", "yoyo", "YoYo LVL2",
        "tornado", "probe", "spinorama", "spinorama Shooting Stars",
        "Grappling Hook", "Grappling Hook LVL2", "Grappling Hook Mountaineer",
        "throwingStick", "throwingStick LVL2", "Gas", "Gas LVL2",
        "jump", "Jump LVL2", "wet_towel", "megaLob", "megaPokecheck"
    };

    public static bool Supports(RewardCatalog.Entry entry) =>
        Supported.Contains(entry.Asset.Length > 0 ? entry.Asset : entry.Key);

    public static bool IsOpponentPlayer(string file)
    {
        var roster = Directory.GetParent(Path.GetFullPath(file));
        var team = roster?.Parent;
        return roster?.Name.Equals("players", StringComparison.OrdinalIgnoreCase) == true
            && team?.Parent?.Name.Equals("teams", StringComparison.OrdinalIgnoreCase) == true
            && File.Exists(Path.Combine(team.FullName, "team.txt"));
    }
}
