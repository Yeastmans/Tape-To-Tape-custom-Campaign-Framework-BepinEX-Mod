using System.Security.Cryptography;
using Avalonia.Media.Imaging;
using CampaignCore;

namespace CampaignCreator;

internal static class LogoExport
{
    internal static string CustomFolder => Path.Combine(GameSavePaths.Root, "CustomLogos");
    internal static string? FindFile(string root, string source)
    {
        // Native names use the logo sprites read from the player's install, never the old GPU-readback
        // PNGs in CustomLogos (which can contain only uninitialized 0xCD pixels).
        if (!string.IsNullOrWhiteSpace(source) && !Path.IsPathRooted(source) &&
            source.IndexOfAny(new[] { '/', '\\' }) < 0
            && StudioArt.Path("team-logos", Path.GetFileName(LogoFiles.NativeFile(root, source))) is { } native)
            return native;
        return LogoFiles.Resolve(root, CustomFolder, source);
    }
    internal static CampaignDocument Snapshot(CampaignDocument source, string root)
    {
        var result = new CampaignDocument(source.ToString());
        if (result.Get("Export Logo PNG").Length > 0)
        {
            byte[] stored;
            try { stored = Convert.FromBase64String(result.Get("Export Logo PNG")); }
            catch (FormatException) { throw new ArgumentException("Embedded logo is not valid PNG data."); }
            string checksum = Convert.ToHexString(SHA256.HashData(stored)).ToLowerInvariant();
            if (stored.Length > 16 * 1024 * 1024 || checksum != result.Get("Export Logo SHA256") || result.Get("Export Logo ID") != "custom_t2t_" + checksum + ".png")
                throw new ArgumentException("Embedded logo checksum mismatch. Import the original PNG again.");
            return result;
        }
        string name = source.Get("Logo From");
        if (name.Length == 0 || name.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            result.Set("Export Logo Mode", "none"); return result;
        }
        string file = FindFile(root, name) ?? throw new ArgumentException($"Logo '{name}' has no image available. Import its PNG or refresh the game's logo catalog before exporting.");
        byte[] bytes = File.ReadAllBytes(file);
        if (bytes.Length > 16 * 1024 * 1024 || bytes.Length < 24 || !bytes.Take(8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}))
            throw new ArgumentException("Choose a PNG logo smaller than 16 MB.");
        using (var decoded = new Bitmap(new MemoryStream(bytes)))
            if (decoded.PixelSize.Width < 1 || decoded.PixelSize.Height < 1 || decoded.PixelSize.Width > 8192 || decoded.PixelSize.Height > 8192)
                throw new ArgumentException("Logo dimensions must be between 1 and 8192 pixels.");
        string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        result.Set("Export Logo Mode", "embedded"); result.Set("Export Logo SHA256", hash);
        result.Set("Export Logo ID", "custom_t2t_" + hash + ".png");
        result.Set("Export Logo PNG", Convert.ToBase64String(bytes));
        return result;
    }
    internal static void Copy(CampaignDocument source, CampaignDocument target)
    {
        foreach (var (key,value) in source.Fields.Where(p => p.Key.StartsWith("Export Logo ", StringComparison.OrdinalIgnoreCase))) target.Set(key,value);
    }
    internal static Bitmap? Preview(string root, string source)
    {
        var file = FindFile(root, source);
        return file == null ? null : new Bitmap(file);
    }
}
