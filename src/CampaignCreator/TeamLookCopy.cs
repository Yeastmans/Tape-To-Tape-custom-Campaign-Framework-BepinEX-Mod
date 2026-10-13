using CampaignCore;

namespace CampaignCreator;

/// <summary>The fields shown in Team look, excluding identity, roster and gameplay settings.</summary>
public static class TeamLookCopy
{
    // A team's look never includes a stick (each player's in the base game) or a skate style.
    // Fill look copies equipment and colors only; the team keeps its own logo.
    public static IEnumerable<string> Keys => DefaultPlayerLook.Keys.Where(k => !PlayerColorModel.IsStickKey(k) && k != "Logo From");

    public static CampaignDocument Load(string teamFile)
    {
        if (!ContentImport.Matches(teamFile, "team")) throw new ArgumentException("Choose a team to fill the look.");
        var raw = new CampaignDocument(File.ReadAllText(teamFile));
        var team = ContentImport.ReadDocument(teamFile);
        var sourceGoalie = new TeamGoalieEquipment(teamFile);
        var goalie = new CampaignDocument("");
        foreach (string key in FieldSchema.GoalieAssets.Where(k => k != "Logo Skin"))
            goalie.Set(key, raw.Get(key) is { Length: > 0 } value ? value : sourceGoalie.Get(key));
        new UpgradeCleanup().CleanImportedValues(goalie, "goalie");
        var look = new CampaignDocument("");
        Copy(look, team, key => goalie.Get(key));
        return look;
    }

    public static void Copy(CampaignDocument destination, CampaignDocument source, Func<string, string> goalieValue)
    {
        foreach (string key in Keys)
        {
            string value = FieldSchema.GoalieAssets.Contains(key) ? goalieValue(key) : source.Get(key);
            destination.Set(key, value);
        }
    }
}
