using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace CampaignCore;

// The bundled inventory is extracted from the fingerprinted current game.
// Old runtime dumps may add labels/previews, but cannot reintroduce removed IDs.
public static class EquipmentCompatibility
{
    // These native sprites are applied by injuries, status effects, or talents
    // during a match. They are not persistent appearance choices.
    private static readonly HashSet<string> GameplayVisuals = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Helmet/Helmet_Enraged",
        "Faces/Helmets/Helmet_Enrage",
        "Faces/Helmets/Helmet_Frog",
        "Faces/Helmets/Helmet_Golden",
        "Faces/Anyteam/Heavy_Helmet",
        "Faces/Anyteam/Propeller_Helmet",
        "Faces/Helmets/Heavy_Helmet",
        "Faces/Helmets/Propeller_Helmet",
        "Body/Crusaders/GSpiked_Armor",
        "Body_Bicep/Crusaders_Spiked",
        "Body_Bicep/Injured",
        "Body_Gloves/Injured",
        "Body_Skates/Injured",
        "Body_Skates/Winged"
    };
    private static readonly Dictionary<string, string> Issues = Load();
    private static Dictionary<string, string> Load()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var stream = typeof(EquipmentCompatibility).Assembly.GetManifestResourceStream("NativeEquipmentCatalog.tsv");
        if (stream == null) throw new InvalidOperationException("Missing native equipment compatibility catalog");
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is string line)
        {
            var c = line.Split('\t');
            if (c.Length < 9 || c[0] == "role") continue;
            string reason = "";
            if (c[2].EndsWith("_iso", StringComparison.OrdinalIgnoreCase) || c[2].EndsWith("_lso", StringComparison.OrdinalIgnoreCase)) reason = "Shader support layer, not standalone equipment";
            else if (c[2] == "Faces/Custom/Helmet_Face" || c[2] == "Helmet/Helmet_Customization_Face") reason = "Internal face support layer";
            else if (c[2] == "Faces/Custom/Helmet_Reference" || c[2] == "Faces/Helmets/Helmet_Reference") reason = "Internal reference helmet artwork, not standalone equipment";
            else if (c[0] == "goalie" && c[7].Length > 0 && c[1] != "Logos" && c[1] != "Numbers") reason = "Missing artwork in a goalie view: " + c[7];
            if (!result.TryGetValue(c[2], out string existing) || existing.Length > 0) result[c[2]] = reason;
        }
        return result;
    }
    public static string Issue(string value)
    {
        if (GameplayVisuals.Contains(value)) return "Gameplay effect artwork, not persistent equipment";
        if (Issues.TryGetValue(value, out string reason)) return reason;
        if (value == "Numbers/Custom_Number" || value.StartsWith("Faces/CustomHeads/", StringComparison.Ordinal) || Regex.IsMatch(value, @"^Numbers/Number_\d{1,2}(LH)?$")) return "";
        return "Not present in the current native equipment catalog";
    }
    public static bool IsSelectable(string value) => Issue(value).Length == 0;
}
