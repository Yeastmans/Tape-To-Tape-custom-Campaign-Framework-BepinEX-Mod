using System.Security.Cryptography;
using Avalonia.Media.Imaging;
using CampaignCore;

namespace CampaignCreator;

internal static class LogoExport
{
    private static readonly byte[] PngHeader = { 137, 80, 78, 71, 13, 10, 26, 10 };
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
    internal static CampaignDocument Snapshot(CampaignDocument source, string root, bool away)
    {
        var result = new CampaignDocument(source.ToString());
        var choice = JerseyLogoOptions.Read(source, away);
        string prefix = choice.MetadataPrefix;
        RequireCompleteEmbedded(source, away);
        if (choice.Mode != JerseyLogoMode.Image)
        {
            JerseyLogoOptions.Clear(result, away);
            if (choice.Mode == JerseyLogoMode.None) result.Set(JerseyLogoOptions.Field(away), "none");
            if (choice.Mode == JerseyLogoMode.None) result.Set(prefix + " Mode", "none");
            return result;
        }
        byte[] bytes;
        if (source.Get(prefix + " PNG").Length > 0)
        {
            try { bytes = Convert.FromBase64String(source.Get(prefix + " PNG")); }
            catch (FormatException) { throw new ArgumentException($"{JerseyLogoOptions.Field(away)} embedded PNG data is invalid."); }
            CheckPng(bytes, JerseyLogoOptions.Field(away));
            string checksum = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (source.Get(prefix + " SHA256") != checksum || source.Get(prefix + " ID") != "custom_t2t_" + checksum + ".png")
                throw new ArgumentException($"{JerseyLogoOptions.Field(away)} embedded checksum mismatch. Import the original PNG again.");
            result.Set(prefix + " Mode", "embedded");
            return result;
        }
        string? file = FindFile(root, choice.Source);
        if (file == null) throw new ArgumentException($"{JerseyLogoOptions.Field(away)} '{choice.Source}' has no image available. Import its PNG or refresh the game's logo catalog before exporting.");
        bytes = File.ReadAllBytes(file);
        CheckPng(bytes, JerseyLogoOptions.Field(away));
        string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        result.Set(prefix + " Mode", "embedded"); result.Set(prefix + " SHA256", hash);
        result.Set(prefix + " ID", "custom_t2t_" + hash + ".png");
        result.Set(prefix + " PNG", Convert.ToBase64String(bytes));
        return result;
    }
    internal static void RequireCompleteEmbedded(CampaignDocument source, bool away)
    {
        string prefix = JerseyLogoOptions.Prefix(away);
        if (JerseyLogoOptions.Read(source, away).Mode == JerseyLogoMode.Image
            && source.Get(prefix + " Mode").Equals("embedded", StringComparison.OrdinalIgnoreCase)
            && source.Get(prefix + " PNG").Length == 0)
            throw new ArgumentException($"{JerseyLogoOptions.Field(away)} embedded PNG is missing. Import the original PNG again.");
    }
    private static void CheckPng(byte[] bytes, string slot)
    {
        if (bytes.Length > 16 * 1024 * 1024 || bytes.Length < 24 || !bytes.Take(8).SequenceEqual(PngHeader))
            throw new ArgumentException($"{slot} must be a PNG logo smaller than 16 MB.");
        try
        {
            using var decoded = new Bitmap(new MemoryStream(bytes));
            if (decoded.PixelSize.Width < 1 || decoded.PixelSize.Height < 1 || decoded.PixelSize.Width > 8192 || decoded.PixelSize.Height > 8192)
                throw new ArgumentException($"{slot} dimensions must be between 1 and 8192 pixels.");
        }
        catch (ArgumentException) { throw; }
        catch (Exception ex) { throw new ArgumentException($"{slot} must contain valid PNG image data.", ex); }
    }
    internal static void Copy(CampaignDocument source, CampaignDocument target)
    {
        foreach (var (key,value) in source.Fields.Where(p => p.Key.StartsWith("Export Logo ", StringComparison.OrdinalIgnoreCase))) target.Set(key,value);
    }
    internal static void Copy(CampaignDocument source, CampaignDocument target, bool sourceAway, bool targetAway)
        => JerseyLogoOptions.Copy(source, sourceAway, target, targetAway);
    internal static void CopyIdentityToJersey(CampaignDocument source, CampaignDocument target, bool away)
    {
        string prefix = JerseyLogoOptions.Prefix(away);
        foreach (string suffix in new[] { "Mode", "ID", "SHA256", "PNG" })
        {
            string value = source.Get("Export Logo " + suffix);
            if (value.Length > 0) target.Set(prefix + " " + suffix, value);
        }
    }
    // Preview shares export validation and embedded precedence; the caller's
    // document and the other kit remain untouched.
    internal static Bitmap? Preview(string root, CampaignDocument team, bool away)
    {
        var effective = new CampaignDocument(team.ToString());
        if (JerseyLogoOptions.Read(team, away).Mode == JerseyLogoMode.Inherit)
        {
            effective.Set(JerseyLogoOptions.Field(away), team.Get("Logo From"));
            CopyIdentityToJersey(team, effective, away);
        }
        if (JerseyLogoOptions.Read(effective, away).Mode != JerseyLogoMode.Image) return null;
        var snapshot = Snapshot(effective, root, away);
        return new Bitmap(new MemoryStream(Convert.FromBase64String(snapshot.Get(JerseyLogoOptions.Prefix(away) + " PNG"))));
    }
    internal static Bitmap? Preview(string root, string source)
    {
        var file = FindFile(root, source);
        return file == null ? null : new Bitmap(file);
    }
}
