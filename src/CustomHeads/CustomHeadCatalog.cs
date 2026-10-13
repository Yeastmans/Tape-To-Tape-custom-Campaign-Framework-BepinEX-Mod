#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CampaignCore;

// Shared authoring/runtime contract, linked into the net8 editor and net6 plugin.
public sealed class CustomHeadDefinition
{
    public int Version { get; set; }
    public string Name { get; set; } = "";
    public bool SupportsHelmet { get; set; }
    public bool SeparateHelmetArtwork { get; set; }
    public string TemplateSkin { get; set; } = "Faces/Toronto/Mathieu";
    public string TemplateHelmetSkin { get; set; } = "Faces/Toronto/Mathieu_Helmet";
    public List<CustomHeadBinding> Bindings { get; set; } = new();
    public List<CustomHeadImage> Images { get; set; } = new();
    [System.Text.Json.Serialization.JsonIgnore] public string Folder { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore] public string Skin => "Faces/CustomHeads/" + Name;
    public string Asset(string relative)
    {
        string full = Path.GetFullPath(Path.Combine(Folder, relative));
        if (!full.StartsWith(Path.GetFullPath(Folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Head asset must be inside its project folder.");
        return full;
    }
    public CustomHeadImage Direction(string direction, bool helmet)
    {
        if (Version == 2)
        {
            string placeholder = direction == "N2" ? "N-Helmet2" : direction + "-Helmet";
            var binding = Bindings.Single(b => b.Helmet == helmet && b.Slot == "Helmet" && b.Placeholder == placeholder);
            return Images.Single(i => i.File == binding.File);
        }
        string file = direction switch { "E" => "W", "NW" => "NE", "SE" => "SW", _ => direction };
        string group = helmet && SeparateHelmetArtwork ? "Helmet" : "Head";
        return Images.Single(i => i.Group == group && Path.GetFileNameWithoutExtension(i.File) == file);
    }
    public static CustomHeadDefinition Read(string folder)
    {
        string manifest = Path.Combine(folder, "head-project.json");
        if (new FileInfo(manifest).Length > 256 * 1024) throw new InvalidDataException("Head manifest is too large.");
        var d = JsonSerializer.Deserialize<CustomHeadDefinition>(File.ReadAllText(manifest)) ?? throw new InvalidDataException("Invalid head project.");
        d.Folder = Path.GetFullPath(folder);
        if ((d.Version != 1 && d.Version != 2) || string.IsNullOrWhiteSpace(d.Name) || d.Name.Length > 80 || d.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || d.Name.Contains('/') || d.Name.Contains('\\') || d.Name.EndsWith(".") || d.Name.EndsWith("_Helmet", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Invalid custom head name or version.");
        foreach (string dir in d.Version == 2 ? new[] { "S", "N", "W", "E", "NE", "NW", "SW", "SE", "N2" } : new[] { "S", "N", "W", "NE", "SW" })
        {
            Validate(d.Direction(dir, false));
            if (d.SupportsHelmet) Validate(d.Direction(dir, true));
        }
        Validate(d.Images.Single(i => i.Group == "Portrait"));
        if (d.Version == 2)
        {
            if (!NativeSkin(d.TemplateSkin) || (d.SupportsHelmet && !NativeSkin(d.TemplateHelmetSkin)) || d.Bindings.Count > 128)
                throw new InvalidDataException("Invalid native head template.");
            if (d.Bindings.GroupBy(b => (b.Helmet, b.Slot, b.Placeholder)).Any(g => g.Count() != 1))
                throw new InvalidDataException("Duplicate native head attachment binding.");
            foreach (var binding in d.Bindings)
            {
                if (binding.Slot != "Helmet" && binding.Slot != "Helmet_Top") throw new InvalidDataException("Invalid head attachment slot.");
                if (string.IsNullOrWhiteSpace(binding.Placeholder) || binding.Placeholder.Length > 100)
                    throw new InvalidDataException("Invalid head attachment name.");
                var image = d.Images.Single(i => i.File == binding.File);
                if (image.Group != (binding.Helmet ? "Helmet" : "Head")) throw new InvalidDataException("Head binding uses the wrong image group.");
                Validate(image);
            }
        }
        return d;
        bool NativeSkin(string skin) => skin != null && skin.StartsWith("Faces/", StringComparison.Ordinal)
            && !skin.StartsWith("Faces/CustomHeads/", StringComparison.Ordinal) && !skin.Contains("..") && skin.Length <= 200;
        void Validate(CustomHeadImage image)
        {
            if (image.Width != 512 || image.Height != 512 || image.NativeWidth < 1 || image.NativeHeight < 1 || image.NativeWidth > 2048 || image.NativeHeight > 2048
                || image.Left < 0 || image.Top < 0 || image.ContentWidth < 1 || image.ContentHeight < 1
                || image.Left + image.ContentWidth > 512 || image.Top + image.ContentHeight > 512)
                throw new InvalidDataException("Head PNGs require valid 512 x 512 canvas placement.");
            if (image.HasNativePlacement && (image.NativeVisibleLeft < 0 || image.NativeVisibleTop < 0
                || image.NativeVisibleWidth <= 0 || image.NativeVisibleHeight <= 0
                || image.NativeVisibleLeft + image.NativeVisibleWidth > 512 || image.NativeVisibleTop + image.NativeVisibleHeight > 512))
                throw new InvalidDataException("Invalid native visible artwork size.");
            ValidatePng(d.Asset(image.File));
        }
    }
    public static void ValidatePng(string file)
    {
        using var f = File.OpenRead(file);
        var bytes = new byte[24];
        if (f.Length > 8 * 1024 * 1024 || f.Read(bytes, 0, bytes.Length) != bytes.Length
            || !bytes.Take(8).SequenceEqual(new byte[] { 137,80,78,71,13,10,26,10 })
            || bytes[12] != 73 || bytes[13] != 72 || bytes[14] != 68 || bytes[15] != 82
            || bytes[16] != 0 || bytes[17] != 0 || bytes[18] != 2 || bytes[19] != 0
            || bytes[20] != 0 || bytes[21] != 0 || bytes[22] != 2 || bytes[23] != 0)
            throw new InvalidDataException("Expected a 512 x 512 PNG: " + Path.GetFileName(file));
    }
}
public sealed class CustomHeadImage
{
    public string File { get; set; } = "";
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

public sealed class CustomHeadBinding
{
    public string Slot { get; set; } = "";
    public string Placeholder { get; set; } = "";
    public string File { get; set; } = "";
    public bool Helmet { get; set; }
    public float NativeWidth { get; set; }
    public float NativeHeight { get; set; }
}

// Align the visible artwork, rather than the transparent PNG canvas, to the
// native collar. The returned sampling rectangle leaves the attachment and its
// bone weights intact; the native mirrored attachments handle E/NW/SE.
public static class HeadArtworkPlacement
{
    public readonly struct Sampling
    {
        public readonly float Left, Top, Width, Height, Scale, MoveX, MoveY;
        public Sampling(float left, float top, float width, float height, float scale, float moveX, float moveY)
        { Left = left; Top = top; Width = width; Height = height; Scale = scale; MoveX = moveX; MoveY = moveY; }
    }

    public static Sampling Align(CustomHeadImage image, string direction, int left, int top, int right, int bottom)
    {
        if (left < 0 || top < 0 || right > image.Width || bottom > image.Height || right <= left || bottom <= top)
            throw new InvalidDataException("Head artwork has no valid visible bounds.");
        direction = direction switch { "E" => "W", "NW" => "NE", "SE" => "SW", "N2" => "N", _ => direction };
        // Neck positions in the Mathieu attachment's upright image rectangle.
        // Side images have a diagonal neck cut behind the chin, so its anchor
        // is behind the midpoint of the visible head.
        var anchor = direction switch
        {
            "S" => (targetX: .50f, targetY: .95f, sourceX: .50f, sourceY: .98f),
            "N" => (targetX: .50f, targetY: .98f, sourceX: .50f, sourceY: .98f),
            "W" => (targetX: .70f, targetY: .91f, sourceX: .60f, sourceY: .94f),
            "NE" => (targetX: .50f, targetY: .97f, sourceX: .50f, sourceY: .96f),
            "SW" => (targetX: .57f, targetY: .93f, sourceX: .55f, sourceY: .96f),
            _ => throw new InvalidDataException("Unknown head direction: " + direction)
        };
        float width = right - left, height = bottom - top;
        if (image.HasNativePlacement)
        {
            // New projects use the selected native head's visible size and
            // anchor. The untouched example maps exactly to its original art.
            float nativeScale = Math.Min(image.NativeVisibleHeight / height,
                Math.Min(image.ContentWidth / width, image.ContentHeight / height));
            float nativeX = image.NativeVisibleLeft + image.NativeVisibleWidth * anchor.sourceX;
            float nativeY = image.NativeVisibleTop + image.NativeVisibleHeight * anchor.sourceY;
            float dx = nativeX - (left + width * anchor.sourceX) * nativeScale;
            float dy = nativeY - (top + height * anchor.sourceY) * nativeScale;
            dx = Math.Max(image.Left - left * nativeScale, Math.Min(image.Left + image.ContentWidth - right * nativeScale, dx));
            dy = Math.Max(image.Top - top * nativeScale, Math.Min(image.Top + image.ContentHeight - bottom * nativeScale, dy));
            return new Sampling((image.Left - dx) / nativeScale, (image.Top - dy) / nativeScale,
                image.ContentWidth / nativeScale, image.ContentHeight / nativeScale, nativeScale, dx, dy);
        }
        // The template rectangle also has transparent margins. Match the
        // native head's visible height, rather than the entire image rectangle.
        // In particular, the back view otherwise disappears into the shoulders.
        float visibleHeight = direction switch { "S" => .805f, "N" => .985f, "W" => .797f, "NE" => .822f, "SW" => .862f, _ => 1f };
        float scale = Math.Min(image.ContentHeight * visibleHeight / height,
            Math.Min(image.ContentWidth / width, image.ContentHeight / height));
        float moveX = image.Left + image.ContentWidth * anchor.targetX - (left + width * anchor.sourceX) * scale;
        float moveY = image.Top + image.ContentHeight * anchor.targetY - (top + height * anchor.sourceY) * scale;
        // Keep all visible pixels inside the native quad, including unusually
        // broad or tall replacements. Sampling can extend into transparent
        // canvas margins, but the artwork itself is never clipped.
        moveX = Math.Max(image.Left - left * scale, Math.Min(image.Left + image.ContentWidth - right * scale, moveX));
        moveY = Math.Max(image.Top - top * scale, Math.Min(image.Top + image.ContentHeight - bottom * scale, moveY));
        return new Sampling((image.Left - moveX) / scale, (image.Top - moveY) / scale,
            image.ContentWidth / scale, image.ContentHeight / scale, scale, moveX, moveY);
    }
}
public static class CustomHeadCatalog
{
    public static List<CustomHeadDefinition> Load(string contentRoot, Action<string> warning = null)
    {
        var result = new List<CustomHeadDefinition>();
        string root = Path.Combine(contentRoot, "custom-heads");
        if (!Directory.Exists(root)) return result;
        foreach (string folder in Directory.GetDirectories(root))
        {
            if (!File.Exists(Path.Combine(folder, "head-project.json"))) continue;
            try
            {
                var head = CustomHeadDefinition.Read(folder);
                if (result.Any(h => h.Skin.Equals(head.Skin, StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("Duplicate custom head name.");
                result.Add(head); HeadAppearance.RegisterCustom(head.Skin, head.SupportsHelmet);
            }
            catch (Exception ex) { warning?.Invoke(Path.GetFileName(folder) + ": " + ex.Message); }
        }
        return result;
    }
}
