using CampaignCore;

namespace CampaignCreator;

/// <summary>Resolve legacy native-player references before freezing an export snapshot.</summary>
internal static class ExportPlayerResolver
{
    internal static CampaignDocument Resolve(CampaignDocument player, string role)
    {
        string reference = player.Get("Import Player").Trim();
        if (reference.Length == 0) return new CampaignDocument(player.ToString());
        if (reference.Equals("random", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Choose a fixed player for '{player.Get("Name", "this roster slot")}' before exporting to Play Now.");

        string? directory = StudioArt.BaseRosters("Base Game Players");
        var matches = directory == null ? [] : Directory.EnumerateFiles(directory, "*.txt")
            .Where(file => ContentImport.Matches(file, role)).Select(file => new CampaignDocument(File.ReadAllText(file)))
            .Where(doc => doc.Get("Name").Equals(reference, StringComparison.OrdinalIgnoreCase)
                || doc.Get(VanillaCopyData.PlayerAsset).Equals(reference, StringComparison.OrdinalIgnoreCase)
                || doc.Get(VanillaCopyData.PlayerId).Equals(reference, StringComparison.OrdinalIgnoreCase)).ToArray();
        CampaignDocument resolved;
        if (matches.Length == 1) resolved = matches[0];
        else if (CompleteSnapshot(player, role)) resolved = new CampaignDocument(player.ToString());
        else throw new ArgumentException($"Cannot resolve imported player '{reference}'. Use Read game artwork on Home, or import this player's complete record into the roster, then export again.");

        // Runtime import clones the named player, then applies authored overrides.
        // Blank legacy fields inherit from that player; explicit 'none' is retained.
        foreach (var (key, value) in player.Fields)
            if (value.Length > 0) resolved.Set(key, value);
        resolved.Remove("Import Player");
        return resolved;
    }

    private static bool CompleteSnapshot(CampaignDocument player, string role)
    {
        string[] required = role == "goalie"
            ? ["Name", "Skill", "Catching", "Glove", "Blocker", "Five Hole", "Standing Speed", "Butterfly Speed", "Recovery", "Pass Power", "Shot Power", "Poke Check", "Depth", "Control"]
            : ["Name", "Face", "Speed", "Shot Power", "Accuracy", "Checking", "Size", "Number"];
        return required.All(key => player.Get(key).Length > 0);
    }
}
