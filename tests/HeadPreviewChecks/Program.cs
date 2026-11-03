using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CampaignCreator;
using SkiaSharp;

int checks = 0, failures = 0;
void Check(bool condition, string label) { checks++; Console.WriteLine((condition ? "PASS " : "FAIL ") + label); if (!condition) failures++; }
var previewType = typeof(HeadProject).Assembly.GetType("CampaignCreator.HeadProjectPreview")
    ?? Assembly.GetExecutingAssembly().GetType("CampaignCreator.HeadProjectPreview");
Check(previewType != null, "Head Editor has a renderer for edited artwork and the actual equipped helmet");
if (previewType == null) return 1;
if (args.Length < 3) throw new ArgumentException("Supply output folder, existing game-art cache, and game installation.");
string output = Path.GetFullPath(args[0]), sourceCache = Path.GetFullPath(args[1]);
if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
    throw new IOException("Use a new, empty output folder; existing files are never overwritten by these checks.");
Directory.CreateDirectory(output);
string cache = Path.Combine(output, "cache"); Directory.CreateDirectory(cache);
// A junction is unnecessary: copy only the two selected native template families.
string templates = Path.Combine(cache, "head-templates"); Directory.CreateDirectory(templates);
File.Copy(Path.Combine(sourceCache, "head-templates", "catalog.json"), Path.Combine(templates, "catalog.json"), true);
var catalog = JsonNode.Parse(File.ReadAllText(Path.Combine(templates, "catalog.json")))!["templates"]!.AsArray();
foreach (string skin in new[] { "Faces/Toronto/Mathieu", "Faces/Toronto/Mathieu_Helmet", "Faces/Anyteam/Bench_Stumple", "Faces/Anyteam/Bench_Stumple_Helmet", "Faces/Canadians/Captain", "Faces/Canadians/Captain_Helmet" })
{
    var row = catalog.Single(r => r!["skin"]!.GetValue<string>() == skin)!;
    string relative = row["folder"]!.GetValue<string>();
    foreach (string file in Directory.GetFiles(Path.Combine(sourceCache, "head-templates", relative), "*", SearchOption.AllDirectories))
    {
        string target = Path.Combine(templates, relative, Path.GetRelativePath(Path.Combine(sourceCache, "head-templates", relative), file));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target, true);
    }
}
foreach (var assembly in new[] { typeof(HeadProject).Assembly, previewType.Assembly }.Distinct())
    assembly.GetType("CampaignCreator.StudioArt")!.GetMethod("Use", BindingFlags.Static | BindingFlags.Public)!.Invoke(null, [cache]);
string? Available(HeadProject p, bool helmet) => (string?)previewType.GetMethod("Availability")!.Invoke(null, [p, helmet]);
SKBitmap Render(HeadProject p, string direction, bool helmet) => (SKBitmap)previewType.GetMethod("Render")!.Invoke(null, [p, direction, helmet])!;
void Save(SKBitmap bitmap, string file) { using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Png, 100); using var stream = File.Create(file); data.SaveTo(stream); }
byte[] Hash(SKBitmap bitmap) => SHA256.HashData(bitmap.GetPixelSpan());
var project = HeadProject.CreateFromTemplate(output, "Native preview", "Faces/Toronto/Mathieu");
var before = Directory.GetFiles(project.Folder, "*", SearchOption.AllDirectories).ToDictionary(f => f, f => SHA256.HashData(File.ReadAllBytes(f)));
Check(Available(project, false) == null, "Preserved head references support a preview before equipment extraction");
Check(Available(project, true) != null, "An old cache clearly reports missing equipped-helmet preview assets");
previewType.GetMethod("EnsureAssets")!.Invoke(null, [project, args[2]]);
Check(Available(project, true) == null, "Narrow equipment extraction extends an old cache");
foreach (string direction in new[] { "S", "N", "W", "E", "NE", "NW", "SW", "SE" })
{
    using var bare = Render(project, direction, false); using var helmet = Render(project, direction, true);
    Check(bare.Width == 512 && bare.Height == 512 && helmet.Width == 512 && helmet.Height == 512, direction + " preview has stable dimensions");
    Check(!Hash(bare).SequenceEqual(Hash(helmet)), direction + " toggle changes the rendered equipment");
    Save(bare, Path.Combine(output, direction + "-bare.png")); Save(helmet, Path.Combine(output, direction + "-helmet.png"));
}
Check(before.All(p => SHA256.HashData(File.ReadAllBytes(p.Key)).SequenceEqual(p.Value)), "Rendering and extraction do not change project PNGs or reference files");
// Moving the artwork within a transparent canvas must preserve its runtime fit.
var front = project.Images.Single(i => i.Group == "Head" && i.Direction == "S");
using var original = Render(project, "S", false);
using (var png = SKBitmap.Decode(project.FilePath(front.File)))
using (var shifted = new SKBitmap(512, 512, SKColorType.Bgra8888, SKAlphaType.Premul))
{
    using var canvas = new SKCanvas(shifted); canvas.Clear(SKColors.Transparent); canvas.DrawBitmap(png, 7, -4);
    Save(shifted, project.FilePath(front.File));
}
using var aligned = Render(project, "S", false);
Save(aligned, Path.Combine(output, "S-shifted-aligned.png"));
int shiftedAlphaDifference = original.Pixels.Zip(aligned.Pixels).Count(p => Math.Abs(p.First.Alpha - p.Second.Alpha) > 16);
Console.WriteLine("Translation alpha pixels differing by more than16: " + shiftedAlphaDifference);
Check(shiftedAlphaDifference < 16, "Preview applies the game's visible-artwork alignment instead of canvas padding");
var empty = HeadProject.CreateFromTemplate(output, "Empty preview", "Faces/Toronto/Mathieu", true);
using (var blank = Render(empty, "S", false)) Check(blank.Pixels.All(p => p.Alpha == 0), "Empty directional PNGs render transparently");
using (var equipped = Render(empty, "S", true)) Check(equipped.Pixels.Any(p => p.Alpha != 0), "Helmet remains a separate preview layer when the head is empty");
Check(!Directory.EnumerateFiles(empty.Folder, "*", SearchOption.AllDirectories).Any(f => f.Contains("equipped", StringComparison.OrdinalIgnoreCase)), "Equipment overlay is never exported into head artwork");
var stu = HeadProject.CreateFromTemplate(output, "Stu preview", "Faces/Anyteam/Bench_Stumple_Helmet");
using (var arbitrary = Render(stu, "SW", false)) Check(arbitrary.Pixels.Any(p => p.Alpha != 0), "An arbitrary native head uses its own saved attachment geometry");
var layered = HeadProject.CreateFromTemplate(output, "Layered preview", "Faces/Canadians/Captain");
Check(layered.Images.Count(i => i.Group == "Head") > 5, "Extra-layer case exercises a native head with more than five images");
foreach (string direction in new[] { "S", "N", "W", "E", "NE", "NW", "SW", "SE" })
{
    using var bare = Render(layered, direction, false); using var helmet = Render(layered, direction, true);
    Check(bare.Pixels.Any(p => p.Alpha != 0) && helmet.Pixels.Any(p => p.Alpha != 0), direction + " renders the extra native layer with native draw order");
}
using (var sheet = new SKBitmap(1024, 560, SKColorType.Bgra8888, SKAlphaType.Premul))
{
    using var canvas = new SKCanvas(sheet); canvas.Clear(new SKColor(36, 47, 61));
    using var text = new SKPaint { Color = SKColors.White, TextSize = 18, IsAntialias = true };
    int index = 0;
    foreach (string direction in new[] { "S", "SW", "W", "NW", "N", "NE", "E", "SE" })
    {
        using var png = SKBitmap.Decode(Path.Combine(output, direction + "-helmet.png"));
        int x = index % 4 * 256, y = index / 4 * 280;
        canvas.DrawBitmap(png, new SKRect(x, y, x + 256, y + 256)); canvas.DrawText(direction, x + 12, y + 275, text); index++;
    }
    Save(sheet, Path.Combine(output, "helmet-contact-sheet.png"));
}
Console.WriteLine($"{checks - failures}/{checks} renderer checks passed. PNGs are diagnostic renders, not gameplay acceptance.");
return failures == 0 ? 0 : 1;
