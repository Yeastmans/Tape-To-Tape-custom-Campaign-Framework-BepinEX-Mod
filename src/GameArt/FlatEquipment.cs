using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using SkiaSharp;

namespace GameArt;

/// <summary>
/// Flat equipment pieces for the player editor: the game's own atlas regions (from the player's install)
/// laid out in the editor's separated format. Torso and sleeve scans are uncurved onto flat sewing panels;
/// small equipment keeps its native outline. A port of tools/build_flat_equipment.py; the only shipped
/// input is flat-regions.json (skin → atlas region rectangles, made by tools/export_flat_equipment.mjs).
/// The image steps follow Pillow 12's integer arithmetic so the output matches what the Studio used to ship.
/// </summary>
public sealed class FlatEquipment : IArtExtractor
{
    public string Folder => "flat-equipment";
    public string Description => "Flat equipment";

    internal sealed record Region(string Role, string Skin, string Slot, string View, string Name, string Page, double PageWidth, double X, double Y, double Width, double Height, int Degrees);

    // One (role, skin, view, part) group; slots keep their first-seen order, as the Python dict did.
    private sealed class Group(string role, string skin, string view, string part)
    {
        public readonly string Role = role, Skin = skin, View = view, Part = part;
        public readonly List<Region> Slots = [];
        private readonly Dictionary<string, int> bySlot = new(StringComparer.Ordinal);
        public void Add(Region row)
        {
            // Prefer the named base region over alternate animation frames.
            if (!bySlot.TryGetValue(row.Slot, out int at)) { bySlot[row.Slot] = Slots.Count; Slots.Add(row); }
            else if (row.Name.Length < Slots[at].Name.Length) Slots[at] = row;
        }
        public Region? Slot(string name) => bySlot.TryGetValue(name, out int at) ? Slots[at] : null;
    }

    private sealed record Entry(string Role, string Skin, string View, string Part, string File, bool Custom, List<string> Regions);
    private sealed class MissingPage(string page) : Exception(page) { public string Page { get; } = page; }

    private static readonly Dictionary<string, string> Parts = new(StringComparer.Ordinal)
    {
        ["Body"] = "jersey", ["Body_Bicep"] = "sleeves", ["Body_Gloves"] = "gloves", ["Body_Glove"] = "gloves", ["Body_Pants"] = "pants",
        ["Body_Skates"] = "skates", ["Body_Pads"] = "socks", ["Body_Blocker"] = "blocker", ["Sticks"] = "stick", ["Body_Stick"] = "stick", ["Helmet"] = "helmet",
    };

    public void Extract(UnityGame game, string output, IProgress<string>? progress)
    {
        var source = Maps.Load("flat-regions.json");
        var groups = new List<Group>();
        var byKey = new Dictionary<(string, string, string, string), Group>();
        foreach (var node in source["rows"]!.AsArray())
        {
            var row = new Region(node!["role"]!.GetValue<string>(), node["skin"]!.GetValue<string>(), node["slot"]!.GetValue<string>(), node["view"]!.GetValue<string>(),
                node["region"]!.GetValue<string>(), node["page"]!.GetValue<string>(), node["pageWidth"]!.GetValue<double>(), node["x"]!.GetValue<double>(),
                node["y"]!.GetValue<double>(), node["width"]!.GetValue<double>(), node["height"]!.GetValue<double>(), node["degrees"]!.GetValue<int>());
            string prefix = row.Skin.Split('/')[0];
            string? part = Parts.GetValueOrDefault(prefix);
            if (prefix == "Faces" && row.Skin.Split('/')[^1].StartsWith("Helmet_", StringComparison.Ordinal)) part = "helmet";
            if (part == null) continue;
            var key = (row.Role, row.Skin, row.View, part);
            if (!byKey.TryGetValue(key, out var group)) groups.Add(byKey[key] = group = new Group(row.Role, row.Skin, row.View, part));
            group.Add(row);
        }

        var crops = Crops(game, groups.SelectMany(g => g.Slots), progress);
        var results = new (List<(Entry Entry, Img Image)> Pieces, string? Missing)[groups.Count];
        Parallel.For(0, groups.Count, i => results[i] = Build(groups[i], crops));

        var index = new List<Entry>();
        var missing = new List<(string Role, string Skin, string View, string Error)>();
        for (int i = 0; i < groups.Count; i++)
        {
            foreach (var (entry, _) in results[i].Pieces) index.Add(entry);
            if (results[i].Missing is { } page) missing.Add((groups[i].Role, groups[i].Skin, groups[i].View, page));
        }
        Parallel.ForEach(results.SelectMany(r => r.Pieces), piece => piece.Image.Save(Path.Combine(output, piece.Entry.File)));

        var json = new Json();
        json.Array(index, (j, e) => j.Object(o => o
            .Field("role", e.Role).Field("skin", e.Skin).Field("view", e.View).Field("part", e.Part).Field("file", e.File)
            .Field("custom", e.Custom).Field("regions", r => r.Array(e.Regions, (k, s) => k.String(s)))));
        File.WriteAllText(Path.Combine(output, "index.json"), json.ToString());
        var report = new Json();
        report.Object(o => o
            .Field("sourceSha256", source["sourceSha256"]!.GetValue<string>())
            .Field("method", "Native atlas panels flattened and laid out; source colours retained")
            .Field("missing", m => m.Array(missing, (j, x) => j.Object(p => p.Field("role", x.Role).Field("skin", x.Skin).Field("view", x.View).Field("error", x.Error)))));
        File.WriteAllText(Path.Combine(output, "provenance.json"), report.ToString());
        progress?.Report($"{index.Count} flat equipment pieces, {missing.Count} groups without an atlas page");
    }

    // ---- Native regions ----

    // Every region cut from its atlas page up front, one page at a time (pages are up to 4096 × 4096).
    // A page missing from the install maps its regions to null.
    private static Dictionary<Region, Img?> Crops(UnityGame game, IEnumerable<Region> rows, IProgress<string>? progress)
    {
        var crops = new Dictionary<Region, Img?>(ReferenceEqualityComparer.Instance);
        var pages = rows.GroupBy(r => r.Page, StringComparer.Ordinal).ToArray();
        int done = 0;
        foreach (var page in pages)
        {
            string prefix = (page.Key.StartsWith("goalie", StringComparison.OrdinalIgnoreCase) ? "goaliesprites_assets_" : "skatersprites_assets_")
                + Path.GetFileNameWithoutExtension(page.Key).ToLowerInvariant() + "_";
            using var atlas = game.BundleTexture(prefix);
            foreach (var row in page) crops[row] = atlas == null ? null : Crop(atlas, row);
            if (++done % 25 == 0) progress?.Report($"Flat equipment atlas pages {done} / {pages.Length}");
        }
        return crops;
    }

    private static Img Crop(SKBitmap atlas, Region r)
    {
        double scale = atlas.Width / r.PageWidth;
        int x = (int)Math.Round(r.X * scale), y = (int)Math.Round(r.Y * scale), w = (int)Math.Round(r.Width * scale), h = (int)Math.Round(r.Height * scale);
        bool turned = r.Degrees == 90;
        var im = Img.FromBgra(atlas, x, y, x + (turned ? h : w), y + (turned ? w : h));
        if (turned) im = im.Rotate270();
        // Some game pages store the outer matte as opaque black. Remove only
        // border-connected matte pixels, preserving the drawn black seams.
        int width = im.W + 2, height = im.H + 2;
        var dark = new bool[width * height];
        for (int py = 0; py < height; py++)
            for (int px = 0; px < width; px++)
            {
                if (px == 0 || py == 0 || px == width - 1 || py == height - 1) { dark[py * width + px] = true; continue; }
                int n = ((py - 1) * im.W + px - 1) * 4;
                dark[py * width + px] = Math.Max(im.P[n], Math.Max(im.P[n + 1], im.P[n + 2])) < 8 || im.P[n + 3] < 16;
            }
        var seen = new bool[width * height];
        var stack = new Stack<int>();
        stack.Push(0); seen[0] = true;
        while (stack.Count > 0)
        {
            int at = stack.Pop(), px = at % width, py = at / width;
            if (px > 0 && py > 0 && px < width - 1 && py < height - 1) im.P[((py - 1) * im.W + px - 1) * 4 + 3] = 0;
            foreach (int next in (ReadOnlySpan<int>)[px + 1 < width ? at + 1 : -1, px > 0 ? at - 1 : -1, py + 1 < height ? at + width : -1, py > 0 ? at - width : -1])
                if (next >= 0 && !seen[next] && dark[next]) { seen[next] = true; stack.Push(next); }
        }
        return im.BBox() is { } box ? im.Crop(box.X0, box.Y0, box.X1, box.Y1) : im;
    }

    // ---- Layout (one group → its pieces) ----

    private static (List<(Entry, Img)> Pieces, string? Missing) Build(Group group, Dictionary<Region, Img?> crops)
    {
        var pieces = new List<(Entry, Img)>();
        string role = group.Role, skin = group.Skin, view = group.View, part = group.Part;
        bool custom = skin.Contains("colors", StringComparison.OrdinalIgnoreCase);
        Img? Get(params string[] names)
        {
            foreach (string name in names)
                if (group.Slot(name) is { } row) return (crops[row] ?? throw new MissingPage(row.Page)).Clone();
            return null;
        }
        try
        {
            var variants = new List<string> { part };
            if (part == "jersey") variants.Add(role == "skater" ? "socks" : "pants");
            foreach (string piece in variants)
            {
                var (sw, sh) = piece switch
                {
                    "jersey" or "sleeves" => (320, 320), "helmet" => (150, 125), "gloves" => (320, 115), "blocker" => (100, 130), "pants" => (230, 140),
                    "socks" => (230, role == "goalie" ? 190 : 110), "skates" => (230, 110), _ => (110, 630),
                };
                var canvas = new Img(sw, sh, 4);
                if (piece is "jersey" or "sleeves")
                {
                    if (piece == "jersey")
                    {
                        var body = Get("Body");
                        if (body == null) continue;
                        // The atlas reserves a deep head opening for the posed
                        // character. Flatten that foreshortened collar/shoulder area.
                        int split = (int)Math.Round(body.H * .57);
                        var upper = FlatPanel(body.Crop(0, 0, body.W, split), 150, 48);
                        var lower = FlatPanel(body.Crop(0, split, body.W, body.H), 150, 202);
                        var panel = new Img(150, 250, 4);
                        panel.AlphaComposite(upper, 0, 0); panel.AlphaComposite(lower, 0, 48);
                        if (custom)
                        {
                            // The hem stripe is curved in the atlas to follow the
                            // character's belly. Its centre scan preserves the exact
                            // native band widths and order on a straight flat hem.
                            panel.Paste(panel.Crop(74, 130, 76, 250).Resize(150, 120, Filter.Nearest), 0, 130);
                        }
                        panel = ShapePanel(panel, TorsoShape());
                        // Supersampled collar avoids a stair-stepped cutout.
                        var mask = new Img(600, 1000, 1); mask.Fill(255);
                        mask.Ellipse(204, -76, 396, view == "S" ? 88 : 40, 0);
                        panel.MultiplyAlpha(mask.Resize(panel.W, panel.H, Filter.Lanczos));
                        canvas.AlphaComposite(panel, 85, 52);
                    }
                    foreach (bool left in new[] { true, false })
                    {
                        string side = left ? "left" : "right";
                        var arm = piece == "sleeves" ? Get("Bicep " + side, "Bicep_" + side) : Get("Forearm " + side, "Arm_" + side, "Bicep_" + side, "Forearm_" + side);
                        if (arm == null) continue;
                        if (!custom) arm = RemoveExposedSkin(arm);
                        if (arm.BBox() == null) continue;
                        if (piece == "jersey" && role == "skater" && !custom)
                        {
                            int visible = 0, flesh = 0;
                            for (int n = 0; n < arm.P.Length; n += 4)
                                if (arm.P[n + 3] > 80) { visible++; if (Flesh(arm.P[n], arm.P[n + 1], arm.P[n + 2])) flesh++; }
                            if (flesh > visible * .55) continue;
                        }
                        var sleeve = FlatPanel(arm, 57, role == "goalie" ? 141 : 95);
                        sleeve = ShapePanel(sleeve, SleeveShape(sleeve.W, sleeve.H));
                        sleeve = sleeve.Rotate(left ? -10 : 10);
                        bool upperArm = role == "goalie" || piece == "sleeves";
                        canvas.AlphaComposite(sleeve, upperArm ? (left ? 27 : 218) : (left ? 10 : 235), upperArm ? 65 : 155);
                    }
                    // Skater upper sleeves come from the independent Bicep skin layer.
                }
                else if (piece is "pants" or "socks" or "skates")
                {
                    string[] names = piece switch
                    {
                        "pants" => ["Pants left", "Pants_left", "Pants right", "Pants_right"],
                        "socks" => ["Leg left", "Pad_left", "Leg right", "Pad_right"],
                        _ => ["Skate left", "Skate_left", "Skate right", "Skate_right"],
                    };
                    for (int i = 0; i < 2; i++)
                    {
                        var item = Get(names[2 * i], names[2 * i + 1]);
                        if (item == null) continue;
                        if (piece == "pants" && role == "skater") item = FlatPanel(Upright(item), 108, sh);
                        else if (piece == "socks") item = Upright(role == "skater" && !custom ? RemoveExposedSkin(item) : item);
                        Put(canvas, item, i * 122, 0, 108, sh);
                    }
                    if (piece == "pants" && role == "skater")
                    {
                        // A continuous waistband joins the two native leg panels;
                        // the curved crotch and leg openings define the shorts.
                        canvas.AlphaComposite(canvas.Crop(106, 0, 108, 95).Resize(14, 95, Filter.Bicubic), 108, 0);
                        canvas = ShapePanel(canvas, ShortsShape());
                    }
                }
                else if (piece == "gloves")
                {
                    if (role == "goalie")
                    {
                        if (Get("Glove") is { } item) Put(canvas, item, 220, 0, 100, 115);
                    }
                    else
                    {
                        string[] slots = ["Glove left", "Glove right"];
                        for (int i = 0; i < 2; i++)
                            if (Get(slots[i]) is { } item) Put(canvas, Upright(item), i * 220, 0, 100, 115);
                    }
                }
                else
                {
                    string[] names = piece switch { "helmet" => ["Helmet_Custom", "Helmet", "Helmet_Top"], "blocker" => ["Blocker"], _ => ["Stick"] };
                    foreach (string name in names)
                        if (Get(name) is { } item) Put(canvas, piece == "stick" ? Upright(item) : item, 0, 0, sw, sh);
                }
                if (canvas.BBox() == null) continue;
                string file = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes($"{role}|{skin}|{view}|{piece}"))).ToLowerInvariant()[..20] + ".png";
                pieces.Add((new Entry(role, skin, view, piece, file, custom, group.Slots.Select(s => s.Name).ToList()), canvas));
            }
            return (pieces, null);
        }
        catch (MissingPage error) { return (pieces, error.Page); }
    }

    private static bool Flesh(double r, double g, double b) => r > g * 1.08 && r < g * 1.8 && g > b * 1.05 && g < b * 1.9;

    /// <summary>Remove the curved side boundaries of a posed garment panel row by row.</summary>
    private static Img FlatPanel(Img im, int width, int height)
    {
        im = im.Resize(im.W, height, Filter.Bicubic);
        var result = new Img(width, height, 4);
        for (int y = 0; y < height; y++)
        {
            // Compact only fabric pixels. Transparent spaces between the posed
            // panel and its outline must not become long black stretching marks.
            int count = 0;
            for (int x = 0; x < im.W; x++) if (im.P[(y * im.W + x) * 4 + 3] > 32) count++;
            if (count == 0) continue;
            var row = new Img(count, 1, 4);
            for (int x = 0, n = 0; x < im.W; x++)
            {
                int at = (y * im.W + x) * 4;
                if (im.P[at + 3] <= 32) continue;
                row.P[n] = im.P[at]; row.P[n + 1] = im.P[at + 1]; row.P[n + 2] = im.P[at + 2]; row.P[n + 3] = 255; n += 4;
            }
            result.Paste(row.Resize(width, 1, Filter.Bicubic), 0, y);
        }
        return result;
    }

    /// <summary>Fit the native fabric across curved sides instead of cutting off its pattern.</summary>
    private static Img ShapePanel(Img im, Img mask)
    {
        var result = new Img(mask.W, mask.H, 4);
        for (int y = 0; y < mask.H; y++)
        {
            int x0 = -1, x1 = -1;
            for (int x = 0; x < mask.W; x++) if (mask.P[y * mask.W + x] != 0) { if (x0 < 0) x0 = x; x1 = x + 1; }
            if (x0 < 0) continue;
            result.Paste(im.Crop(0, y, im.W, y + 1).Resize(x1 - x0, 1, Filter.Bicubic), x0, y);
        }
        result.MultiplyAlpha(mask);
        return result;
    }

    /// <summary>Antialiased curved sewing outline, sampled at four times output size.</summary>
    private static Img Contour(int width, int height, (double X, double Y) start, ((double X, double Y) C1, (double X, double Y) C2, (double X, double Y) End)[] curves)
    {
        var points = new List<(int X, int Y)> { ((int)Math.Round(start.X * 4), (int)Math.Round(start.Y * 4)) };
        var current = start;
        foreach (var (c1, c2, end) in curves)
        {
            for (int step = 1; step < 33; step++)
            {
                double t = step / 32.0, u = 1 - t;
                double x = u * u * u * current.X + 3 * u * u * t * c1.X + 3 * u * t * t * c2.X + t * t * t * end.X;
                double y = u * u * u * current.Y + 3 * u * u * t * c1.Y + 3 * u * t * t * c2.Y + t * t * t * end.Y;
                points.Add(((int)Math.Round(x * 4), (int)Math.Round(y * 4)));
            }
            current = end;
        }
        var mask = new Img(width * 4, height * 4, 1);
        mask.Polygon(points, 255);
        return mask.Resize(width, height, Filter.Lanczos);
    }

    private static Img? torso, shorts;
    private static readonly Dictionary<(int, int), Img> sleeves = new();
    private static Img TorsoShape() => torso ??= Contour(150, 250, (7, 19), [
        ((9, 8), (34, 3), (51, 2)), ((68, 0), (84, 0), (99, 2)),
        ((117, 3), (141, 8), (143, 19)), ((151, 40), (139, 75), (140, 112)),
        ((140, 156), (145, 207), (147, 236)), ((149, 246), (136, 248), (119, 249)),
        ((87, 250), (62, 250), (31, 249)), ((13, 248), (1, 246), (3, 236)),
        ((5, 207), (10, 156), (10, 112)), ((11, 75), (-1, 40), (7, 19))]);
    private static Img SleeveShape(int w, int h)
    {
        lock (sleeves)
            return sleeves.TryGetValue((w, h), out var cached) ? cached : sleeves[(w, h)] = Contour(w, h, (6, 1), [
                ((w * .3, 0), (w * .8, 0), (w - 4, 7)), ((w, h * .3), (w - 5, h * .7), (w - 8, h - 6)),
                ((w - 9, h), (w * .3, h), (10, h - 2)), ((6, h - 2), (5, h - 5), (5, h - 9)),
                ((4, h * .6), (0, h * .2), (6, 1))]);
    }
    private static Img ShortsShape() => shorts ??= Contour(230, 140, (27, 3), [
        ((67, 0), (163, 0), (203, 3)), ((209, 24), (218, 62), (218, 112)),
        ((219, 126), (218, 135), (207, 137)), ((184, 140), (159, 140), (139, 137)),
        ((132, 124), (125, 108), (115, 90)), ((105, 108), (98, 124), (91, 137)),
        ((71, 140), (46, 140), (23, 137)), ((12, 135), (11, 126), (12, 112)),
        ((12, 62), (21, 24), (27, 3))]);

    private static Img RemoveExposedSkin(Img im)
    {
        var p = (byte[])im.P.Clone();
        int w = im.W, h = im.H;
        var flesh = new bool[w * h];
        var skinRows = new bool[h];
        for (int y = 0; y < h; y++)
        {
            int visible = 0, exposed = 0;
            for (int x = 0; x < w; x++)
            {
                int n = (y * w + x) * 4;
                flesh[y * w + x] = Flesh(p[n], p[n + 1], p[n + 2]);
                if (p[n + 3] > 32) { visible++; if (flesh[y * w + x]) exposed++; }
            }
            skinRows[y] = exposed > Math.Max(2, visible * .25);
        }
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (flesh[y * w + x] || skinRows[y]) p[(y * w + x) * 4 + 3] = 0;
        int first = -1, last = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int n = (y * w + x) * 4;
                if (p[n + 3] > 32 && Math.Max(p[n], Math.Max(p[n + 1], p[n + 2])) > 40) { if (first < 0) first = y; last = y; break; }
            }
        if (first < 0) return new Img(w, h, 4);
        return new Img(w, h, 4, p).Crop(0, first, w, last + 1);
    }

    private static Img Upright(Img im)
    {
        // Principal axis of the opaque pixels (numpy.cov of (row, column) points, largest eigenvector).
        long count = 0; double sumRow = 0, sumCol = 0;
        for (int y = 0; y < im.H; y++) for (int x = 0; x < im.W; x++) if (im.P[(y * im.W + x) * 4 + 3] > 80) { count++; sumRow += y; sumCol += x; }
        if (count < 10) return im;
        double meanRow = sumRow / count, meanCol = sumCol / count, rr = 0, rc = 0, cc = 0;
        for (int y = 0; y < im.H; y++)
            for (int x = 0; x < im.W; x++)
                if (im.P[(y * im.W + x) * 4 + 3] > 80) { double dr = y - meanRow, dc = x - meanCol; rr += dr * dr; rc += dr * dc; cc += dc * dc; }
        rr /= count - 1; rc /= count - 1; cc /= count - 1;
        double angle = 0.5 * Math.Atan2(2 * rc, cc - rr) * (180.0 / Math.PI);
        while (angle < 0) angle += 180;
        im = im.Rotate(angle - 90);
        return im.BBox() is { } box ? im.Crop(box.X0, box.Y0, box.X1, box.Y1) : im;
    }

    private static void Put(Img canvas, Img im, int x, int y, int w, int h)
    {
        im = im.Thumbnail(w, h);
        canvas.AlphaComposite(im, (int)Math.Round(x + (w - im.W) / 2.0), (int)Math.Round(y + (h - im.H) / 2.0));
    }

    // ---- Pillow-compatible raster operations ----

    internal enum Filter { Nearest, Bicubic, Lanczos }

    /// <summary>An 8-bit image with 4 (RGBA, straight alpha) or 1 (L) channels, row-major from the top.</summary>
    internal sealed class Img(int w, int h, int c, byte[]? pixels = null)
    {
        public readonly int W = w, H = h, C = c;
        public readonly byte[] P = pixels ?? new byte[w * h * c];

        public Img Clone() => new(W, H, C, (byte[])P.Clone());
        public void Fill(byte value) => Array.Fill(P, value);

        public static Img FromBgra(SKBitmap atlas, int x0, int y0, int x1, int y1)
        {
            var im = new Img(x1 - x0, y1 - y0, 4);
            var src = atlas.GetPixelSpan();
            int stride = atlas.RowBytes;
            for (int y = Math.Max(y0, 0); y < Math.Min(y1, atlas.Height); y++)
                for (int x = Math.Max(x0, 0); x < Math.Min(x1, atlas.Width); x++)
                {
                    int s = y * stride + x * 4, d = ((y - y0) * im.W + x - x0) * 4;
                    im.P[d] = src[s + 2]; im.P[d + 1] = src[s + 1]; im.P[d + 2] = src[s]; im.P[d + 3] = src[s + 3];
                }
            return im;
        }

        public void Save(string file)
        {
            using var bitmap = new SKBitmap(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Unpremul));
            System.Runtime.InteropServices.Marshal.Copy(P, 0, bitmap.GetPixels(), P.Length);
            Png.Save(bitmap, file);
        }

        /// <summary>Image.getbbox: the box of non-zero alpha (RGBA) or value (L); null when empty.</summary>
        public (int X0, int Y0, int X1, int Y1)? BBox()
        {
            int x0 = W, y0 = H, x1 = -1, y1 = -1;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    if (P[(y * W + x) * C + C - 1] != 0) { if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; y1 = y; }
            return x1 < 0 ? null : (x0, y0, x1 + 1, y1 + 1);
        }

        /// <summary>Image.crop: areas outside the image come back transparent.</summary>
        public Img Crop(int x0, int y0, int x1, int y1)
        {
            var im = new Img(Math.Max(x1 - x0, 0), Math.Max(y1 - y0, 0), C);
            for (int y = Math.Max(y0, 0); y < Math.Min(y1, H); y++)
            {
                int from = Math.Max(x0, 0), to = Math.Min(x1, W);
                if (to > from) Buffer.BlockCopy(P, (y * W + from) * C, im.P, ((y - y0) * im.W + from - x0) * C, (to - from) * C);
            }
            return im;
        }

        /// <summary>Image.paste without a mask: replaces pixels, clipped to this image.</summary>
        public void Paste(Img im, int dx, int dy)
        {
            for (int y = Math.Max(dy, 0); y < Math.Min(dy + im.H, H); y++)
            {
                int from = Math.Max(dx, 0), to = Math.Min(dx + im.W, W);
                if (to > from) Buffer.BlockCopy(im.P, ((y - dy) * im.W + from - dx) * C, P, (y * W + from) * C, (to - from) * C);
            }
        }

        /// <summary>Image.alpha_composite(im, dest): Pillow's integer "over" with 7 extra bits.</summary>
        public void AlphaComposite(Img im, int dx, int dy)
        {
            for (int y = Math.Max(dy, 0); y < Math.Min(dy + im.H, H); y++)
                for (int x = Math.Max(dx, 0); x < Math.Min(dx + im.W, W); x++)
                {
                    int s = ((y - dy) * im.W + x - dx) * 4, d = (y * W + x) * 4;
                    uint sa = im.P[s + 3];
                    if (sa == 0) continue;
                    uint da = P[d + 3];
                    uint blend = da * (255 - sa), outa255 = sa * 255 + blend;
                    uint coef1 = sa * 255 * 255 * (1 << 7) / outa255, coef2 = 255 * (1 << 7) - coef1;
                    for (int k = 0; k < 3; k++)
                    {
                        uint tmp = im.P[s + k] * coef1 + P[d + k] * coef2 + (0x80 << 7);
                        P[d + k] = (byte)((((tmp >> 8) + tmp) >> 8) >> 7);
                    }
                    uint a = outa255 + 0x80;
                    P[d + 3] = (byte)(((a >> 8) + a) >> 8);
                }
        }

        /// <summary>putalpha(ImageChops.multiply(getchannel('A'), mask)).</summary>
        public void MultiplyAlpha(Img mask)
        {
            for (int i = 0; i < W * H; i++) P[i * 4 + 3] = (byte)(P[i * 4 + 3] * mask.P[i] / 255);
        }

        // Transpose.ROTATE_270 (a quarter turn clockwise).
        public Img Rotate270()
        {
            var im = new Img(H, W, C);
            for (int y = 0; y < im.H; y++) for (int x = 0; x < im.W; x++) Array.Copy(P, ((H - 1 - x) * W + y) * C, im.P, (y * im.W + x) * C, C);
            return im;
        }
        // Transpose.ROTATE_90 (a quarter turn counter-clockwise).
        public Img Rotate90()
        {
            var im = new Img(H, W, C);
            for (int y = 0; y < im.H; y++) for (int x = 0; x < im.W; x++) Array.Copy(P, (x * W + (W - 1 - y)) * C, im.P, (y * im.W + x) * C, C);
            return im;
        }
        public Img Rotate180()
        {
            var im = new Img(W, H, C);
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) Array.Copy(P, ((H - 1 - y) * W + W - 1 - x) * C, im.P, (y * W + x) * C, C);
            return im;
        }

        // RGBA ↔ RGBa (Pillow resamples and transforms RGBA with premultiplied alpha).
        private Img Premultiply()
        {
            var im = new Img(W, H, 4);
            for (int i = 0; i < P.Length; i += 4)
            {
                uint a = P[i + 3];
                for (int k = 0; k < 3; k++) { uint tmp = P[i + k] * a + 128; im.P[i + k] = (byte)(((tmp >> 8) + tmp) >> 8); }
                im.P[i + 3] = (byte)a;
            }
            return im;
        }
        private Img Unpremultiply()
        {
            var im = new Img(W, H, 4);
            for (int i = 0; i < P.Length; i += 4)
            {
                int a = P[i + 3];
                for (int k = 0; k < 3; k++) im.P[i + k] = a is 0 or 255 ? P[i + k] : (byte)Math.Min(255, 255 * P[i + k] / a);
                im.P[i + 3] = (byte)a;
            }
            return im;
        }

        /// <summary>Image.resize (no box, no reducing gap).</summary>
        public Img Resize(int w, int h, Filter filter)
        {
            if (w == W && h == H) return Clone();
            if (filter == Filter.Nearest) return Nearest(w, h);
            if (C == 4) return Premultiply().ResizeCore(w, h, filter).Unpremultiply();
            return ResizeCore(w, h, filter);
        }

        private Img ResizeCore(int w, int h, Filter filter)
        {
            // Very tall images are resized vertically first.
            if (H > W * 100 && h < H) return Resample(W, h, filter, 0, 0, W, H).Resample(w, h, filter, 0, 0, W, h);
            return Resample(w, h, filter, 0, 0, W, H);
        }

        // Nearest neighbour through Pillow's affine scale (accumulated source positions).
        private Img Nearest(int w, int h)
        {
            var im = new Img(w, h, C);
            double a0 = (double)W / w, a4 = (double)H / h;
            var xin = new int[w];
            double xo = a0 * 0.5;
            for (int x = 0; x < w; x++) { xin[x] = xo < 0 ? -1 : (int)xo; xo += a0; }
            double yo = a4 * 0.5;
            for (int y = 0; y < h; y++)
            {
                int yi = yo < 0 ? -1 : (int)yo;
                if (yi >= 0 && yi < H)
                    for (int x = 0; x < w; x++)
                        if (xin[x] >= 0 && xin[x] < W) Array.Copy(P, (yi * W + xin[x]) * C, im.P, (y * w + x) * C, C);
                yo += a4;
            }
            return im;
        }

        private const int PrecisionBits = 32 - 8 - 2;

        private static double BicubicKernel(double x)
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
        private static double LanczosKernel(double x) => -3.0 <= x && x < 3.0 ? Sinc(x) * Sinc(x / 3) : 0.0;

        // Resample.c precompute_coeffs + normalize_coeffs_8bpc.
        private static (int[] Bounds, int[] Kernel, int Size) Coefficients(int inSize, double in0, double in1, int outSize, Filter filter)
        {
            Func<double, double> fn = filter == Filter.Lanczos ? LanczosKernel : BicubicKernel;
            double scale = (in1 - in0) / outSize, filterscale = scale < 1.0 ? 1.0 : scale;
            double support = (filter == Filter.Lanczos ? 3.0 : 2.0) * filterscale;
            int ksize = (int)Math.Ceiling(support) * 2 + 1;
            var bounds = new int[outSize * 2];
            var weights = new double[outSize * ksize];
            for (int xx = 0; xx < outSize; xx++)
            {
                double center = in0 + (xx + 0.5) * scale, ww = 0.0, ss = 1.0 / filterscale;
                int xmin = (int)(center - support + 0.5);
                if (xmin < 0) xmin = 0;
                int xmax = (int)(center + support + 0.5);
                if (xmax > inSize) xmax = inSize;
                xmax -= xmin;
                for (int x = 0; x < xmax; x++) { double v = fn((x + xmin - center + 0.5) * ss); weights[xx * ksize + x] = v; ww += v; }
                for (int x = 0; x < xmax; x++) if (ww != 0.0) weights[xx * ksize + x] /= ww;
                bounds[xx * 2] = xmin; bounds[xx * 2 + 1] = xmax;
            }
            var kernel = new int[weights.Length];
            for (int i = 0; i < weights.Length; i++)
                kernel[i] = weights[i] < 0 ? (int)(-0.5 + weights[i] * (1 << PrecisionBits)) : (int)(0.5 + weights[i] * (1 << PrecisionBits));
            return (bounds, kernel, ksize);
        }

        private static byte Clip8(int v) => v >= (1 << PrecisionBits << 8) ? (byte)255 : v <= 0 ? (byte)0 : (byte)(v >> PrecisionBits);

        // Resample.c ImagingResampleInner: horizontal pass (8-bit intermediate), then vertical.
        private Img Resample(int w, int h, Filter filter, double bx0, double by0, double bx1, double by1)
        {
            if (bx0 == (int)bx0 && bx1 - bx0 == w && by0 == (int)by0 && by1 - by0 == h) return Crop((int)bx0, (int)by0, (int)bx1, (int)by1);
            bool horizontal = w != W || bx0 != 0 || bx1 != w, vertical = h != H || by0 != 0 || by1 != h;
            var (hb, hk, hs) = Coefficients(W, bx0, bx1, w, filter);
            var (vb, vk, vs) = Coefficients(H, by0, by1, h, filter);
            int first = vb[0], last = vb[h * 2 - 2] + vb[h * 2 - 1];
            Img current = this;
            if (horizontal)
            {
                for (int i = 0; i < h; i++) vb[i * 2] -= first;
                var temp = new Img(w, last - first, C);
                for (int yy = 0; yy < temp.H; yy++)
                    for (int xx = 0; xx < w; xx++)
                    {
                        int xmin = hb[xx * 2], xmax = hb[xx * 2 + 1], k = xx * hs;
                        for (int c = 0; c < C; c++)
                        {
                            int ss = 1 << (PrecisionBits - 1);
                            for (int x = 0; x < xmax; x++) ss += P[((yy + first) * W + x + xmin) * C + c] * hk[k + x];
                            temp.P[(yy * w + xx) * C + c] = Clip8(ss);
                        }
                    }
                current = temp;
            }
            if (vertical)
            {
                var result = new Img(current.W, h, C);
                for (int yy = 0; yy < h; yy++)
                {
                    int ymin = vb[yy * 2], ymax = vb[yy * 2 + 1], k = yy * vs;
                    for (int xx = 0; xx < current.W; xx++)
                        for (int c = 0; c < C; c++)
                        {
                            int ss = 1 << (PrecisionBits - 1);
                            for (int y = 0; y < ymax; y++) ss += current.P[((y + ymin) * current.W + xx) * C + c] * vk[k + y];
                            result.P[(yy * current.W + xx) * C + c] = Clip8(ss);
                        }
                }
                return result;
            }
            return current == this ? Clone() : current;
        }

        /// <summary>Image.thumbnail with LANCZOS (RGBA skips the reducing-gap step).</summary>
        public Img Thumbnail(int boxW, int boxH)
        {
            int x = boxW, y = boxH;
            if (x >= W && y >= H) return this;
            double aspect = (double)W / H;
            static int RoundAspect(double number, Func<int, double> key)
            {
                int lo = (int)Math.Floor(number), hi = (int)Math.Ceiling(number);
                return Math.Max(key(hi) < key(lo) ? hi : lo, 1);
            }
            if ((double)x / y >= aspect) x = RoundAspect(y * aspect, n => Math.Abs(aspect - (double)n / y));
            else y = RoundAspect(x / aspect, n => n == 0 ? 0 : Math.Abs(aspect - (double)x / n));
            return x == W && y == H ? this : Resize(x, y, Filter.Lanczos);
        }

        /// <summary>Image.rotate(angle, expand=True, resample=BICUBIC).</summary>
        public Img Rotate(double degrees)
        {
            double angle = degrees % 360.0;
            if (angle != 0 && angle < 0) angle += 360.0;
            if (angle == 0) return Clone();
            if (angle == 180) return Rotate180();
            if (angle == 90) return Rotate90();
            if (angle == 270) return Rotate270();
            int w = W, h = H;
            double cx = w / 2.0, cy = h / 2.0, radians = -(angle * (Math.PI / 180.0));
            double[] m = [PyRound15(Math.Cos(radians)), PyRound15(Math.Sin(radians)), 0.0, PyRound15(-Math.Sin(radians)), PyRound15(Math.Cos(radians)), 0.0];
            (double, double) Apply(double x, double y) => (m[0] * x + m[1] * y + m[2], m[3] * x + m[4] * y + m[5]);
            (m[2], m[5]) = Apply(-cx - 0, -cy - 0);
            m[2] += cx; m[5] += cy;
            double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
            foreach (var (px, py) in new (double, double)[] { (0, 0), (w, 0), (w, h), (0, h) })
            {
                var (tx, ty) = Apply(px, py);
                minX = Math.Min(minX, tx); maxX = Math.Max(maxX, tx); minY = Math.Min(minY, ty); maxY = Math.Max(maxY, ty);
            }
            int nw = (int)(Math.Ceiling(maxX) - Math.Floor(minX)), nh = (int)(Math.Ceiling(maxY) - Math.Floor(minY));
            (m[2], m[5]) = Apply(-(nw - w) / 2.0, -(nh - h) / 2.0);
            if (C != 4) return AffineBicubic(nw, nh, m);
            return Premultiply().AffineBicubic(nw, nh, m).Unpremultiply();
        }

        // Python's round(x, 15): correctly rounded through the decimal text.
        private static double PyRound15(double v) => double.Parse(v.ToString("F15", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

        // Geometry.c affine transform with the bicubic filter (a = -1); outside samples are transparent.
        private Img AffineBicubic(int w, int h, double[] a)
        {
            var im = new Img(w, h, C);
            var v = new double[4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    double xin0 = x + 0.5, yin0 = y + 0.5;
                    double xin = a[0] * xin0 + a[1] * yin0 + a[2], yin = a[3] * xin0 + a[4] * yin0 + a[5];
                    if (xin < 0.0 || xin >= W || yin < 0.0 || yin >= H) continue;
                    xin -= 0.5; yin -= 0.5;
                    int ix = xin < 0.0 ? (int)Math.Floor(xin) : (int)xin, iy = yin < 0.0 ? (int)Math.Floor(yin) : (int)yin;
                    double dx = xin - ix, dy = yin - iy;
                    ix--; iy--;
                    int x0 = Math.Clamp(ix, 0, W - 1), x1 = Math.Clamp(ix + 1, 0, W - 1), x2 = Math.Clamp(ix + 2, 0, W - 1), x3 = Math.Clamp(ix + 3, 0, W - 1);
                    for (int c = 0; c < C; c++)
                    {
                        double Row(int row) => Cubic(P[(row * W + x0) * C + c], P[(row * W + x1) * C + c], P[(row * W + x2) * C + c], P[(row * W + x3) * C + c], dx);
                        double v1 = Row(Math.Clamp(iy, 0, H - 1));
                        double v2 = iy + 1 >= 0 && iy + 1 < H ? Row(iy + 1) : v1;
                        double v3 = iy + 2 >= 0 && iy + 2 < H ? Row(iy + 2) : v2;
                        double v4 = iy + 3 >= 0 && iy + 3 < H ? Row(iy + 3) : v3;
                        double value = Cubic(v1, v2, v3, v4, dy);
                        im.P[(y * w + x) * C + c] = value <= 0.0 ? (byte)0 : value >= 255.0 ? (byte)255 : (byte)value;
                    }
                }
            return im;
        }
        private static double Cubic(double v1, double v2, double v3, double v4, double d)
        {
            double p1 = v2, p2 = -v1 + v3, p3 = 2 * (v1 - v2) + v3 - v4, p4 = -v1 + v2 - v3 + v4;
            return p1 + d * (p2 + d * (p3 + d * p4));
        }

        // ---- ImageDraw fills on an L image ----

        private void HLine(int x0, int y, int x1, byte ink)
        {
            if (y < 0 || y >= H) return;
            if (x0 < 0) x0 = 0; else if (x0 >= W) return;
            if (x1 < 0) return; else if (x1 >= W) x1 = W - 1;
            if (x0 <= x1) Array.Fill(P, ink, y * W + x0, x1 - x0 + 1);
        }

        private sealed class Edge
        {
            public int XMin, XMax, YMin, YMax, X0, Y0;
            public double Dx;
            public Edge(int x0, int y0, int x1, int y1)
            {
                (XMin, XMax) = x0 <= x1 ? (x0, x1) : (x1, x0);
                (YMin, YMax) = y0 <= y1 ? (y0, y1) : (y1, y0);
                Dx = y0 == y1 ? 0.0 : (double)(x1 - x0) / (y1 - y0);
                X0 = x0; Y0 = y0;
            }
        }

        private static int RoundUp(float f) => (int)(f >= 0.0 ? Math.Floor(f + 0.5F) : -Math.Floor(Math.Abs(f) + 0.5F));
        private static int RoundDown(float f) => (int)(f >= 0.0 ? Math.Ceiling(f - 0.5F) : -Math.Ceiling(Math.Abs(f) - 0.5F));

        /// <summary>ImageDraw.polygon(points, fill=ink) (Draw.c polygon_generic, scanlines at integer y).</summary>
        public void Polygon(IReadOnlyList<(int X, int Y)> xy, byte ink)
        {
            var edges = new List<Edge>();
            int i;
            for (i = 0; i < xy.Count - 1; i++)
            {
                var (x0, y0) = xy[i]; var (x1, y1) = xy[i + 1];
                if (y0 == y1 && i != 0 && y0 == xy[i - 1].Y)
                {
                    // A horizontal line that immediately follows another horizontal line.
                    var lastEdge = edges[^1];
                    if (x1 > x0 && x0 > xy[i - 1].X) { lastEdge.XMax = x1; continue; }
                    if (x1 < x0 && x0 < xy[i - 1].X) { lastEdge.XMin = x1; continue; }
                }
                edges.Add(new Edge(x0, y0, x1, y1));
            }
            if (xy[i] != xy[0]) edges.Add(new Edge(xy[i].X, xy[i].Y, xy[0].X, xy[0].Y));

            int ymin = H - 1, ymax = 0;
            var table = new List<Edge>();
            foreach (var e in edges)
            {
                if (ymin > e.YMin) ymin = e.YMin;
                if (ymax < e.YMax) ymax = e.YMax;
                if (e.YMin == e.YMax) { HLine(e.XMin, e.YMin, e.XMax, ink); continue; }
                table.Add(e);
            }
            if (ymin < 0) ymin = 0;
            if (ymax > H) ymax = H;
            var xx = new float[table.Count * 2];
            for (; ymin <= ymax; ymin++)
            {
                int j = 0;
                for (i = 0; i < table.Count; i++)
                {
                    var current = table[i];
                    if (ymin < current.YMin || ymin > current.YMax) continue;
                    xx[j++] = (float)((ymin - current.Y0) * current.Dx + current.X0);
                    if (ymin == current.YMax && ymin < ymax)
                    {
                        // Needed to draw consistent polygons
                        xx[j] = xx[j - 1];
                        j++;
                    }
                    else if (current.Dx != 0 && j % 2 == 1 && MathF.Round(xx[j - 1]) == xx[j - 1])
                    {
                        // Connect discontiguous corners
                        for (int k = 0; k < i; k++)
                        {
                            var other = table[k];
                            if ((current.Dx > 0 && other.Dx <= 0) || (current.Dx < 0 && other.Dx >= 0)) continue;
                            // Check if the two edges join to make a corner
                            if (MathF.Round(xx[j - 1]) == MathF.Round((float)((ymin - other.Y0) * other.Dx + other.X0)))
                            {
                                // Determine points from the edges on the next row, or the previous row on the last one.
                                int offset = ymin == current.YMax ? -1 : 1;
                                float adjacent = (float)((ymin + offset - current.Y0) * current.Dx + current.X0);
                                if (ymin + offset >= other.YMin && ymin + offset <= other.YMax)
                                {
                                    float adjacentOther = (float)((ymin + offset - other.Y0) * other.Dx + other.X0);
                                    if (xx[j - 1] > adjacent + 1 && xx[j - 1] > adjacentOther + 1)
                                        xx[j - 1] = MathF.Round(MathF.Max(adjacent, adjacentOther)) + 1;
                                    else if (xx[j - 1] < adjacent - 1 && xx[j - 1] < adjacentOther - 1)
                                        xx[j - 1] = MathF.Round(MathF.Min(adjacent, adjacentOther)) - 1;
                                    break;
                                }
                            }
                        }
                    }
                }
                Array.Sort(xx, 0, j);
                for (i = 1; i < j; i += 2)
                {
                    int start = RoundUp(xx[i - 1]), end = RoundDown(xx[i]);
                    if (end < start) continue;
                    HLine(start, ymin, end, ink);
                }
            }
        }

        /// <summary>ImageDraw.ellipse(box, fill=ink) (Draw.c ellipseNew: quarter-ellipse walk on a doubled grid).</summary>
        public void Ellipse(int x0, int y0, int x1, int y1, byte ink)
        {
            int a = x1 - x0, b = y1 - y0;
            if (a < 0 || b < 0) return;
            long a2 = (long)a * a, b2 = (long)b * b, a2b2 = a2 * b2;
            long Delta(long x, long y) => Math.Abs(a2 * y * y + b2 * x * x - a2b2);
            // Outer quarter (the filled ellipse has no inner one).
            int cx = a, cy = b % 2, ex = a % 2, ey = b;
            bool finished = false;
            bool Next(out int rx, out int ry)
            {
                rx = cx; ry = cy;
                if (finished) return false;
                if (cx == ex && cy == ey) finished = true;
                else
                {
                    int nx = cx, ny = cy + 2;
                    long nd = Delta(nx, ny);
                    if (nx > 1)
                    {
                        long d = Delta(cx - 2, cy + 2);
                        if (nd > d) { nx = cx - 2; ny = cy + 2; nd = d; }
                        d = Delta(cx - 2, cy);
                        if (nd > d) { nx = cx - 2; ny = cy; }
                    }
                    cx = nx; cy = ny;
                }
                return true;
            }
            int leftmost = a % 2;
            if (!Next(out int pr, out int py)) return;
            bool done = false;
            while (!done)
            {
                int y = py, l = leftmost, r = pr;
                int qx = 0, qy = 0; bool more;
                while ((more = Next(out qx, out qy)) && qy <= y) { }
                if (!more) done = true; else { pr = qx; py = qy; }
                var spans = new List<(int L, int Y, int R)>();
                if ((l > 0 || l < r) && y > 0) spans.Add((l == 0 ? 2 : l, y, r));
                if (y > 0) spans.Add((-r, y, -l));
                if (l > 0 || l < r) spans.Add((l == 0 ? 2 : l, -y, r));
                spans.Add((-r, -y, -l));
                for (int s = spans.Count - 1; s >= 0; s--)
                    HLine(x0 + (spans[s].L + a) / 2, y0 + (spans[s].Y + b) / 2, x0 + (spans[s].R + a) / 2, ink);
            }
        }
    }

    // ---- json.dumps(indent=2) as Python writes it on Windows (CRLF, ASCII escapes) ----

    private sealed class Json
    {
        private readonly StringBuilder text = new();
        private int depth;
        private void Line() { text.Append("\r\n").Append(' ', depth * 2); }
        public Json String(string value)
        {
            text.Append('"');
            foreach (char ch in value)
                text.Append(ch switch
                {
                    '"' => "\\\"", '\\' => "\\\\", '\n' => "\\n", '\r' => "\\r", '\t' => "\\t", '\b' => "\\b", '\f' => "\\f",
                    _ when ch < 0x20 || ch > 0x7e => "\\u" + ((int)ch).ToString("x4"),
                    _ => ch.ToString(),
                });
            text.Append('"');
            return this;
        }
        public Json Array<T>(IReadOnlyList<T> items, Action<Json, T> write)
        {
            if (items.Count == 0) { text.Append("[]"); return this; }
            text.Append('['); depth++;
            for (int i = 0; i < items.Count; i++) { if (i > 0) text.Append(','); Line(); write(this, items[i]); }
            depth--; Line(); text.Append(']');
            return this;
        }
        public Json Object(Action<Fields> write)
        {
            text.Append('{'); depth++;
            var fields = new Fields(this);
            write(fields);
            depth--;
            if (fields.Count > 0) Line();
            text.Append('}');
            return this;
        }
        public sealed class Fields(Json json)
        {
            public int Count;
            private Json Name(string name) { if (Count++ > 0) json.text.Append(','); json.Line(); json.String(name); json.text.Append(": "); return json; }
            public Fields Field(string name, string value) { Name(name).String(value); return this; }
            public Fields Field(string name, bool value) { Name(name).text.Append(value ? "true" : "false"); return this; }
            public Fields Field(string name, Action<Json> value) { value(Name(name)); return this; }
        }
        public override string ToString() => text.ToString();
    }
}
