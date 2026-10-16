using System;
using System.Collections.Generic;
using System.Linq;

namespace CampaignCore;

public sealed class RewardPoolDefinition
{
    public const string ChaosTalents = "Chaos Talents";
    public const string ChaosRelics = "Chaos Relics";
    public const string IncludeChaosTalentsKey = "Include Chaos Talents";
    public const string IncludeRegularTalentsKey = "Include Regular Talents";
    public const string IncludeChaosRelicsKey = "Include Chaos Relics";
    public const string IncludeRegularRelicsKey = "Include Regular Relics";
    public static IReadOnlyList<string> Kinds { get; } = Array.AsReadOnly(new[] { "Relics", "Talents", "Goalie Talents", "Abilities", ChaosTalents, ChaosRelics });
    public static string CatalogKind(string kind) => kind == ChaosTalents ? "Talents" : kind == ChaosRelics ? "Relics" : kind;
    public static string CatalogExpansionKey(string kind) => kind == "Talents" ? IncludeChaosTalentsKey : kind == ChaosTalents ? IncludeRegularTalentsKey
        : kind == "Relics" ? IncludeChaosRelicsKey : kind == ChaosRelics ? IncludeRegularRelicsKey : "";
    public bool UseBaseGame { get; set; }
    // Editor catalog visibility only; it never changes the saved allow-list.
    public bool IncludeOtherCategory { get; set; }
    public HashSet<string> Selected { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public static RewardPoolDefinition Read(CampaignDocument document, string kind, IEnumerable<string> normal,
        Func<string, string> resolve = null)
    {
        resolve ??= value => value;
        string mode = document.Get("Pool Mode " + kind);
        var result = new RewardPoolDefinition { UseBaseGame = !mode.Equals("Custom", StringComparison.OrdinalIgnoreCase) };
        string expansion = document.Get(CatalogExpansionKey(kind));
        result.IncludeOtherCategory = expansion.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || expansion.Equals("true", StringComparison.OrdinalIgnoreCase) || expansion == "1";
        IEnumerable<string> Tokens(string key) => document.Get(key).Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).Select(resolve);
        if (mode.Length > 0)
        {
            result.Selected.UnionWith(Tokens("Selected " + kind));
            return result;
        }
        // Older files describe a custom pool by subtraction. Interpret that
        // selection once; new saves always contain the complete allow-list.
        var defaults = normal.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var excluded = Tokens("Excluded " + kind).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (kind == "Goalie Talents") excluded.UnionWith(Tokens("Excluded Talents").Where(defaults.Contains));
        excluded.IntersectWith(defaults);
        var included = Tokens("Included " + kind).ToArray();
        result.UseBaseGame = excluded.Count == 0 && included.Length == 0;
        result.Selected.UnionWith(defaults); result.Selected.ExceptWith(excluded); result.Selected.UnionWith(included);
        return result;
    }
    public void Write(CampaignDocument document, string kind)
    {
        document.Set("Pool Mode " + kind, UseBaseGame ? "Base Game" : "Custom");
        document.Set("Selected " + kind, string.Join(", ", Selected.OrderBy(s => s, StringComparer.OrdinalIgnoreCase)));
        string expansionKey = CatalogExpansionKey(kind);
        if (expansionKey.Length > 0) document.Set(expansionKey, IncludeOtherCategory ? "yes" : "no");
        document.Remove("Excluded " + kind); document.Remove("Included " + kind);
    }
}
