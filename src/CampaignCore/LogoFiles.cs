using System;
using System.IO;

namespace CampaignCore;

public static class LogoFiles
{
    public static string NativeFile(string root, string name) => Path.Combine(root, "_team_logos", CustomTeamIdentity.Create("logo", name) + ".png");
    public static string Resolve(string root, string customFolder, string source)
    {
        if (string.IsNullOrWhiteSpace(source) || source.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;
        if (Path.IsPathRooted(source)) return File.Exists(source) ? source : null;
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
