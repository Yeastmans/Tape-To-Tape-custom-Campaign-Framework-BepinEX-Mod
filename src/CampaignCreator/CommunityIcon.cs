using SkiaSharp;

namespace CampaignCreator;

/// <summary>Creates a bounded upload thumbnail from an uploader's PNG without changing the source.</summary>
internal static class CommunityIcon
{
    internal static string LoadPng(string path)
    {
        if (!Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Choose a PNG file for your campaign icon.");
        using var stream = File.OpenRead(path);
        if (stream.Length > 16 * 1024 * 1024)
            throw new InvalidDataException("Choose a PNG smaller than 16 MB.");
        using var codec = SKCodec.Create(stream) ?? throw new InvalidDataException("This PNG could not be read.");
        if (codec.EncodedFormat != SKEncodedImageFormat.Png)
            throw new InvalidDataException("Choose a PNG image. Renaming another image type does not convert it.");
        int width = codec.Info.Width, height = codec.Info.Height;
        if (width < 1 || height < 1 || width > 8192 || height > 8192 || (long)width * height > 16_777_216)
            throw new InvalidDataException("Choose a PNG up to 16 megapixels, with neither side over 8,192 pixels.");
        using var decoded = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (codec.GetPixels(decoded.Info, decoded.GetPixels()) != SKCodecResult.Success)
            throw new InvalidDataException("This PNG is incomplete or damaged. Choose another PNG.");
        // 256 px normally fits; noisy RGBA images may need the smaller size to meet the upload limit.
        foreach (int maximum in new[] { 256, 192 })
        {
            double scale = Math.Min(1d, (double)maximum / Math.Max(width, height));
            int w = Math.Max(1, (int)Math.Round(width * scale)), h = Math.Max(1, (int)Math.Round(height * scale));
            using var thumbnail = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(thumbnail))
            using (var paint = new SKPaint { FilterQuality = SKFilterQuality.High })
            {
                canvas.Clear(SKColors.Transparent);
                canvas.DrawBitmap(decoded, new SKRect(0, 0, w, h), paint);
            }
            using var image = SKImage.FromBitmap(thumbnail);
            using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            string value = "data:image/png;base64," + Convert.ToBase64String(png.ToArray());
            if (value.Length <= 350000)
                return new CommunityPresentation("Campaign", Icon: value).ValidateForUpload("Campaign").Icon;
        }
        throw new InvalidDataException("This PNG could not be made small enough to upload.");
    }
}
