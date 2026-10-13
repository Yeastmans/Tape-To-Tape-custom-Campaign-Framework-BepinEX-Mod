using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Atlas = GameArt.HeadTemplate.Atlas;

namespace GameArt;

/// <summary>Transparent map-node and stadium regions, masked by the game's attachment geometry.</summary>
public sealed class NodeImages : IArtExtractor
{
    public string Folder => "node-images";
    public string Description => "Map nodes and stadiums";

    public void Extract(UnityGame game, string output, IProgress<string>? progress)
    {
        var meshes = Maps.Load("node-meshes.json")["meshes"]!.AsArray()
            .GroupBy(m => m!["region"]!.GetValue<string>()).ToDictionary(g => g.Key, g => g.ToArray());
        // Unity asset-file numbers can change between game builds. Several atlases
        // share this name, so identify the map by its attachment regions instead.
        var candidates = game.TextAssets().Where(t => t.Name == "skeleton.atlas")
            .Select(t => (Source: t, Atlas: Atlas.Parse(Encoding.UTF8.GetString(game.TextBytes(t.File, t.Info)))))
            .Where(t => meshes.Keys.All(t.Atlas.Regions.ContainsKey)).ToArray();
        if (candidates.Length != 1)
            throw new InvalidDataException($"Expected one map-node atlas containing all {meshes.Count} attachment regions; found {candidates.Length}.");
        var (source, atlas) = candidates[0];
        string regions = Path.Combine(output, "regions");
        Directory.CreateDirectory(regions);
        var manifest = new JsonArray();
        foreach (var page in atlas.Pages.Values)
        {
            var texture = source.File.file.GetAssetsOfType(AssetClassID.Texture2D)
                .Single(i => game.Read(source.File, i)["m_Name"].AsString == Path.GetFileNameWithoutExtension(page.Name));
            var pixels = RawTexture.Decode(game, source.File, texture);
            if (pixels.Width != page.Width || pixels.Height != page.Height)
                throw new InvalidDataException($"Map atlas {page.Name} dimensions do not match its texture.");
            foreach (var region in atlas.Regions.Values.Where(r => r.Page == page.Name))
            {
                if (!meshes.TryGetValue(region.Name, out var shapes))
                    throw new InvalidDataException("No map attachment geometry for " + region.Name);
                bool turned = region.Degrees is 90 or 270;
                int w = turned ? region.Height : region.Width, h = turned ? region.Width : region.Height;
                if (region.X < 0 || region.Y < 0 || region.X + w > pixels.Width || region.Y + h > pixels.Height)
                    throw new InvalidDataException("Map region outside its texture: " + region.Name);
                byte[] crop = pixels.Crop(region.X, region.Y, w, h).ToArray();
                var mask = new byte[w * h];
                foreach (var mesh in shapes)
                {
                    if (mesh!["quad"]?.GetValue<bool>() == true) { Array.Fill(mask, (byte)255); continue; }
                    var uv = mesh["regionUVs"]!.AsArray().Select(v => v!.GetValue<double>()).ToArray();
                    var points = new double[uv.Length];
                    for (int i = 0; i < uv.Length; i += 2)
                    {
                        // Place normalized mesh coordinates onto the current atlas, including trim and packing rotation.
                        double u = uv[i], v = uv[i + 1], x, y;
                        switch (region.Degrees)
                        {
                            case 90:
                                x = v * region.OriginalHeight - (region.OriginalHeight - region.OffsetY - region.Height);
                                y = (1 - u) * region.OriginalWidth - (region.OriginalWidth - region.OffsetX - region.Width);
                                break;
                            case 180:
                                x = (1 - u) * region.OriginalWidth - (region.OriginalWidth - region.OffsetX - region.Width);
                                y = (1 - v) * region.OriginalHeight - region.OffsetY;
                                break;
                            case 270:
                                x = (1 - v) * region.OriginalHeight - region.OffsetY;
                                y = u * region.OriginalWidth - region.OffsetX;
                                break;
                            default:
                                x = u * region.OriginalWidth - region.OffsetX;
                                y = v * region.OriginalHeight - (region.OriginalHeight - region.OffsetY - region.Height);
                                break;
                        }
                        // The original renderer stores atlas UVs in float32 before rasterizing the mask.
                        points[i] = (double)(float)((region.X + x) / page.Width) * pixels.Width - region.X;
                        points[i + 1] = (double)(float)((region.Y + y) / page.Height) * pixels.Height - region.Y;
                    }
                    var triangles = mesh["triangles"]!.AsArray().Select(v => v!.GetValue<int>()).ToArray();
                    for (int t = 0; t < triangles.Length; t += 3)
                    {
                        double[] triangle = [points[2 * triangles[t]], points[2 * triangles[t] + 1],
                            points[2 * triangles[t + 1]], points[2 * triangles[t + 1] + 1],
                            points[2 * triangles[t + 2]], points[2 * triangles[t + 2] + 1]];
                        Pillow.FillPolygon(mask, w, h, triangle, 255);
                    }
                }
                for (int i = 0; i < mask.Length; i++) crop[i * 4 + 3] = (byte)(crop[i * 4 + 3] * mask[i] / 255);
                var upright = Pillow.Rotate(Pixels.TopDown(crop, w, h), -region.Degrees);
                string name = Regex.Replace(region.Name, "[^A-Za-z0-9_.-]+", "_") + ".png";
                using var bitmap = upright.ToBitmap();
                Png.Save(bitmap, Path.Combine(regions, name));
                manifest.Add(new JsonObject { ["region"] = region.Name, ["file"] = "regions/" + name,
                    ["width"] = upright.Width, ["height"] = upright.Height,
                    ["atlasBounds"] = new JsonArray(region.X, region.Y, w, h), ["rotation"] = region.Degrees });
            }
        }
        if (manifest.Count == 0) throw new InvalidDataException("The game has no map node regions.");
        File.WriteAllText(Path.Combine(output, "manifest.json"), new JsonObject {
            ["source"] = Path.Combine(game.DataFolder, "data.unity3d"), ["assetFile"] = source.File.name,
            ["atlasPathId"] = source.Info.PathId, ["regions"] = manifest
        }.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        progress?.Report($"{manifest.Count} map node and stadium regions");
    }
}
