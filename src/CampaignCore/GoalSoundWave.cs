#nullable disable
using System;
using System.IO;
using System.Text;

namespace CampaignCore;

public static class GoalSoundWave
{
    public const int MaxBytes = 12 * 1024 * 1024;
    public sealed class Wave { public float[] Samples; public int Channels, Rate; public Wave(float[] samples, int channels, int rate) { Samples = samples; Channels = channels; Rate = rate; } }
    public static Wave Decode(byte[] bytes)
    {
        if (bytes.Length > MaxBytes) throw new ArgumentException("Goal sounds must be 12 MB or smaller.");
        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        string Tag() => Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (Tag() != "RIFF") throw new ArgumentException("Choose a PCM WAV file.");
        uint length = reader.ReadUInt32();
        if (Tag() != "WAVE" || length + 8L != bytes.Length) throw new ArgumentException("Invalid WAV file length.");
        int format = 0, channels = 0, rate = 0, bits = 0, align = 0;
        byte[] data = null;
        while (stream.Position + 8 <= stream.Length)
        {
            string tag = Tag(); uint size = reader.ReadUInt32(); long end = stream.Position + size;
            if (end > stream.Length) throw new ArgumentException("Truncated WAV file.");
            if (tag == "fmt ")
            {
                if (size < 16) throw new ArgumentException("Invalid WAV format.");
                format = reader.ReadUInt16(); channels = reader.ReadUInt16(); rate = reader.ReadInt32();
                reader.ReadUInt32(); align = reader.ReadUInt16(); bits = reader.ReadUInt16();
            }
            else if (tag == "data") data = reader.ReadBytes((int)size);
            stream.Position = end + (size & 1);
        }
        if (format != 1 || (channels != 1 && channels != 2) || bits != 16 || rate < 8000 || rate > 96000 || align != channels * 2)
            throw new ArgumentException("Use a 16-bit PCM WAV, mono or stereo, 8–96 kHz.");
        if (data == null || data.Length == 0 || data.Length % align != 0 || data.Length / (double)(align * rate) > 60)
            throw new ArgumentException("Goal sounds must contain audio and be at most 60 seconds long.");
        var samples = new float[data.Length / 2];
        for (int i = 0; i < samples.Length; i++) samples[i] = (short)(data[i * 2] | data[i * 2 + 1] << 8) / 32768f;
        return new Wave(samples, channels, rate);
    }
}

