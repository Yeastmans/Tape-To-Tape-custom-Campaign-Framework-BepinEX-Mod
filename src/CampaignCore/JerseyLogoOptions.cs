using System;
using System.Linq;

namespace CampaignCore;

public enum JerseyLogoMode { Inherit, Image, None }

public sealed class JerseyLogoChoice
{
    public JerseyLogoMode Mode { get; }
    public string Source { get; }
    public string MetadataPrefix { get; }

    public JerseyLogoChoice(JerseyLogoMode mode, string source, string metadataPrefix)
    {
        Mode = mode;
        Source = source;
        MetadataPrefix = metadataPrefix;
    }
}

public static class JerseyLogoOptions
{
    public static string Field(bool away) => AppearanceFields.JerseyLogoField(away);
    public static string Prefix(bool away) => away ? "Export Jersey Logo Away" : "Export Jersey Logo";
    private static bool IsMetadata(string key, bool away) => new[] { "Mode", "ID", "SHA256", "PNG" }
        .Any(suffix => key.Equals(Prefix(away) + " " + suffix, StringComparison.OrdinalIgnoreCase));

    public static JerseyLogoChoice Read(CampaignDocument document, bool away)
    {
        string source = document.Get(Field(away)).Trim();
        JerseyLogoMode mode = source.Length == 0 ? JerseyLogoMode.Inherit
            : source.Equals("none", StringComparison.OrdinalIgnoreCase) ? JerseyLogoMode.None : JerseyLogoMode.Image;
        return new JerseyLogoChoice(mode, source, Prefix(away));
    }

    // The existing personal shared logo wins only when the player owns their look.
    // A frozen export has no team document, so its resolved per-kit choice is read back.
    public static JerseyLogoChoice Resolve(CampaignDocument player, CampaignDocument team, bool away)
    {
        if (team.Fields.Count == 0 && player.Get(Prefix(away) + " Mode").Length > 0)
        {
            JerseyLogoChoice frozen = Read(player, away);
            if (frozen.Mode != JerseyLogoMode.Inherit) return frozen;
        }
        string shared = player.Get("Logo From").Trim();
        if (AppearanceFields.OverridesTeam(player) && shared.Length > 0)
            return Choice(shared, away);
        JerseyLogoChoice kit = Read(team, away);
        if (kit.Mode != JerseyLogoMode.Inherit) return kit;
        shared = team.Fields.Count == 0 ? player.Get("Logo From").Trim() : team.Get("Logo From").Trim();
        return Choice(shared, away);
    }

    private static JerseyLogoChoice Choice(string source, bool away)
        => new JerseyLogoChoice(source.Length == 0 ? JerseyLogoMode.Inherit
            : source.Equals("none", StringComparison.OrdinalIgnoreCase) ? JerseyLogoMode.None : JerseyLogoMode.Image,
            source, Prefix(away));

    public static void Clear(CampaignDocument document, bool away)
    {
        document.Remove(Field(away));
        foreach (string key in document.Fields.Keys.Where(k => IsMetadata(k, away)).ToArray()) document.Remove(key);
    }

    public static void Copy(CampaignDocument source, bool sourceAway, CampaignDocument target, bool targetAway)
    {
        string selected = source.Get(Field(sourceAway));
        Clear(target, targetAway);
        if (selected.Length > 0) target.Set(Field(targetAway), selected);
        string sourcePrefix = Prefix(sourceAway) + " ", targetPrefix = Prefix(targetAway) + " ";
        foreach (var pair in source.Fields.Where(p => IsMetadata(p.Key, sourceAway)).ToArray())
            target.Set(targetPrefix + pair.Key.Substring(sourcePrefix.Length), pair.Value);
    }
}
