using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Platform;

// Inspect the actual published binaries, not the editor assembly referenced by other test suites.
try
{
if (args.Length is < 1 or > 3) throw new ArgumentException("Usage: GameArtPackageChecks <publish-directory> [runtime-dll] [source-root]");
string publish = Path.GetFullPath(args[0]);
string root = args.Length > 2 ? Path.GetFullPath(args[2])
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
if (!File.Exists(Path.Combine(root, "src/CampaignCreator/CampaignCreator.csproj"))
    || !Directory.Exists(Path.Combine(root, "assets/studio-guide"))
    || !Directory.Exists(Path.Combine(root, "src/CustomCampaignFramework/Assets/MapSeams")))
    throw new ArgumentException("Source root must contain the CampaignCreator project, guide illustrations and approved MapSeams: " + root);
void Check(bool ok, string text) { if (!ok) throw new Exception(text); Console.WriteLine("PASS " + text); }
var bannedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
    "head-preview", "head-templates", "team-logos", "base-rosters", "base-maps", "flat-equipment", "node-images", "map-preview", "game-art", "spine-runtime", "node_modules"
};
foreach (string file in Directory.EnumerateFiles(publish, "*", SearchOption.AllDirectories))
{
    string relative = Path.GetRelativePath(publish, file);
    if (relative.Split(Path.DirectorySeparatorChar).Any(bannedFolders.Contains)) throw new Exception("Extracted game art is present: " + relative);
    string extension = Path.GetExtension(file).ToLowerInvariant();
    if (extension is ".png" or ".jpg" or ".jpeg" or ".webp" or ".skel" or ".atlas" or ".bundle"
        || Path.GetFileName(file).Contains("Spine", StringComparison.OrdinalIgnoreCase)) throw new Exception("Game pixels or Spine runtime are present: " + relative);
}
Check(true, "Published loose files contain no extracted game art or Spine runtime");
var studio = Assembly.LoadFrom(Path.Combine(publish, "T2T Campaign Studio.dll"));
AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
var pictures = AssetLoader.GetAssets(new Uri("avares://T2T Campaign Studio/"), null)
    .Where(p => p.AbsolutePath != "/!AvaloniaResourceXamlInfo").ToArray();
var allowed = Directory.GetFiles(Path.Combine(root, "assets/studio-guide"), "*.png")
    .ToDictionary(f => "/Assets/StudioGuide/" + Path.GetFileName(f), StringComparer.Ordinal);
foreach (string file in Directory.GetFiles(Path.Combine(root, "src/CustomCampaignFramework/Assets/MapSeams"), "*.png"))
    allowed.Add("/Assets/MapPreview/" + Path.GetFileName(file), file);
allowed.Add("/!__AvaloniaDefaultWindowIcon", Path.Combine(root, "assets/branding/CampaignStudio.ico"));
Check(pictures.Length == allowed.Count, "Studio embeds exactly the source guide illustrations, six approved seams and its application icon");
foreach (var uri in pictures)
{
    string path = Uri.UnescapeDataString(uri.AbsolutePath);
    Check(allowed.TryGetValue(path, out string? reference), "Allowed embedded picture: " + path);
    using var stream = AssetLoader.Open(uri);
    using var source = File.OpenRead(reference!);
    Check(SHA256.HashData(stream).AsSpan().SequenceEqual(SHA256.HashData(source)), "Published picture matches reviewed source: " + path);
}
Check(!studio.GetReferencedAssemblies().Any(a => a.Name!.Contains("Spine", StringComparison.OrdinalIgnoreCase)), "Studio has no Spine runtime reference");
var gameArt = Assembly.LoadFrom(Path.Combine(publish, "GameArt.dll"));
var gameArtResources = gameArt.GetManifestResourceNames();
var expectedResources = new Dictionary<string,string>(StringComparer.Ordinal) {
    ["GameArt.RewardFamilies.tsv"] = "assets/reward-pools/reward-families.tsv",
    ["GameArt.RewardCatalog.tsv"] = "assets/reward-pools/reward_catalog.tsv",
};
foreach (string file in new[] { "flat-regions.json", "goalie-mask-templates.json", "head-poses.json", "head-templates.json", "mathieu-head-assets.json", "node-meshes.json" })
    expectedResources.Add("GameArt.Maps." + file, "assets/game-art-maps/" + file);
Check(gameArtResources.Length == expectedResources.Count && gameArtResources.All(expectedResources.ContainsKey), "GameArt embeds exactly the six reviewed geometry maps and two text catalogs");
foreach (string name in gameArtResources)
{
    using var stream = gameArt.GetManifestResourceStream(name)!;
    using var source = File.OpenRead(Path.Combine(root, expectedResources[name]));
    Check(SHA256.HashData(stream).AsSpan().SequenceEqual(SHA256.HashData(source)), "GameArt resource matches reviewed source: " + name);
    if (name.EndsWith(".json", StringComparison.Ordinal)) {
        using var jsonStream = gameArt.GetManifestResourceStream(name)!;
        using var json = JsonDocument.Parse(jsonStream);
        Check(json.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array, "Geometry JSON is readable: " + name);
    }
}
if (args.Length > 1)
{
    var plugin = Assembly.LoadFrom(Path.GetFullPath(args[1]));
    Check(!plugin.GetManifestResourceNames().Any(n => n.EndsWith(".png") || n.EndsWith(".atlas") || n.EndsWith(".skel")), "Mod DLL embeds no game images or skeleton sources");
    string seams = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1]))!, "MapSeams");
    foreach (string file in Directory.GetFiles(seams, "*"))
    {
        string reference = Path.Combine(root, "src/CustomCampaignFramework/Assets/MapSeams", Path.GetFileName(file));
        Check(File.Exists(reference) && File.ReadAllBytes(file).AsSpan().SequenceEqual(File.ReadAllBytes(reference)), "Mod seam matches approved source: " + Path.GetFileName(file));
    }
    Check(Directory.GetFiles(seams, "*").Length == 6, "Mod package contains the six original seams");
    Check(!Directory.EnumerateFiles(Path.Combine(root, "installer/bepinex"), "*", SearchOption.AllDirectories)
        .Any(f => Path.GetExtension(f).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp" or ".skel" or ".atlas" or ".bundle"), "Loader package contains no game artwork or skeleton sources");
}
Check(File.Exists(Path.Combine(publish, "THIRD_PARTY_NOTICES-GameArt.txt")), "GameArt attribution ships with the editor");
Console.WriteLine("Published game-art package checks passed.");
return 0;
}
catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
