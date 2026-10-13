using System;
using System.Collections.Generic;
using System.Linq;

namespace CampaignCore;

// Effective values used by the editor, export, and native renderer bridge.
public static class AppearanceContract
{
    public static readonly string[] Schemes = { "jersey", "helmet", "gloves", "pants", "socks", "skates", "stick", "number" };
    public static string ColorKey(string scheme, int channel, bool away)
    {
        string a = away ? " Away" : "";
        if (scheme == "jersey") return "Jersey" + a + new[] { " Color", " Secondary Color", " Accent Color" }[channel];
        if (scheme == "skates") return new[] { "Skates", "Blade", "Laces" }[channel] + a + " Color";
        return char.ToUpperInvariant(scheme[0]) + scheme.Substring(1) + a + new[] { " Color", " Secondary Color", " Tertiary Color" }[channel];
    }
    public static string TeamKey(string key) => key switch {
        "Jersey Color" => "Jersey Primary", "Jersey Secondary Color" => "Jersey Secondary", "Jersey Accent Color" => "Jersey Accent",
        "Jersey Away Color" => "Away Primary", "Jersey Away Secondary Color" => "Away Secondary", "Jersey Away Accent Color" => "Away Accent",
        "Number Tertiary Color" => "Number Color Home", "Number Away Tertiary Color" => "Number Color Away", _ => key };
    public static string HomeKey(string key) => key switch { "Glove Away" => "Glove Skin", "Blocker Away" => "Blocker Skin", "Pads Away" => "Pads Skin", "Stick Away" => "Stick Skin", "Goalie Pants Away" => "Goalie Pants Skin", _ => key.Replace(" Away", "") };
    // The game draws socks from the jersey (Body) skin. A blank sock style follows that kit's jersey.
    public static string SockStyle(Func<string, string> value, bool away)
    {
        string socks = value(away ? "Socks Away" : "Socks");
        return socks.Length > 0 ? socks : value(away ? "Body Away" : "Body");
    }
    public static IEnumerable<string> Keys(string role)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string key in role == "goalie" ? FieldSchema.GoalieAssets : FieldSchema.SkaterAssets)
            if (key != "Face") keys.Add(key);
        foreach (string scheme in Schemes)
            foreach (bool away in new[] { false, true })
                for (int channel = 0; channel < 3; channel++) keys.Add(ColorKey(scheme, channel, away));
        foreach (string key in new[] { "Logo From", "Logo Skin", "Bicep Color", "Bicep Away Color" }) keys.Add(key);
        return keys;
    }
    public static string DefaultSkin(string role, string key)
    {
        key = HomeKey(key);
        if (role == "goalie") return key switch {
            "Skin" or "Goalie Pants Skin" => "Body/Customization_colors", "Helmet Skin" => "Helmet/Helmet_Customization_colors",
            "Glove Skin" => "Body_Glove/Customization/Customization_colors", "Blocker Skin" => "Body_Blocker/Customization/Customization_colors",
            "Pads Skin" => "Body_Pads/Customization/Customization_colors", "Stick Skin" => "Body_Stick/Customization/Customization_colors", _ => "" };
        return key switch { "Body" => "Body/Customization/Customization_colors", "Stick" => "Sticks/Black",
            "Helmet" => "Faces/Custom/Helmet_Colors", "Bicep" => "Body_Bicep/Customization/Customization_colors",
            "Gloves" => "Body_Gloves/Customization/Customization_colors", "Pants" => "Body_Pants/Customization/Customization_colors",
            "Skates" => "Body_Skates/Customization/Customization_colors", _ => "" };
    }
    // Base game: neither the team nor the player customizer has a skate style; every custom
    // skater wears the colorable skate, colored by the team kit or the player's own skate colors.
    public static string StandardSkates => DefaultSkin("skater", "Skates");
    // The Studio's default kit (what an unset piece previews as).
    public static string DefaultColor(string scheme, int channel) => scheme switch {
        "number" => "255,255,255", "skates" => new[] { "23,38,50", "201,215,224", "255,255,255" }[channel],
        "stick" => "198,213,224", _ => new[] { "23,75,121", "36,154,104", "255,255,255" }[channel] };
    public static List<PlayerColorModel.Change> PinSkates(CampaignDocument doc)
    {
        var changes = new List<PlayerColorModel.Change>();
        foreach (string key in new[] { "Skates", "Skates Away" })
        {
            if (!doc.Fields.ContainsKey(key)) continue;
            string old = doc.Get(key);
            if (old.Length > 0 && !old.Equals(StandardSkates, StringComparison.OrdinalIgnoreCase))
                changes.Add(new PlayerColorModel.Change(key, old, "", "removed (base game has no skate style; standard colorable skate)"));
            doc.Remove(key);
        }
        return changes;
    }
    public static bool IsColor(string key) => AppearanceFields.ColorKey(key).Length > 0 || key.StartsWith("Bicep ", StringComparison.OrdinalIgnoreCase) && key.EndsWith(" Color", StringComparison.OrdinalIgnoreCase);
    // Keys a player file owns: every style and logo key, plus only the stick/skate colors.
    public static IEnumerable<string> PlayerKeys(string role) => Keys(role).Where(k => !IsColor(k)).Concat(PlayerColorModel.PlayerKeys);
    public static bool Independent(CampaignDocument player, CampaignDocument team, string role)
        => AppearanceFields.OverridesTeam(player);
    // A logo is one shared player selection. Either premade kit makes that
    // selection incompatible; it must never erase the team's identity logo.
    public static bool SupportsJerseyLogo(string role, string home, string away)
    {
        string key = role == "goalie" ? "Skin" : "Body";
        string Normalize(string skin) => string.IsNullOrWhiteSpace(skin)
            || skin.Equals("standard", StringComparison.OrdinalIgnoreCase)
            || skin.Equals("team colors", StringComparison.OrdinalIgnoreCase)
                ? DefaultSkin(role, key) : skin;
        home = Normalize(home);
        away = Normalize(away);
        string customizable = DefaultSkin(role, key);
        return home.Equals(customizable, StringComparison.OrdinalIgnoreCase)
            && away.Equals(customizable, StringComparison.OrdinalIgnoreCase);
    }
    public static bool SupportsJerseyLogo(CampaignDocument player, string role)
        => SupportsJerseyLogo(role, player.Get(role == "goalie" ? "Skin" : "Body"), player.Get(role == "goalie" ? "Skin Away" : "Body Away"));
    public static void ClearJerseyLogo(CampaignDocument player)
    {
        player.Set("Logo From", ""); player.Set("Logo Skin", "");
        foreach (string key in player.Fields.Keys.Where(k => k.StartsWith("Export Logo ", StringComparison.OrdinalIgnoreCase)).ToArray()) player.Remove(key);
    }
    public static CampaignDocument Resolve(CampaignDocument player, CampaignDocument team, CampaignDocument goalie, string role)
    {
        var result = new CampaignDocument(player.ToString());
        bool independent = Independent(player, team, role);
        foreach (string key in Keys(role))
        {
            string inherited = role == "goalie" && FieldSchema.GoalieAssets.Contains(key) && key != "Logo Skin" ? goalie.Get(key) : team.Get(TeamKey(key));
            string value;
            if (IsColor(key))
            {
                // The game's model (PlayerColorModel): the kit is the team's; only the stick and
                // skate colors can be the player's, one value for both kits, independent of the
                // equipment-style toggle.
                string home = key.Replace(" Away", "");
                string own = player.Get(home);
                // Stick colors never come from the team: blank falls to the default stick colors below.
                bool stick = PlayerColorModel.StickKeys.Contains(home, StringComparer.OrdinalIgnoreCase);
                value = PlayerColorModel.IsPlayerColor(home) && own.Length > 0 ? own : stick ? "" : inherited;
            }
            else if (key == "Stick" && role == "skater") value = player.Get("Stick"); // the player's own stick style, always
            else value = independent && key.Contains(" Away", StringComparison.OrdinalIgnoreCase)
                ? player.Get(key)
                : independent && player.Get(key).Length > 0 ? player.Get(key) : inherited;
            result.Set(key, value);
        }
        // An unset away choice uses its own customizable style. The editor reads
        // the authored color, so an unset channel remains visible as a checker.
        foreach (string key in Keys(role))
        {
            string value = result.Get(key);
            if (value.Length == 0 || value.Equals("standard", StringComparison.OrdinalIgnoreCase) || value.Equals("team colors", StringComparison.OrdinalIgnoreCase))
            {
                string fallback = DefaultSkin(role, key);
                if (fallback.Length > 0) result.Set(key, fallback);
            }
        }
        if (role == "skater") { result.Set("Skates", StandardSkates); result.Set("Skates Away", StandardSkates); }
        if (role == "skater" && result.Get("Face").Length > 0)
        {
            result.Set("Face", HeadAppearance.Base(result.Get("Face")));
            if (!HeadAppearance.CanWearHelmet(result.Get("Face")))
            {
                result.Set("Helmet", "none");
                result.Set("Helmet Away", "none");
            }
        }
        foreach (bool away in new[] { false, true })
        {
            string bicep = result.Get(away ? "Bicep Away Color" : "Bicep Color");
            if (bicep.Length > 0) result.Set(ColorKey("jersey", 1, away), bicep);
            foreach (string scheme in Schemes)
            for (int c = 0; c < 3; c++)
            {
                string key = ColorKey(scheme, c, away);
                if (result.Get(key).Length > 0) continue;
                result.Set(key, DefaultColor(scheme, c));
            }
        }
        if (!SupportsJerseyLogo(result, role)) ClearJerseyLogo(result);
        return result;
    }
    public static string Hex(CampaignDocument effective, string token, bool away)
    {
        int c = token[token.Length - 1] - '0'; string scheme = token.Substring(0, token.Length - 1);
        if (scheme == "numbers") scheme = "number";
        if (scheme == "bicep") scheme = "jersey";
        string value = effective.Get(ColorKey(scheme, c, away));
        return FieldSchema.TryRgb(value, out byte r, out byte g, out byte b) ? $"#{r:X2}{g:X2}{b:X2}" : "#FFFFFF";
    }
}

