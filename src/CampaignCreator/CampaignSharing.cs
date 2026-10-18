using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CampaignCore;

namespace CampaignCreator;

/// <summary>
/// Campaign zips and the community folder. Same format and endpoints as the old Creator, so
/// campaigns shared from either tool install in both: one top-level folder per zip, with the
/// CustomLogos PNGs it references bundled under <c>_logos/</c>. The Cloudflare worker holds
/// the Dropbox credentials; Studio only knows its public URL.
/// </summary>
internal static partial class CampaignSharing
{
    internal const string WorkerUrl = "https://t2tcampaings.shaespring14.workers.dev";
    internal const string LogoBundle = "_logos";
    internal const string HeadBundle = "_heads";
    // Local-only folders: save state, Studio backups and removed teams never ship.
    private static readonly string[] SkipFiles = ["save.txt", "save.txt.bak"];
    private static readonly string[] SkipFolders = [LogoBundle, HeadBundle, "map_backups", "_removed_opponents", "custom_reward_progress"];
    private static readonly HttpClient http = CreateClient();

    internal sealed record CommunityItem(string Name, string Path, long Size, string Modified);
    internal sealed record InstallResult(string Folder, int LogosCopied, int LogosKept, int HeadsCopied, int HeadsKept);
    internal sealed record PackageResult(int Logos, int Heads);

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("T2T-CampaignCreator");
        return client;
    }

    /// <summary>Logo names referenced by "Logo From" / "Squad Head" that exist as CustomLogos PNGs.</summary>
    private static Dictionary<string, string> ReferencedLogos(string campaign)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string folder = LogoExport.CustomFolder;
        if (!Directory.Exists(folder)) return result;
        var onDisk = Directory.GetFiles(folder, "*.png").GroupBy(f => Path.GetFileNameWithoutExtension(f), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.GetFiles(campaign, "*.txt", SearchOption.AllDirectories))
        {
            if (Path.GetRelativePath(campaign, file).Split(Path.DirectorySeparatorChar).Any(p => SkipFolders.Contains(p, StringComparer.OrdinalIgnoreCase))) continue;
            var doc = new CampaignDocument(File.ReadAllText(file));
            foreach (string key in new[] { "Logo From", "Squad Head" })
            {
                string value = doc.Get(key).Trim();
                if (value.StartsWith("CustomLogos/", StringComparison.OrdinalIgnoreCase)) value = value["CustomLogos/".Length..];
                if (value.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) value = value[..^4];
                if (value.Length > 0 && onDisk.TryGetValue(value, out var png)) result[value] = png;
            }
        }
        return result;
    }

    /// <summary>Zips a campaign with its referenced logos and custom head projects.</summary>
    internal static PackageResult Zip(string campaign, string destination, string contentRoot)
    {
        var customRewards = CustomRewardCatalog.PortableItems(contentRoot, campaign);
        CustomRewardCatalog.ValidateCampaignLoadouts(contentRoot, campaign, customRewards);
        string top = Path.GetFileName(campaign.TrimEnd('\\', '/'));
        var logos = ReferencedLogos(campaign);
        var heads = ReferencedHeads(campaign, contentRoot);
        if (File.Exists(destination)) File.Delete(destination);
        using var zip = ZipFile.Open(destination, ZipArchiveMode.Create);
        foreach (string file in Directory.GetFiles(campaign, "*", SearchOption.AllDirectories))
        {
            var parts = Path.GetRelativePath(campaign, file).Split(Path.DirectorySeparatorChar);
            if (parts[..^1].Any(p => SkipFolders.Contains(p, StringComparer.OrdinalIgnoreCase)) || SkipFiles.Contains(parts[^1], StringComparer.OrdinalIgnoreCase)) continue;
            zip.CreateEntryFromFile(file, top + "/" + string.Join('/', parts), CompressionLevel.Optimal);
        }
        foreach (var reward in customRewards.Values) {
            if (File.Exists(Path.Combine(campaign, CustomRewardDefinition.Folder, reward.Id + CustomRewardDefinition.Extension))) continue;
            using var writer = new StreamWriter(zip.CreateEntry($"{top}/{CustomRewardDefinition.Folder}/{reward.Id}{CustomRewardDefinition.Extension}", CompressionLevel.Optimal).Open());
            writer.Write(reward.ToDocument().ToString());
        }
        foreach (string png in logos.Values.Distinct(StringComparer.OrdinalIgnoreCase).Order())
            zip.CreateEntryFromFile(png, $"{top}/{LogoBundle}/{Path.GetFileName(png)}", CompressionLevel.Optimal);
        foreach (var head in heads)
            foreach (string relative in HeadFiles(head.Folder))
                zip.CreateEntryFromFile(head.Asset(relative), $"{top}/{HeadBundle}/{head.Name}/{relative.Replace('\\', '/')}", CompressionLevel.Optimal);
        return new PackageResult(logos.Count, heads.Count);
    }

    /// <summary>The single top-level folder a campaign zip installs as.</summary>
    internal static string TopFolder(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var tops = zip.Entries.Select(e => e.FullName.Replace('\\', '/').Split('/')[0]).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (tops.Count != 1) throw new InvalidDataException("This zip isn't a campaign: it needs exactly one campaign folder at its root.");
        if (!zip.Entries.Any(e => e.FullName.Replace('\\', '/').Equals(tops[0] + "/campaign.txt", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("This zip isn't a campaign: " + tops[0] + "/campaign.txt is missing.");
        return tops[0];
    }

    /// <summary>Installs a campaign zip as <paramref name="folderName"/> under campaigns/, then copies its bundled logos (never overwriting yours).</summary>
    internal static InstallResult Install(string zipPath, string contentRoot, string folderName)
    {
        string top = TopFolder(zipPath);
        string campaigns = Path.GetFullPath(Path.Combine(contentRoot, "campaigns"));
        string target = Path.GetFullPath(Path.Combine(campaigns, CreatorContent.ValidateName(folderName)));
        if (Directory.Exists(target)) throw new IOException("A campaign named " + folderName + " already exists.");
        string staging = Path.Combine(contentRoot, ".studio-drafts", "import-" + Guid.NewGuid().ToString("N"));
        var addedHeads = new List<string>();
        int headsKept = 0;
        try
        {
            using (var zip = ZipFile.OpenRead(zipPath))
                foreach (var entry in zip.Entries)
                {
                    string relative = entry.FullName.Replace('\\', '/')[(top.Length + 1)..];
                    if (relative.Length == 0 || entry.FullName.EndsWith('/')) continue;
                    string destination = Path.GetFullPath(Path.Combine(staging, relative));
                    if (!destination.StartsWith(Path.GetFullPath(staging) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The zip contains an unsafe path: " + entry.FullName);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    entry.ExtractToFile(destination);
                }
            CustomRewardCatalog.ValidateCampaignLoadouts(contentRoot, staging, CustomRewardCatalog.PortableItems(contentRoot, staging));
            headsKept = InstallHeads(staging, contentRoot, addedHeads);
            Directory.CreateDirectory(campaigns);
            Directory.Move(staging, target);
        }
        catch
        {
            foreach (string head in addedHeads) Directory.Delete(head, true);
            throw;
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
        int copied = 0, kept = 0;
        string bundle = Path.Combine(target, LogoBundle);
        if (Directory.Exists(bundle))
        {
            Directory.CreateDirectory(LogoExport.CustomFolder);
            foreach (string png in Directory.GetFiles(bundle, "*.png"))
            {
                string destination = Path.Combine(LogoExport.CustomFolder, Path.GetFileName(png));
                if (File.Exists(destination)) { kept++; continue; }
                File.Copy(png, destination); copied++;
            }
        }
        return new InstallResult(Path.GetFileName(target), copied, kept, addedHeads.Count, headsKept);
    }

    /// <summary>Moves an existing campaign aside so an import can take its name.</summary>
    internal static string SetAside(string contentRoot, string folderName)
    {
        string source = Path.Combine(contentRoot, "campaigns", folderName);
        string backup = Path.Combine(contentRoot, "campaign_backups", folderName + " " + DateTime.Now.ToString("yyyy-MM-dd HHmmss"));
        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
        Directory.Move(source, backup);
        return backup;
    }

    internal static string FreeName(string contentRoot, string folderName)
    {
        string name = folderName;
        for (int i = 2; Directory.Exists(Path.Combine(contentRoot, "campaigns", name)); i++) name = $"{folderName} ({i})";
        return name;
    }

    internal static async Task<List<CommunityItem>> ListAsync()
    {
        using var doc = JsonDocument.Parse(await http.GetStringAsync(WorkerUrl + "/list"));
        var rootElement = doc.RootElement;
        if (!rootElement.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
            throw new InvalidOperationException(rootElement.TryGetProperty("error", out var e) ? e.GetString() : "The community list couldn't be loaded.");
        var items = new List<CommunityItem>();
        if (rootElement.TryGetProperty("items", out var list))
            foreach (var item in list.EnumerateArray())
                items.Add(new CommunityItem(item.GetProperty("name").GetString() ?? "", item.GetProperty("path").GetString() ?? "",
                    item.TryGetProperty("size", out var size) && size.TryGetInt64(out long bytes) ? bytes : 0,
                    item.TryGetProperty("modified", out var modified) ? modified.GetString() ?? "" : ""));
        return items;
    }

    internal static async Task DownloadAsync(string path, string destination)
    {
        using var response = await http.GetAsync(WorkerUrl + "/download?path=" + Uri.EscapeDataString(path), HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        await using var file = File.Create(destination);
        await response.Content.CopyToAsync(file);
    }

    // Ownership is private application data, never campaign content or ZIP payload.
    internal static CommunityCampaignClient Community { get; } = new(http, WorkerUrl,
        new CommunityOwnershipStore(Path.Combine(UserContentPaths.Root, "community-ownership.json")));

    /// <summary>Uploads a campaign and saves management access before sending its bytes.</summary>
    internal static async Task<string> UploadAsync(string zipPath, string displayName, string author, string description, string campaignName, IProgress<double>? progress = null)
        => (await Community.UploadAsync(zipPath, displayName, progress)).Path;
}