using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;
using SkiaSharp;

namespace GameArt;

/// <summary>
/// A view of 4-byte pixels (alpha last; BGRA here, the channel order does not matter) in a byte array.
/// Row y starts at <see cref="Origin"/> + y × <see cref="Stride"/>, so a bottom-up Unity texture and any
/// crop of it are read top-down without copying.
/// </summary>
internal readonly record struct Pixels(byte[] Data, int Width, int Height, int Origin, int Stride)
{
    public static Pixels TopDown(byte[] data, int width, int height) => new(data, width, height, 0, width * 4);
    public int Row(int y) => Origin + y * Stride;
    public Pixels Crop(int x, int y, int width, int height) => new(Data, width, height, Row(y) + x * 4, Stride);

    /// <summary>A packed top-down copy.</summary>
    public byte[] ToArray()
    {
        var result = new byte[Width * Height * 4];
        for (int y = 0; y < Height; y++) Buffer.BlockCopy(Data, Row(y), result, y * Width * 4, Width * 4);
        return result;
    }

    /// <summary>A top-down, non-premultiplied BGRA bitmap for <see cref="Png.Save"/>.</summary>
    public SKBitmap ToBitmap(bool opaque = false)
    {
        var bitmap = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Bgra8888, opaque ? SKAlphaType.Opaque : SKAlphaType.Unpremul));
        IntPtr target = bitmap.GetPixels();
        for (int y = 0; y < Height; y++) System.Runtime.InteropServices.Marshal.Copy(Data, Row(y), target + y * bitmap.RowBytes, Width * 4);
        return bitmap;
    }
}

/// <summary>Uncached texture decoding for the very large map textures (the UnityGame cache would keep them alive).</summary>
internal static class RawTexture
{
    /// <summary>Decodes a Texture2D in data.unity3d to BGRA pixels, viewed top-down.</summary>
    public static Pixels Decode(UnityGame game, AssetsFileInstance file, AssetFileInfo info)
    {
        var texture = TextureFile.ReadTextureFile(game.Read(file, info));
        texture.FillPictureData(file);
        byte[] bgra = texture.DecodeTextureRaw(texture.pictureData)
            ?? throw new InvalidDataException($"Texture {texture.m_Name} uses unsupported format {(TextureFormat)texture.m_TextureFormat}.");
        int width = texture.m_Width, height = texture.m_Height;
        if (bgra.Length < width * height * 4) throw new InvalidDataException($"Texture {texture.m_Name} decoded to too few pixels.");
        // Unity stores rows bottom-up.
        return new Pixels(bgra, width, height, (height - 1) * width * 4, -width * 4);
    }
}

/// <summary>
/// Pixel operations ported from Pillow 12.3 (src/libImaging Resample.c, Convert.c, Paste.c, Draw.c, Geometry.c;
/// Pillow is MIT-CMU licensed, © 1997-2011 Secret Labs AB, © 1995-2011 Fredrik Lundh and contributors),
/// so the artwork built here matches the PNGs the Studio's earlier Python tools made from the same game files.
/// </summary>
internal static class Pillow
{
    // ---- Resampling (Image.resize) ----

    public enum Filter { Bicubic, Lanczos }

    private const int PrecisionBits = 32 - 8 - 2;

    private static double Bicubic(double x)
    {
        const double a = -0.5;
        if (x < 0.0) x = -x;
        if (x < 1.0) return ((a + 2.0) * x - (a + 3.0)) * x * x + 1;
        if (x < 2.0) return (((x - 5) * x + 8) * x - 4) * a;
        return 0.0;
    }
    private static double Sinc(double x)
    {
        if (x == 0.0) return 1.0;
        x *= Math.PI;
        return Math.Sin(x) / x;
    }
    private static double Lanczos(double x) => -3.0 <= x && x < 3.0 ? Sinc(x) * Sinc(x / 3) : 0.0;

    /// <summary>Filter taps per output pixel in 22-bit fixed point (precompute_coeffs + normalize_coeffs_8bpc).</summary>
    private static (int KSize, int[] Bounds, int[] Kk) Coefficients(int inSize, float in0, float in1, int outSize, Filter filter)
    {
        Func<double, double> function = filter == Filter.Bicubic ? Bicubic : Lanczos;
        double filterSupport = filter == Filter.Bicubic ? 2.0 : 3.0;
        double scale = (double)(in1 - in0) / outSize, filterscale = scale < 1.0 ? 1.0 : scale;
        double support = filterSupport * filterscale;
        int ksize = (int)Math.Ceiling(support) * 2 + 1;
        var bounds = new int[outSize * 2];
        var kk = new int[outSize * ksize];
        var k = new double[ksize];
        double inverse = 1.0 / filterscale;
        for (int xx = 0; xx < outSize; xx++)
        {
            double center = in0 + (xx + 0.5) * scale, ww = 0.0;
            int xmin = (int)(center - support + 0.5);
            if (xmin < 0) xmin = 0;
            int xmax = (int)(center + support + 0.5);
            if (xmax > inSize) xmax = inSize;
            xmax -= xmin;
            Array.Clear(k);
            for (int x = 0; x < xmax; x++) { double w = function((x + xmin - center + 0.5) * inverse); k[x] = w; ww += w; }
            if (ww != 0.0) for (int x = 0; x < xmax; x++) k[x] /= ww;
            for (int x = 0; x < ksize; x++)
                kk[xx * ksize + x] = k[x] < 0 ? (int)(-0.5 + k[x] * (1 << PrecisionBits)) : (int)(0.5 + k[x] * (1 << PrecisionBits));
            bounds[xx * 2] = xmin;
            bounds[xx * 2 + 1] = xmax;
        }
        return (ksize, bounds, kk);
    }

    private static byte Clip8(int value) { value >>= PrecisionBits; return value <= 0 ? (byte)0 : value >= 255 ? (byte)255 : (byte)value; }

    /// <summary>
    /// Pillow's resize of a 4-band 8-bit image (horizontal pass, then vertical) over the whole source box.
    /// Callers premultiply RGBA first, as Image.resize does.
    /// </summary>
    public static byte[] Resize(Pixels source, int width, int height, Filter filter)
    {
        bool horizontal = width != source.Width, vertical = height != source.Height;
        var (kv, boundsV, kkV) = Coefficients(source.Height, 0, source.Height, height, filter);
        int first = boundsV[0], last = boundsV[height * 2 - 2] + boundsV[height * 2 - 1];
        Pixels input = source;
        if (horizontal)
        {
            for (int i = 0; i < height; i++) boundsV[i * 2] -= first;
            var (kh, boundsH, kkH) = Coefficients(source.Width, 0, source.Width, width, filter);
            int rows = last - first;
            var temp = new byte[width * rows * 4];
            Parallel.For(0, rows, yy =>
            {
                byte[] data = source.Data;
                int line = source.Row(yy + first), output = yy * width * 4;
                for (int xx = 0; xx < width; xx++)
                {
                    int xmin = boundsH[xx * 2], xmax = boundsH[xx * 2 + 1], k = xx * kh;
                    int s0 = 1 << (PrecisionBits - 1), s1 = s0, s2 = s0, s3 = s0;
                    for (int x = 0; x < xmax; x++)
                    {
                        int p = line + (x + xmin) * 4, weight = kkH[k + x];
                        s0 += data[p] * weight; s1 += data[p + 1] * weight; s2 += data[p + 2] * weight; s3 += data[p + 3] * weight;
                    }
                    temp[output++] = Clip8(s0); temp[output++] = Clip8(s1); temp[output++] = Clip8(s2); temp[output++] = Clip8(s3);
                }
            });
            input = Pixels.TopDown(temp, width, rows);
        }
        if (!vertical) return input.ToArray();
        var result = new byte[input.Width * height * 4];
        Parallel.For(0, height, yy =>
        {
            byte[] data = input.Data;
            int ymin = boundsV[yy * 2], ymax = boundsV[yy * 2 + 1], k = yy * kv, output = yy * input.Width * 4;
            for (int xx = 0; xx < input.Width; xx++)
            {
                int s0 = 1 << (PrecisionBits - 1), s1 = s0, s2 = s0, s3 = s0;
                for (int y = 0; y < ymax; y++)
                {
                    int p = input.Row(y + ymin) + xx * 4, weight = kkV[k + y];
                    s0 += data[p] * weight; s1 += data[p + 1] * weight; s2 += data[p + 2] * weight; s3 += data[p + 3] * weight;
                }
                result[output++] = Clip8(s0); result[output++] = Clip8(s1); result[output++] = Clip8(s2); result[output++] = Clip8(s3);
            }
        });
        return result;
    }

    /// <summary>Image.thumbnail's size: fits inside the box keeping the aspect ratio; null when no shrinking is needed.</summary>
    public static (int Width, int Height)? ThumbnailSize(int width, int height, int maxWidth, int maxHeight)
    {
        if (maxWidth >= width && maxHeight >= height) return null;
        double aspect = (double)width / height;
        int x = maxWidth, y = maxHeight;
        static int RoundAspect(double number, Func<int, double> key)
        {
            int low = (int)Math.Floor(number), high = (int)Math.Ceiling(number);
            return Math.Max(key(high) < key(low) ? high : low, 1);
        }
        if ((double)x / y >= aspect) x = RoundAspect(y * aspect, n => Math.Abs(aspect - (double)n / y));
        else y = RoundAspect(x / aspect, n => n == 0 ? 0 : Math.Abs(aspect - (double)x / n));
        return (x, y);
    }

    /// <summary>Image.thumbnail of an RGBA image (premultiply, resize, unpremultiply); the pixels unchanged when already small enough.</summary>
    public static Pixels Thumbnail(Pixels image, int maxWidth, int maxHeight, Filter filter)
    {
        if (ThumbnailSize(image.Width, image.Height, maxWidth, maxHeight) is not { } size || size == (image.Width, image.Height)) return image;
        var premultiplied = image.ToArray();
        Premultiply(premultiplied);
        var resized = Resize(Pixels.TopDown(premultiplied, image.Width, image.Height), size.Width, size.Height, filter);
        Unpremultiply(resized);
        return Pixels.TopDown(resized, size.Width, size.Height);
    }

    // ---- Mode conversions (RGBA <-> RGBa) ----

    private static int MulDiv255(int a, int b) { int t = a * b + 128; return ((t >> 8) + t) >> 8; }

    public static void Premultiply(byte[] pixels)
    {
        for (int i = 0; i < pixels.Length; i += 4)
        {
            int alpha = pixels[i + 3];
            if (alpha == 255) continue;
            pixels[i] = (byte)MulDiv255(pixels[i], alpha);
            pixels[i + 1] = (byte)MulDiv255(pixels[i + 1], alpha);
            pixels[i + 2] = (byte)MulDiv255(pixels[i + 2], alpha);
        }
    }

    public static void Unpremultiply(byte[] pixels)
    {
        for (int i = 0; i < pixels.Length; i += 4)
        {
            int alpha = pixels[i + 3];
            if (alpha == 255 || alpha == 0) continue;
            for (int c = 0; c < 3; c++) pixels[i + c] = (byte)Math.Min(255, 255 * pixels[i + c] / alpha);
        }
    }

    // ---- Paste with an RGBA image as its own mask (Image.paste(im, box, im)) ----

    public static void PasteWithAlpha(Pixels target, Pixels image, int left, int top)
    {
        for (int y = 0; y < image.Height; y++)
        {
            int ty = top + y;
            if (ty < 0 || ty >= target.Height) continue;
            for (int x = 0; x < image.Width; x++)
            {
                int tx = left + x;
                if (tx < 0 || tx >= target.Width) continue;
                int i = image.Row(y) + x * 4, o = target.Row(ty) + tx * 4, a = image.Data[i + 3];
                for (int c = 0; c < 4; c++)
                {
                    int t = target.Data[o + c] * (255 - a) + image.Data[i + c] * a + 128;
                    target.Data[o + c] = (byte)(((t >> 8) + t) >> 8);
                }
            }
        }
    }

    // ---- Transposes (Image.rotate by multiples of 90 with expand) ----

    /// <summary>Image.rotate(angle, expand=True) for angle a multiple of 90 (counter-clockwise, as Pillow).</summary>
    public static Pixels Rotate(Pixels image, int angle)
    {
        angle = ((angle % 360) + 360) % 360;
        if (angle == 0) return image;
        int w = image.Width, h = image.Height;
        int ow = angle == 180 ? w : h, oh = angle == 180 ? h : w;
        var result = new byte[ow * oh * 4];
        for (int ny = 0; ny < oh; ny++)
            for (int nx = 0; nx < ow; nx++)
            {
                // ROTATE_90: new(x, y) = old(w-1-y, x); ROTATE_270: old(y, h-1-x); ROTATE_180: old(w-1-x, h-1-y).
                var (sx, sy) = angle switch { 90 => (w - 1 - ny, nx), 270 => (ny, h - 1 - nx), _ => (w - 1 - nx, h - 1 - ny) };
                Buffer.BlockCopy(image.Data, image.Row(sy) + sx * 4, result, (ny * ow + nx) * 4, 4);
            }
        return Pixels.TopDown(result, ow, oh);
    }

    // ---- Filled polygons on an 8-bit mask (ImageDraw.polygon(xy, fill=ink)) ----

    private struct Edge { public int D, X0, Y0, XMin, YMin, XMax, YMax; public float Dx; }

    private static Edge MakeEdge(int x0, int y0, int x1, int y1)
    {
        var e = new Edge { X0 = x0, Y0 = y0 };
        (e.XMin, e.XMax) = x0 <= x1 ? (x0, x1) : (x1, x0);
        (e.YMin, e.YMax) = y0 <= y1 ? (y0, y1) : (y1, y0);
        if (y0 == y1) { e.D = 0; e.Dx = 0f; }
        else { e.Dx = (float)(x1 - x0) / (y1 - y0); e.D = y0 == e.YMin ? 1 : -1; }
        return e;
    }

    private static int RoundUp(float f) => (int)(f >= 0.0 ? MathF.Floor(f + 0.5f) : -MathF.Floor(MathF.Abs(f) + 0.5f));
    private static int RoundDown(float f) => (int)(f >= 0.0 ? MathF.Ceiling(f - 0.5f) : -MathF.Ceiling(MathF.Abs(f) - 0.5f));
    private static float RoundF(float f) => MathF.Round(f, MidpointRounding.AwayFromZero);

    private static void HLine(byte[] mask, int width, int height, int x0, int y0, int x1, byte ink)
    {
        if (y0 < 0 || y0 >= height) return;
        if (x0 < 0) x0 = 0; else if (x0 >= width) return;
        if (x1 < 0) return; else if (x1 >= width) x1 = width - 1;
        if (x0 <= x1) mask.AsSpan(y0 * width + x0, x1 - x0 + 1).Fill(ink);
    }

    /// <summary>Fills a polygon given as x, y pairs (coordinates truncated to integers, as Pillow's draw_polygon does).</summary>
    public static void FillPolygon(byte[] mask, int width, int height, ReadOnlySpan<double> points, byte ink)
    {
        int count = points.Length / 2;
        if (count <= 0) return;
        var xy = new int[count * 2];
        for (int i = 0; i < xy.Length; i++) xy[i] = (int)points[i];
        var edges = new List<Edge>(count);
        int v;
        for (v = 0; v < count - 1; v++)
        {
            int x0 = xy[v * 2], y0 = xy[v * 2 + 1], x1 = xy[v * 2 + 2], y1 = xy[v * 2 + 3];
            if (y0 == y1 && v != 0 && y0 == xy[v * 2 - 1])
            {
                // A horizontal line that immediately follows another horizontal line extends it.
                var last = edges[^1];
                if (x1 > x0 && x0 > xy[v * 2 - 2]) { last.XMax = x1; edges[^1] = last; continue; }
                if (x1 < x0 && x0 < xy[v * 2 - 2]) { last.XMin = x1; edges[^1] = last; continue; }
            }
            edges.Add(MakeEdge(x0, y0, x1, y1));
        }
        if (xy[v * 2] != xy[0] || xy[v * 2 + 1] != xy[1]) edges.Add(MakeEdge(xy[v * 2], xy[v * 2 + 1], xy[0], xy[1]));
        FillEdges(mask, width, height, edges, ink);
    }

    // polygon_generic: scan-line fill, including Pillow's corner handling.
    private static void FillEdges(byte[] mask, int width, int height, List<Edge> e, byte ink)
    {
        int ymin = height - 1, ymax = 0;
        var table = new List<Edge>(e.Count);
        foreach (var edge in e)
        {
            if (ymin > edge.YMin) ymin = edge.YMin;
            if (ymax < edge.YMax) ymax = edge.YMax;
            if (edge.YMin == edge.YMax) { HLine(mask, width, height, edge.XMin, edge.YMin, edge.XMax, ink); continue; }
            table.Add(edge);
        }
        if (ymin < 0) ymin = 0;
        if (ymax > height) ymax = height;
        var xx = new float[table.Count * 2];
        for (; ymin <= ymax; ymin++)
        {
            int j = 0;
            for (int i = 0; i < table.Count; i++)
            {
                var current = table[i];
                if (ymin < current.YMin || ymin > current.YMax) continue;
                xx[j++] = (ymin - current.Y0) * current.Dx + current.X0;
                if (ymin == current.YMax && ymin < ymax)
                {
                    // Needed to draw consistent polygons.
                    xx[j] = xx[j - 1];
                    j++;
                }
                else if ((ymin == current.YMin || ymin == current.YMax) && current.Dx != 0)
                {
                    // Connect discontiguous corners.
                    for (int k = 0; k < i; k++)
                    {
                        var other = table[k];
                        if ((ymin != other.YMin && ymin != other.YMax) || other.Dx == 0) continue;
                        if (RoundF(xx[j - 1]) != RoundF((ymin - other.Y0) * other.Dx + other.X0)) continue;
                        int offset = ymin == current.YMax ? -1 : 1;
                        float adjacent = (ymin + offset - current.Y0) * current.Dx + current.X0;
                        if (ymin + offset < other.YMin || ymin + offset > other.YMax) continue;
                        float adjacentOther = (ymin + offset - other.Y0) * other.Dx + other.X0;
                        if (xx[j - 1] > adjacent + 1 && xx[j - 1] > adjacentOther + 1)
                            xx[j - 1] = RoundF(MathF.Max(adjacent, adjacentOther)) + 1;
                        else if (xx[j - 1] < adjacent - 1 && xx[j - 1] < adjacentOther - 1)
                            xx[j - 1] = RoundF(MathF.Min(adjacent, adjacentOther)) - 1;
                        break;
                    }
                }
            }
            Array.Sort(xx, 0, j);
            for (int i = 1; i < j; i += 2) HLine(mask, width, height, RoundUp(xx[i - 1]), ymin, RoundDown(xx[i]), ink);
        }
    }
}
