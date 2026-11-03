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
            + "3. Export a transparent 512 x 512 PNG over the matching project PNG listed below, then use Refresh PNGs in Studio. Keep your PSD as the editable source.\n\n"
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
        using var path = new SKPath();
        if (direction is "W" or "E")
        {
            path.MoveTo(.43f, 1); path.LineTo(.43f, .84f);
            path.CubicTo(.20f, .86f, .16f, .75f, .15f, .64f); path.LineTo(.07f, .60f);
            path.QuadTo(0, .57f, .04f, .53f); path.LineTo(.18f, .40f);
            path.CubicTo(.16f, .11f, .31f, 0, .56f, 0);
            path.CubicTo(.86f, 0, 1, .18f, .98f, .43f);
            path.CubicTo(.98f, .64f, .82f, .73f, .73f, .79f);
            path.LineTo(.73f, 1); path.Close();
        }
        else
        {
            path.MoveTo(.37f, 1); path.LineTo(.37f, .82f);
            path.CubicTo(.18f, .77f, .14f, .65f, .12f, .56f);
            path.CubicTo(0, .56f, 0, .37f, .11f, .38f);
            path.CubicTo(.08f, .10f, .24f, 0, .50f, 0);
            path.CubicTo(.76f, 0, .92f, .10f, .89f, .38f);
            path.CubicTo(1, .37f, 1, .56f, .88f, .56f);
            path.CubicTo(.86f, .65f, .82f, .77f, .63f, .82f);
            path.LineTo(.63f, 1); path.Close();
        }
        // A clean faceless scalp/neck, not a recoloured native hairstyle or beard.
        // Native visible bounds retain the supplied head scale and collar baseline.
        path.Transform(SKMatrix.CreateScale(bounds.Width - 4, bounds.Height - 4));
        path.Transform(SKMatrix.CreateTranslation(bounds.Left + 2, bounds.Top + 2));
        using var canvas = new SKCanvas(result);
        using var fill = new SKPaint { Color = new SKColor(214, 177, 138), IsAntialias = true };
        using var line = new SKPaint { Color = new SKColor(98, 71, 53), Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true };
        canvas.DrawPath(path, fill); canvas.DrawPath(path, line); return result;
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
