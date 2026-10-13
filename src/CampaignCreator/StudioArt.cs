using Avalonia.Media.Imaging;
using GameArt;

namespace CampaignCreator;

/// <summary>
/// Game artwork (heads, logos, equipment, stadiums, map backgrounds, base rosters) built from the
/// player's own Tape to Tape install by GameArt.ArtCache. Nothing here ships with the Studio:
/// before the first build every lookup returns null and the editors show "preview unavailable".
/// </summary>
internal static class StudioArt
{
    private static string? folder;
    private static bool resolved;

    /// <summary>The art cache in use, or null when none has been built yet.</summary>
    public static string? Folder
    {
        get { if (!resolved) { folder = ArtCache.Latest(); resolved = true; } return folder; }
    }

    public static void Use(string? cache) { folder = cache; resolved = true; }

    /// <summary>Full path of an art file or folder (relative, e.g. "team-logos", "x.png"), or null when absent.</summary>
    public static string? Path(params string[] parts)
    {
        if (Folder is not { } root) return null;
        string path = System.IO.Path.Combine([root, .. parts]);
        return File.Exists(path) || Directory.Exists(path) ? path : null;
    }

    public static Bitmap? Bitmap(params string[] parts) => Path(parts) is { } file ? new Bitmap(file) : null;

    /// <summary>A folder of the base-game roster snapshot ("Base Game Teams" / "Base Game Players"), or null.</summary>
    public static string? BaseRosters(string folder) => Path("base-rosters", folder);

    /// <summary>Names of the base-game teams that have a logo, from the extracted logo index.</summary>
    public static IEnumerable<string> LogoTeams()
    {
        if (Path("team-logos", "index.json") is not { } index) return [];
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(index));
        return json.RootElement.TryGetProperty("teams", out var teams) ? teams.EnumerateObject().Select(p => p.Name).ToArray() : [];
    }
}
