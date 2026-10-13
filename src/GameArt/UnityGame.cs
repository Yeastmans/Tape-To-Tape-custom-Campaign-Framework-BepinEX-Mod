using AssetsTools.NET;
using AssetsTools.NET.Cpp2IL;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;
using SkiaSharp;

namespace GameArt;

/// <summary>
/// Read-only access to the installed game's Unity files: data.unity3d (no type trees) and the
/// Addressables bundles (with type trees). Built-in type layouts are borrowed from the bundles,
/// which come from the same Unity build, so no class database has to ship.
/// </summary>
public sealed class UnityGame : IDisposable
{
    private const int Texture2DId = 28, TextAssetId = 49, MonoBehaviourId = 114, MonoScriptId = 115, SpriteId = 213;
    private readonly AssetsManager manager = new();
    private readonly Dictionary<int, TypeTreeType> typeTrees = new();
    private readonly Dictionary<int, AssetTypeTemplateField> templates = new();
    private readonly Dictionary<string, AssetsFileInstance> data = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string File, long Id), SKBitmap> textures = new();
    private readonly Dictionary<string, AssetTypeTemplateField> behaviourTemplates = new(StringComparer.Ordinal);
    private Dictionary<(string File, long Id), (string Assembly, string Namespace, string Class)>? scripts;
    private Cpp2IlTempGenerator? generator;

    public string Root { get; }
    public string DataFolder => Path.Combine(Root, "Tape to Tape_Data");
    public string BundleFolder => Path.Combine(DataFolder, "StreamingAssets", "aa", "StandaloneWindows64");
    public UnityVersion Version { get; }
    public IEnumerable<AssetsFileInstance> DataFiles => data.Values;

    public static bool LooksLikeGame(string root) =>
        File.Exists(Path.Combine(root, "Tape to Tape_Data", "data.unity3d")) && File.Exists(Path.Combine(root, "GameAssembly.dll"));

    public UnityGame(string root)
    {
        if (!LooksLikeGame(root)) throw new DirectoryNotFoundException("Tape to Tape was not found in " + root);
        Root = root;
        try
        {
            HarvestTypeTrees();
            var bundle = manager.LoadBundleFile(Path.Combine(DataFolder, "data.unity3d"), true);
            Version = new UnityVersion(bundle.file.Header.EngineVersion);
            for (int i = 0; i < bundle.file.BlockAndDirInfo.DirectoryInfos.Count; i++)
                if (bundle.file.IsAssetsFile(i)) { var file = manager.LoadAssetsFileFromBundle(bundle, i, false); data[file.name] = file; }
        }
        catch { Dispose(); throw; }
    }

    // The Addressables bundles carry type trees for the built-in classes data.unity3d stores without them.
    private void HarvestTypeTrees()
    {
        int[] wanted = [Texture2DId, TextAssetId, MonoBehaviourId, MonoScriptId, SpriteId];
        foreach (string path in Directory.GetFiles(BundleFolder, "*.bundle").Order(StringComparer.Ordinal))
        {
            var bundle = manager.LoadBundleFile(path, true);
            try
            {
                for (int i = 0; i < bundle.file.BlockAndDirInfo.DirectoryInfos.Count; i++)
                {
                    if (!bundle.file.IsAssetsFile(i)) continue;
                    var file = manager.LoadAssetsFileFromBundle(bundle, i, false);
                    if (!file.file.Metadata.TypeTreeEnabled) continue;
                    foreach (var type in file.file.Metadata.TypeTreeTypes)
                        if (wanted.Contains(type.TypeId) && !typeTrees.ContainsKey(type.TypeId)) typeTrees[type.TypeId] = type;
                }
            }
            // The path overload closes streams but leaves their asset instances in FileLookup.
            // Unload by instance so a later extractor can safely reopen the same atlas bundle.
            finally { manager.UnloadBundleFile(bundle); }
            if (typeTrees.Count == wanted.Length) break;
        }
        var missing = wanted.Where(id => !typeTrees.ContainsKey(id)).Select(id => ((AssetClassID)id).ToString()).ToArray();
        if (missing.Length > 0) throw new InvalidDataException("The game's bundles have no type layout for: " + string.Join(", ", missing));
    }

    private AssetTypeTemplateField NewTemplate(int typeId)
    {
        var template = new AssetTypeTemplateField();
        template.FromTypeTree(typeTrees[typeId]);
        return template;
    }
    private AssetTypeTemplateField Template(int typeId) => templates.TryGetValue(typeId, out var t) ? t : templates[typeId] = NewTemplate(typeId);

    /// <summary>Reads a built-in object from data.unity3d with a borrowed layout.</summary>
    public AssetTypeValueField Read(AssetsFileInstance file, AssetFileInfo info) =>
        Template(info.TypeId).MakeValue(file.file.Reader, info.GetAbsoluteByteOffset(file.file));

    public AssetsFileInstance DataFile(string name) => data.TryGetValue(name, out var file) ? file : throw new FileNotFoundException("Not in data.unity3d: " + name);

    /// <summary>Follows a PPtr (m_FileID, m_PathID) from <paramref name="from"/>; null for an empty reference.</summary>
    public (AssetsFileInstance File, AssetFileInfo Info)? Resolve(AssetsFileInstance from, AssetTypeValueField pointer)
    {
        int fileId = pointer["m_FileID"].AsInt; long pathId = pointer["m_PathID"].AsLong;
        if (pathId == 0) return null;
        var file = fileId == 0 ? from : data.GetValueOrDefault(Path.GetFileName(from.file.Metadata.Externals[fileId - 1].PathName));
        var info = file?.file.GetAssetInfo(pathId);
        return file == null || info == null ? null : (file, info);
    }

    // ---- Text assets (Spine skeletons and atlases) ----

    public IEnumerable<(string Name, AssetsFileInstance File, AssetFileInfo Info)> TextAssets()
    {
        foreach (var file in data.Values)
            foreach (var info in file.file.GetAssetsOfType(AssetClassID.TextAsset))
                yield return (Read(file, info)["m_Name"].AsString, file, info);
    }
    public byte[] TextBytes(AssetsFileInstance file, AssetFileInfo info) => Read(file, info)["m_Script"].AsByteArray;

    // ---- Textures and sprites ----

    /// <summary>Decodes a Texture2D to a top-down SKBitmap. Cached; owned by this instance.</summary>
    public SKBitmap Texture(AssetsFileInstance file, AssetFileInfo info)
    {
        if (textures.TryGetValue((file.name, info.PathId), out var cached)) return cached;
        var field = file.file.Metadata.TypeTreeEnabled ? manager.GetBaseField(file, info) : Read(file, info);
        return textures[(file.name, info.PathId)] = Decode(field, file);
    }

    private static SKBitmap Decode(AssetTypeValueField field, AssetsFileInstance file)
    {
        var texture = TextureFile.ReadTextureFile(field);
        texture.FillPictureData(file);
        byte[] bgra = texture.DecodeTextureRaw(texture.pictureData)
            ?? throw new InvalidDataException($"Texture {texture.m_Name} uses unsupported format {(TextureFormat)texture.m_TextureFormat}.");
        int width = texture.m_Width, height = texture.m_Height, stride = width * 4;
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        // Unity stores rows bottom-up.
        IntPtr pixels = bitmap.GetPixels();
        for (int y = 0; y < height; y++)
            System.Runtime.InteropServices.Marshal.Copy(bgra, (height - 1 - y) * stride, pixels + y * stride, stride);
        return bitmap;
    }

    /// <summary>The first Texture2D of the Addressables bundle whose name starts with <paramref name="prefix"/> (lighting "_lso_" variants skipped).</summary>
    public SKBitmap? BundleTexture(string prefix)
    {
        string? path = Directory.GetFiles(BundleFolder, prefix + "*.bundle")
            .Where(p => !Path.GetFileName(p).Contains("_lso_")
                && (!prefix.EndsWith('_') || System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileNameWithoutExtension(p)[prefix.Length..], "^[0-9a-f]{32}$")))
            .Order(StringComparer.Ordinal).FirstOrDefault();
        if (path == null) return null;
        var bundle = manager.LoadBundleFile(path, true);
        try
        {
            for (int i = 0; i < bundle.file.BlockAndDirInfo.DirectoryInfos.Count; i++)
            {
                if (!bundle.file.IsAssetsFile(i)) continue;
                var file = manager.LoadAssetsFileFromBundle(bundle, i, false);
                var info = file.file.GetAssetsOfType(AssetClassID.Texture2D).FirstOrDefault();
                if (info != null) return Decode(manager.GetBaseField(file, info), file);
            }
            return null;
        }
        finally { manager.UnloadBundleFile(bundle); }
    }

    /// <summary>Crops a Sprite out of its texture (rect-packed or unpacked sprites).</summary>
    public SKBitmap Sprite(AssetsFileInstance file, AssetFileInfo info)
    {
        var sprite = Read(file, info);
        var rd = sprite["m_RD"];
        var target = Resolve(file, rd["texture"]) ?? throw new InvalidDataException("Sprite " + sprite["m_Name"].AsString + " has no texture.");
        var page = Texture(target.File, target.Info);
        var rect = rd["textureRect"];
        float x = rect["x"].AsFloat, y = rect["y"].AsFloat, w = rect["width"].AsFloat, h = rect["height"].AsFloat;
        uint settings = rd["settingsRaw"].AsUInt;
        bool packed = (settings & 1) != 0, tight = ((settings >> 1) & 1) == 0;
        uint rotation = (settings >> 2) & 0xF;
        if (packed && (tight || rotation != 0))
            throw new NotSupportedException($"Sprite {sprite["m_Name"].AsString} is tightly packed or rotated (settings {settings}).");
        var area = SKRectI.Create((int)MathF.Round(x), page.Height - (int)MathF.Round(y + h), (int)MathF.Round(w), (int)MathF.Round(h));
        using var result = new SKBitmap(area.Width, area.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        page.ExtractSubset(result, area);
        return result.Copy();
    }

    public IEnumerable<(string Name, AssetsFileInstance File, AssetFileInfo Info)> Sprites()
    {
        foreach (var file in data.Values)
            foreach (var info in file.file.GetAssetsOfType(AssetClassID.Sprite))
                yield return (Read(file, info)["m_Name"].AsString, file, info);
    }

    // ---- MonoBehaviours (IL2CPP script fields via Cpp2IL) ----

    private Dictionary<(string File, long Id), (string Assembly, string Namespace, string Class)> Scripts()
    {
        if (scripts != null) return scripts;
        scripts = new();
        foreach (var file in data.Values)
            foreach (var info in file.file.GetAssetsOfType(AssetClassID.MonoScript))
            {
                var script = Read(file, info);
                scripts[(file.name, info.PathId)] = (script["m_AssemblyName"].AsString, script["m_Namespace"].AsString, script["m_ClassName"].AsString);
            }
        return scripts;
    }

    internal AssetTypeTemplateField Header()
    {
        var header = NewTemplate(MonoBehaviourId);
        header.Children = header.Children.Take(4).ToList(); // m_GameObject, m_Enabled, m_Script, m_Name
        return header;
    }

    /// <summary>Every MonoBehaviour in data.unity3d whose script class is <paramref name="className"/>, fully deserialized.</summary>
    public IEnumerable<(AssetsFileInstance File, AssetFileInfo Info, AssetTypeValueField Value)> Behaviours(string className)
    {
        var header = Header();
        var known = Scripts();
        foreach (var file in data.Values)
            foreach (var info in file.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
            {
                var head = header.MakeValue(file.file.Reader, info.GetAbsoluteByteOffset(file.file));
                var pointer = head["m_Script"];
                int fileId = pointer["m_FileID"].AsInt;
                string scriptFile = fileId == 0 ? file.name : Path.GetFileName(file.file.Metadata.Externals[fileId - 1].PathName);
                if (!known.TryGetValue((scriptFile, pointer["m_PathID"].AsLong), out var script) || script.Class != className) continue;
                yield return (file, info, BehaviourTemplate(script).MakeValue(file.file.Reader, info.GetAbsoluteByteOffset(file.file)));
            }
    }

    internal AssetTypeTemplateField BehaviourTemplate((string Assembly, string Namespace, string Class) script)
    {
        string key = script.Assembly + "|" + script.Namespace + "|" + script.Class;
        if (behaviourTemplates.TryGetValue(key, out var cached)) return cached;
        // LibCpp2IL reports its (lazy) metadata loading on the console; the Studio has no use for it.
        var console = Console.Out;
        Console.SetOut(TextWriter.Null);
        try
        {
            generator ??= new Cpp2IlTempGenerator(Path.Combine(DataFolder, "il2cpp_data", "Metadata", "global-metadata.dat"), Path.Combine(Root, "GameAssembly.dll"));
            return behaviourTemplates[key] = generator.GetTemplateField(Header(), script.Assembly, script.Namespace, script.Class, Version);
        }
        finally { Console.SetOut(console); }
    }

    public void Dispose()
    {
        foreach (var bitmap in textures.Values) bitmap.Dispose();
        textures.Clear();
        manager.UnloadAll(true);
    }
}
