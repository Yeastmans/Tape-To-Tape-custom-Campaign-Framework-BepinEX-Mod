using CampaignCore;

namespace CampaignCreator;

/// <summary>The fields shown in Team look, excluding identity, roster and gameplay settings.</summary>
public static class TeamLookCopy
{
    // A team's look never includes a stick (each player's in the base game) or a skate style.
    // Fill look copies equipment and colors only; the team keeps its own logo.
    public static IEnumerable<string> Keys => DefaultPlayerLook.Keys.Where(k => !PlayerColorModel.IsStickKey(k) && k != "Logo From"
        && k != JerseyLogoOptions.Field(false) && k != JerseyLogoOptions.Field(true)
        && !k.StartsWith(JerseyLogoOptions.Prefix(false) + " ", StringComparison.OrdinalIgnoreCase)
        && !k.StartsWith(JerseyLogoOptions.Prefix(true) + " ", StringComparison.OrdinalIgnoreCase));

    // Snapshot before writing: copying Away to Home must not change values still
    // being read. The team identity logo, mask and number style apply to both kits.
    public static CampaignDocument Kit(bool sourceAway, Func<string, string> read)
    {
        var result = new CampaignDocument("");
        foreach (string awayKey in Keys.Where(k => k.Contains(" Away") || k.StartsWith("Away ")))
        {
            string homeKey = awayKey switch
            {
                "Away Primary" => "Jersey Primary",
                "Away Secondary" => "Jersey Secondary",
                "Away Accent" => "Jersey Accent",
                "Number Color Away" => "Number Color Home",
                _ => AppearanceContract.HomeKey(awayKey)
            };
            result.Set(sourceAway ? homeKey : awayKey, read(sourceAway ? awayKey : homeKey));
        }
        bool targetAway = !sourceAway;
        string selected = read(JerseyLogoOptions.Field(sourceAway));
        var sourceLogo = new CampaignDocument("");
        sourceLogo.Set(JerseyLogoOptions.Field(sourceAway), selected);
        foreach (string suffix in new[] { "Mode", "ID", "SHA256", "PNG" })
            if (selected.Length > 0 && !selected.Equals("none", StringComparison.OrdinalIgnoreCase))
                sourceLogo.Set(JerseyLogoOptions.Prefix(sourceAway) + " " + suffix,
                    read(JerseyLogoOptions.Prefix(sourceAway) + " " + suffix));
        JerseyLogoOptions.Copy(sourceLogo, sourceAway, result, targetAway);
        // The editor writes only fields present in this draft; include empty
        // targets so an inherited or no-logo copy removes any old payload.
        result.Set(JerseyLogoOptions.Field(targetAway), selected);
        foreach (string suffix in new[] { "Mode", "ID", "SHA256", "PNG" })
        {
            string key = JerseyLogoOptions.Prefix(targetAway) + " " + suffix;
            if (!result.Fields.ContainsKey(key)) result.Set(key, "");
        }
        return result;
    }

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
