using CampaignCore;

namespace CampaignCreator;

internal static class RosterLines
{
    internal static readonly string[] Skaters = ["Left Wing", "Center", "Right Wing", "Left Defense", "Right Defense"];
    internal static string PlayerPosition(string file, CampaignDocument doc, string role)
    {
        string fullPath = Path.GetFullPath(file);
        for (string? folder = Path.GetDirectoryName(fullPath); folder != null; folder = Path.GetDirectoryName(folder))
            if (Path.GetFileName(folder).Equals("players", StringComparison.OrdinalIgnoreCase)
                && File.Exists(Path.Combine(Path.GetDirectoryName(folder)!, "team.txt")))
                return Assign(folder).GetValueOrDefault(fullPath, doc.Get("Position"));
        return string.IsNullOrWhiteSpace(doc.Get("Position")) ? (role == "goalie" ? "Goalie" : "Center") : doc.Get("Position");
    }
    internal static Dictionary<string,string> Assign(string directory)
    {
        var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        var used=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var extras=new List<(string file,string position)>();
        foreach(string file in (Directory.Exists(directory) ? Directory.GetFiles(directory,"*.txt",SearchOption.AllDirectories) : []).Order(StringComparer.OrdinalIgnoreCase))
        {
            var doc=new CampaignDocument(File.ReadAllText(file));
            string position=RosterPosition.Read(file,directory,doc);
            string basePosition=position.Replace("Line 2 ","",StringComparison.OrdinalIgnoreCase);
            if((Skaters.Contains(basePosition,StringComparer.OrdinalIgnoreCase) || position.Equals("Goalie",StringComparison.OrdinalIgnoreCase)) && used.Add(position)) result[file]=position;
            else extras.Add((file,ContentImport.IsGoalie(doc,file)?"Goalie":basePosition));
        }
        foreach(var (file,position) in extras)
        {
            string target="Line 2 "+position;
            if(position!="Goalie" && (!Skaters.Contains(position,StringComparer.OrdinalIgnoreCase) || used.Contains(target)))
                target="Line 2 "+(Skaters.FirstOrDefault(p=>!used.Contains("Line 2 "+p)) ?? "Center");
            result[file]=target; used.Add(target);
        }
        return result;
    }
    internal static void Normalize(string directory)
    {
        foreach(var (file,position) in Assign(directory))
        {
            var doc=new CampaignDocument(File.ReadAllText(file));
            if(doc.Get("Position")==position) continue;
            doc.Set("Position",position); doc.Save(file);
        }
    }
    internal static void SavePositions(IReadOnlyDictionary<string, string> positions)
    {
        var before = positions.Keys.ToDictionary(file => file, File.ReadAllText, StringComparer.OrdinalIgnoreCase);
        var written = new List<string>();
        try
        {
            foreach (var (file, position) in positions)
            {
                var doc = new CampaignDocument(before[file]);
                if (doc.Get("Position") == position) continue;
                doc.Set("Position", position); doc.Save(file); written.Add(file);
            }
        }
        catch
        {
            foreach (string file in written) new CampaignDocument(before[file]).Save(file);
            throw;
        }
    }
}
