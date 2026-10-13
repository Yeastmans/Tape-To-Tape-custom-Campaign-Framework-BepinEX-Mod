using System.Text.Json;
using SkiaSharp;
using CampaignCore;

namespace CampaignCreator;

// Authoring metadata only. Native attachment geometry stays in head-assets.json.
public sealed class HeadProject
{
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "";
    public bool SupportsHelmet { get; set; } = true;
    public bool SeparateHelmetArtwork { get; set; }
    public bool StartedFromScratch { get; set; }
    public string TemplateSkin { get; set; } = "Faces/Toronto/Mathieu";
    public string TemplateHelmetSkin { get; set; } = "Faces/Toronto/Mathieu_Helmet";
    public List<CustomHeadBinding> Bindings { get; set; } = [];
    public List<HeadImage> Images { get; set; } = [];
    [System.Text.Json.Serialization.JsonIgnore] public string Folder { get; set; } = "";
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    // Built from the player's install with the rest of the game art (StudioArt).
    public static string TemplateFolder => StudioArt.Path("head-templates", "Mathieu") ?? Path.Combine(StudioArt.Folder ?? AppContext.BaseDirectory, "head-templates", "Mathieu");
    public string FilePath(string relative)
    {
        string path = Path.GetFullPath(Path.Combine(Folder, relative));
        if (!path.StartsWith(Path.GetFullPath(Folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Head asset paths must stay inside the head folder.");
        return path;
    }
    public void Save()
    {
        string target = FilePath("head-project.json"), temp = target + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
        File.Move(temp, target, true);
    }
    public string Install(string contentRoot)
    {
        Save();
        var definition = CampaignCore.CustomHeadDefinition.Read(Folder);
        string target = Path.GetFullPath(Path.Combine(contentRoot, "custom-heads", CreatorContent.ValidateName(Name)));
        if (target.Equals(Path.GetFullPath(Folder), StringComparison.OrdinalIgnoreCase)) return target;
        string parent = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(parent);
        string stage = Path.Combine(parent, ".install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            // Copy only project assets, avoiding unrelated files in an opened folder.
            var files = Images.SelectMany(i => new[] { i.File, i.BoundsFile, i.SourceFile, OutlineFile(i) })
                .Concat(new[] { "head-project.json", "README.txt" }).Where(p => p.Length > 0).Distinct();
            foreach (string relative in files)
            {
                string source = FilePath(relative);
                if (!File.Exists(source)) continue;
                string dest = Path.Combine(stage, relative); Directory.CreateDirectory(Path.GetDirectoryName(dest)!); File.Copy(source, dest);
            }
            if (Directory.Exists(FilePath("reference")))
                foreach (string source in Directory.GetFiles(FilePath("reference"), "*", SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(Folder, source);
                    string dest = Path.Combine(stage, relative); Directory.CreateDirectory(Path.GetDirectoryName(dest)!); File.Copy(source, dest, true);
                }
            CampaignCore.CustomHeadDefinition.Read(stage);
            string? backup = null;
            if (Directory.Exists(target))
            {
                string backups = Path.Combine(contentRoot, ".head-backups"); Directory.CreateDirectory(backups);
                backup = Path.Combine(backups, Name + "-" + Guid.NewGuid().ToString("N")); Directory.Move(target, backup);
            }
            try { MoveNewProject(stage, target); }
            catch { if (backup != null) Directory.Move(backup, target); throw; }
            return target;
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }
    public static HeadProject Open(string folder)
    {
        var p = JsonSerializer.Deserialize<HeadProject>(File.ReadAllText(Path.Combine(folder, "head-project.json")), Json)
            ?? throw new InvalidDataException("Invalid head project.");
        p.Folder = Path.GetFullPath(folder);
        if ((p.Version != 1 && p.Version != 2) || p.Images.Count == 0) throw new InvalidDataException("Unsupported or empty head project.");
        foreach (var image in p.Images)
        {
            p.FilePath(image.File); p.FilePath(image.BoundsFile);
            if (image.Width < 1 || image.Height < 1 || image.Width > 4096 || image.Height > 4096)
                throw new InvalidDataException("Invalid head image dimensions.");
        }
        return p;
    }
    public static HeadProject Create(string parent, string name)
    {
        name = CreatorContent.ValidateName(name);
        string destination = Path.Combine(parent, name);
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("A head with that name already exists.");
        if (!Directory.Exists(TemplateFolder)) throw new IOException("The head template hasn't been read from your game yet. Use Read game artwork on Home with Tape to Tape installed.");
        Directory.CreateDirectory(parent);
        string stage = Path.Combine(parent, ".head-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            var p = new HeadProject { Name = name, Folder = stage };
            // Preserve the native source and mappings independently of normalized authoring PNGs.
            foreach (string file in Directory.GetFiles(TemplateFolder, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(stage, "reference", Path.GetRelativePath(TemplateFolder, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
            }
            foreach (var (direction, label) in new[] { ("S", "Front"), ("N", "Back"), ("W", "Side · mirrored for right"), ("NE", "Back diagonal · mirrored"), ("SW", "Front diagonal · mirrored") })
                p.AddImage(Path.Combine(TemplateFolder, "frames", direction + "-Helmet_Top.png"), "frames/" + direction + ".png", label, "Head");
            p.AddImage(Path.Combine(TemplateFolder, "portrait.png"), "portrait.png", "Portrait", "Portrait");
            foreach (string file in Directory.GetFiles(Path.Combine(TemplateFolder, "support"), "*.png"))
                p.AddImage(file, "support/" + Path.GetFileName(file), Path.GetFileNameWithoutExtension(file), "Support");
            File.WriteAllText(p.FilePath("README.txt"), "Head artwork project: " + name + "\n\nEdit the PNGs using their native outline guides. Keep the canvas size and transparency.\nOutlines trace the original head and neck at their canvas position; inspect changed artwork and helmet fit in game.\nNative geometry and original assets are preserved under reference/.\nThe portrait and all directions are edited separately.\nUse Add to player head options in Head Editor to install. Restart the game after editing. Requires the custom-head plugin build.\n");
            p.Save(); Directory.Move(stage, destination); p.Folder = destination; return p;
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }

    public static HeadProject CreateFromTemplate(string parent, string name, string nativeSkin, bool fromScratch = false)
    {
        name = CreatorContent.ValidateName(name);
        string destination = Path.Combine(parent, name);
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("A head with that name already exists.");
        var catalog = NativeHeadTemplates.Read();
        var selected = catalog.SingleOrDefault(t => t.Skin == nativeSkin)
            ?? throw new IOException("Read game artwork on Home to load the native head templates.");
        var bare = catalog.Single(t => t.Skin == selected.BaseSkin);
        if (!bare.Available) throw new IOException("The selected native head is unavailable: " + bare.Error);
        var fitted = catalog.SingleOrDefault(t => t.Skin == selected.HelmetSkin && t.Available);
        Directory.CreateDirectory(parent);
        string stage = Path.Combine(parent, ".head-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
        try
        {
            var p = new HeadProject { Version = 2, Name = name, Folder = stage, TemplateSkin = bare.Skin,
                TemplateHelmetSkin = fitted?.Skin ?? "", SupportsHelmet = fitted != null, SeparateHelmetArtwork = fitted != null,
                StartedFromScratch = fromScratch };
            AddNative(bare, false); if (fitted != null) AddNative(fitted, true);
            p.AddImage(selected.File("portrait.png"), "portrait.png", "Portrait", "Portrait",
                selected.IsHelmet ? "reference/Helmet/portrait.png" : "reference/Bare/portrait.png");
            if (fromScratch)
            {
                // Keep native size/placement metadata and guides while starting
                // every editable direction, layer and portrait without artwork.
                using var blank = new SKBitmap(512, 512, SKColorType.Rgba8888, SKAlphaType.Premul);
                blank.Erase(SKColors.Transparent);
                foreach (var item in p.Images) WritePng(blank, p.FilePath(item.File));
            }
            File.WriteAllText(p.FilePath("README.txt"), "Head artwork project: " + name + "\nNative template: " + bare.Label
                + "\n\nEdit the 512 x 512 PNGs, preserving their canvas and transparency. Native artwork sizes are shown in Head Editor.\n"
                + (fromScratch ? "Started from scratch: editable PNGs and the portrait are blank. Use native head outlines and collar references to draw or import your artwork.\n" : "Started with the selected native artwork. Replace or edit the PNGs to make your own head.\n")
                + "The game retains this template's attachment size, placement, mirroring and bone weights. Visible replacement artwork is fitted to the native example.\n"
                + "Directions may have different images, an alternate back image or an extra top layer; edit every required card.\n"
                + "Under-helmet artwork is separate. The game supplies the selectable helmet.\n"
                + "Original native images, attachment data and eight body placement examples are under reference/Bare and reference/Helmet.\n"
                + "The portrait is separate. Install with Add to player head options, then restart the game.\n");
            p.Save(); CustomHeadDefinition.Read(stage);
            MoveNewProject(stage, destination); p.Folder = destination; return p;
            void AddNative(NativeHeadTemplate template, bool helmet)
            {
                string group = helmet ? "Helmet" : "Head", reference = helmet ? "reference/Helmet" : "reference/Bare";
                string nativeFolder = Path.GetDirectoryName(template.File("head-assets.json"))!;
                foreach (string source in Directory.GetFiles(nativeFolder, "*", SearchOption.AllDirectories))
                {
                    string dest = p.FilePath(reference + "/" + Path.GetRelativePath(nativeFolder, source));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!); File.Copy(source, dest);
                }
                var assets = template.Assets();
                foreach (var image in assets["images"]!.AsArray())
                {
                    string file = image!["file"]!.GetValue<string>(), direction = image["direction"]!.GetValue<string>();
                    string edit = helmet ? "helmet/" + file : file;
                    string label = DirectionLabel(direction) + (image["primary"]!.GetValue<bool>() ? "" : " · top layer");
                    p.AddImage(template.File(file), edit, label, group, reference + "/" + file, direction, true);
                }
                foreach (var binding in assets["bindings"]!.AsArray())
                    p.Bindings.Add(new CustomHeadBinding { Slot = binding!["slot"]!.GetValue<string>(),
                        Placeholder = binding["placeholder"]!.GetValue<string>(), Helmet = helmet,
                        File = (helmet ? "helmet/" : "") + binding["file"]!.GetValue<string>(),
                        NativeWidth = binding["width"]!.GetValue<float>(), NativeHeight = binding["height"]!.GetValue<float>() });
            }
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }

    public static string DirectionLabel(string direction) => direction switch {
        "S" => "Front", "N" => "Back", "N2" => "Alternate back", "W" => "Left side", "E" => "Right side",
        "NE" => "Back right", "NW" => "Back left", "SW" => "Front left", "SE" => "Front right", _ => direction };
    static void MoveNewProject(string source, string destination)
    {
        // Windows scanners may briefly hold a newly written project folder.
        // Retry only while the destination is still absent; never replace it.
        for (int attempt = 0; ; attempt++)
        {
            try { Directory.Move(source, destination); return; }
            catch (IOException) when (attempt < 4 && Directory.Exists(source)
                && !Directory.Exists(destination) && !File.Exists(destination))
            { Thread.Sleep(100 * (attempt + 1)); }
        }
    }
    void AddImage(string source, string file, string label, string group, string? sourceFile = null, string direction = "", bool nativePlacement = false)
    {
        using var original = SKBitmap.Decode(source) ?? throw new InvalidDataException("Cannot read template PNG.");
        const int size = 512;
        float scale = Math.Min(1f, (size - 8f) / Math.Max(original.Width, original.Height));
        int width = (int)Math.Round(original.Width * scale), height = (int)Math.Round(original.Height * scale);
        int left = (size - width) / 2, top = (size - height) / 2;
        var item = new HeadImage { File = file, SourceFile = "reference/" + Path.GetRelativePath(TemplateFolder, source).Replace('\\', '/'), Label = label, Group = group, Width = size, Height = size,
            NativeWidth = original.Width, NativeHeight = original.Height, Left = left, Top = top, ContentWidth = width, ContentHeight = height,
            BoundsFile = "outlines/" + file, Direction = direction };
        if (sourceFile != null) item.SourceFile = sourceFile;
        using var canvasImage = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(canvasImage))
        {
            canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint { FilterQuality = SKFilterQuality.High };
            canvas.DrawBitmap(original, new SKRect(left, top, left + width, top + height), paint);
        }
        WritePng(canvasImage, FilePath(file));
        if (nativePlacement)
        {
            int l = size, t = size, r = 0, b = 0;
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                if (canvasImage.GetPixel(x, y).Alpha > 32) { l = Math.Min(l, x); t = Math.Min(t, y); r = Math.Max(r, x + 1); b = Math.Max(b, y + 1); }
            if (r > l && b > t) { item.HasNativePlacement = true; item.NativeVisibleLeft = l; item.NativeVisibleTop = t;
                item.NativeVisibleWidth = r - l; item.NativeVisibleHeight = b - t; }
        }
        using var outline = HeadOutline.Trace(canvasImage);
        WritePng(outline, FilePath(item.BoundsFile)); Images.Add(item);
    }
    static string OutlineFile(HeadImage item) => "outlines/" + item.File.Replace('\\', '/');
    public SKBitmap NativeOutline(HeadImage item)
    {
        if (!Images.Contains(item)) throw new ArgumentException("Unknown image slot.");
        if (string.IsNullOrWhiteSpace(item.SourceFile)) throw new InvalidDataException("This head has no original reference PNG for its outline.");
        using var original = SKBitmap.Decode(FilePath(item.SourceFile)) ?? throw new InvalidDataException("Cannot read the original reference PNG for its outline.");
        using var normalized = new SKBitmap(item.Width, item.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(normalized))
        {
            canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint { FilterQuality = SKFilterQuality.High };
            canvas.DrawBitmap(original, new SKRect(item.Left, item.Top, item.Left + item.ContentWidth, item.Top + item.ContentHeight), paint);
        }
        return HeadOutline.Trace(normalized);
    }
    public string ExportOutline(HeadImage item)
    {
        using var outline = NativeOutline(item);
        string path = FilePath(OutlineFile(item)); WritePng(outline, path); return path;
    }
    public string ExportOutlineGuides()
    {
        foreach (var item in Images) ExportOutline(item);
        return FilePath("outlines");
    }
    static void WritePng(SKBitmap bitmap, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path); data.SaveTo(stream);
    }
    public void EnsureHelmetImages()
    {
        if (Images.Any(i => i.Group == "Helmet")) return;
        foreach (var bare in Images.Where(i => i.Group == "Head").ToArray())
        {
            var fitted = JsonSerializer.Deserialize<HeadImage>(JsonSerializer.Serialize(bare))!;
            fitted.Group = "Helmet"; fitted.File = "helmet/" + Path.GetFileName(bare.File); fitted.BoundsFile = OutlineFile(fitted);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath(fitted.File))!);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath(fitted.BoundsFile))!);
            File.Copy(FilePath(bare.File), FilePath(fitted.File), true);
            Images.Add(fitted);
            ExportOutline(fitted);
        }
    }
    // Slot names accepted for imported PNG file names (case, spaces, dashes and underscores ignored).
    static readonly (string File, string[] Names)[] SlotNames =
    [
        ("frames/S.png", ["s", "front", "face"]), ("frames/N.png", ["n", "back"]), ("frames/W.png", ["w", "side", "profile"]),
        ("frames/NE.png", ["ne", "backdiagonal", "backdiag"]), ("frames/SW.png", ["sw", "frontdiagonal", "frontdiag"]), ("portrait.png", ["portrait"]),
    ];
    public HeadImage? SlotFor(string pngPath)
    {
        string stem = new string(Path.GetFileNameWithoutExtension(pngPath).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        if (Version == 2)
        {
            string direction = stem switch { "s" or "front" or "face" => "S", "n" or "back" => "N",
                "n2" or "alternateback" => "N2", "w" or "side" or "profile" or "left" => "W", "e" or "right" => "E",
                "ne" or "backdiagonal" or "backdiag" or "backright" => "NE", "nw" or "backleft" => "NW",
                "sw" or "frontdiagonal" or "frontdiag" or "frontleft" => "SW", "se" or "frontright" => "SE", _ => "" };
            if (direction.Length > 0)
            {
                string placeholder = direction == "N2" ? "N-Helmet2" : direction + "-Helmet";
                var binding = Bindings.FirstOrDefault(b => !b.Helmet && b.Slot == "Helmet" && b.Placeholder == placeholder);
                return binding == null ? null : Images.First(i => i.File == binding.File);
            }
        }
        foreach (var (file, names) in SlotNames)
            if (names.Contains(stem)) return Images.FirstOrDefault(i => i.File == file);
        return null;
    }
    // Imports PNGs of any size: trims transparent margins (or uses the whole image when opaque) and fits the artwork
    // into the slot's template rectangle on the 512 canvas. Returns a human-readable summary.
    public string Import(IEnumerable<string> files)
    {
        var done = new List<string>(); var skipped = new List<string>(); bool portraitDone = false; HeadImage? front = null;
        foreach (string source in files)
        {
            var slot = SlotFor(source);
            if (slot == null) { skipped.Add(Path.GetFileName(source)); continue; }
            FitInto(slot, source); done.Add(slot.Label);
            if (slot.File == "portrait.png") portraitDone = true; else if (slot.File == "frames/S.png") front = slot;
        }
        if (front != null && !portraitDone && Images.FirstOrDefault(i => i.File == "portrait.png") is { } portrait)
        {
            string pPath = FilePath(portrait.File); if (File.Exists(pPath)) File.Copy(pPath, pPath + ".bak", true);
            File.Copy(FilePath(front.File), pPath, true); done.Add("Portrait (from front)");
        }
        Save();
        string text = done.Count == 0 ? "No PNGs imported." : "Imported " + string.Join(", ", done) + ".";
        if (skipped.Count > 0) text += " Skipped " + string.Join(", ", skipped) + " — name files Front, Back, Side, Back diagonal, Front diagonal or Portrait.";
        return text;
    }
    void FitInto(HeadImage item, string source)
    {
        using var decoded = SKBitmap.Decode(source) ?? throw new InvalidDataException(Path.GetFileName(source) + " is not a readable PNG.");
        int x0 = decoded.Width, y0 = decoded.Height, x1 = -1, y1 = -1;
        for (int y = 0; y < decoded.Height; y++) for (int x = 0; x < decoded.Width; x++)
            if (decoded.GetPixel(x, y).Alpha > 32) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
        if (x1 < x0 || y1 < y0) throw new InvalidDataException(Path.GetFileName(source) + " has no visible artwork.");
        int w = x1 - x0 + 1, h = y1 - y0 + 1;
        float scale = Math.Min((item.ContentWidth - 16f) / w, (item.ContentHeight - 16f) / h);
        float dw = w * scale, dh = h * scale;
        using var canvasImage = new SKBitmap(item.Width, item.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(canvasImage))
        {
            canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint { FilterQuality = SKFilterQuality.High };
            float left = item.Left + (item.ContentWidth - dw) / 2, top = item.Top + (item.ContentHeight - dh) / 2;
            canvas.DrawBitmap(decoded, new SKRect(x0, y0, x1 + 1, y1 + 1), new SKRect(left, top, left + dw, top + dh), paint);
        }
        string path = FilePath(item.File), temp = path + ".tmp";
        WritePng(canvasImage, temp);
        if (File.Exists(path)) File.Copy(path, path + ".bak", true);
        File.Move(temp, path, true);
    }
    public void Replace(HeadImage item, string source)
    {
        if (!Images.Contains(item)) throw new ArgumentException("Unknown image slot.");
        using var stream = File.OpenRead(source);
        using var codec = SKCodec.Create(stream) ?? throw new InvalidDataException("Choose a valid PNG image.");
        if (codec.EncodedFormat != SKEncodedImageFormat.Png) throw new InvalidDataException("Choose a PNG image.");
        if (codec.Info.Width != item.Width || codec.Info.Height != item.Height)
            throw new InvalidDataException($"Use a {item.Width} × {item.Height} PNG. Keep the supplied canvas size.");
        using var decoded = SKBitmap.Decode(codec) ?? throw new InvalidDataException("Cannot decode this PNG.");
        string path = FilePath(item.File), temp = path + ".tmp";
        WritePng(decoded, temp);
        if (File.Exists(path)) File.Copy(path, path + ".bak", true);
        File.Move(temp, path, true);
    }
}

public sealed class HeadImage
{
    public string File { get; set; } = "";
    public string SourceFile { get; set; } = "";
    public string BoundsFile { get; set; } = "";
    public string Label { get; set; } = "";
    public string Group { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public int NativeWidth { get; set; }
    public int NativeHeight { get; set; }
    public int Left { get; set; }
    public int Top { get; set; }
    public int ContentWidth { get; set; }
    public int ContentHeight { get; set; }
    public string Direction { get; set; } = "";
    public bool HasNativePlacement { get; set; }
    public int NativeVisibleLeft { get; set; }
    public int NativeVisibleTop { get; set; }
    public int NativeVisibleWidth { get; set; }
    public int NativeVisibleHeight { get; set; }
}
