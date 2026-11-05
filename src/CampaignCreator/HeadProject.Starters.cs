using SkiaSharp;

namespace CampaignCreator;

public enum HeadBlankStyle { PlainHeadWithGuides, TransparentWithGuides, TransparentOnly }

public sealed partial class HeadProject
{
    public const string BasicStarterSkin = "Faces/Toronto/Mathieu";
    public HeadBlankStyle? BlankStyle { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public bool StarterHasGuides => BlankStyle != HeadBlankStyle.TransparentOnly;

    public static HeadProject CreateBlank(string parent, string name, HeadBlankStyle style)
        => CreateFromTemplate(parent, name, BasicStarterSkin, fromScratch: true, blankStyle: style);

    private void PrepareBlankStarter(HeadBlankStyle style)
    {
        BlankStyle = style;
        if (style == HeadBlankStyle.PlainHeadWithGuides)
            foreach (var item in Images)
            {
                using var original = NativeCanvas(item);
                using var blankHead = HeadStarterArt.PlainHead(original, item.Direction);
                WritePng(blankHead, FilePath(item.File));
            }
        EnsureDrawingTemplates();
    }

    private SKBitmap NativeCanvas(HeadImage item)
    {
        using var original = SKBitmap.Decode(FilePath(item.SourceFile)) ?? throw new InvalidDataException("The native guide image is missing.");
        var result = new SKBitmap(item.Width, item.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(result); canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { FilterQuality = SKFilterQuality.High };
        canvas.DrawBitmap(original, new SKRect(item.Left, item.Top, item.Left + item.ContentWidth, item.Top + item.ContentHeight), paint);
        return result;
    }

    public SKBitmap DrawingGuide(HeadImage item)
    {
        if (BlankStyle == null) return NativeOutline(item);
        using var original = NativeCanvas(item);
        using var shape = HeadStarterArt.PlainHead(original, item.Direction);
        return HeadStarterArt.Guide(shape);
    }

    public string DrawingTemplate(HeadImage item)
    {
        if (!Images.Contains(item)) throw new ArgumentException("Choose an image in this project.");
        return FilePath("drawing/" + item.Group + "/" + DrawingName(item) + ".psd");
    }
    private static string DrawingName(HeadImage item) => new string((item.Label.Replace(" · ", " - ") + " [" + Path.GetFileNameWithoutExtension(item.File) + "]")
        .Where(c => !Path.GetInvalidFileNameChars().Contains(c) && c != '/' && c != '\\').ToArray());

    // Carry the known editable source files when installing a project from another
    // folder. Do not sweep unrelated files or drawing-app backups into the library.
    private IEnumerable<string> DrawingFiles()
    {
        foreach (var item in Images)
        {
            yield return Path.GetRelativePath(Folder, DrawingTemplate(item));
            foreach (string layer in new[] { "Base head", "Face", "Hair", "Guides" })
                yield return "drawing-layers/" + item.Group + "/" + DrawingName(item) + "/" + layer + ".png";
        }
    }

    // Existing layered documents are the artist's source files. Opening or
    // refreshing a project never regenerates them over that work.
    public string EnsureDrawingTemplates()
    {
        foreach (var item in Images)
        {
            string target = DrawingTemplate(item);
            if (File.Exists(target)) continue;
            using var artwork = SKBitmap.Decode(FilePath(item.File)) ?? throw new InvalidDataException("Cannot read " + item.Label);
            using var empty = new SKBitmap(item.Width, item.Height, SKColorType.Rgba8888, SKAlphaType.Premul); empty.Erase(SKColors.Transparent);
            using var guide = DrawingGuide(item);
            var layers = new List<LayeredPsd.Layer> {
                new("Base head", artwork), new("Face", empty), new("Hair", empty)
            };
            if (StarterHasGuides) layers.Add(new("GUIDES - hide before PNG export", guide, Visible: false));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            LayeredPsd.Write(target, layers);
            string pngFolder = FilePath("drawing-layers/" + item.Group + "/" + DrawingName(item));
            Directory.CreateDirectory(pngFolder);
            WriteNew(artwork, Path.Combine(pngFolder, "Base head.png"));
            WriteNew(empty, Path.Combine(pngFolder, "Face.png")); WriteNew(empty, Path.Combine(pngFolder, "Hair.png"));
            if (StarterHasGuides) WriteNew(guide, Path.Combine(pngFolder, "Guides.png"));
        }
        return FilePath("drawing");
        static void WriteNew(SKBitmap bitmap, string path) { if (!File.Exists(path)) WritePng(bitmap, path); }
    }

    private void WriteDrawingInstructions()
    {
        File.WriteAllText(FilePath("README.txt"), "Blank head starter: " + Name + "\n\n"
            + "1. Open a PSD under drawing in an editor that supports Photoshop layers. Separate PNG layers are under drawing-layers if your app does not open PSDs.\n"
            + "2. Paint on the Face and Hair layers. The Base head layer is editable too. Guides are a separate hidden layer: show them while drawing, then hide them before exporting a PNG.\n"
            + "3. Save a transparent PNG with the original 512 x 512 canvas intact. Use Import PNGs or Replace PNG in Studio; filenames such as Front [S].png are accepted. Keep fitted exports in their Helmet folder. If you save directly over the matching project PNG listed below, use Refresh PNGs. Keep your PSD as the editable source.\n"
            + "Full-canvas PNGs preserve exact placement. Original-size game PNGs use each view's own offset. Cropped exports are aligned to the saved reference; their original offset cannot always be recovered, so check the preview.\n\n"
            + "Head files are the normal views. Helmet files are the face/hair fitted under an equipped game helmet, not painted helmet artwork. Edit both sets when using helmets. The game supplies its real helmet and normal equipment behaviour.\n"
            + "The preview helmet toggle changes only the preview. It never paints a helmet into the exported head.\n"
            + "Existing PSD files are kept when you reopen or refresh. To make a new starter, create a new named head.\n\n"
            + string.Join("\n", Images.Select(i => i.Group + " / " + i.Label + " -> " + i.File))
            + "\n\nAdd to player head options when the required views are ready, select it in the player editor, and restart the game.\n");
    }
}

internal static class HeadStarterArt
{
    private static SKRectI Bounds(SKBitmap image)
    {
        int left = image.Width, top = image.Height, right = 0, bottom = 0;
        var pixels = image.Pixels;
        for (int y = 0; y < image.Height; y++) for (int x = 0; x < image.Width; x++)
            if (pixels[y * image.Width + x].Alpha > 32) { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x + 1); bottom = Math.Max(bottom, y + 1); }
        if (right <= left || bottom <= top) throw new InvalidDataException("The head layout has no visible outline.");
        return new(left, top, right, bottom);
    }

    internal static SKBitmap PlainHead(SKBitmap native, string direction)
    {
        var bounds = Bounds(native);
        var result = new SKBitmap(native.Width, native.Height, SKColorType.Rgba8888, SKAlphaType.Premul); result.Erase(SKColors.Transparent);
        using var canvas = new SKCanvas(result);
        canvas.Translate(bounds.Left + 3, bounds.Top + 3);
        canvas.Scale((bounds.Width - 6) / 100f, (bounds.Height - 6) / 100f);
        if (direction is "E" or "SE" or "NW") { canvas.Translate(100, 0); canvas.Scale(-1, 1); }
        string view = direction switch { "W" or "E" => "side", "SW" or "SE" => "front-quarter", "NE" or "NW" => "back-quarter", "N" or "N2" => "back", _ => "front" };
        // Original vector contours: a blank drawing base with anatomical planes,
        // distinct rotation, ears and a collar anchor; no eyes, mouth or hair.
        string silhouette = view switch
        {
            "side" => "M56 1 C82 0 98 17 97 39 C97 57 88 67 75 75 C71 83 72 93 74 100 L45 100 L45 82 C35 84 26 80 23 74 L21 64 L23 58 L15 56 Q5 54 7 50 L22 37 L25 27 C27 10 39 1 56 1 Z",
            "front-quarter" => "M53 1 C78 0 93 13 94 34 L90 43 C100 40 100 55 92 62 L84 64 Q80 75 61 84 L63 100 L34 100 L36 84 Q18 81 13 68 L11 59 Q2 55 7 49 L15 40 L17 27 C19 10 32 1 53 1 Z",
            "back-quarter" => "M44 1 C68 0 83 14 84 32 L82 39 L95 49 Q100 54 89 57 L87 67 Q83 78 68 82 L67 100 L35 100 L31 80 C13 73 3 61 2 43 C0 17 17 1 44 1 Z",
            "back" => "M50 1 C75 1 88 15 88 37 Q97 35 98 44 Q99 56 89 61 Q85 73 70 79 L68 100 L32 100 L30 79 Q15 73 11 61 Q1 56 2 44 Q3 35 12 37 C12 15 25 1 50 1 Z",
            _ => "M50 1 C73 1 86 13 87 34 L86 41 Q97 36 98 45 Q99 58 88 61 L83 70 Q78 80 65 85 L66 100 L34 100 L35 85 Q22 80 17 70 L12 61 Q1 58 2 45 Q3 36 14 41 L13 34 C14 13 27 1 50 1 Z"
        };
        using var path = SKPath.ParseSvgPathData(silhouette);
        var skin = new SKColor(218, 178, 139); var shade = new SKColor(183, 137, 102);
        var light = new SKColor(239, 205, 167); var outline = new SKColor(91, 64, 47);
        using var paint = new SKPaint { Color = skin, IsAntialias = true, StrokeJoin = SKStrokeJoin.Round, StrokeCap = SKStrokeCap.Round };
        canvas.DrawPath(path, paint);
        canvas.Save(); canvas.ClipPath(path, antialias: true);
        if (view == "side")
        {
            Shape("M77 4 C101 19 103 49 81 67 L67 77 L74 100 L57 100 L46 82 Q65 72 66 57 C79 38 82 19 77 4 Z", shade);
            Shape("M41 9 Q27 20 28 36 L16 51 L23 52 Q36 27 54 17 Z", light);
            Ear("M68 40 C81 35 85 48 78 59 Q74 66 67 61 C61 57 60 45 68 40 Z", "M68 47 Q77 41 78 49 Q78 54 72 56 L72 60 M69 49 Q66 53 70 55");
            Line("M27 73 Q36 80 47 77 L62 67 M48 84 L53 96 M69 79 L65 95");
        }
        else if (view == "front-quarter")
        {
            Shape("M72 5 Q98 23 88 43 L82 61 Q76 78 57 84 L63 100 L49 100 L44 84 Q67 73 70 57 C76 38 76 19 72 5 Z", shade);
            Shape("M35 9 Q21 18 22 38 L16 49 Q21 52 23 46 Q29 24 45 14 Z", light);
            Ear("M84 42 Q97 38 97 49 Q96 59 87 62 Q80 61 80 53 Q78 47 84 42 Z", "M85 48 Q93 42 93 49 Q93 54 88 55 L87 59 M84 49 Q82 53 86 55");
            Line("M21 71 Q34 82 48 80 L66 73 M38 86 L42 97 M59 85 L54 96");
        }
        else if (view == "back-quarter")
        {
            Shape("M66 7 Q85 19 81 39 L96 52 L87 59 L86 68 Q80 79 67 81 L59 71 Q69 54 63 40 Z", light);
            Shape("M7 39 Q14 68 37 73 L43 100 L30 100 L28 78 Q3 69 1 48 Z", shade);
            Shape("M58 66 Q70 76 69 82 L67 100 L52 100 L49 81 Z", shade);
            Ear("M65 44 C76 39 82 47 78 57 Q76 66 69 65 C64 64 59 51 65 44 Z", "M66 50 Q73 44 75 50 L72 56 Q67 55 69 60");
            Line("M22 70 Q42 82 57 73 M34 83 L40 97 M62 84 L57 96");
        }
        else if (view == "back")
        {
            Shape("M76 8 Q93 25 87 49 Q86 69 69 78 L68 100 L56 100 L58 79 Q77 58 76 8 Z", shade);
            Shape("M28 9 Q13 22 16 43 L21 54 Q19 27 39 12 Z", light);
            Shape("M30 74 Q50 85 70 74 L66 89 Q50 95 34 89 Z", shade);
            Line("M10 44 Q5 45 8 53 L13 57 M90 44 Q95 45 92 53 L87 57 M27 70 Q50 80 73 70 M35 80 L40 96 M65 80 L60 96");
        }
        else
        {
            Shape("M76 9 Q91 25 84 45 L81 67 Q77 78 64 84 L66 100 L57 100 L56 85 Q73 68 73 50 Z", shade);
            Shape("M27 10 Q12 25 18 43 L23 49 Q21 27 39 12 Z", light);
            Shape("M32 78 Q50 88 68 78 L64 90 Q50 96 36 90 Z", shade);
            Line("M12 45 Q5 41 7 49 Q7 54 13 56 L14 51 M88 45 Q95 41 93 49 Q93 54 87 56 L86 51 M25 72 Q35 82 50 83 Q65 82 75 72 M37 87 L42 97 M63 87 L58 97");
        }
        canvas.Restore();
        paint.Color = outline; paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = 1.2f; canvas.DrawPath(path, paint);
        return result;

        void Shape(string svg, SKColor color)
        {
            using var shape = SKPath.ParseSvgPathData(svg); paint.Color = color; paint.Style = SKPaintStyle.Fill; canvas.DrawPath(shape, paint);
        }
        void Line(string svg)
        {
            using var line = SKPath.ParseSvgPathData(svg); paint.Color = outline; paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = .8f; canvas.DrawPath(line, paint);
        }
        void Ear(string svg, string folds)
        {
            Shape(svg, skin); Line(svg); Line(folds);
        }
    }

    internal static SKBitmap Guide(SKBitmap native)
    {
        var result = HeadOutline.Trace(native); var bounds = Bounds(native);
        int y = bounds.Bottom - 2, left = native.Width, right = -1;
        for (int x = 0; x < native.Width; x++) if (native.GetPixel(x, y).Alpha > 32) { left = Math.Min(left, x); right = x; }
        if (right < left) { left = bounds.Left; right = bounds.Right - 1; }
        float center = (left + right) / 2f;
        using var canvas = new SKCanvas(result);
        using var paint = new SKPaint { Color = new SKColor(255, 191, 81), StrokeWidth = 2, IsAntialias = true };
        canvas.DrawLine(left - 12, y, right + 12, y, paint);
        canvas.DrawLine(center, y - 12, center, Math.Min(result.Height - 1, y + 12), paint);
        return result;
    }
}
