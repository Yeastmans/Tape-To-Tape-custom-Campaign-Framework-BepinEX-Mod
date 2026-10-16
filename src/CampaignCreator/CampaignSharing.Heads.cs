using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CampaignCore;

namespace CampaignCreator;

internal static partial class CampaignSharing
{
    private const string HeadPrefix = "Faces/CustomHeads/";
    private const string MaskPrefix = "Helmet/CustomMasks/";

    private static IEnumerable<string> CampaignDocuments(string campaign) =>
        Directory.EnumerateFiles(campaign, "*.txt", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(campaign, "goalie-equipment.cfg", SearchOption.AllDirectories)).Where(file =>
            !Path.GetRelativePath(campaign, file).Split(Path.DirectorySeparatorChar)
                .Any(part => SkipFolders.Contains(part, StringComparer.OrdinalIgnoreCase))
            && !SkipFiles.Contains(Path.GetFileName(file), StringComparer.OrdinalIgnoreCase));

    private static string? HeadName(string value)
    {
        if (value.StartsWith(MaskPrefix, StringComparison.OrdinalIgnoreCase)) return value;
        if (!value.StartsWith(HeadPrefix, StringComparison.OrdinalIgnoreCase)) return null;
        return value.EndsWith("_Helmet", StringComparison.OrdinalIgnoreCase) ? value[..^7] : value;
    }

    private static List<CustomHeadDefinition> ReferencedHeads(string campaign, string contentRoot)
    {
        var needed = CampaignDocuments(campaign).SelectMany(file => new CampaignDocument(File.ReadAllText(file)).Fields.Values)
            .Select(HeadName).Where(name => name != null).Cast<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<CustomHeadDefinition>();
        foreach (string root in new[] { Path.Combine(contentRoot, "custom-heads"), Path.Combine(campaign, HeadBundle) })
        {
            if (!Directory.Exists(root)) continue;
            foreach (string folder in Directory.GetDirectories(root))
            {
                string manifest = Path.Combine(folder, "head-project.json");
                if (!File.Exists(manifest)) continue;
                // Unrelated unfinished projects must not block sharing a campaign.
                string? name;
                try
                {
                    var json = JsonNode.Parse(File.ReadAllText(manifest));
                    name = json?["Name"]?.GetValue<string>();
                    if (name != null) name = (json?["IsGoalie"]?.GetValue<bool>() == true ? MaskPrefix : HeadPrefix) + name;
                }
                catch { continue; }
                if (name == null || !needed.Contains(name)) continue;
                result.Add(CustomHeadDefinition.Read(folder));
                needed.Remove(name);
            }
        }
        if (needed.Count > 0) throw new InvalidDataException("Custom head files are missing: " + string.Join(", ", needed) + ". Install those head projects before sharing this campaign.");
        return result;
    }

    private static string[] HeadFiles(string folder)
    {
        var project = HeadProject.Open(folder);
        var files = project.Images.SelectMany(image => new[] {
            image.File, image.BoundsFile, image.SourceFile, "outlines/" + image.File.Replace('\\', '/')
        }).Concat(new[] { "head-project.json", "README.txt" });
        return files.Where(file => file.Length > 0 && File.Exists(project.FilePath(file)))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool SameHead(CustomHeadDefinition incoming, CustomHeadDefinition existing)
    {
        var files = incoming.Images.Select(image => image.File).Append("head-project.json");
        return files.All(relative => File.Exists(existing.Asset(relative))
            && SHA256.HashData(File.ReadAllBytes(incoming.Asset(relative)))
                .SequenceEqual(SHA256.HashData(File.ReadAllBytes(existing.Asset(relative)))));
    }

    private static int InstallHeads(string campaign, string contentRoot, List<string> added)
    {
        string bundle = Path.Combine(campaign, HeadBundle);
        if (!Directory.Exists(bundle)) return 0; // Older campaign packages remain supported.
        var incoming = Directory.GetDirectories(bundle).Select(CustomHeadDefinition.Read).ToList();
        if (incoming.GroupBy(head => head.Name, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            throw new InvalidDataException("The campaign contains duplicate custom head names.");
        string library = Path.Combine(contentRoot, "custom-heads");
        Directory.CreateDirectory(library);
        var existing = CustomHeadCatalog.Load(contentRoot);
        var renamed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int kept = 0;
        foreach (var head in incoming)
        {
            var match = existing.FirstOrDefault(item => item.Skin.Equals(head.Skin, StringComparison.OrdinalIgnoreCase));
            if (match != null && SameHead(head, match)) { kept++; continue; }
            string name = head.Name;
            for (int n = 2; Directory.Exists(Path.Combine(library, name)) || File.Exists(Path.Combine(library, name))
                || existing.Any(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); n++)
                name = head.Name[..Math.Min(head.Name.Length, 65)] + " (import " + n + ")";
            if (name != head.Name)
            {
                renamed[head.Skin] = (head.IsGoalie ? MaskPrefix : HeadPrefix) + name;
                string manifest = Path.Combine(head.Folder, "head-project.json");
                var json = JsonNode.Parse(File.ReadAllText(manifest))!;
                json["Name"] = name;
                File.WriteAllText(manifest, json.ToJsonString());
            }
            string destination = Path.Combine(library, CreatorContent.ValidateName(name));
            Directory.CreateDirectory(destination);
            added.Add(destination);
            foreach (string relative in HeadFiles(head.Folder))
            {
                string file = Path.Combine(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.Copy(head.Asset(relative), file);
            }
            existing.Add(CustomHeadDefinition.Read(destination));
        }
        if (renamed.Count > 0)
            foreach (string file in CampaignDocuments(campaign))
            {
                var document = new CampaignDocument(File.ReadAllText(file));
                bool changed = false;
                foreach (var field in document.Fields)
                    if (HeadName(field.Value) is { } name && renamed.TryGetValue(name, out var replacement))
                    {
                        string suffix = field.Value.StartsWith(HeadPrefix, StringComparison.OrdinalIgnoreCase)
                            && field.Value.EndsWith("_Helmet", StringComparison.OrdinalIgnoreCase) ? "_Helmet" : "";
                        document.Set(field.Key, replacement + suffix); changed = true;
                    }
                if (changed) document.Save(file);
            }
        return kept;
    }
}
