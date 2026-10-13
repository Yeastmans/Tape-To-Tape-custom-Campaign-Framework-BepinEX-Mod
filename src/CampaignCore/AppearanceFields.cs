using System;
using System.Collections.Generic;
using System.Linq;

namespace CampaignCore;

// One serialized color vocabulary for campaign files and Play Now sidecars.
public static class AppearanceFields
{
    private static readonly Dictionary<string, string> Colors = BuildColors();
    private static Dictionary<string, string> BuildColors()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string key in FieldSchema.PlayerColors.Concat(FieldSchema.EquipmentColors()))
        {
            result[key] = key;
            if (key.Contains("Secondary Color")) result[key.Replace("Secondary Color", "Color 2")] = key;
            if (key.Contains("Tertiary Color")) result[key.Replace("Tertiary Color", "Color 3")] = key;
            if (key.Contains(" Away")) result[key.Replace(" Away", "") + " Away"] = key;
        }
        foreach (string key in FieldSchema.TeamColors)
        {
            string canonical = key switch {
                "Jersey Primary" => "Jersey Color", "Jersey Secondary" => "Jersey Secondary Color", "Jersey Accent" => "Jersey Accent Color",
                "Away Primary" => "Jersey Away Color", "Away Secondary" => "Jersey Away Secondary Color", "Away Accent" => "Jersey Away Accent Color",
                "Number Color Home" => "Number Tertiary Color", "Number Color Away" => "Number Away Tertiary Color", _ => key };
            result[key] = canonical;
        }
        return result;
    }
    public static string ColorKey(string key) => Colors.TryGetValue(key, out var canonical) ? canonical : "";
    public static bool OverridesTeam(CampaignDocument player)
        => new[] { "yes", "true", "1" }.Contains(player.Get("Override Team Settings"), StringComparer.OrdinalIgnoreCase);
}
