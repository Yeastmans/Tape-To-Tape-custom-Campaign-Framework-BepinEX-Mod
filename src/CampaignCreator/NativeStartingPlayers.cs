namespace CampaignCreator;

public static class NativeStartingPlayers
{
    // Extracted from shipped ForwardData assets, never from mutable runtime clones.
    public static HashSet<string> Names(string? pool = null)
    {
        using var stream = typeof(NativeStartingPlayers).Assembly.GetManifestResourceStream("CampaignCreator.NativeStartingPlayers.tsv")!;
        using var reader = new StreamReader(stream);
        string category = pool == "draft_pool" ? "2" : "4";
        return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r').Split('\t')).Where(row => row.Length == 2 && (pool == null || row[0] == category))
            .Select(row => row[1]).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
