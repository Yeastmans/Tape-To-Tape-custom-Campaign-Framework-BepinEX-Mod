using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using CampaignCore;

namespace CampaignCreator;

internal sealed partial class TeamEditorView
{
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern bool PlaySound(string? sound, IntPtr module, uint flags);
    private Control GoalSoundControls()
    {
        string name = doc.Get("Goal Sound"), encoded = doc.Get("Goal Sound Data");
        var caption = new TextBlock { Text = name.Length == 0 ? "Base game sound" : name };
        var feedback = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var volume = new Slider { Minimum = 0, Maximum = 100, Value = double.TryParse(doc.Get("Goal Sound Volume", "100"), out var v) ? Math.Clamp(v, 0, 100) : 100, Width = 250 };
        var level = new TextBlock { Text = $"Volume: {volume.Value:0}%" };
        string? preview = null;
        void Stop() { if (OperatingSystem.IsWindows()) PlaySound(null, IntPtr.Zero, 0); if (preview != null) { try { File.Delete(preview); } catch { } preview = null; } }
        Track("Goal Sound", () => name); Track("Goal Sound Data", () => encoded);
        Track("Goal Sound Volume", () => Math.Round(volume.Value).ToString(System.Globalization.CultureInfo.InvariantCulture));
        volume.PropertyChanged += (_, e) => { if (e.Property == Slider.ValueProperty) { level.Text = $"Volume: {volume.Value:0}%"; changed(); } };
        var lifetime = new CancellationTokenSource();
        bool importing = false;
        var choose = ActionButton("Choose audio file", async () =>
        {
            if (importing) return;
            var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Choose goal sound", AllowMultiple = false, FileTypeFilter = new[] { new FilePickerFileType("Audio files") { Patterns = new[] { "*.wav", "*.mp3", "*.ogg", "*.opus", "*.flac", "*.m4a", "*.aac", "*.wma", "*.aif", "*.aiff", "*.alac", "*.ac3", "*.amr", "*.ape", "*.mka" } }, new FilePickerFileType("All files") { Patterns = new[] { "*" } } } });
            if (files.Count == 0) return;
            try
            {
                importing = true; feedback.Text = "Converting audio�";
                using var input = await files[0].OpenReadAsync();
                byte[] bytes = await GoalAudioImport.ConvertAsync(input, lifetime.Token);
                lifetime.Token.ThrowIfCancellationRequested();
                var wave = GoalSoundWave.Decode(bytes);
                Stop(); name = files[0].Name; encoded = Convert.ToBase64String(bytes); caption.Text = name;
                feedback.Text = $"{wave.Samples.Length / (double)(wave.Channels * wave.Rate):0.0} seconds. Audio is saved with the team and included in exports."; changed();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { feedback.Text = ex.Message; }
            finally { importing = false; }
        });
        var play = ActionButton("Preview", () =>
        {
            try
            {
                Stop(); if (encoded.Length == 0) { feedback.Text = "Choose a sound first."; return; }
                if (!OperatingSystem.IsWindows()) { feedback.Text = "Audio preview is available on Windows."; return; }
                byte[] bytes = Convert.FromBase64String(encoded); GoalSoundWave.Decode(bytes);
                // Apply the authored volume to the preview's PCM samples too.
                using (var stream = new MemoryStream(bytes)) using (var reader = new BinaryReader(stream))
                {
                    stream.Position = 12;
                    while (stream.Position + 8 <= stream.Length)
                    {
                        string tag = System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4)); uint size = reader.ReadUInt32(); long start = stream.Position;
                        if (tag == "data") for (long i = start; i < start + size; i += 2) { short sample = (short)((short)(bytes[i] | bytes[i + 1] << 8) * volume.Value / 100); bytes[i] = (byte)sample; bytes[i + 1] = (byte)(sample >> 8); }
                        stream.Position = start + size + (size & 1);
                    }
                }
                preview = Path.Combine(Path.GetTempPath(), "t2t-goal-" + Guid.NewGuid().ToString("N") + ".wav"); File.WriteAllBytes(preview, bytes);
                if (!PlaySound(preview, IntPtr.Zero, 0x20003)) throw new Exception("Windows could not play this sound.");
            }
            catch (Exception ex) { feedback.Text = ex.Message; }
        });
        DetachedFromVisualTree += (_, _) => { lifetime.Cancel(); Stop(); };
        caption.VerticalAlignment = level.VerticalAlignment = VerticalAlignment.Center;
        return new StackPanel { Spacing = StudioTheme.SpaceXs, Children = {
            Label("Goal celebration", 14),
            Label("Plays when this team scores, including your selected custom squad. Replaces the base-game goal sound. Import MP3, WAV, OGG, FLAC, M4A and other audio formats. Choose a clip up to 60 seconds; it is converted automatically and saved with the team.", 12),
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = StudioTheme.SpaceSm, Children = { choose, play, ActionButton("Stop", Stop), ActionButton("Use base game sound", () => { Stop(); name = encoded = ""; caption.Text = "Base game sound"; feedback.Text = ""; changed(); }), caption } },
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = StudioTheme.SpaceMd, Children = { level, volume } }, feedback } };
    }
}
