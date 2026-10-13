using CampaignCore;

namespace CampaignCreator;

internal static class SquadTransitionColors
{
    internal static readonly string[] Keys = ["Transition Primary", "Transition Secondary", "Transition Tertiary"];
    internal const string InheritedMarker = "Studio Template Transition Colors";
    private static readonly Dictionary<string, string[]> Colors = Load();
    internal static string Get(string template, string key)
    {
        int index = Array.IndexOf(Keys, key);
        return index >= 0 && Colors.TryGetValue(SquadTemplatePolicy.RuntimeIdentity(template), out var colors) ? colors[index] : "";
    }
    private static Dictionary<string, string[]> Load()
    {
        using var stream = typeof(SquadTransitionColors).Assembly.GetManifestResourceStream("CampaignCreator.SquadTransitionColors.tsv")!;
        using var reader = new StreamReader(stream);
        var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith('#')) continue;
            var cells = line.Split('\t');
            if (cells.Length == 4) result[cells[0]] = cells.Skip(1).ToArray();
        }
        return result;
    }
}
