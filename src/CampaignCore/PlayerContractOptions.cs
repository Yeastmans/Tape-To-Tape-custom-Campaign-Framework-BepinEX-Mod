using System;
using System.Globalization;

namespace CampaignCore;

/// <summary>An opt-in duration for a newly signed skater, in matches played.</summary>
public static class PlayerContractOptions
{
    public const string EnabledKey = "Custom Contract";
    public const string MatchesKey = "Contract Matches";
    public const int DefaultMatches = 4;
    public static bool Enabled(CampaignDocument player)
    {
        string value = player?.Get(EnabledKey).Trim() ?? "";
        return value.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("on", StringComparison.OrdinalIgnoreCase) || value == "1";
    }
    public static bool TryMatches(string value, out int matches) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out matches) && matches > 0;
    // Invalid hand-edited durations must not become an immediate departure.
    public static int? Duration(CampaignDocument player) => Enabled(player)
        && TryMatches(player.Get(MatchesKey).Trim(), out int matches) ? matches : null;
}
