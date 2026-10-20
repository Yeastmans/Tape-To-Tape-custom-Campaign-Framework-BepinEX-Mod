using CampaignCore;

namespace CampaignCreator;

// One report per export; no changes are made to authored campaign files.
internal sealed class ExportReport
{
    private readonly HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
    internal List<string> Skipped { get; } = new();
    internal void Skip(CampaignDocument source, string field, string value)
    {
        string owner = source.Get("Name", source.Get("Team Name", "Export"));
        string entry = $"{owner}: {field} = {value}";
        if (seen.Add(entry)) Skipped.Add(entry);
    }
}

internal static class ExportSelections
{
    internal static string? Asset(GameData game, string role, string field, string value, CampaignDocument source, ExportReport? report, string? reportField = null)
    {
        value = value.Trim();
        // "none" is a runtime instruction to remove a piece, not a catalog asset.
        if (value.Length == 0 || value.Equals("none", StringComparison.OrdinalIgnoreCase)
            || value.Equals("standard", StringComparison.OrdinalIgnoreCase) || value.Equals("team colors", StringComparison.OrdinalIgnoreCase)) return value.ToLowerInvariant();
        if (field == "Number Skin" && System.Text.RegularExpressions.Regex.IsMatch(value, @"^Numbers/Number_\d+(LH)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return value;
        if (role == "goalie" && GoalieEquipmentCatalog.Values(field).Contains(value, StringComparer.OrdinalIgnoreCase)) return value;
        try { return game.Assets.Resolve(role, field, value); }
        catch (ArgumentException) { report?.Skip(source, reportField ?? field, value); return null; }
    }

    internal static string Id(Dictionary<string, string> map, string value, CampaignDocument source, string field, ExportReport? report)
    {
        value = value.Trim();
        if (value.Length == 0 || value.Equals("none", StringComparison.OrdinalIgnoreCase)) return "";
        if (map.TryGetValue(value, out var id)) return id;
        if (map.Values.Contains(value, StringComparer.OrdinalIgnoreCase)) return value;
        report?.Skip(source, field, value);
        return "";
    }

    internal static CampaignDocument Logo(CampaignDocument source, string root, ExportReport? report)
    {
        string name = source.Get("Logo From");
        if (source.Get("Export Logo PNG").Length == 0 && name.Length > 0
            && !name.Equals("none", StringComparison.OrdinalIgnoreCase) && LogoExport.FindFile(root, name) == null)
        {
            report?.Skip(source, "Logo From", name);
            source = new CampaignDocument(source.ToString());
            AppearanceContract.ClearJerseyLogo(source);
        }
        return LogoExport.Snapshot(source, root);
    }

    internal static CampaignDocument JerseyLogo(CampaignDocument source, string root, bool away, ExportReport? report)
    {
        var choice = JerseyLogoOptions.Read(source, away);
        if (choice.Mode == JerseyLogoMode.Image && source.Get(choice.MetadataPrefix + " PNG").Length == 0
            && choice.Source.Equals(source.Get("Logo From"), StringComparison.OrdinalIgnoreCase)
            && source.Get("Export Logo PNG").Length > 0)
        {
            source = new CampaignDocument(source.ToString());
            LogoExport.CopyIdentityToJersey(source, source, away);
        }
        if (choice.Mode == JerseyLogoMode.Image && source.Get(choice.MetadataPrefix + " PNG").Length == 0
            && LogoExport.FindFile(root, choice.Source) == null)
        {
            report?.Skip(source, JerseyLogoOptions.Field(away), choice.Source);
            source = new CampaignDocument(source.ToString());
            JerseyLogoOptions.Clear(source, away);
        }
        return LogoExport.Snapshot(source, root, away);
    }

    // Run after inheritance, at the last boundary before serializing. Resolving
    // appearance again after omissions would fill removed fields with defaults.
    internal static CampaignDocument Sidecar(CampaignDocument source, GameData game, string role, ExportReport? report)
    {
        var result = new CampaignDocument(source.ToString());
        foreach (string field in (role == "goalie" ? FieldSchema.GoalieAssets : FieldSchema.SkaterAssets).Append("Logo Skin").Distinct())
        {
            string value = result.Get(field);
            if (value.Length == 0) continue;
            string? asset = Asset(game, role, field, value, source, report);
            if (asset == null) result.Remove(field);
            else result.Set(field, asset);
        }
        foreach (string field in new[] { "Talents", "Goalie Talents", "Team Talents", "Relics", "Team Relics" })
        {
            if (!result.Fields.ContainsKey(field)) continue;
            string[] ids = ExportTranslation.Ids(game, result.Get(field), field.Contains("Relics"), report, source, field);
            if (ids.Length == 0) result.Remove(field);
            else result.Set(field, string.Join(",", ids));
        }
        if (result.Fields.ContainsKey("Ability"))
        {
            string id = Id(game.Abilities, result.Get("Ability"), source, "Ability", report);
            if (id.Length == 0) result.Remove("Ability"); else result.Set("Ability", id);
        }
        result = Logo(result, game.Root, report);
        foreach (bool away in new[] { false, true }) result = JerseyLogo(result, game.Root, away, report);
        return result;
    }
}
