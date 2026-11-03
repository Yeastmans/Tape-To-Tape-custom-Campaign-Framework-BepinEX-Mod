using System.Text.Json;
using System.Text.Json.Nodes;
using CampaignCore;
using GameArt;
using SkiaSharp;

namespace CampaignCreator;

/// <summary>
/// Static native idle-pose fit preview. Uses the runtime's image sampling and the project's
/// preserved geometry. The optional helmet is an independent, preview-only equipment layer.
/// </summary>
public static class HeadProjectPreview
{
    public static IReadOnlyList<string> Directions { get; } = Array.AsReadOnly(new[] { "S", "SW", "W", "NW", "N", "NE", "E", "SE" });
    public const string HelmetDescription = "Standard helmet fit preview · neutral reference colours";

    /// <summary>Null when ready, otherwise a user-facing reason. This never extracts files or reads game bundles.</summary>
    public static string? Availability(HeadProject project, bool helmet)
    {
        if (project.IsGoalie) return "This preview is for skater heads.";
        if (helmet && !project.SupportsHelmet) return "This head does not support a separate helmet.";
        try
        {
            var assets = Reference(project, helmet);
            foreach (string direction in Directions)
                foreach (var mesh in Views(assets, direction))
                {
                    var binding = Binding(assets, mesh, direction);
                    var item = Image(project, binding, helmet);
                    if (!File.Exists(project.FilePath(item.File))) return "A required head PNG is missing: " + item.File;
                }
            if (helmet && !HeadPreviewAssets.Ready(StudioArt.Folder))
                return "Read the standard helmet from your installed game to enable this fit preview.";
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        { return "Head preview unavailable: " + e.Message; }
    }

    /// <summary>Call off the UI thread. Extends an existing game-art cache with only eight helmet PNGs.</summary>
    public static void EnsureAssets(HeadProject project, string? gameRoot = null)
    {
        if (project.IsGoalie || !project.SupportsHelmet) return;
        string cache = StudioArt.Folder ?? throw new IOException("Read game artwork on Home before loading the helmet preview.");
        if (HeadPreviewAssets.Ready(cache)) return;
        gameRoot ??= ArtCache.FindGame();
        if (gameRoot == null) throw new IOException("Tape to Tape was not found. Use Read game artwork on Home to locate the game.");
        HeadPreviewAssets.Ensure(cache, gameRoot);
    }

    /// <summary>Caller owns the returned 512 × 512 transparent bitmap. Never writes any project file.</summary>
    public static SKBitmap Render(HeadProject project, string direction, bool helmet)
    {
        if (!Directions.Contains(direction, StringComparer.Ordinal)) throw new ArgumentException("Choose one of the eight head directions.", nameof(direction));
        if (Availability(project, helmet) is { } reason) throw new InvalidDataException(reason);
        var assets = Reference(project, helmet);
        var equipment = HeadPreviewAssets.HelmetGeometry();
        // A stable frame for both toggle states makes fit differences visible without zooming.
        var framing = Views(Reference(project, false), direction).ToList();
        if (project.SupportsHelmet) framing.AddRange(Views(Reference(project, true), direction));
        framing.AddRange(Views(equipment, direction));
        var allPoints = framing.SelectMany(m => Floats(m["vertices"]!)).ToArray();
        float left = allPoints.Where((_, i) => i % 2 == 0).Min(), right = allPoints.Where((_, i) => i % 2 == 0).Max();
        float bottom = allPoints.Where((_, i) => i % 2 == 1).Min(), top = allPoints.Where((_, i) => i % 2 == 1).Max();
        if (right <= left || top <= bottom) throw new InvalidDataException("The native head pose has no visible dimensions.");
        float scale = Math.Min(448 / (right - left), 448 / (top - bottom));
        float ox = (512 - (right - left) * scale) / 2, oy = (512 - (top - bottom) * scale) / 2;
        var result = new SKBitmap(512, 512, SKColorType.Bgra8888, SKAlphaType.Premul);
        try
        {
            using var canvas = new SKCanvas(result); canvas.Clear(SKColors.Transparent);
            var layers = Views(assets, direction).Select(m => (Mesh: m, Equipment: false)).ToList();
            if (helmet) layers.AddRange(Views(equipment, direction).Select(m => (Mesh: m, Equipment: true)));
            foreach (var layer in layers.OrderBy(l => l.Mesh["order"]!.GetValue<int>()))
            {
                var mesh = layer.Mesh;
                float[] uv;
                string file;
                HeadImage? item = null;
                if (layer.Equipment)
                {
                    uv = Floats(mesh["localUVs"]!);
                    file = Path.Combine(HeadPreviewAssets.Folder(StudioArt.Folder!), mesh["file"]!.GetValue<string>());
                }
                else
                {
                    var binding = Binding(assets, mesh, direction);
                    item = Image(project, binding, helmet); file = project.FilePath(item.File);
                    // Replacement regions are upright, regardless of old atlas packing/rotation.
                    uv = binding["regionUVs"] is { } nativeUv ? Floats(nativeUv) : [0, 1, 0, 0, 1, 0, 1, 1];
                }
                using var bitmap = SKBitmap.Decode(file) ?? throw new InvalidDataException("Cannot decode preview artwork: " + Path.GetFileName(file));
                var sampling = item == null ? new HeadArtworkPlacement.Sampling(0, 0, bitmap.Width, bitmap.Height, 1, 0, 0) : Sampling(item, bitmap);
                var world = Floats(mesh["vertices"]!);
                if (world.Length != uv.Length || world.Length % 2 != 0) throw new InvalidDataException("The native head mesh and image coordinates do not match.");
                var positions = new SKPoint[world.Length / 2]; var texture = new SKPoint[positions.Length];
                for (int i = 0; i < positions.Length; i++)
                {
                    positions[i] = new SKPoint((world[i * 2] - left) * scale + ox, (top - world[i * 2 + 1]) * scale + oy);
                    texture[i] = new SKPoint(sampling.Left + uv[i * 2] * sampling.Width, sampling.Top + uv[i * 2 + 1] * sampling.Height);
                }
                var triangles = mesh["triangles"]!.AsArray().Select(n => checked((ushort)n!.GetValue<int>())).ToArray();
                if (triangles.Any(t => t >= positions.Length)) throw new InvalidDataException("The native head mesh has an invalid triangle.");
                using var image = SKImage.FromBitmap(bitmap);
                using var shader = image.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp);
                using var paint = new SKPaint { Shader = shader, FilterQuality = SKFilterQuality.Medium, IsAntialias = false };
                using var vertices = SKVertices.CreateCopy(SKVertexMode.Triangles, positions, texture, null, triangles);
                canvas.DrawVertices(vertices, SKBlendMode.Modulate, paint);
            }
            canvas.Flush(); return result;
        }
        catch { result.Dispose(); throw; }
    }

    private static JsonObject Reference(HeadProject project, bool helmet)
    {
        if (project.Version == 1) return HeadPreviewAssets.LegacyTemplate(helmet);
        string file = project.FilePath(helmet ? "reference/Helmet/head-assets.json" : "reference/Bare/head-assets.json");
        var result = JsonNode.Parse(File.ReadAllText(file))?.AsObject() ?? throw new InvalidDataException("Native attachment reference is empty.");
        if (result["views"] is not JsonObject) throw new InvalidDataException("Native head placement references are missing.");
        return result;
    }
    private static IEnumerable<JsonObject> Views(JsonObject data, string direction) => data["views"]?[direction]?.AsArray().Select(m => m!.AsObject())
        ?? throw new InvalidDataException("The native reference has no " + direction + " view.");
    private static JsonObject Binding(JsonObject assets, JsonObject mesh, string direction)
    {
        string slot = mesh["slot"]!.GetValue<string>();
        string placeholder = mesh["placeholder"]?.GetValue<string>() ?? direction + (slot == "Helmet_Top" ? "-Helmet_top" : "-Helmet");
        return assets["bindings"]!.AsArray().Single(b => b!["slot"]!.GetValue<string>() == slot && b["placeholder"]!.GetValue<string>() == placeholder)!.AsObject();
    }
    private static HeadImage Image(HeadProject project, JsonObject binding, bool helmet)
    {
        string file;
        if (project.Version >= 2)
        {
            string slot = binding["slot"]!.GetValue<string>(), placeholder = binding["placeholder"]!.GetValue<string>();
            file = project.Bindings.Single(b => b.Helmet == helmet && b.Slot == slot && b.Placeholder == placeholder).File;
        }
        else
        {
            string direction = Path.GetFileNameWithoutExtension(binding["file"]!.GetValue<string>());
            string group = helmet && project.SeparateHelmetArtwork ? "Helmet" : "Head";
            return project.Images.Single(i => i.Group == group && Path.GetFileNameWithoutExtension(i.File) == direction);
        }
        return project.Images.Single(i => i.File == file);
    }
    private static float[] Floats(JsonNode array) => array.AsArray().Select(n => n!.GetValue<float>()).ToArray();
    private static HeadArtworkPlacement.Sampling Sampling(HeadImage item, SKBitmap bitmap)
    {
        if (bitmap.Width != item.Width || bitmap.Height != item.Height) throw new InvalidDataException("Head PNG dimensions changed. Keep the 512 × 512 canvas.");
        int left = bitmap.Width, top = bitmap.Height, right = 0, bottom = 0;
        for (int y = 0; y < bitmap.Height; y++) for (int x = 0; x < bitmap.Width; x++)
            if (bitmap.GetPixel(x, y).Alpha > 32) { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x + 1); bottom = Math.Max(bottom, y + 1); }
        if (right <= left || bottom <= top) return new HeadArtworkPlacement.Sampling(item.Left, item.Top, item.ContentWidth, item.ContentHeight, 1, 0, 0);
        var image = new CustomHeadImage { Width = item.Width, Height = item.Height, NativeWidth = item.NativeWidth, NativeHeight = item.NativeHeight,
            Left = item.Left, Top = item.Top, ContentWidth = item.ContentWidth, ContentHeight = item.ContentHeight,
            HasNativePlacement = item.HasNativePlacement, NativeVisibleLeft = item.NativeVisibleLeft, NativeVisibleTop = item.NativeVisibleTop,
            NativeVisibleWidth = item.NativeVisibleWidth, NativeVisibleHeight = item.NativeVisibleHeight };
        string direction = item.Direction.Length > 0 ? item.Direction : Path.GetFileNameWithoutExtension(item.File);
        return HeadArtworkPlacement.Align(image, direction, left, top, right, bottom);
    }
}
