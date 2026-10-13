using CampaignCore;
namespace CampaignCreator;
public static class EditorRequirements
{
    public static string[] Required(bool team, string role) => team ? ["Team Name", "City", "Abbreviation", "Body", "Gloves", "Pants"] : role == "goalie"
        ? ["Name", "Position", "Number", "Skill", "Catching", "Glove", "Blocker", "Five Hole", "Standing Speed", "Butterfly Speed", "Control", "Recovery", "Pass Power", "Shot Power", "Poke Check", "Depth"]
        : ["Name", "Position", "Number", "Size", "Left Handed", "Speed", "Shot Power", "Accuracy", "Checking", "Face"];
    public static bool HasNativeTeamEquipment(CampaignDocument doc) =>
        doc.Get(VanillaCopyData.OpponentId).Length > 0 || doc.Get(VanillaCopyData.SquadId).Length > 0;
    public static string[] Missing(CampaignDocument doc, bool team, string role) => Required(team, role)
        .Where(k => !(team && HasNativeTeamEquipment(doc) && k is "Body" or "Gloves" or "Pants" && string.IsNullOrWhiteSpace(doc.Get(k))))
        .Where(k => string.IsNullOrWhiteSpace(doc.Get(k)) || doc.Get(k).Equals("none", StringComparison.OrdinalIgnoreCase))
        .Concat(team ? OpponentEffects.Conflicts(OpponentEffects.Parse(doc.Get("Opponent Effects"))).Select(e => "Opponent effects: " + e) : Array.Empty<string>()).ToArray();
    public static string Zone(string key) => key is "Name" or "Number" or "Team Name" or "City" or "Abbreviation" ? "Identity" : key == "Face" ? "Appearance / Head" : FieldSchema.SkaterAssets.Concat(FieldSchema.GoalieAssets).Contains(key) ? "Appearance / Equipment" : "Details & attributes";
}
