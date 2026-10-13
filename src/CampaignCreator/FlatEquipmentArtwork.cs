using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace CampaignCreator;

internal sealed class FlatEquipmentArtwork : IDisposable
{
    private sealed record Entry(string role, string skin, string view, string part, string file, bool custom);
    // Built from the player's install (StudioArt); empty until the art cache exists.
    private static Entry[]? entries;
    private static Entry[] Entries => entries ??= Load();
    private readonly Dictionary<string, Bitmap> cache = new();
    private static Entry[] Load()
    {
        if (StudioArt.Path("flat-equipment", "index.json") is not { } index) return [];
        using var stream = File.OpenRead(index);
        return JsonSerializer.Deserialize<Entry[]>(stream) ?? [];
    }
    /// <summary>Re-reads the index after the art cache is (re)built.</summary>
    internal static void Reload() => entries = null;
    internal bool UsesBackArtwork(string role, string part, string skin, bool back) =>
        !back && role == "goalie"
        && (skin.Equals("Body/Toronto", StringComparison.OrdinalIgnoreCase) && part is "jersey" or "pants"
            || skin.Equals("Body_Pads/Golfers", StringComparison.OrdinalIgnoreCase) && part == "socks")
        && !Entries.Any(e => e.role == role && e.part == part && e.view == "S" && e.skin.Equals(skin, StringComparison.OrdinalIgnoreCase))
        && Entries.Any(e => e.role == role && e.part == part && e.view == "N" && e.skin.Equals(skin, StringComparison.OrdinalIgnoreCase));
    internal Bitmap? Get(string role, string part, string skin, bool back, Func<string, string> color)
    {
        string view = back || UsesBackArtwork(role, part, skin, back) ? "N" : "S";
        var candidates = Entries.Where(e => e.role == role && e.part == part && e.view == view).ToArray();
        var entry = candidates.FirstOrDefault(e => e.skin.Equals(skin, StringComparison.OrdinalIgnoreCase));
        if (entry == null && EquipmentStylePicker.IsCustom(skin)) entry = candidates.FirstOrDefault(e => e.custom);
        if (entry == null && !skin.Contains('/'))
        {
            var matches = candidates.Where(e => e.skin.Split('/').Last().Equals(skin, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length == 1) entry = matches[0];
        }
        if (entry == null) return null;
        string palettePart = part switch { "sleeves" => "bicep", "blocker" when role == "goalie" => "skates", _ => part };
        var palette = Enumerable.Range(0, 3).Select(i => Color.TryParse(color(palettePart + i), out var selected) ? (Color?)selected : null).ToArray();
        string key = entry.file + (entry.custom ? string.Join("", palette.Select(c => c?.ToString() ?? "unset")) : "");
        if (cache.TryGetValue(key, out var image)) return image;
        if (StudioArt.Bitmap("flat-equipment", entry.file) is not { } original) return null;
        if (!entry.custom) return cache[key] = original;
        int width = original.PixelSize.Width, height = original.PixelSize.Height;
        // Render through a known BGRA format before replacing the game's RGB mask channels.
        using var normalized = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96,96));
        using (var dc = normalized.CreateDrawingContext()) dc.DrawImage(original, new Rect(0,0,width,height));
        original.Dispose();
        var result = new WriteableBitmap(new PixelSize(width,height), new Vector(96,96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var pixels = result.Lock())
        {
            normalized.CopyPixels(new PixelRect(0,0,width,height), pixels.Address, pixels.RowBytes*height, pixels.RowBytes);
            var bytes = new byte[pixels.RowBytes*height]; Marshal.Copy(pixels.Address,bytes,0,bytes.Length);
            for (int y=0;y<height;y++) for (int x=0;x<width;x++)
            {
                int n=y*pixels.RowBytes+x*4; double a=bytes[n+3]/255d;
                if (a==0) continue;
                double r=bytes[n+2]/a,g=bytes[n+1]/a,b=bytes[n]/a;
                // Red: primary, yellow: secondary, magenta: accent. Green is
                // the game's fixed fourth channel (laces/blades/trim by part).
                int channel = r>g*1.25 && r>b*1.25 ? 0 : r>80 && g>80 && b<Math.Min(r,g)*.6 ? 1 : r>80 && b>80 && g<Math.Min(r,b)*.6 ? 2 : -1;
                if (channel<0 && g>r*1.25 && g>b*1.25) channel=part=="skates"?1:2;
                if (channel<0) continue;
                double shade=Math.Max(r,Math.Max(g,b))/255d;
                var c=palette[channel] ?? (((x/8+y/8)%2==0) ? UnsetColor.Purple : Colors.Black); bytes[n]=(byte)Math.Clamp(c.B*a*shade,0,255); bytes[n+1]=(byte)Math.Clamp(c.G*a*shade,0,255); bytes[n+2]=(byte)Math.Clamp(c.R*a*shade,0,255);
            }
            Marshal.Copy(bytes,0,pixels.Address,bytes.Length);
        }
        // Colour dragging must not retain an unbounded set of raster copies.
        if (cache.Count>80) { foreach(var bitmap in cache.Values) bitmap.Dispose(); cache.Clear(); }
        return cache[key]=result;
    }
    public void Dispose() { foreach (var image in cache.Values) image.Dispose(); cache.Clear(); }
}

