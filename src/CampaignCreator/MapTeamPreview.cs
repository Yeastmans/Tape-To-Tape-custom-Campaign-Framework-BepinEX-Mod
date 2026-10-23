using Avalonia.Media.Imaging;
using CampaignCore;

namespace CampaignCreator;

public sealed class MapTeamPreview : IDisposable
{
    private readonly string root, campaign;
    private readonly Dictionary<string, Bitmap> images = new(StringComparer.OrdinalIgnoreCase);
    public MapTeamPreview(string root, string campaign) { this.root = root; this.campaign = campaign; }
    public CampaignDocument? Team(string key)
    {
        if (string.IsNullOrEmpty(key) || Path.GetFileName(key) != key) return null;
        string file = Path.Combine(campaign, "teams", key, "team.txt");
        return File.Exists(file) ? new CampaignDocument(File.ReadAllText(file)) : null;
    }
    public string Description(string key) => Team(key)?.Get("Description") ?? "";
    public Bitmap? Logo(string key)
    {
        var team = Team(key);
        string source = team?.Get("Logo From", team.Get("Import Team")) ?? "";
        if (string.IsNullOrWhiteSpace(source) || source.Equals("RANDOM", StringComparison.OrdinalIgnoreCase)) return null;
        string? file;
        try { file = LogoExport.FindFile(root, source, campaign); } catch (ArgumentException) { return null; }
        if (file == null || !File.Exists(file)) return null;
        if (images.TryGetValue(file, out var bitmap)) return bitmap;
        try { return images[file] = new Bitmap(file); }
        catch (Exception ex) when (ex is IOException or ArgumentException) { return null; }
    }
    public void Dispose() { foreach (var image in images.Values) image.Dispose(); images.Clear(); }
}
