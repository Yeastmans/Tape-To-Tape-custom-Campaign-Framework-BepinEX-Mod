using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SkiaSharp;

namespace GameArt;

/// <summary>Small, optional equipment cache for Head Editor. Never rebuilds the general art cache.</summary>
public static class HeadPreviewAssets
{
    private static readonly Lazy<JsonObject> Map = new(() => Maps.Load("head-templates.json").AsObject());
    private static readonly object Gate = new();
    private static JsonObject Helmet => Map.Value["equippedHelmet"]!.AsObject();
    private static string MapHash => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Helmet.ToJsonString())));
    public static string Folder(string cache) => Path.Combine(cache, "head-preview-equipment");

    public static JsonObject HelmetGeometry() => Helmet.DeepClone().AsObject();
    public static JsonObject LegacyTemplate(bool helmet) => Map.Value["templates"]!.AsArray()
        .Single(t => t!["skin"]!.GetValue<string>() == "Faces/Toronto/Mathieu" + (helmet ? "_Helmet" : ""))!.DeepClone().AsObject();

    public static bool Ready(string? cache)
    {
        if (cache == null) return false;
        try
        {
            string folder = Folder(cache);
            var metadata = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "equipment.json")));
            return metadata?["mapHash"]?.GetValue<string>() == MapHash
                && Helmet["regions"]!.AsArray().All(r => File.Exists(Path.Combine(folder, r!["editableFile"]!.GetValue<string>())));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) { return false; }
    }

    /// <summary>Reads eight native shell crops from the installed game. Existing head projects are never written.</summary>
    public static void Ensure(string cache, string gameRoot)
    {
        lock (Gate)
        {
            if (Ready(cache)) return;
            using var game = new UnityGame(gameRoot);
            var source = game.TextAssets().Where(a => a.Name == "Ultimate_Sprite.skel").ToArray();
            if (source.Length != 1 || !Convert.ToHexString(SHA256.HashData(game.TextBytes(source[0].File, source[0].Info)))
                .Equals(Helmet["skeletonSha256"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("This game's helmet geometry has changed. Update Campaign Studio to preview its helmet.");
            var metadata = HelmetGeometry();
            var regions = metadata["regions"]!.AsArray();
            var atlas = HeadTemplate.Atlas.Read(game, "Player_Sprite.atlas", regions.Select(r => r!["name"]!.GetValue<string>()));
            string folder = Path.GetFullPath(Folder(cache));
            Directory.CreateDirectory(folder);
            // Stage just these files. A failed extraction cannot turn a partial cache into a ready one.
            string stage = Path.Combine(folder, ".extract-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            var pages = new Dictionary<string, SKBitmap>(StringComparer.Ordinal);
            try
            {
                foreach (var node in regions)
                {
                    var entry = node!.AsObject();
                    var region = atlas.Regions[entry["name"]!.GetValue<string>()];
                    if (!pages.TryGetValue(region.Page, out var page))
                    {
                        page = HeadTemplate.PageTexture(game, region.Page);
                        var size = atlas.Pages[region.Page];
                        if (page.Width != size.Width || page.Height != size.Height)
                        {
                            if ((long)page.Width * size.Height != (long)page.Height * size.Width)
                            { page.Dispose(); throw new InvalidDataException("The helmet texture dimensions do not match its native atlas."); }
                            var resized = page.Resize(new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul), SKFilterQuality.High);
                            page.Dispose(); page = resized ?? throw new InvalidDataException("Cannot read the native helmet texture.");
                        }
                        pages.Add(region.Page, page);
                    }
                    using var crop = HeadTemplate.Crop(page, region);
                    string file = entry["editableFile"]!.GetValue<string>();
                    if (Path.GetFileName(file) != file) throw new InvalidDataException("Invalid helmet preview filename.");
                    Png.Save(crop, Path.Combine(stage, file));
                    // Actual crop identity is recorded separately from the atlas-independent mesh UVs.
                    entry["page"] = region.Page; entry["x"] = region.X; entry["y"] = region.Y;
                    entry["width"] = region.Width; entry["height"] = region.Height; entry["degrees"] = region.Degrees;
                    entry["originalWidth"] = region.OriginalWidth; entry["originalHeight"] = region.OriginalHeight;
                    entry["offsetX"] = region.OffsetX; entry["offsetY"] = region.OffsetY;
                }
                metadata["mapHash"] = MapHash;
                File.WriteAllText(Path.Combine(stage, "equipment.json"), metadata.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                foreach (var entry in regions) File.Move(Path.Combine(stage, entry!["editableFile"]!.GetValue<string>()), Path.Combine(folder, entry["editableFile"]!.GetValue<string>()), true);
                File.Move(Path.Combine(stage, "equipment.json"), Path.Combine(folder, "equipment.json"), true);
            }
            finally
            {
                foreach (var page in pages.Values) page.Dispose();
                foreach (string file in Directory.GetFiles(stage)) File.Delete(file);
                Directory.Delete(stage);
            }
        }
    }
}
