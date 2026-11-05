using System.Text.Json;
using CampaignCore;

namespace CampaignCreator;

// Local picker preferences, deliberately outside the head project. Hiding a head
// must not remove runtime assets, alter existing players or travel with exports.
public static class HeadOptionVisibility
{
    const string FileName = ".studio-hidden-heads.json";

    static HashSet<string> Read(string root)
    {
        string file = Path.Combine(root, FileName);
        return new(File.Exists(file) ? JsonSerializer.Deserialize<string[]>(File.ReadAllText(file)) ?? [] : [], StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsVisible(string root, string skin) => !Read(root).Contains(HeadAppearance.Base(skin));

    public static IEnumerable<string> Choices(string root, IEnumerable<string> skins, string selected = "")
    {
        var hidden = Read(root);
        selected = HeadAppearance.Base(selected);
        // A saved assignment stays selectable on that player until explicitly
        // changed. Its project and preview are still available in the full catalog.
        return HeadAppearance.Choices(skins).Where(s => !hidden.Contains(s) || s.Equals(selected, StringComparison.OrdinalIgnoreCase));
    }

    public static void SetVisible(string root, string skin, bool visible)
    {
        skin = HeadAppearance.Base(skin);
        if (!skin.StartsWith("Faces/CustomHeads/", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only added custom heads can be removed from head options.");
        var hidden = Read(root);
        if (!(visible ? hidden.Remove(skin) : hidden.Add(skin))) return;
        Directory.CreateDirectory(root);
        string file = Path.Combine(root, FileName), temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(hidden.OrderBy(s => s, StringComparer.OrdinalIgnoreCase), new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, file, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
