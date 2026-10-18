using System;

namespace CampaignCore;

public static class FreeAgentStats
{
    public const string KeepKey = "Keep Free Agent Stats";
    public static bool Keep(CampaignDocument document)
    {
        string value = document.Get(KeepKey).Trim();
        return value.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("on", StringComparison.OrdinalIgnoreCase) || value == "1";
    }
}
