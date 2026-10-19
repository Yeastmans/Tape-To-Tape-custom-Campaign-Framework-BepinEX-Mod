using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CampaignCore;

public enum FieldKind { Text, Color, Choice, Boolean, Number, Multiple }

/// <summary>Legacy file semantics shared by the editor, export and runtime catalog.</summary>
public static class FieldSchema
{
    public const string RefereeHeadKey = "Referee Head";
    public static readonly string[] Sizes = { "ExtraSmall", "Small", "Medium", "Big", "ExtraBig", "ExtraExtraBig" };
    public static readonly string[] Stats = { "Speed", "Shot Power", "Accuracy", "Checking", "Skill", "Catching", "Glove", "Blocker", "Five Hole", "Standing Speed", "Stand Speed", "Butterfly Speed", "Control", "Recovery", "Pass Power", "Poke Check", "Pokecheck", "Depth" };
    public static readonly string[] TeamColors = { "Jersey Primary", "Jersey Secondary", "Jersey Accent", "Away Primary", "Away Secondary", "Away Accent", "Number Color Home", "Number Color Away" };
    // "Number Style" = SkaterData.numberFormat. Players may inherit the team or keep an explicit native style.
    public static readonly string[] NumberStyles = { "Regular", "Roman numerals", "Prison" };
    public const string InheritNumberStyle = "Inherit team";
    public static int PlayerNumberStyleIndex(string value) => string.IsNullOrWhiteSpace(value)
        || value.Trim().Equals(InheritNumberStyle, StringComparison.OrdinalIgnoreCase) ? -1 : NumberStyleIndex(value);
    public static string EffectiveNumberStyle(string player, string team)
        => PlayerNumberStyleIndex(player) < 0 ? team : player;
    public static int NumberStyleIndex(string value)
    {
        string v = (value ?? "").Replace(" ", "");
        if (v.Equals("RomanNumerals", StringComparison.OrdinalIgnoreCase) || v.Equals("Roman", StringComparison.OrdinalIgnoreCase)) return 1;
        return v.Equals("Prison", StringComparison.OrdinalIgnoreCase) ? 2 : 0;
    }
    // Back-number text as CustomNumberApplier.GenerateNumberString writes it.
    public static string NumberText(int number, string style)
    {
        switch (NumberStyleIndex(style))
        {
            case 1:
                var roman = new System.Text.StringBuilder();
                foreach (var (value, digits) in new[] { (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"), (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I") })
                    while (number >= value) { roman.Append(digits); number -= value; }
                return roman.ToString();
            case 2: return number.ToString(CultureInfo.InvariantCulture).PadLeft(5, '0');
            default: return number.ToString(CultureInfo.InvariantCulture);
        }
    }
    public static readonly string[] PlayerColors ={ "Jersey Color", "Jersey Secondary Color", "Jersey Accent Color", "Jersey Away Color", "Jersey Away Secondary Color", "Jersey Away Accent Color" };
    // Socks hold a jersey (Body) style whose leg pieces replace the jersey's own; blank follows the jersey.
    public static readonly string[] SkaterAssets = { "Face", "Body", "Body Away", "Socks", "Socks Away", "Helmet", "Helmet Away", "Bicep", "Bicep Away", "Gloves", "Gloves Away", "Pants", "Pants Away", "Skates", "Skates Away", "Stick", "Glasses", "Number Skin" };
    public static readonly string[] GoalieAssets = { "Skin", "Skin Away", "Goalie Pants Skin", "Goalie Pants Away", "Helmet Skin", "Glove Skin", "Glove Away", "Blocker Skin", "Blocker Away", "Pads Skin", "Pads Away", "Stick Skin", "Stick Away", "Logo Skin" };
    public static IEnumerable<string> EquipmentColors()
    {
        foreach (var piece in new[] { "Helmet", "Gloves", "Pants", "Socks", "Number", "Stick", "Skates", "Blade", "Laces", "Bicep" })
        foreach (var away in new[] { "", " Away" })
        {
            yield return piece + away + " Color";
            if (new[] { "Helmet", "Gloves", "Pants", "Socks", "Number", "Stick" }.Contains(piece)) yield return piece + away + " Secondary Color";
            if (new[] { "Helmet", "Gloves", "Pants", "Socks", "Number", "Stick" }.Contains(piece)) yield return piece + away + " Tertiary Color";
        }
    }
    private static bool Has(IEnumerable<string> set, string key) => set.Contains(key, StringComparer.OrdinalIgnoreCase);
    public static FieldKind Kind(string key, string value = "")
    {
        if (key.Equals(SquadPlayerPools.ModeKey, StringComparison.OrdinalIgnoreCase)) return FieldKind.Boolean;
        if (Has(PlayerKitColors.ModeKeys, key)) return FieldKind.Boolean;
        if (key.Equals(PlayerBrawlOptions.PrinceKey, StringComparison.OrdinalIgnoreCase)) return FieldKind.Boolean;
        if (key.Equals(PlayerContractOptions.EnabledKey, StringComparison.OrdinalIgnoreCase)) return FieldKind.Boolean;
        if (key.Equals(PlayerContractOptions.MatchesKey, StringComparison.OrdinalIgnoreCase)) return FieldKind.Number;
        if (Has(RelicOpponentInteractions.All.Select(i => i.SettingKey), key)) return FieldKind.Choice;
        if (key.Equals("Skin Color", StringComparison.OrdinalIgnoreCase) || key.Equals("Size", StringComparison.OrdinalIgnoreCase)
            || key.Equals("Number Style", StringComparison.OrdinalIgnoreCase)) return FieldKind.Choice;
        if (key.IndexOf("Color", StringComparison.OrdinalIgnoreCase) >= 0 || Has(TeamColors, key) || key.StartsWith("Transition ", StringComparison.OrdinalIgnoreCase)) return FieldKind.Color;
        if (Has(SkaterAssets.Concat(GoalieAssets), key) || Has(new[] { "Ability", "Squad Template", "Logo From", "Position", RefereeHeadKey }, key)) return FieldKind.Choice;
        if (Has(new[] { "Talents", "Goalie Talents", "Team Talents", "Team Relics", "Relics", "Random Pool", "Team Random Pool" }, key)) return FieldKind.Multiple;
        if (Has(new[] { "Left Handed", "Replace Challenges", "Replace Soccer Ball", "Replace Golf Ball", "Use Player Teams", "No Bench Bonus", "Disable Bench Bonus", "Golf Fairway", "Tycoons Sponsors", "Canada Curses" }, key)
            || Has(new[] { "yes", "no", "true", "false" }, value)) return FieldKind.Boolean;
        if (Has(Stats, key) || Has(new[] { "Number", "Bench Size", "Random Talents", "Team Random Talents", "Size Offset", "Pass Read", "Stat Scale" }, key)) return FieldKind.Number;
        return FieldKind.Text;
    }
    public static bool TryRgb(string value, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        var parts = value.Split(',');
        return parts.Length == 3 && byte.TryParse(parts[0].Trim(), out r) && byte.TryParse(parts[1].Trim(), out g) && byte.TryParse(parts[2].Trim(), out b);
    }
    public static string Rgb(byte r, byte g, byte b) => string.Format(CultureInfo.InvariantCulture, "{0}, {1}, {2}", r, g, b);
}
