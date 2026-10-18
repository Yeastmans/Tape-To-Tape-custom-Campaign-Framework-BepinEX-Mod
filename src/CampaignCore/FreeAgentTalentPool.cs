using System;
using System.Collections.Generic;

namespace CampaignCore;

/// <summary>Independent recruitment allow-list. Missing settings retain the native pool.</summary>
public sealed class FreeAgentTalentPool
{
    public const string ModeKey = "Free Agent Talent Pool Mode";
    public const string SelectedKey = "Free Agent Selected Talents";
    public const string DisableExtrasKey = "Disable Extra Recruit Talents";
    public bool UseBaseGame { get; private set; }
    public HashSet<string> Selected { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public static bool ExtrasDisabled(CampaignDocument document)
    {
        string value = document.Get(DisableExtrasKey).Trim();
        return value.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("on", StringComparison.OrdinalIgnoreCase) || value == "1";
    }

    public static FreeAgentTalentPool Read(CampaignDocument document)
    {
        string mode = document.Get(ModeKey);
        if (mode.Length > 0 && !mode.Equals("Base Game", StringComparison.OrdinalIgnoreCase)
            && !mode.Equals("Custom", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Free-agent talent pool mode must be Base Game or Custom.");
        var result = new FreeAgentTalentPool { UseBaseGame = !mode.Equals("Custom", StringComparison.OrdinalIgnoreCase) };
        string text = document.Get(SelectedKey);
        if (string.IsNullOrWhiteSpace(text)) return result;
        foreach (string token in text.Split(','))
        {
            string id = token.Trim();
            if (!(Guid.TryParseExact(id, "D", out _) || Guid.TryParseExact(id, "N", out _)))
                throw new InvalidOperationException("Invalid free-agent talent ID: " + id);
            // Native assets use both dashed and compact IDs. These are asset
            // keys, not interchangeable GUID representations; keep the key.
            result.Selected.Add(id);
        }
        return result;
    }
}
