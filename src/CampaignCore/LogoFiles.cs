using System;
using System.IO;

namespace CampaignCore;

public static class LogoFiles
{
    public static string NativeFile(string root, string name) => Path.Combine(root, "_team_logos", CustomTeamIdentity.Create("logo", name) + ".png");
    public static string Resolve(string root, string customFolder, string source)
        => Resolve(root, customFolder, source, null);
    public static string Resolve(string root, string customFolder, string source, string campaignFolder)
    {
        if (string.IsNullOrWhiteSpace(source) || source.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;
        if (Path.IsPathRooted(source)) return File.Exists(source) ? source : null;
        // A packaged campaign owns its artwork. Never let a cached donor logo
        // or another campaign's same-named image shadow the authored PNG.
        if (!string.IsNullOrWhiteSpace(campaignFolder))
        {
            string campaign = Path.GetFullPath(campaignFolder);
            string relative = Path.GetFullPath(Path.Combine(campaign, source));
            if (relative.StartsWith(campaign + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(relative)) return relative;
            string named = Path.Combine(campaign, "logos", Path.GetFileNameWithoutExtension(source) + ".png");
            if (File.Exists(named)) return named;
        }
        string local = Path.GetFullPath(Path.Combine(root, source));
        if (local.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(local)) return local;
        string native = NativeFile(root, source);
        if (File.Exists(native)) return native;
        string leaf = Path.GetFileNameWithoutExtension(source);
        if (leaf.StartsWith("custom_", StringComparison.OrdinalIgnoreCase)) leaf = leaf.Substring(7);
        foreach (string folder in new[] { Path.Combine(root, "logos"), customFolder })
        {
            string file = Path.Combine(folder, leaf + ".png");
            if (File.Exists(file)) return file;
        }
        return null;
    }
}
