using CampaignCore;

namespace CampaignCreator;

public static class DefaultPlayerLook
{
    public static string FilePath(string root) => Path.Combine(root, "studio-default-look.cfg");
    public static IEnumerable<string> ColorKeys => FieldSchema.TeamColors.Concat(FieldSchema.EquipmentColors()).Distinct();
    public static IEnumerable<string> Keys => ColorKeys
        .Concat(FieldSchema.SkaterAssets.Where(k => k is not ("Face" or "Glasses" or "Number Skin" or "Skates" or "Skates Away")))
        .Concat(FieldSchema.GoalieAssets.Where(k => k != "Logo Skin")).Append("Logo From").Append("Number Style")
        .Concat(new[] { false, true }.SelectMany(away => new[] { JerseyLogoOptions.Field(away) }
            .Concat(new[] { "Mode", "ID", "SHA256", "PNG" }.Select(suffix => JerseyLogoOptions.Prefix(away) + " " + suffix)))).Distinct();

    public static CampaignDocument Load(string root) => new(File.Exists(FilePath(root)) ? File.ReadAllText(FilePath(root)) : "");

    public static void Save(string root, Func<string, string> read)
    {
        var doc = new CampaignDocument("");
        foreach (string key in Keys) doc.Set(key, read(key));
        Directory.CreateDirectory(root);
        string temporary = FilePath(root) + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { doc.Save(temporary); File.Move(temporary, FilePath(root), true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    // New content gets a style for every piece, even ones the saved default left blank:
    // a blank away piece takes its home choice (the game's own rule), then the
    // customizable style the game would use. Blank socks already mean "same as jersey".
    public static string Value(CampaignDocument defaults, string key)
    {
        string value = defaults.Get(key);
        if (value.Length > 0 || FieldSchema.Kind(key) != FieldKind.Choice || key is "Socks" or "Socks Away") return value;
        if (key.Contains(" Away")) value = defaults.Get(AppearanceContract.HomeKey(key));
        return value.Length > 0 ? value : AppearanceContract.DefaultSkin(FieldSchema.GoalieAssets.Contains(key) ? "goalie" : "skater", key);
    }

    public static void Apply(string root, CampaignDocument player)
    {
        if (!File.Exists(FilePath(root))) return;
        var defaults = Load(root);
        string role = player.Get("Position").Equals("Goalie", StringComparison.OrdinalIgnoreCase) ? "goalie" : "skater";
        bool applied = false;
        // Colors are the team kit's; a copied stick/skate color would pin every new player to the default.
        // Stick colors are the player's own in the base game, so a new player starts with the saved ones.
        // The logo is the team's: a copied one would override every new player's team logo.
        foreach (string key in AppearanceContract.Keys(role).Where(k => !AppearanceContract.IsColor(k) && k is not ("Logo From" or "Logo Skin")).Concat(PlayerColorModel.StickKeys))
        {
            string value = Value(defaults, AppearanceContract.TeamKey(key));
            if (value.Length > 0) { player.Set(key, value); applied = true; }
        }
        if (applied)
        {
            player.Set("Override Team Settings", "no");
            // These are newly authored personal colors, not a legacy team-kit
            // override. Preserve them when the player editor first migrates it.
            player.Set(PlayerColorModel.VersionKey, PlayerColorModel.CurrentVersion);
        }
    }

    // The team a new team starts as, and the kit a player with no team previews in.
    // Goalie styles come back separately: a team keeps them in goalie-equipment.cfg.
    public static CampaignDocument TeamLook(string root, out CampaignDocument goalie)
    {
        var team = new CampaignDocument("");
        goalie = new CampaignDocument("");
        if (!File.Exists(FilePath(root))) return team;
        var defaults = Load(root);
        foreach (string key in Keys)
        {
            if (PlayerColorModel.IsStickKey(key)) continue; // the stick is each player's
            string value = Value(defaults, key);
            if (value.Length == 0) continue;
            if (FieldSchema.GoalieAssets.Contains(key)) goalie.Set(key, value);
            else team.Set(key, value);
        }
        return team;
    }

    // A new team starts with the whole default look. Goalie styles belong in the
    // team's goalie-equipment.cfg (TeamGoalieEquipment), which seeds goalies added later.
    public static void ApplyTeamLook(string root, CampaignDocument team, string teamDirectory)
    {
        if (!File.Exists(FilePath(root))) return;
        var look = TeamLook(root, out var goalie);
        foreach (var pair in look.Fields) team.Set(pair.Key, pair.Value);
        if (goalie.Fields.Count > 0) goalie.Save(Path.Combine(teamDirectory, "goalie-equipment.cfg"));
    }
}
