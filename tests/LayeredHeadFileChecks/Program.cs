using System.Text.Json;
using System.Runtime.InteropServices;
using CampaignCreator;
using SkiaSharp;

if (args.Length != 1)
    throw new ArgumentException("Pass an empty fixture output directory.");
string output = Path.GetFullPath(args[0]);
if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
    throw new IOException("Use a new, empty output folder; existing files are never overwritten by these checks.");
Directory.CreateDirectory(output);
int checks = 0;
var fixtures = new List<string>();

void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    checks++;
}

void Reject<T>(Action action, string name) where T : Exception
{
    try { action(); }
    catch (T) { checks++; return; }
    throw new InvalidOperationException(name);
}

SKBitmap Bitmap(int width, int height, byte[]? rgba = null)
{
    var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
    bitmap.Erase(SKColors.Transparent);
    if (rgba != null) Marshal.Copy(rgba, 0, bitmap.GetPixels(), rgba.Length);
    return bitmap;
}

void Export(string name, IReadOnlyList<LayeredPsd.Layer> layers)
{
    string path = Path.Combine(output, name + ".psd");
    LayeredPsd.Write(path, layers);
    Check(File.Exists(path), name + ": Write must produce the PSD file.");
    Check(new FileInfo(path).Length > 26, name + ": PSD must contain image data.");
    fixtures.Add(name);
}

try
{
    using var basis = Bitmap(3, 2,
    [
        200, 50, 10, 255, 40, 80, 120, 128, 12, 34, 56, 0,
        10, 20, 30, 255, 80, 120, 160, 64, 240, 120, 60, 128
    ]);
    using var face = Bitmap(3, 2,
    [
        20, 220, 40, 128, 0, 0, 0, 0, 1, 250, 128, 255,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0
    ]);
    using var hair = Bitmap(3, 2,
    [
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 255, 255, 200, 40, 80, 128, 0, 0, 0, 0
    ]);
    using var guides = Bitmap(3, 2);
    guides.Erase(SKColors.Magenta);
    LayeredPsd.Layer[] stack = [new("Base head", basis), new("Face", face), new("Hair", hair), new("Guides", guides, false)];
    byte[] original = basis.GetPixelSpan().ToArray();
    Export("four-layers", stack);
    Check(basis.GetPixelSpan().SequenceEqual(original), "Export must not alter caller-owned pixels.");
    Export("visible-guides", [stack[0], stack[1], stack[2], stack[3] with { Visible = true }]);
    Export("all-hidden", stack.Select(layer => layer with { Visible = false }).ToArray());

    using var clear = Bitmap(1, 1, [22, 44, 66, 0]);
    Export("transparent-color", [new("Face – 顔 😀", clear)]);
    Export("long-name", [new(new string('a', 300) + "顔", clear)]);
    using var premultiplied = new SKBitmap(1, 1, SKColorType.Bgra8888, SKAlphaType.Premul);
    Marshal.Copy(new byte[] { 10, 20, 30, 85 }, 0, premultiplied.GetPixels(), 4);
    Export("premultiplied-bgra", [new("Base head", premultiplied)]);

    // Literal spans, repeated runs, row boundaries, and the PackBits 128-byte limit.
    using var stripes = Bitmap(257, 3);
    byte[] pixels = new byte[257 * 3 * 4];
    for (int y = 0; y < 3; y++)
        for (int x = 0; x < 257; x++)
        {
            int offset = (y * 257 + x) * 4;
            pixels[offset] = (byte)(y == 0 ? x % 256 : y == 1 ? x < 127 ? 7 : x < 130 ? 8 : 9 : x % 2);
            pixels[offset + 1] = (byte)(x % 5);
            pixels[offset + 2] = (byte)(255 - x % 256);
            pixels[offset + 3] = 255;
        }
    Marshal.Copy(pixels, 0, stripes.GetPixels(), pixels.Length);
    Export("packbits-boundaries", [new("Patterns", stripes)]);

    using var large = Bitmap(512, 512);
    using var empty = Bitmap(512, 512);
    using var largeGuides = Bitmap(512, 512);
    large.Erase(new SKColor(211, 177, 134, 255));
    largeGuides.Erase(SKColors.Magenta);
    Export("starter-512", [new("Base head", large), new("Face", empty), new("Hair", empty), new("Guides", largeGuides, false)]);
    Check(new FileInfo(Path.Combine(output, "starter-512.psd")).Length < 128 * 1024,
        "Uniform 512px layers should be compact enough for sharing.");

    string preserved = Path.Combine(output, "preserved.psd");
    byte[] sentinel = [11, 22, 33, 44, 55];
    File.WriteAllBytes(preserved, sentinel);
    Reject<ArgumentException>(() => LayeredPsd.Write(preserved, []), "Empty layers must be rejected.");
    Reject<ArgumentNullException>(() => LayeredPsd.Write(preserved, null!), "Null layers must be rejected.");
    Reject<ArgumentException>(() => LayeredPsd.Write(" ", stack), "Blank destination must be rejected.");
    Reject<ArgumentException>(() => LayeredPsd.Write(preserved, [stack[0], new("Wrong size", clear)]), "Mismatched dimensions must be rejected.");
    Reject<ArgumentException>(() => LayeredPsd.Write(preserved, [new(null!, basis)]), "Null names must be rejected.");
    Reject<ArgumentException>(() => LayeredPsd.Write(preserved, [new("Null bitmap", null!)]), "Null bitmaps must be rejected.");
    Reject<ArgumentException>(() => LayeredPsd.Write(preserved, [null!]), "Null layers must be rejected.");
    using var zero = new SKBitmap();
    Reject<ArgumentException>(() => LayeredPsd.Write(preserved, [new("Empty", zero)]), "Empty bitmaps must be rejected.");
    using var tooWide = Bitmap(30001, 1);
    Reject<ArgumentException>(() => LayeredPsd.Write(preserved, [new("Too wide", tooWide)]), "PSD v1 cannot exceed 30000 pixels.");
    Reject<ArgumentException>(() => LayeredPsd.Write(preserved, Enumerable.Repeat(stack[0], 32768).ToArray()), "Layer count must fit a signed 16-bit PSD count.");
    Check(File.ReadAllBytes(preserved).SequenceEqual(sentinel), "Invalid input must preserve an existing destination.");

    if (OperatingSystem.IsWindows())
    {
        using (var locked = new FileStream(preserved, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            bool failed = false;
            try { LayeredPsd.Write(preserved, stack); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { failed = true; }
            Check(failed, "A locked destination must fail safely.");
        }
        Check(File.ReadAllBytes(preserved).SequenceEqual(sentinel), "Failed replacement must preserve existing bytes.");
    }
    Check(Directory.GetFiles(output, "*.tmp").Length == 0, "Failed writes must remove temporary files.");
    LayeredPsd.Write(preserved, stack);
    Check(new FileInfo(preserved).Length > sentinel.Length, "Successful writes must replace existing files.");
    Check(Directory.GetFiles(output, "*.tmp").Length == 0, "Successful writes must leave no temporary files.");
    File.WriteAllText(Path.Combine(output, "dotnet-results.json"), JsonSerializer.Serialize(new { checks, fixtures }));
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error.ToString());
    return 1;
}
