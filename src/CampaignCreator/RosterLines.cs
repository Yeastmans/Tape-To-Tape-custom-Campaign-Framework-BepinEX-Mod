using CampaignCore;

namespace CampaignCreator;

internal static class RosterLines
{
    internal static readonly string[] Skaters = ["Left Wing", "Center", "Right Wing", "Left Defense", "Right Defense"];
    internal static Dictionary<string,string> Assign(string directory)
    {
        var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        var used=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var extras=new List<(string file,string position)>();
        foreach(string file in (Directory.Exists(directory) ? Directory.GetFiles(directory,"*.txt",SearchOption.AllDirectories) : []).Order(StringComparer.OrdinalIgnoreCase))
        {
            var doc=new CampaignDocument(File.ReadAllText(file));
            string position=doc.Get("Position",Path.GetFileNameWithoutExtension(file).Split(" - ")[0]);
            if(Path.GetRelativePath(directory,file).Contains("second-line",StringComparison.OrdinalIgnoreCase) && !position.StartsWith("Line 2 ",StringComparison.OrdinalIgnoreCase)) position="Line 2 "+position;
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
}
