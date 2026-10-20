using CampaignCore;

namespace CampaignCreator;

public static class GoalieFreeAgentContent
{
    public static string[] Files(string campaign)
    {
        string directory = GoalieFreeAgents.DirectoryPath(campaign);
        return Directory.Exists(directory) ? Directory.GetFiles(directory, "*.txt").Order(StringComparer.OrdinalIgnoreCase).ToArray() : [];
    }

    public static string Create(string root, string campaign, string name, string source = "")
    {
        if (source.Length > 0 && !ContentImport.Matches(source, "goalie"))
            throw new ArgumentException("Choose a goalie file for this pool.");
        string directory = GoalieFreeAgents.DirectoryPath(campaign);
        var selected = GoalieFreeAgents.SelectedFiles(campaign);
        name = CreatorContent.AvailablePlayerName(directory, name);
        var doc = CreatorContent.CustomPlayerCopy(source, name);
        doc.Set("Position", "Goalie");
        doc.Remove(VanillaCopyData.PlayerId); doc.Remove(VanillaCopyData.PlayerAsset);
        if (source.Length == 0) { DefaultPlayerLook.Apply(root, doc); doc.Set("Override Team Settings", "no"); }
        string staging = Path.Combine(root, ".studio-staging"); Directory.CreateDirectory(staging);
        string temporary = Path.Combine(staging, "goalie-" + Guid.NewGuid().ToString("N") + ".txt");
        string file = Path.Combine(directory, name + ".txt");
        bool published = false;
        try
        {
            doc.Save(temporary); Directory.CreateDirectory(directory); File.Move(temporary, file); published = true;
            SelectedPlayerPool.Save(directory, selected.Append(file).Select(p => Path.GetFileName(p)));
            return file;
        }
        catch { if (published) File.Delete(file); throw; }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
