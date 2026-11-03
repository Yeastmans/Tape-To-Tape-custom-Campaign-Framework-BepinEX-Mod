using System.Buffers.Binary;
using System.Text;
using SkiaSharp;

namespace CampaignCreator;

/// <summary>Writes editable, 8-bit RGB Photoshop documents without an editor dependency.</summary>
public static class LayeredPsd
{
    public sealed record Layer(string Name, SKBitmap Bitmap, bool Visible = true);

    /// <summary>
    /// Writes equal-sized layers in bottom-to-top order. The caller retains ownership
    /// of the bitmaps. Hidden layers are preserved but excluded from the merged preview.
    /// An existing destination is replaced only after the complete file has been flushed.
    /// </summary>
    public static void Write(string destination, IReadOnlyList<Layer> layers)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentNullException.ThrowIfNull(layers);
        if (layers.Count is < 1 or > short.MaxValue)
            throw new ArgumentException("A PSD must contain between 1 and 32767 layers.", nameof(layers));

        int width = 0, height = 0;
        foreach (var layer in layers)
        {
            if (layer?.Name == null || layer.Bitmap == null || layer.Bitmap.Handle == IntPtr.Zero)
                throw new ArgumentException("Every layer needs a name and a valid bitmap.", nameof(layers));
            int w = layer.Bitmap.Width, h = layer.Bitmap.Height;
            if (w is < 1 or > 30000 || h is < 1 or > 30000 || (long)w * h * 4 > Array.MaxLength)
                throw new ArgumentException("Layer dimensions exceed the supported PSD v1 image size.", nameof(layers));
            if (width != 0 && (w != width || h != height))
                throw new ArgumentException("All layers must have the same dimensions.", nameof(layers));
            if (layer.Bitmap.GetPixels() == IntPtr.Zero)
                throw new ArgumentException("Every layer needs readable bitmap pixels.", nameof(layers));
            width = w;
            height = h;
        }

        // Store unassociated color channels: transparent pixels may contain colors that
        // the artist needs later. Drawing onto a premultiplied canvas would lose them.
        var encoded = new List<EncodedLayer>(layers.Count);
        var composite = new float[checked(width * height * 4)];
        foreach (var layer in layers)
        {
            byte[] rgba = ReadRgba(layer.Bitmap);
            if (layer.Visible) Composite(composite, rgba);
            encoded.Add(new(layer.Name, layer.Visible, EncodeChannels(rgba, width, height)));
        }
        byte[][] merged = EncodeChannels(MergedPreview(composite), width, height);

        string fullPath = Path.GetFullPath(destination);
        string temporary = Path.Combine(Path.GetDirectoryName(fullPath)!, "." + Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                WriteDocument(file, width, height, encoded, merged);
                file.Flush(flushToDisk: true);
            }
            // The temporary file is on the same volume, so replacement is a single
            // filesystem rename. A failed write or replacement leaves the old file intact.
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private sealed record EncodedLayer(string Name, bool Visible, byte[][] Channels);

    private static byte[] ReadRgba(SKBitmap bitmap)
    {
        using var converted = new SKBitmap(bitmap.Width, bitmap.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var source = bitmap.PeekPixels();
        if (source == null || !source.ReadPixels(converted.Info, converted.GetPixels(), converted.RowBytes))
            throw new IOException("Could not read layer pixels as RGBA.");
        return converted.GetPixelSpan().ToArray();
    }

    private static void Composite(float[] destination, byte[] source)
    {
        for (int i = 0; i < source.Length; i += 4)
        {
            float alpha = source[i + 3] / 255f, remainder = 1 - alpha;
            for (int c = 0; c < 3; c++)
                destination[i + c] = source[i + c] * alpha + destination[i + c] * remainder;
            destination[i + 3] = alpha + destination[i + 3] * remainder;
        }
    }

    private static byte[] MergedPreview(float[] composite)
    {
        var rgba = new byte[composite.Length];
        for (int i = 0; i < rgba.Length; i += 4)
        {
            // Photoshop's compatibility preview stores RGB over white, alongside
            // the transparency plane. Layer channel data above stays straight alpha.
            float white = 255 * (1 - composite[i + 3]);
            for (int c = 0; c < 3; c++) rgba[i + c] = RoundByte(composite[i + c] + white);
            rgba[i + 3] = RoundByte(composite[i + 3] * 255);
        }
        return rgba;
    }

    private static byte RoundByte(float value) => (byte)Math.Clamp((int)(value + .5f), 0, 255);

    private static byte[][] EncodeChannels(byte[] rgba, int width, int height)
    {
        var channels = new byte[4][];
        var row = new byte[width];
        var packed = new byte[width + (width + 127) / 128];
        for (int channel = 0; channel < 4; channel++)
        {
            using var data = new MemoryStream();
            WriteU16(data, 1); // PackBits RLE.
            data.Write(new byte[height * 2]);
            var lengths = new ushort[height];
            for (int y = 0; y < height; y++)
            {
                int source = y * width * 4 + channel;
                for (int x = 0; x < width; x++) row[x] = rgba[source + x * 4];
                int length = PackRow(row, packed);
                lengths[y] = checked((ushort)length);
                data.Write(packed.AsSpan(0, length));
            }
            data.Position = 2;
            foreach (ushort length in lengths) WriteU16(data, length);
            channels[channel] = data.ToArray();
        }
        return channels;
    }

    private static int PackRow(ReadOnlySpan<byte> row, Span<byte> output)
    {
        int source = 0, target = 0;
        while (source < row.Length)
        {
            int run = RepeatedCount(row, source);
            if (run >= 3)
            {
                output[target++] = (byte)(257 - run);
                output[target++] = row[source];
                source += run;
            }
            else
            {
                int start = source++;
                while (source < row.Length && source - start < 128 && RepeatedCount(row, source) < 3)
                    source++;
                int count = source - start;
                output[target++] = (byte)(count - 1);
                row.Slice(start, count).CopyTo(output[target..]);
                target += count;
            }
        }
        return target;
    }

    private static int RepeatedCount(ReadOnlySpan<byte> row, int start)
    {
        int length = 1;
        while (length < 128 && start + length < row.Length && row[start + length] == row[start]) length++;
        return length;
    }

    // PSD v1 structure: https://www.adobe.com/devnet-apps/photoshop/fileformatashtml/
    private static void WriteDocument(Stream file, int width, int height, IReadOnlyList<EncodedLayer> layers, byte[][] merged)
    {
        WriteAscii(file, "8BPS");
        WriteU16(file, 1);
        file.Write(new byte[6]);
        WriteU16(file, 4);
        WriteU32(file, (uint)height);
        WriteU32(file, (uint)width);
        WriteU16(file, 8);
        WriteU16(file, 3); // RGB.
        WriteU32(file, 0); // No color mode data or image resources.
        WriteU32(file, 0);
        WriteBlock(file, () =>
        {
            WriteBlock(file, () =>
            {
                // Negative layer count designates the merged image's fourth channel
                // as transparency, not an unrelated saved selection.
                WriteU16(file, unchecked((ushort)-layers.Count));
                foreach (var layer in layers) WriteLayerRecord(file, width, height, layer);
                foreach (var layer in layers)
                    foreach (byte[] channel in layer.Channels) file.Write(channel);
            }, padToEven: true);
            WriteU32(file, 0); // No global layer mask.
        });

        WriteU16(file, 1);
        // The merged image has one RLE header, all channel row lengths, then data.
        foreach (byte[] channel in merged) file.Write(channel.AsSpan(2, height * 2));
        foreach (byte[] channel in merged) file.Write(channel.AsSpan(2 + height * 2));
    }

    private static void WriteLayerRecord(Stream file, int width, int height, EncodedLayer layer)
    {
        WriteU32(file, 0); // Top and left.
        WriteU32(file, 0);
        WriteU32(file, (uint)height);
        WriteU32(file, (uint)width);
        WriteU16(file, 4);
        for (int channel = 0; channel < 4; channel++)
        {
            WriteU16(file, channel == 3 ? ushort.MaxValue : (ushort)channel); // -1 is layer transparency.
            WriteU32(file, (uint)layer.Channels[channel].Length);
        }
        WriteAscii(file, "8BIMnorm");
        file.WriteByte(255); // Full opacity, normal blend, no clipping.
        file.WriteByte(0);
        file.WriteByte(layer.Visible ? (byte)8 : (byte)10); // Bit 1 means hidden; bit 3 validates bit 4.
        file.WriteByte(0);
        WriteBlock(file, () =>
        {
            WriteU32(file, 0); // No layer mask or blending ranges.
            WriteU32(file, 0);
            byte[] fallbackName = Encoding.ASCII.GetBytes(layer.Name[..Math.Min(layer.Name.Length, 255)]);
            file.WriteByte((byte)fallbackName.Length);
            file.Write(fallbackName);
            Pad(file, fallbackName.Length + 1, 4);
            WriteAscii(file, "8BIMluni");
            WriteBlock(file, () =>
            {
                byte[] unicode = Encoding.BigEndianUnicode.GetBytes(layer.Name);
                WriteU32(file, (uint)(unicode.Length / 2));
                file.Write(unicode);
                Pad(file, 4 + unicode.Length, 4);
            });
        });
    }

    private static void WriteBlock(Stream file, Action write, bool padToEven = false)
    {
        long start = file.Position;
        WriteU32(file, 0);
        write();
        if (padToEven && (file.Position - start - 4) % 2 != 0) file.WriteByte(0);
        long end = file.Position;
        uint length = checked((uint)(end - start - 4));
        file.Position = start;
        WriteU32(file, length);
        file.Position = end;
    }

    private static void Pad(Stream file, int length, int alignment)
    {
        while (length++ % alignment != 0) file.WriteByte(0);
    }

    private static void WriteAscii(Stream file, string text) => file.Write(Encoding.ASCII.GetBytes(text));

    private static void WriteU16(Stream file, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        file.Write(bytes);
    }

    private static void WriteU32(Stream file, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        file.Write(bytes);
    }
}
