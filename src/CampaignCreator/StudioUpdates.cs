using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CampaignCreator;

public sealed record StudioBuild(int Schema, long BuildId, string Label, string Platform);
public sealed record StudioUpdateManifest(int Schema, long BuildId, string Label, string Platform,
    string ReleaseTag, string InstallerName, long InstallerSize, string InstallerSha256,
    string StudioSha256, string ModSha256);
public sealed record StudioUpdate(StudioUpdateManifest Manifest, string DownloadUrl, string ReleaseUrl);

/// <summary>Finds complete Windows releases, including betas and rebuilt releases with the same tag.</summary>
public static class StudioUpdates
{
    public const string Repository = "Yeastmans/Tape-To-Tape-custom-Campaign-Framework-BepinEX-Mod";
    public const string ReleasesUrl = "https://github.com/" + Repository + "/releases";
    public const string ManifestName = "campaign-studio-update.json";
    public const string InstallerName = "T2T_Custom_Campaign_Framework_Setup.exe";
    private const long MaxInstallerSize = 1024L * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly HttpClient Http = CreateClient();

    private sealed record Asset(string Name, long Size, string? Digest,
        [property: JsonPropertyName("browser_download_url")] string DownloadUrl);
    private sealed record Release(bool Draft, string Name,
        [property: JsonPropertyName("tag_name")] string Tag,
        [property: JsonPropertyName("html_url")] string Url, Asset[] Assets);

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("T2T-Campaign-Studio/1.0");
        client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true };
        return client;
    }

    public static StudioBuild ReadBuild(string directory)
    {
        string file = Path.Combine(directory, "studio-build.json");
        if (!File.Exists(file)) return new(1, 0, "Development build", "win-x64");
        var build = JsonSerializer.Deserialize<StudioBuild>(File.ReadAllText(file), Json)
            ?? throw new InvalidDataException("Studio's build information is missing. Reinstall Studio.");
        if (build.Schema != 1 || build.BuildId <= 0 || build.Platform != "win-x64" || string.IsNullOrWhiteSpace(build.Label))
            throw new InvalidDataException("Studio's build information is invalid. Reinstall Studio.");
        return build;
    }

    private static bool IsSha256(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static string AssetHash(Asset asset)
    {
        string? hash = asset.Digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true ? asset.Digest[7..] : null;
        if (!IsSha256(hash)) throw new InvalidDataException("GitHub has not supplied the download checksum yet. Try again later.");
        return hash!;
    }
    private static void VerifyAssetUrl(string url, string tag, string name)
    {
        string expected = "https://github.com/" + Repository + "/releases/download/" + Uri.EscapeDataString(tag) + "/" + name;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.AbsoluteUri != expected)
            throw new InvalidDataException("The update download is not an official release asset.");
    }

    public static async Task<StudioUpdate> FindLatestAsync(CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        // /latest excludes prereleases. The release list also lets a repaired installer keep its beta tag.
        byte[] listing = await ReadSmallAsync("https://api.github.com/repos/" + Repository + "/releases?per_page=30", 4 * 1024 * 1024, deadline.Token);
        var releases = JsonSerializer.Deserialize<Release[]>(listing, Json) ?? [];
        StudioUpdate? newest = null;
        foreach (var release in releases.Where(r => !r.Draft))
        {
            var manifestAsset = release.Assets.SingleOrDefault(a => a.Name == ManifestName);
            if (manifestAsset == null) continue; // Releases predating the updater use manual installation.
            VerifyAssetUrl(manifestAsset.DownloadUrl, release.Tag, ManifestName);
            byte[] bytes = await ReadSmallAsync(manifestAsset.DownloadUrl, 64 * 1024, deadline.Token);
            if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(AssetHash(manifestAsset), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The update information changed during the check. Try again.");
            var manifest = JsonSerializer.Deserialize<StudioUpdateManifest>(bytes, Json)
                ?? throw new InvalidDataException("The published update information is empty.");
            var installer = release.Assets.SingleOrDefault(a => a.Name == InstallerName);
            if (installer == null) throw new InvalidDataException("The release upload is not complete yet. Try again later.");
            ValidateManifest(manifest, release.Tag, installer.Size, AssetHash(installer));
            VerifyAssetUrl(installer.DownloadUrl, release.Tag, InstallerName);
            if (release.Url != "https://github.com/" + Repository + "/releases/tag/" + Uri.EscapeDataString(release.Tag))
                throw new InvalidDataException("The update release page is not in the official repository.");
            if (newest == null || manifest.BuildId > newest.Manifest.BuildId)
                newest = new(manifest, installer.DownloadUrl, release.Url);
        }
        return newest ?? throw new InvalidDataException("No complete automatic-update package is published yet. Check the GitHub releases page.");
    }

    public static void ValidateManifest(StudioUpdateManifest manifest, string tag, long installerSize, string installerHash)
    {
        if (manifest.Schema != 1 || manifest.Platform != "win-x64" || manifest.BuildId <= 0
            || string.IsNullOrWhiteSpace(manifest.Label) || manifest.Label.Length > 200 || manifest.ReleaseTag != tag
            || manifest.InstallerName != InstallerName || manifest.InstallerSize <= 0 || manifest.InstallerSize > MaxInstallerSize
            || manifest.InstallerSize != installerSize || !IsSha256(manifest.InstallerSha256)
            || !manifest.InstallerSha256.Equals(installerHash, StringComparison.OrdinalIgnoreCase)
            || !IsSha256(manifest.StudioSha256) || !IsSha256(manifest.ModSha256))
            throw new InvalidDataException("The published installer and update information do not match. Try again after the release upload finishes.");
    }

    public static bool IsNewer(StudioBuild installed, StudioUpdate update) => update.Manifest.BuildId > installed.BuildId;

    public static async Task<string> DownloadAsync(StudioUpdate update, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var manifest = update.Manifest;
        ValidateManifest(manifest, manifest.ReleaseTag, manifest.InstallerSize, manifest.InstallerSha256);
        VerifyAssetUrl(update.DownloadUrl, manifest.ReleaseTag, InstallerName);
        // A unique directory prevents partial downloads and simultaneous Studio instances from sharing an executable.
        string directory = Path.Combine(Path.GetTempPath(), "T2T-Campaign-Studio-Updates", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string partial = Path.Combine(directory, InstallerName + ".download");
        string complete = Path.Combine(directory, InstallerName);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromMinutes(10));
            using var response = await Http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length != manifest.InstallerSize)
                throw new InvalidDataException("The installer size changed. Check for updates again.");
            await using (var source = await response.Content.ReadAsStreamAsync(deadline.Token))
            await using (var target = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                byte[] buffer = new byte[81920]; long received = 0;
                int count;
                while ((count = await source.ReadAsync(buffer, deadline.Token)) != 0)
                {
                    received += count;
                    if (received > manifest.InstallerSize) throw new InvalidDataException("The installer is larger than the verified release.");
                    hash.AppendData(buffer, 0, count);
                    await target.WriteAsync(buffer.AsMemory(0, count), deadline.Token);
                    progress?.Report(100.0 * received / manifest.InstallerSize);
                }
                if (received != manifest.InstallerSize || !Convert.ToHexString(hash.GetHashAndReset()).Equals(manifest.InstallerSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The downloaded installer failed its checksum check. Nothing was installed. Try again.");
            }
            deadline.Token.ThrowIfCancellationRequested();
            File.Move(partial, complete);
            return complete;
        }
        catch
        {
            if (File.Exists(partial)) File.Delete(partial);
            if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
            throw;
        }
    }

    private static async Task<byte[]> ReadSmallAsync(string url, int maximum, CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[8192]; int count;
        while ((count = await stream.ReadAsync(chunk, cancellationToken)) != 0)
        {
            if (buffer.Length + count > maximum) throw new InvalidDataException("The update response is too large.");
            buffer.Write(chunk, 0, count);
        }
        return buffer.ToArray();
    }
}
