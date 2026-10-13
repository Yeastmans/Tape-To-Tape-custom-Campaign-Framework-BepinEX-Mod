using SkiaSharp;

namespace CampaignCreator;

// A drawing guide from the original head's transparency, in authoring-canvas coordinates.
// No artwork or placement metadata is changed by tracing it.
internal static class HeadOutline
{
    internal static SKBitmap Trace(SKBitmap artwork)
    {
        int width = artwork.Width, height = artwork.Height;
        var pixels = artwork.Pixels;
        var visible = new bool[pixels.Length];
        for (int i = 0; i < pixels.Length; i++) visible[i] = pixels[i].Alpha > 32;
        bool Filled(int x, int y) => x >= 0 && y >= 0 && x < width && y < height && visible[y * width + x];
        var outline = new SKColor[pixels.Length];
        var color = new SKColor(105, 217, 192);
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            if (!Filled(x, y) || (Filled(x - 1, y) && Filled(x + 1, y) && Filled(x, y - 1) && Filled(x, y + 1))) continue;
            // Three pixels stay legible in the scaled card without filling the head.
            for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
            {
                int px = x + dx, py = y + dy;
                if (px >= 0 && py >= 0 && px < width && py < height) outline[py * width + px] = color;
            }
        }
        var result = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        result.Pixels = outline; return result;
    }
}
