using System.Diagnostics;
using CampaignCore;

namespace CampaignCreator;

public static class GoalAudioImport
{
    public const long MaxSourceBytes = 256L * 1024 * 1024;
    public static async Task<byte[]> ConvertAsync(Stream input, CancellationToken cancellation = default)
    {
        string converter = Path.Combine(AppContext.BaseDirectory, "audio-tools", OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
        if (!File.Exists(converter))
        {
            converter = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
                .Concat(OperatingSystem.IsMacOS() ? new[] { "/opt/homebrew/bin", "/usr/local/bin" } : Array.Empty<string>())
                .Where(p => Path.IsPathRooted(p)).Select(p => Path.Combine(p, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg"))
                .FirstOrDefault(File.Exists) ?? throw new FileNotFoundException("Install FFmpeg to import audio, or place its executable in the audio-tools folder alongside Campaign Studio.");
        }
        string folder = Path.Combine(Path.GetTempPath(), "t2t-audio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            string source = Path.Combine(folder, "input"), output = Path.Combine(folder, "goal.wav");
            await using (var file = File.Create(source))
            {
                var buffer = new byte[65536]; long total = 0; int count;
                while ((count = await input.ReadAsync(buffer.AsMemory(), timeout.Token)) > 0)
                {
                    total += count;
                    if (total > MaxSourceBytes) throw new ArgumentException("Choose an audio file smaller than 256 MB.");
                    await file.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
                }
            }
            var start = new ProcessStartInfo(converter) { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardError = true, RedirectStandardOutput = true };
            // Decode at most 61 seconds, then validate the 60-second limit. Do
            // not silently truncate an overlong track into a different sound.
            foreach (string arg in new[] { "-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-protocol_whitelist", "file,pipe", "-i", source,
                "-map", "0:a:0", "-vn", "-t", "61", "-ac", "2", "-ar", "48000", "-c:a", "pcm_s16le", "-f", "wav", output }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new IOException("Could not start the audio converter.");
            var errors = process.StandardError.ReadToEndAsync();
            var stdout = process.StandardOutput.ReadToEndAsync();
            try { await process.WaitForExitAsync(timeout.Token); }
            catch { if (!process.HasExited) process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); throw; }
            string error = await errors; await stdout;
            if (process.ExitCode != 0 || !File.Exists(output))
                throw new ArgumentException("This file could not be decoded as audio. It may be damaged, protected, or use an unsupported codec." + (error.Length == 0 ? "" : "\n" + error[..Math.Min(error.Length, 500)]));
            byte[] bytes = await File.ReadAllBytesAsync(output, timeout.Token);
            GoalSoundWave.Decode(bytes);
            return bytes;
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { throw new TimeoutException("Audio conversion took too long. Try a shorter audio clip."); }
        finally { Directory.Delete(folder, recursive: true); }
    }
}
