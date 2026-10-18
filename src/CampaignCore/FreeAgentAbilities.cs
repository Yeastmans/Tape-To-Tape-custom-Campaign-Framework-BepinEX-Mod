using System;

namespace CampaignCore;

public static class FreeAgentAbilities
{
    public const string DisableKey = "Disable Recruit Abilities";
    public static bool Disabled(CampaignDocument document)
    {
        string value = document.Get(DisableKey).Trim();
        return value.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("on", StringComparison.OrdinalIgnoreCase) || value == "1";
    }
}
