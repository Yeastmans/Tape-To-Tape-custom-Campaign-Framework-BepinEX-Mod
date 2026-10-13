using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CampaignCore;
using SkiaSharp;

namespace GameArt;

/// <summary>One kind of artwork built from the game files into its own folder of the art cache.</summary>
public interface IArtExtractor
{
    /// <summary>Folder name inside the art cache, matching the layout the Studio used to ship.</summary>
    string Folder { get; }
    string Description { get; }
    void Extract(UnityGame game, string output, IProgress<string>? progress);
}

internal static class Png
{
    public static void Save(SKBitmap bitmap, string file)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(file);
        data.SaveTo(stream);
    }
    public static string Sha256(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
    public static bool IsBlank(SKBitmap bitmap)
    {
        uint? first = null;
        var pixels = bitmap.GetPixelSpan();
        for (int i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i + 3] == 0) continue;
            uint value = BitConverter.ToUInt32(pixels.Slice(i, 4));
            if (first == null) first = value; else if (value != first) return false;
        }
        return true;
    }
}

internal static class Maps
{
    /// <summary>A geometry/name table embedded from assets/game-art-maps (numbers only, no artwork).</summary>
    public static JsonNode Load(string name)
    {
        using var stream = typeof(Maps).Assembly.GetManifestResourceStream("GameArt.Maps." + name)
            ?? throw new FileNotFoundException("Missing embedded art map " + name);
        return JsonNode.Parse(stream) ?? throw new InvalidDataException(name + " is empty.");
    }
}

/// <summary>Each team's logo sprite, named the way LogoFiles/LogoExport look it up.</summary>
public sealed class TeamLogos : IArtExtractor
{
    public string Folder => "team-logos";
    public string Description => "Team logos";
    public void Extract(UnityGame game, string output, IProgress<string>? progress)
    {
        var teams = new JsonObject();
        foreach (var (file, _, team) in game.Behaviours("TeamData"))
        {
            // A name used by several TeamData objects (the "Random" placeholder) keeps the last one, as the game's lookup does.
            string name = team["teamName"].AsString;
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (game.Resolve(file, team["logo"]) is not { } logo) continue;
            using var image = game.Sprite(logo.File, logo.Info);
            if (Png.IsBlank(image)) throw new InvalidDataException("Blank logo: " + name);
            string png = CustomTeamIdentity.Create("logo", name) + ".png";
            Png.Save(image, Path.Combine(output, png));
            teams[name] = new JsonObject { ["file"] = png, ["sprite"] = game.Read(logo.File, logo.Info)["m_Name"].AsString, ["sha256"] = Png.Sha256(Path.Combine(output, png)) };
        }
        if (teams.Count == 0) throw new InvalidDataException("No team logos found.");
        File.WriteAllText(Path.Combine(output, "index.json"), new JsonObject { ["teams"] = teams }.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        progress?.Report($"{teams.Count} team logos");
    }
}

/// <summary>
/// Front-facing head previews: the game's own head meshes (from the head-poses map) textured with
/// the atlas pages from the player's install, drawn into a 512 × 640 canvas.
/// </summary>
public sealed class HeadPreviews : IArtExtractor
{
    public string Folder => "head-preview";
    public string Description => "Head previews";
    public void Extract(UnityGame game, string output, IProgress<string>? progress)
    {
        var rows = Maps.Load("head-poses.json").AsArray();
        var pages = new Dictionary<string, SKBitmap?>(StringComparer.OrdinalIgnoreCase);
        SKBitmap? Page(string page)
        {
            if (pages.TryGetValue(page, out var cached)) return cached;
            string prefix = (page.StartsWith("goalie", StringComparison.OrdinalIgnoreCase) ? "goaliesprites_assets_" : "skatersprites_assets_")
                + Path.GetFileNameWithoutExtension(page).ToLowerInvariant() + "_";
            return pages[page] = game.BundleTexture(prefix);
        }
        var catalog = new StringBuilder();
        var unavailable = new JsonArray();
        int done = 0;
        try
        {
            foreach (var row in rows)
            {
                string role = row!["role"]!.GetValue<string>(), skin = row["skin"]!.GetValue<string>();
                var meshes = row["meshes"]!.AsArray();
                string? missing = meshes.Select(m => m!["page"]!.GetValue<string>()).FirstOrDefault(p => Page(p) == null);
                if (missing != null) { unavailable.Add(new JsonObject { ["role"] = role, ["skin"] = skin, ["missingTexture"] = missing }); continue; }
                string file = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(role + skin))).ToLowerInvariant()[..16] + ".png";
                using var canvasBitmap = Render(meshes, p => Page(p)!);
                if (Png.IsBlank(canvasBitmap)) throw new InvalidDataException("Empty head: " + skin);
                Png.Save(canvasBitmap, Path.Combine(output, file));
                catalog.Append(role).Append('\t').Append(skin).Append('\t').Append(file).Append('\n');
                if (++done % 25 == 0) progress?.Report($"Head previews {done} / {rows.Count}");
            }
        }
        finally { foreach (var page in pages.Values) page?.Dispose(); }
        File.WriteAllText(Path.Combine(output, "catalog.tsv"), catalog.ToString());
        File.WriteAllText(Path.Combine(output, "unavailable.json"), unavailable.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        progress?.Report($"{done} head previews");
    }

    // Same framing as the original renderer: fit all vertices into 460 × 580, centred in 512 × 640, y up.
    internal static SKBitmap Render(JsonArray meshes, Func<string, SKBitmap> page)
    {
        var all = meshes.SelectMany(m => m!["vertices"]!.AsArray().Select(v => v!.GetValue<float>())).ToArray();
        float lo = float.MaxValue, hi = float.MinValue, bottom = float.MaxValue, top = float.MinValue;
        for (int i = 0; i + 1 < all.Length; i += 2) { lo = Math.Min(lo, all[i]); hi = Math.Max(hi, all[i]); bottom = Math.Min(bottom, all[i + 1]); top = Math.Max(top, all[i + 1]); }
        float scale = Math.Min(460 / (hi - lo), 580 / (top - bottom));
        float ox = (512 - (hi - lo) * scale) / 2, oy = (640 - (top - bottom) * scale) / 2;
        var result = new SKBitmap(new SKImageInfo(512, 640, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.Transparent);
        foreach (var mesh in meshes)
        {
            var atlas = page(mesh!["page"]!.GetValue<string>());
            var v = mesh["vertices"]!.AsArray().Select(x => x!.GetValue<float>()).ToArray();
            var uv = mesh["uvs"]!.AsArray().Select(x => x!.GetValue<float>()).ToArray();
            var triangles = mesh["triangles"]!.AsArray().Select(x => (ushort)x!.GetValue<int>()).ToArray();
            var positions = new SKPoint[v.Length / 2];
            var texture = new SKPoint[v.Length / 2];
            for (int i = 0; i < positions.Length; i++)
            {
                positions[i] = new SKPoint((v[2 * i] - lo) * scale + ox, (top - v[2 * i + 1]) * scale + oy);
                texture[i] = new SKPoint(uv[2 * i] * atlas.Width, uv[2 * i + 1] * atlas.Height);
            }
            using var image = SKImage.FromBitmap(atlas);
            using var shader = image.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, SKMatrix.Identity);
            using var paint = new SKPaint { Shader = shader, FilterQuality = SKFilterQuality.Medium, IsAntialias = false };
            using var vertices = SKVertices.CreateCopy(SKVertexMode.Triangles, positions, texture, null, triangles);
            canvas.DrawVertices(vertices, SKBlendMode.Modulate, paint);
        }
        canvas.Flush();
        return result;
    }
}
