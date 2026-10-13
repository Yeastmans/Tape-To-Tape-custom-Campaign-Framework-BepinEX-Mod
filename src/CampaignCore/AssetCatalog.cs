using System;
using System.Collections.Generic;
using System.Linq;

namespace CampaignCore;

/// <summary>Asset identity is role + field + full game path; leaf names are only labels.</summary>
public sealed class AssetCatalog
{
    private readonly Dictionary<string, List<string>> entries = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    public void Add(string role, string field, string value)
    {
        // Internal face artwork is not selectable helmet equipment.
        if (value.Equals("Faces/Custom/Helmet_Face", StringComparison.OrdinalIgnoreCase)) return;
        if (role.Equals("goalie", StringComparison.OrdinalIgnoreCase)
            && value.Equals("Helmet/Helmet_Customization_Face", StringComparison.OrdinalIgnoreCase)) return;
        // Bare Helmet_* assets are equipment. Character_Helmet is a distinct,
        // valid face variant and must not be collapsed into Character.
        if (role.Equals("skater", StringComparison.OrdinalIgnoreCase)
            && field.Equals("Face", StringComparison.OrdinalIgnoreCase)
            && value.Substring(value.LastIndexOf('/') + 1).StartsWith("Helmet_", StringComparison.OrdinalIgnoreCase)) return;
        var key = role + ":" + field;
        if (!entries.TryGetValue(key, out var list)) entries[key] = list = new List<string>();
        if (!list.Contains(value)) list.Add(value);
    }
    public IReadOnlyList<string> Values(string role, string field) => entries.TryGetValue(role + ":" + field, out var list) ? list : Array.Empty<string>();
    public string Resolve(string role, string field, string value)
    {
        var list = Values(role, field);
        var exact = list.FirstOrDefault(x => x.Equals(value, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;
        var matches = list.Where(x => x.Substring(x.LastIndexOf('/') + 1).Equals(value, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length == 1) return matches[0];
        throw new ArgumentException(matches.Length > 1
            ? $"Ambiguous {role} {field} '{value}': choose a full asset path."
            : $"Unknown {role} {field} '{value}': refresh game data or select a supported asset.");
    }
}
