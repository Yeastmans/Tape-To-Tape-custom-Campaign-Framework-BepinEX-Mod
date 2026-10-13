using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SkiaSharp;

namespace GameArt;

/// <summary>
/// The Head Editor's starting template (HeadProject.TemplateFolder, head-templates/Mathieu): the
/// native directional head images and hand regions of Faces/Toronto/Mathieu cropped losslessly
/// from the game's atlas pages, a front portrait rendered like the head previews, and the
/// attachment and front-pose mappings each new head project keeps under reference/.
/// </summary>
public sealed class HeadTemplate : IArtExtractor
{
    public string Folder => "head-templates";
    public string Description => "Head Editor template";

    private const string Template = "Mathieu", Skin = "Faces/Toronto/Mathieu", AtlasName = "Player_Sprite.atlas";
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public void Extract(UnityGame game, string output, IProgress<string>? progress)
    {
        ExtractLegacy(game, output, progress);
        ExtractCatalog(game, output, progress);
    }

    private void ExtractLegacy(UnityGame game, string output, IProgress<string>? progress)
    {
        string folder = Path.Combine(output, Template);
        Directory.CreateDirectory(folder);
        // Slot/attachment/sequence mappings from the game's skeleton, with the region list each editable PNG comes from.
        var assets = Maps.Load("mathieu-head-assets.json").AsObject();
        var regions = assets["regions"]!.AsArray();
        var atlas = Atlas.Read(game, AtlasName, regions.Select(r => r!["name"]!.GetValue<string>()));
        // The front pose is the head-previews row for the same skin.
        var pose = Maps.Load("head-poses.json").AsArray()
            .Single(r => r!["role"]!.GetValue<string>() == "skater" && r["skin"]!.GetValue<string>() == Skin)!.AsObject();

        var pages = new Dictionary<string, SKBitmap>(StringComparer.OrdinalIgnoreCase);
        SKBitmap Page(string page)
        {
            if (pages.TryGetValue(page, out var cached)) return cached;
            var size = atlas.Pages.GetValueOrDefault(page) ?? throw new InvalidDataException($"{AtlasName} has no page {page}.");
            var bitmap = PageTexture(game, page);
            if (bitmap.Width != size.Width || bitmap.Height != size.Height)
            {
                string actual = bitmap.Width + " × " + bitmap.Height;
                bitmap.Dispose();
                throw new InvalidDataException($"Atlas page {page} is {actual} in the game files but {size.Width} × {size.Height} in {AtlasName}.");
            }
            return pages[page] = bitmap;
        }

        try
        {
            int moved = 0;
            foreach (var node in regions)
            {
                var entry = node!.AsObject();
                string name = entry["name"]!.GetValue<string>();
                var region = atlas.Regions[name];
                // The game's atlas is authoritative: keep head-assets.json in step with the pixels cropped below.
                moved += Refresh(entry, region) ? 1 : 0;
                string file = entry["editableFile"]!.GetValue<string>();
                string target = Path.GetFullPath(Path.Combine(folder, file));
                if (!target.StartsWith(Path.GetFullPath(folder) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    throw new InvalidDataException("Template file outside its folder: " + file);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var image = Crop(Page(region.Page), region);
                if (Png.IsBlank(image)) throw new InvalidDataException("Blank atlas region: " + name);
                Png.Save(image, target);
            }
            using (var portrait = HeadPreviews.Render(pose["meshes"]!.AsArray(), Page))
            {
                if (Png.IsBlank(portrait)) throw new InvalidDataException("Empty portrait: " + Skin);
                Png.Save(portrait, Path.Combine(folder, "portrait.png"));
            }
            File.WriteAllText(Path.Combine(folder, "head-assets.json"), assets.ToJsonString(Indented));
            File.WriteAllText(Path.Combine(folder, "front-pose.json"), pose.ToJsonString(Indented) + Environment.NewLine);
            progress?.Report($"Head template: {regions.Count} atlas regions" + (moved > 0 ? $" ({moved} repositioned by the game's atlas)" : "") + ", portrait");
        }
        finally { foreach (var page in pages.Values) page.Dispose(); }
    }

    /// <summary>The Addressables texture of an atlas page: the page name plus the bundle hash, so "Player_Sprite" never matches "Player_Sprite_104".</summary>
    internal static SKBitmap PageTexture(UnityGame game, string page)
    {
        string prefix = "skatersprites_assets_" + Path.GetFileNameWithoutExtension(page).ToLowerInvariant() + "_";
        string bundle = Directory.GetFiles(game.BundleFolder, prefix + "*.bundle")
            .Select(p => Path.GetFileNameWithoutExtension(p))
            .Where(n => Regex.IsMatch(n[prefix.Length..], "^[0-9a-f]{32}$"))
            .Order(StringComparer.Ordinal).FirstOrDefault()
            ?? throw new FileNotFoundException("No texture bundle for atlas page " + page);
        return game.BundleTexture(bundle) ?? throw new InvalidDataException("No texture in " + bundle);
    }

    private static bool Refresh(JsonObject entry, Atlas.Region region)
    {
        bool changed = false;
        void Set(string key, JsonNode value)
        {
            if (entry[key]?.ToJsonString() == value.ToJsonString()) return;
            entry[key] = value; changed = true;
        }
        Set("page", region.Page);
        Set("x", region.X); Set("y", region.Y); Set("width", region.Width); Set("height", region.Height);
        Set("originalWidth", region.OriginalWidth); Set("originalHeight", region.OriginalHeight);
        Set("offsetX", region.OffsetX); Set("offsetY", region.OffsetY); Set("degrees", region.Degrees);
        return changed;
    }

    private static void ExtractCatalog(UnityGame game, string output, IProgress<string>? progress)
        => ExtractCatalog(game, output, progress, Maps.Load("head-templates.json").AsObject());

    internal static void ExtractCatalog(UnityGame game, string output, IProgress<string>? progress, JsonObject map)
    {
        var templates = map["templates"]!.AsArray();
        // A renamed/removed head must not make every other native template unavailable.
        // Choose the matching atlas here and validate each template inside its own failure boundary.
        var atlas = Atlas.Read(game, AtlasName, templates.SelectMany(t => t!["regions"]!.AsArray())
            .Select(r => r!["name"]!.GetValue<string>()).Distinct(), allowMissing: true);
        // Do not retain all 106 atlas pages in memory while building the catalog.
        var pages = new Dictionary<string, SKBitmap>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        SKBitmap Page(string name)
        {
            if (pages.TryGetValue(name, out var found)) return found;
            while (queue.Count >= 12) { string old = queue.Dequeue(); pages[old].Dispose(); pages.Remove(old); }
            var picture = PageTexture(game, name); pages.Add(name, picture); queue.Enqueue(name); return picture;
        }
        var catalog = new JsonArray();
        int done = 0;
        try
        {
            foreach (var node in templates)
            {
                var template = node!.DeepClone().AsObject();
                string relative = template["folder"]!.GetValue<string>();
                string folder = SafePath(output, relative);
                var row = new JsonObject { ["skin"] = template["skin"]!.DeepClone(), ["baseSkin"] = template["baseSkin"]!.DeepClone(),
                    ["helmetSkin"] = template["helmetSkin"]!.DeepClone(), ["isHelmet"] = template["isHelmet"]!.DeepClone(),
                    ["label"] = template["label"]!.DeepClone(), ["folder"] = relative };
                try
                {
                    var missing = template["regions"]!.AsArray().Select(r => r!["name"]!.GetValue<string>())
                        .Where(name => !atlas.Regions.ContainsKey(name)).Distinct().ToArray();
                    if (missing.Length > 0)
                        throw new InvalidDataException($"{AtlasName} is missing regions: " + string.Join(", ", missing));
                    Directory.CreateDirectory(folder);
                    foreach (var regionNode in template["regions"]!.AsArray())
                    {
                        var entry = regionNode!.AsObject();
                        var region = atlas.Regions[entry["name"]!.GetValue<string>()]; Refresh(entry, region);
                        using var cropped = Crop(Page(region.Page), region);
                        Png.Save(cropped, SafePath(folder, entry["editableFile"]!.GetValue<string>()));
                    }
                    using (var portrait = HeadPreviews.Render(template["views"]!["S"]!.AsArray(), Page))
                        Png.Save(portrait, Path.Combine(folder, "portrait.png"));
                    Directory.CreateDirectory(Path.Combine(folder, "views"));
                    foreach (var view in template["views"]!.AsObject())
                    {
                        var meshes = map["bodyViews"]![view.Key]!.AsArray().Concat(view.Value!.AsArray())
                            .OrderBy(m => m!["order"]!.GetValue<int>()).ToArray();
                        using var example = RenderExample(meshes, map["framing"]![view.Key]!.AsArray(), Page);
                        Png.Save(example, Path.Combine(folder, "views", view.Key + ".png"));
                    }
                    File.WriteAllText(Path.Combine(folder, "head-assets.json"), template.ToJsonString(Indented));
                    row["available"] = true;
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException)
                {
                    if (Directory.Exists(folder)) Directory.Delete(folder, true);
                    row["available"] = false; row["error"] = ex.Message;
                }
                catalog.Add(row);
                if (++done % 20 == 0) progress?.Report($"Native head templates {done} / {templates.Count}");
            }
        }
        finally { foreach (var page in pages.Values) page.Dispose(); }
        File.WriteAllText(Path.Combine(output, "catalog.json"), new JsonObject { ["version"] = 1, ["templates"] = catalog }.ToJsonString(Indented));
        if (!catalog.Any(c => c!["available"]!.GetValue<bool>())) throw new InvalidDataException("No native head templates could be read.");
        progress?.Report($"{catalog.Count(c => c!["available"]!.GetValue<bool>())} native head templates with native sizes and eight placement examples");
    }

    private static string SafePath(string root, string relative)
    {
        string path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Native template path is outside its folder.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); return path;
    }

    private static SKBitmap RenderExample(IEnumerable<JsonNode?> meshes, JsonArray framing, Func<string, SKBitmap> page)
    {
        float left = framing[0]!.GetValue<float>(), bottom = framing[1]!.GetValue<float>(),
            right = framing[2]!.GetValue<float>(), top = framing[3]!.GetValue<float>();
        float scale = Math.Min(480 / (right - left), 608 / (top - bottom));
        float ox = (512 - (right - left) * scale) / 2, oy = (640 - (top - bottom) * scale) / 2;
        var result = new SKBitmap(new SKImageInfo(512, 640, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(result); canvas.Clear(SKColors.Transparent);
        foreach (var mesh in meshes)
        {
            var atlas = page(mesh!["page"]!.GetValue<string>());
            var v = mesh["vertices"]!.AsArray().Select(n => n!.GetValue<float>()).ToArray();
            var uv = mesh["uvs"]!.AsArray().Select(n => n!.GetValue<float>()).ToArray();
            var positions = new SKPoint[v.Length / 2]; var texture = new SKPoint[v.Length / 2];
            for (int i = 0; i < positions.Length; i++)
            {
                positions[i] = new SKPoint((v[2 * i] - left) * scale + ox, (top - v[2 * i + 1]) * scale + oy);
                texture[i] = new SKPoint(uv[2 * i] * atlas.Width, uv[2 * i + 1] * atlas.Height);
            }
            using var image = SKImage.FromBitmap(atlas);
            using var shader = image.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, SKMatrix.Identity);
            using var paint = new SKPaint { Shader = shader, FilterQuality = SKFilterQuality.Medium, IsAntialias = false };
            // A neutral body makes the native head size and collar join readable.
            using var bodyColor = !mesh["head"]!.GetValue<bool>() ? SKColorFilter.CreateColorMatrix(new float[] {
                .15f,.35f,.10f,0,70/255f, .15f,.35f,.10f,0,80/255f, .15f,.35f,.10f,0,92/255f, 0,0,0,1,0 }) : null;
            paint.ColorFilter = bodyColor;
            var triangles = mesh["triangles"]!.AsArray().Select(n => (ushort)n!.GetValue<int>()).ToArray();
            using var vertices = SKVertices.CreateCopy(SKVertexMode.Triangles, positions, texture, null, triangles);
            canvas.DrawVertices(vertices, SKBlendMode.Modulate, paint);
        }
        canvas.Flush(); return result;
    }

    /// <summary>
    /// Copies a region's pixels unchanged into an upright image of its original size. Spine packs
    /// "rotate:90" regions turned a quarter counter-clockwise, so they are turned back clockwise here.
    /// </summary>
    internal static SKBitmap Crop(SKBitmap page, Atlas.Region r)
    {
        if (r.Degrees is not (0 or 90)) throw new NotSupportedException($"Atlas region {r.Name} is rotated {r.Degrees}°.");
        if (page.ColorType != SKColorType.Bgra8888) throw new NotSupportedException("Unexpected atlas page format " + page.ColorType);
        bool turned = r.Degrees == 90;
        int packedWidth = turned ? r.Height : r.Width, packedHeight = turned ? r.Width : r.Height;
        if (r.X < 0 || r.Y < 0 || r.X + packedWidth > page.Width || r.Y + packedHeight > page.Height
            || r.OffsetX < 0 || r.OffsetY < 0 || r.OffsetX + r.Width > r.OriginalWidth || r.OffsetY + r.Height > r.OriginalHeight)
            throw new InvalidDataException($"Atlas region {r.Name} lies outside its page.");
        int width = r.OriginalWidth, height = r.OriginalHeight;
        // Spine offsets count from the bottom-left of the original image.
        int left = r.OffsetX, top = r.OriginalHeight - r.Height - r.OffsetY;
        var pixels = new byte[width * height * 4];
        var source = page.GetPixelSpan();
        int stride = page.RowBytes;
        for (int y = 0; y < r.Height; y++)
            for (int x = 0; x < r.Width; x++)
            {
                int sx = turned ? r.X + y : r.X + x, sy = turned ? r.Y + r.Width - 1 - x : r.Y + y;
                source.Slice(sy * stride + sx * 4, 4).CopyTo(pixels.AsSpan(((top + y) * width + left + x) * 4, 4));
            }
        var result = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        Marshal.Copy(pixels, 0, result.GetPixels(), pixels.Length);
        return result;
    }

    /// <summary>A Spine texture atlas read from the game's TextAsset (plain text; no Spine runtime involved).</summary>
    internal sealed class Atlas
    {
        public sealed record Page(string Name, int Width, int Height);
        public sealed record Region(string Name, string Page, int X, int Y, int Width, int Height,
            int OriginalWidth, int OriginalHeight, int OffsetX, int OffsetY, int Degrees);

        public Dictionary<string, Page> Pages { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Region> Regions { get; } = new(StringComparer.Ordinal);

        /// <summary>The named atlas containing all requested regions, or the closest match when individual callers handle missing regions.</summary>
        public static Atlas Read(UnityGame game, string name, IEnumerable<string> needed, bool allowMissing = false)
        {
            var wanted = needed.Distinct().ToArray();
            Atlas? best = null;
            string[]? missing = null;
            foreach (var (asset, file, info) in game.TextAssets())
            {
                if (!asset.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                var atlas = Parse(Encoding.UTF8.GetString(game.TextBytes(file, info)));
                var absent = wanted.Where(w => !atlas.Regions.ContainsKey(w)).ToArray();
                if (absent.Length == 0) return atlas;
                if (missing == null || absent.Length < missing.Length) { best = atlas; missing = absent; }
            }
            if (allowMissing && best != null) return best;
            throw new InvalidDataException(missing == null ? $"The game has no {name}." : $"{name} is missing regions: " + string.Join(", ", missing));
        }

        // Spine 3.8/4.x atlas text: blank-line separated pages, each a name and its fields, then
        // regions (a name line followed by "key:value" lines).
        public static Atlas Parse(string text)
        {
            var atlas = new Atlas();
            var lines = text.Replace("\r", "").Split('\n');
            int i = 0;
            string? Line() => i < lines.Length ? lines[i] : null;
            static bool Entry(string? line, out string key, out string[] values)
            {
                int colon = line?.IndexOf(':') ?? -1;
                key = colon < 0 ? "" : line![..colon].Trim();
                values = colon < 0 ? [] : line![(colon + 1)..].Split(',').Select(v => v.Trim()).ToArray();
                return colon >= 0;
            }
            // Optional header fields before the first page.
            while (Line() is { } blank && blank.Trim().Length == 0) i++;
            while (Entry(Line(), out _, out _)) i++;
            Page? page = null;
            while (Line() is { } line)
            {
                if (line.Trim().Length == 0) { page = null; i++; continue; }
                if (page == null)
                {
                    string pageName = line.Trim(); int width = 0, height = 0; i++;
                    while (Entry(Line(), out string key, out string[] v))
                    {
                        if (key == "size") { width = int.Parse(v[0]); height = int.Parse(v[1]); }
                        i++;
                    }
                    page = new Page(pageName, width, height);
                    atlas.Pages[pageName] = page;
                    continue;
                }
                string regionName = line.Trim(); i++;
                int x = 0, y = 0, w = 0, h = 0, ow = 0, oh = 0, ox = 0, oy = 0, degrees = 0;
                while (Entry(Line(), out string key, out string[] v))
                {
                    switch (key)
                    {
                        case "xy": x = int.Parse(v[0]); y = int.Parse(v[1]); break;
                        case "size": w = int.Parse(v[0]); h = int.Parse(v[1]); break;
                        case "bounds": x = int.Parse(v[0]); y = int.Parse(v[1]); w = int.Parse(v[2]); h = int.Parse(v[3]); break;
                        case "offset": ox = int.Parse(v[0]); oy = int.Parse(v[1]); break;
                        case "orig": ow = int.Parse(v[0]); oh = int.Parse(v[1]); break;
                        case "offsets": ox = int.Parse(v[0]); oy = int.Parse(v[1]); ow = int.Parse(v[2]); oh = int.Parse(v[3]); break;
                        case "rotate": degrees = v[0] == "true" ? 90 : v[0] == "false" ? 0 : int.Parse(v[0]); break;
                    }
                    i++;
                }
                if (ow == 0 && oh == 0) { ow = w; oh = h; }
                atlas.Regions.TryAdd(regionName, new Region(regionName, page.Name, x, y, w, h, ow, oh, ox, oy, degrees));
            }
            return atlas;
        }
    }
}
