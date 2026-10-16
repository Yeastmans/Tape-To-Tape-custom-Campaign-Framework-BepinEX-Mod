using System.Text.Json;

namespace GameArt;

// Extract from the player's installed game, never package native PNGs.
public sealed class RelicIcons : IArtExtractor
{
    public string Folder => "relic-icons";
    public string Description => "Relic icons for custom rewards";
    public void Extract(UnityGame game, string output, IProgress<string>? progress)
    {
        using var stream = typeof(RelicIcons).Assembly.GetManifestResourceStream("GameArt.RewardFamilies.tsv")!;
        using var reader = new StreamReader(stream);
        var classes = reader.ReadToEnd().Split('\n').Select(l => l.TrimEnd('\r').Split('\t'))
            .Where(p => p.Length >= 3 && p[0] == "Relics").Select(p => p[2].Split('.').Last()).ToHashSet(StringComparer.Ordinal);
        using var namesStream = typeof(RelicIcons).Assembly.GetManifestResourceStream("GameArt.RewardCatalog.tsv")!;
        using var namesReader = new StreamReader(namesStream);
        var names = namesReader.ReadToEnd().Split('\n').Select(l => l.TrimEnd('\r').Split('\t'))
            .Where(p => p.Length >= 6 && p[0] == "Relics").GroupBy(p => p[2], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First()[5], StringComparer.OrdinalIgnoreCase);
        var rows = new List<object>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (file, _, value) in game.Behaviours(classes)) {
            if (value["id"].IsDummy || value["sprite"].IsDummy) continue;
            string id = value["id"].AsString;
            if (!Guid.TryParse(id, out var guid) || !seen.Add(id)) continue;
            var source = game.Resolve(file, value["sprite"]); if (source == null) continue;
            using var bitmap = game.Sprite(source.Value.File, source.Value.Info);
            string png = guid.ToString("D") + ".png";
            Png.Save(bitmap, Path.Combine(output, png));
            // relicName is a localization key, not the text displayed in-game.
            string name = names.TryGetValue(id, out var label) && !label.StartsWith("[Missing:", StringComparison.OrdinalIgnoreCase)
                ? label : value["m_Name"].AsString;
            rows.Add(new { id, name, file = png });
        }
        if (rows.Count == 0) throw new InvalidDataException("No native relic icons were found.");
        File.WriteAllText(Path.Combine(output, "index.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
        progress?.Report(rows.Count + " relic icons");
    }
}
