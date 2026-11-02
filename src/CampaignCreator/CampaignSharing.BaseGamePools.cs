using CampaignCore;
using System.IO.Compression;
using System.Security.Cryptography;

namespace CampaignCreator;

internal static partial class CampaignSharing
{
    internal static void WritePoolArchive(string profile, string original, string root, string zipPath)
    {
        // Resolve local profile-owned logos against the original location before staging.
        foreach (string file in Directory.GetFiles(profile, "*.txt", SearchOption.AllDirectories))
        {
            var doc = new CampaignDocument(File.ReadAllText(file)); bool changed = false;
            foreach (string key in new[] { "Logo From", JerseyLogoOptions.Field(false), JerseyLogoOptions.Field(true) })
            {
                string value = doc.Get(key);
                string? resolved = LogoExport.FindFile(root, value, original);
                string bundled = Path.Combine(original, LogoBundle, Path.GetFileName(value));
                if (File.Exists(bundled)) resolved = bundled;
                if (resolved != null && (resolved.StartsWith(Path.GetFullPath(original) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || Path.IsPathRooted(value)))
                { doc.Set(key, resolved); changed = true; }
            }
            if (changed) doc.Save(file);
        }
        var logos = ReferencedLogos(profile, root);
        // ReferencedHeads checks both the library and an import's retained bundle.
        string originalHeads = Path.Combine(original, HeadBundle), stagedHeads = Path.Combine(profile, HeadBundle);
        if (Directory.Exists(originalHeads))
            foreach (string file in Directory.GetFiles(originalHeads, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(stagedHeads, Path.GetRelativePath(originalHeads, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
            }
        var heads = ReferencedHeads(profile, root);
        string temporary = zipPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var zip = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                foreach (string file in Directory.GetFiles(profile, "*", SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(profile, file).Replace('\\', '/');
                    if (relative.StartsWith(HeadBundle + "/", StringComparison.OrdinalIgnoreCase)) continue;
                    if (logos.Documents.TryGetValue(file, out var document))
                    { using var writer = new StreamWriter(zip.CreateEntry("base_game_pools/" + relative, CompressionLevel.Optimal).Open()); writer.Write(document.ToString()); }
                    else zip.CreateEntryFromFile(file, "base_game_pools/" + relative, CompressionLevel.Optimal);
                }
                foreach (var (name, bytes) in logos.Images)
                { using var stream = zip.CreateEntry("base_game_pools/" + LogoBundle + "/" + name, CompressionLevel.Optimal).Open(); stream.Write(bytes); }
                foreach (var head in heads)
                    foreach (string relative in HeadFiles(head.Folder))
                        zip.CreateEntryFromFile(head.Asset(relative), $"base_game_pools/{HeadBundle}/{head.Name}/{relative.Replace('\\', '/')}", CompressionLevel.Optimal);
            }
            File.Move(temporary, zipPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static void ValidatePoolAssets(string profile, string root)
    {
        _ = ReferencedHeads(profile, root); // Every custom head must travel with its reference.
        string bundle = Path.Combine(profile, LogoBundle);
        if (Directory.Exists(bundle))
            foreach (string file in Directory.GetFiles(bundle))
            {
                byte[] bytes = File.ReadAllBytes(file);
                LogoExport.CheckPng(bytes, "Shared pool logo");
                string name = Path.GetFileNameWithoutExtension(file);
                if (name.StartsWith("t2t_", StringComparison.OrdinalIgnoreCase) && !name[4..].Equals(Convert.ToHexString(SHA256.HashData(bytes)), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The pool package contains a corrupted logo.");
            }
        foreach (string file in Directory.GetFiles(Path.Combine(profile, "player_teams"), "*.txt", SearchOption.AllDirectories))
        {
            var doc = new CampaignDocument(File.ReadAllText(file));
            foreach (string key in new[] { "Logo From", JerseyLogoOptions.Field(false), JerseyLogoOptions.Field(true) })
            {
                string value = doc.Get(key);
                if (Path.IsPathRooted(value) || value.Contains('/') || value.Contains('\\')) throw new InvalidDataException("Shared player logos must be bundled, not local paths.");
                if (value.StartsWith("t2t_", StringComparison.OrdinalIgnoreCase) && !File.Exists(Path.Combine(bundle, value))) throw new InvalidDataException("A shared player logo is missing from the pool package.");
            }
        }
    }

    internal sealed class PoolAssetChanges : IDisposable
    {
        internal readonly List<string> Heads = [], Logos = [];
        private bool keep;
        internal void Keep() => keep = true;
        public void Dispose()
        {
            if (keep) return;
            foreach (string file in Logos) if (File.Exists(file)) File.Delete(file);
            foreach (string head in Heads) if (Directory.Exists(head)) { BaseGamePoolPackages.SafeAncestors(head); Directory.Delete(head, true); }
        }
    }
    internal static PoolAssetChanges InstallPoolAssets(string profile, string root)
    {
        var changes = new PoolAssetChanges();
        try
        {
            BaseGamePoolPackages.SafeAncestors(Path.Combine(root, "custom-heads"));
            string bundle = Path.Combine(profile, LogoBundle);
            if (Directory.Exists(bundle))
                foreach (string file in Directory.GetFiles(bundle, "*.png"))
                {
                    string target = Path.Combine(LogoExport.CustomFolder, Path.GetFileName(file));
                    BaseGamePoolPackages.SafeAncestors(target);
                    if (File.Exists(target) && !File.ReadAllBytes(target).SequenceEqual(File.ReadAllBytes(file))) throw new InvalidDataException("A shared logo conflicts with an existing image: " + Path.GetFileName(file));
                }
            _ = InstallHeads(profile, root, changes.Heads);
            if (Directory.Exists(bundle))
            {
                Directory.CreateDirectory(LogoExport.CustomFolder);
                foreach (string file in Directory.GetFiles(bundle, "*.png"))
                {
                    string target = Path.Combine(LogoExport.CustomFolder, Path.GetFileName(file));
                    if (File.Exists(target)) continue;
                    changes.Logos.Add(target); File.Copy(file, target);
                }
            }
            return changes;
        }
        catch { changes.Dispose(); throw; }
    }
}
