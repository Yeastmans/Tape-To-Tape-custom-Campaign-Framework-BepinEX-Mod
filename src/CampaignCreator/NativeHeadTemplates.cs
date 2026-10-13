using System.Text.Json;
using System.Text.Json.Nodes;

namespace CampaignCreator;

public sealed class NativeHeadTemplate
{
    public string Skin { get; set; } = "";
    public string BaseSkin { get; set; } = "";
    public string HelmetSkin { get; set; } = "";
    public bool IsHelmet { get; set; }
    public string Label { get; set; } = "";
    public string Folder { get; set; } = "";
    public bool Available { get; set; }
    public string Error { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore] public string Root { get; set; } = "";
    public string File(string relative)
    {
        string folder = Path.GetFullPath(Path.Combine(Root, Folder));
        if (!folder.StartsWith(Path.GetFullPath(Root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Native template folder is outside the artwork cache.");
        string path = Path.GetFullPath(Path.Combine(folder, relative));
        if (!path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Native template asset is outside its folder.");
        return path;
    }
    public JsonObject Assets() => JsonNode.Parse(System.IO.File.ReadAllText(File("head-assets.json")))!.AsObject();
}

public static class NativeHeadTemplates
{
    public static IReadOnlyList<NativeHeadTemplate> Read()
    {
        if (StudioArt.Path("head-templates", "catalog.json") is not { } index) return [];
        using var json = JsonDocument.Parse(System.IO.File.ReadAllText(index));
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var rows = JsonSerializer.Deserialize<List<NativeHeadTemplate>>(json.RootElement.GetProperty("templates").GetRawText(), options)!;
        foreach (var row in rows) { row.Root = Path.GetDirectoryName(index)!; row.File("head-assets.json"); }
        return rows.OrderBy(r => r.Label, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
