using System;
using System.Collections.Generic;
using System.Linq;

namespace CampaignCore;

/// <summary>One campaign template, shared by every additive custom squad.</summary>
public static class SquadTemplatePolicy
{
    public static string Identity(string value)
    {
        string key = (value ?? "").Trim().ToLowerInvariant().Replace(" squad", "").Replace(" meta", "").Replace(" ", "");
        switch (key)
        {
            case "": case "default": case "(default)": return "";
            case "basic": return "vanilla";
            case "doubledecker": return "lines";
            case "speed": return "speedy";
            case "stats": return "statrewards";
            case "defence": return "defense";
            default: return key;
        }
    }

    // No Effects is an authored roster mode. The game still needs a real squad
    // asset for its maps and menu, so its runtime basis is Basic Squad.
    public static string RuntimeIdentity(string value)
        => Identity(value) == "noeffects" ? "vanilla" : Identity(value);

    public static string Resolve(string campaignTemplate, IEnumerable<string> legacyTemplates)
    {
        if (Identity(campaignTemplate).Length > 0) return campaignTemplate.Trim();
        var choices = legacyTemplates.Where(v => Identity(v).Length > 0)
            .GroupBy(Identity).Select(g => g.First()).ToArray();
        if (choices.Length > 1)
            throw new InvalidOperationException("This campaign contains different squad templates. Choose one campaign template for all custom squads.");
        return choices.Length == 1 ? choices[0] : "Basic Squad";
    }

    public static void RequireSame(string campaignTemplate, string requested)
    {
        if (Identity(requested).Length == 0 || Identity(campaignTemplate) != Identity(requested))
            throw new InvalidOperationException("Every custom squad in this campaign must use " + campaignTemplate + ". Change the template for the whole campaign in Custom Squads.");
    }
}
