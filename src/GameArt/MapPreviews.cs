namespace GameArt;

/// <summary>Small map backgrounds decoded directly from the player's installed game sprites.</summary>
public sealed class MapPreviews : IArtExtractor
{
    public string Folder => "map-preview";
    public string Description => "Map backgrounds";

    public void Extract(UnityGame game, string output, IProgress<string>? progress)
    {
        string[] names = ["Rogue2_Maptest", "Level2_background", "Stage3Background"];
        var sprites = game.Sprites().Where(s => names.Contains(s.Name)).ToDictionary(s => s.Name);
        for (int act = 1; act <= names.Length; act++)
        {
            if (!sprites.TryGetValue(names[act - 1], out var entry))
                throw new InvalidDataException("Missing map background: " + names[act - 1]);
            var sprite = game.Read(entry.File, entry.Info);
            var rd = sprite["m_RD"];
            if ((rd["settingsRaw"].AsUInt & 1) != 0)
                throw new NotSupportedException("Packed map background: " + entry.Name);
            var target = game.Resolve(entry.File, rd["texture"])
                ?? throw new InvalidDataException("No texture for map background: " + entry.Name);
            var pixels = RawTexture.Decode(game, target.File, target.Info);
            var rect = rd["textureRect"];
            int x = (int)MathF.Round(rect["x"].AsFloat), y = (int)MathF.Round(rect["y"].AsFloat);
            int w = (int)MathF.Round(rect["width"].AsFloat), h = (int)MathF.Round(rect["height"].AsFloat);
            var crop = pixels.Crop(x, pixels.Height - y - h, w, h);
            var preview = Pillow.Thumbnail(crop, 2000, 500, Pillow.Filter.Lanczos);
            using var bitmap = preview.ToBitmap();
            Png.Save(bitmap, Path.Combine(output, $"act-{act}.png"));
            progress?.Report($"Map background {act} / 3");
        }
    }
}
