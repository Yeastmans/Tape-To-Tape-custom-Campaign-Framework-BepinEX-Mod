using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CampaignCore;

namespace GameArt;

/// <summary>
/// The Studio's copy of the game artwork, built from the player's own install and rebuilt when the
/// game (or the extractors) change. Lives in %LOCALAPPDATA%\T2T Campaign Studio\game-art\&lt;fingerprint&gt;.
/// </summary>
public static class ArtCache
{
    // Bump when any extractor's output changes, so existing caches are rebuilt.
    public const int FormatVersion = 5;
    private const string ManifestName = "manifest.json";
    private static readonly object BuildGate = new();

    public static string BaseFolder => Environment.GetEnvironmentVariable("T2T_GAME_ART_CACHE") is { Length: > 0 } folder
        ? Path.GetFullPath(folder) : Path.Combine(UserContentPaths.Root, "game-art");

    public static IReadOnlyList<IArtExtractor> Extractors { get; } = typeof(IArtExtractor).Assembly.GetTypes()
        .Where(t => typeof(IArtExtractor).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false })
        .Select(t => (IArtExtractor)Activator.CreateInstance(t)!).OrderBy(e => e.Folder, StringComparer.Ordinal).ToArray();

    /// <summary>Identifies the installed game build and the extractor set. Cheap: sizes and dates only.</summary>
    public static string Fingerprint(string gameRoot)
    {
        var text = new StringBuilder("format=" + FormatVersion + "\n");
        void Add(string path) { var info = new FileInfo(path); text.Append(info.Name).Append('|').Append(info.Exists ? info.Length + "|" + info.LastWriteTimeUtc.Ticks : "missing").Append('\n'); }
        string data = Path.Combine(gameRoot, "Tape to Tape_Data");
        Add(Path.Combine(data, "data.unity3d"));
        Add(Path.Combine(gameRoot, "GameAssembly.dll"));
        Add(Path.Combine(data, "il2cpp_data", "Metadata", "global-metadata.dat"));
        string bundles = Path.Combine(data, "StreamingAssets", "aa", "StandaloneWindows64");
        if (Directory.Exists(bundles))
            foreach (string file in Directory.GetFiles(bundles, "*.bundle").Order(StringComparer.Ordinal)) Add(file);
        foreach (var extractor in Extractors) text.Append(extractor.GetType().FullName).Append('\n');
        foreach (string map in typeof(ArtCache).Assembly.GetManifestResourceNames().Order(StringComparer.Ordinal))
        {
            using var stream = typeof(ArtCache).Assembly.GetManifestResourceStream(map)!;
            text.Append(map).Append('|').Append(Convert.ToHexString(SHA256.HashData(stream))).Append('\n');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..16].ToLowerInvariant();
    }

    /// <summary>The finished cache for this game build, or null when it still has to be built.</summary>
    public static string? Ready(string gameRoot)
    {
        string folder = Path.Combine(BaseFolder, Fingerprint(gameRoot));
        return Complete(folder) ? folder : null;
    }

    private static JsonObject? ReadManifest(string folder)
    {
        try { return JsonNode.Parse(File.ReadAllText(Path.Combine(folder, ManifestName))) as JsonObject; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    private static bool Complete(string folder)
    {
        var manifest = ReadManifest(folder);
        if (manifest?["format"] is not JsonValue format || !format.TryGetValue<int>(out int version) || version != FormatVersion
            || manifest["extractors"] is not JsonArray results || results.Count != Extractors.Count) return false;
        return Extractors.All(e => results.OfType<JsonObject>().Count(r =>
            r["folder"]?.ToString() == e.Folder && r["ok"] is JsonValue ok && ok.TryGetValue<bool>(out bool success) && success) == 1
            && HasFiles(Path.Combine(folder, e.Folder)));
    }

    private static bool HasFiles(string folder)
    {
        try { return Directory.Exists(folder) && Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Any(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>The most recently built cache, whichever game build it came from (used when the game can't be found).</summary>
    public static string? Latest() => Directory.Exists(BaseFolder)
        ? Directory.GetDirectories(BaseFolder).Where(d => Regex.IsMatch(Path.GetFileName(d), "^[0-9a-f]{16}$") && ReadManifest(d) != null)
            .OrderByDescending(d => File.GetLastWriteTimeUtc(Path.Combine(d, ManifestName))).FirstOrDefault()
        : null;

    /// <summary>Extractors that failed in a built cache, with their error.</summary>
    public static IReadOnlyList<(string Folder, string Error)> Failures(string cache)
    {
        var manifest = ReadManifest(cache);
        if (manifest?["extractors"] is not JsonArray results) return [("cache", "The artwork manifest is missing or unreadable.")];
        var failures = new List<(string Folder, string Error)>();
        foreach (var extractor in Extractors)
        {
            var rows = results.OfType<JsonObject>().Where(r => r["folder"]?.ToString() == extractor.Folder).ToArray();
            if (rows.Length != 1) failures.Add((extractor.Folder, "The artwork manifest has a missing or repeated result."));
            else if (rows[0]["ok"] is not JsonValue ok || !ok.TryGetValue<bool>(out bool success) || !success)
                failures.Add((extractor.Folder, rows[0]["error"]?.ToString() ?? "The artwork could not be read."));
            else if (!HasFiles(Path.Combine(cache, extractor.Folder))) failures.Add((extractor.Folder, "The extracted artwork is missing."));
        }
        return failures;
    }

    /// <summary>
    /// Builds the cache for this game build (staged, then moved into place) and removes older builds.
    /// An extractor that fails is recorded in the manifest; the others still complete.
    /// </summary>
    public static string Build(string gameRoot, IProgress<string>? progress = null, CancellationToken cancellation = default, bool force = false)
    {
        lock (BuildGate) return BuildCore(gameRoot, progress, cancellation, force);
    }

    private static string BuildCore(string gameRoot, IProgress<string>? progress, CancellationToken cancellation, bool force)
    {
        if (!UnityGame.LooksLikeGame(gameRoot)) throw new DirectoryNotFoundException("Tape to Tape was not found in " + gameRoot);
        string fingerprint = Fingerprint(gameRoot), target = Path.Combine(BaseFolder, fingerprint);
        if (!force && Complete(target)) return target;
        string staging = Path.Combine(BaseFolder, ".building-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            cancellation.ThrowIfCancellationRequested();
            var results = new JsonArray();
            progress?.Report("Opening the game files");
            using (var game = new UnityGame(gameRoot))
            {
                foreach (var extractor in Extractors)
                {
                    cancellation.ThrowIfCancellationRequested();
                    progress?.Report(extractor.Description);
                    string folder = Path.Combine(staging, extractor.Folder);
                    Directory.CreateDirectory(folder);
                    try { extractor.Extract(game, folder, progress); results.Add(new JsonObject { ["folder"] = extractor.Folder, ["ok"] = true }); }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // Never expose half-written files from a failed extractor to the editor.
                        TryDelete(folder);
                        results.Add(new JsonObject { ["folder"] = extractor.Folder, ["ok"] = false, ["error"] = ex.GetType().Name + ": " + ex.Message });
                    }
                }
            }
            cancellation.ThrowIfCancellationRequested();
            if (Fingerprint(gameRoot) != fingerprint)
                throw new IOException("Tape to Tape changed while its artwork was being read. Try again after the game update finishes.");
            var manifest = new JsonObject
            {
                ["fingerprint"] = fingerprint, ["format"] = FormatVersion, ["built"] = DateTime.UtcNow.ToString("O"),
                ["game"] = gameRoot, ["extractors"] = results
            };
            File.WriteAllText(Path.Combine(staging, ManifestName), manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            string backup = Path.Combine(BaseFolder, ".previous-" + Guid.NewGuid().ToString("N"));
            bool hadTarget = Directory.Exists(target);
            if (hadTarget) Directory.Move(target, backup);
            try { Directory.Move(staging, target); }
            catch { if (hadTarget) Directory.Move(backup, target); throw; }
            if (hadTarget) TryDelete(backup);
        }
        catch { TryDelete(staging); throw; }
        if (Complete(target))
            foreach (string old in Directory.GetDirectories(BaseFolder).Where(d => Regex.IsMatch(Path.GetFileName(d), "^[0-9a-f]{16}$")
                && ReadManifest(d) != null && !Path.GetFullPath(d).Equals(Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)))
                TryDelete(old);
        return target;
    }

    private static void TryDelete(string folder)
    {
        string path = Path.GetFullPath(folder), allowed = Path.GetFullPath(BaseFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Artwork cleanup outside the cache: " + path);
        try
        {
            if (Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0) Directory.Delete(path, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Finds the Tape to Tape install: the folders the installer recorded, then Steam's libraries.
    /// </summary>
    public static string? FindGame(params string[] recordedFiles)
    {
        foreach (string file in recordedFiles.Append(Path.Combine(UserContentPaths.Root, "game-folder.txt")))
        {
            try
            {
                if (!File.Exists(file)) continue;
                string folder = File.ReadAllText(file).Trim();
                if (folder.Length > 0 && UnityGame.LooksLikeGame(folder)) return folder;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        foreach (string library in SteamLibraries())
        {
            string folder = Path.Combine(library, "steamapps", "common", "Tape to Tape");
            if (UnityGame.LooksLikeGame(folder)) return folder;
        }
        return null;
    }

    /// <summary>Records a folder the player picked so later launches find it.</summary>
    public static void Remember(string gameRoot)
    {
        Directory.CreateDirectory(UserContentPaths.Root);
        File.WriteAllText(Path.Combine(UserContentPaths.Root, "game-folder.txt"), gameRoot);
    }

    private static IEnumerable<string> SteamLibraries()
    {
        var roots = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                if (key?.GetValue("SteamPath") is string path) roots.Add(path.Replace('/', '\\'));
            }
            catch (System.Security.SecurityException) { }
            roots.Add(@"C:\Program Files (x86)\Steam");
        }
        else
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            roots.Add(Path.Combine(home, ".steam", "steam"));
            roots.Add(Path.Combine(home, ".local", "share", "Steam"));
        }
        foreach (string root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            yield return root;
            string vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;
            string text;
            try { text = File.ReadAllText(vdf); } catch (IOException) { continue; }
            foreach (Match match in Regex.Matches(text, "\"path\"\\s*\"([^\"]+)\""))
                yield return match.Groups[1].Value.Replace(@"\\", @"\");
        }
    }
}
