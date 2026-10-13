using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CampaignCreator;

internal static class EquipmentColorGuide
{
    internal static readonly (string Part, string Channels, string Note)[] Entries =
    [
        ("Skater helmet", "Helmet colour", "Only the primary colour changes the skater helmet."),
        ("Goalie mask", "Design / Cage", "The shell uses the team helmet colour. Separate goalie mask controls change the design (secondary) and cage (tertiary)."),
        ("Name & numbers", "Name / Outline / Number", "Primary colours the name, secondary the outline, tertiary the actual digits."),
        ("Gloves", "Main / Details", "Only the first two channels are used. The third is irrelevant for the observed gloves."),
        ("Jersey", "Main / Stripe 1 / Stripe 2", "The two stripe colours run around the upper sleeves and belly/hem, as shown on the flat diagram."),
        ("Goalie blocker", "Custom color / Premade", "The customizable blocker uses the team's skates color scheme; a premade keeps its own artwork."),
        ("Goalie pants", "Pants primary", "The brief-shaped goalie pants use only pants primary, with no secondary or tertiary regions."),
        ("Socks / goalie pads", "Base / Thin stripe / Thick stripe", "These channels colour skater socks. On goalies they colour the pads and their design."),
        ("Skates", "Skates / Laces / Blades", "The customizable skate uses primary for the boot, secondary for laces and tertiary for blades. Existing saved colours are preserved."),
        ("Goalie stick", "Custom color / Premade", "The customizable goalie stick uses the team's stick color scheme; a premade keeps its own artwork."),
        ("Skater stick", "Stick / Tape / Writing", "All three channels are preserved by Export to Play Now. The selected stick artwork determines which channels are visible.")
    ];

    internal static string Channel(string key)
    {
        if (key is "Number Color Home" or "Number Color Away") return "Number";
        string k = key.Replace(" Away", "");
        return k switch {
            "Jersey Primary" or "Away Primary" or "Jersey Color" => "Main jersey",
            "Jersey Secondary" or "Away Secondary" or "Jersey Secondary Color" => "Stripe 1",
            "Jersey Accent" or "Away Accent" or "Jersey Accent Color" => "Stripe 2",
            "Helmet Color" => "Helmet / mask",
            "Helmet Secondary Color" => "Goalie design",
            "Helmet Tertiary Color" => "Goalie cage",
            "Gloves Color" => "Main glove",
            "Gloves Secondary Color" => "Glove details",
            "Gloves Tertiary Color" => "Unused glove channel",
            "Number Color" => "Name",
            "Number Secondary Color" => "Outline",
            "Number Tertiary Color" or "Number Color Home" or "Number Color Away" => "Number",
            "Socks Color" => "Socks / pads base",
            "Socks Secondary Color" => "Thin stripe / pad design 1",
            "Socks Tertiary Color" => "Thick stripe / pad design 2",
            "Skates Color" => "Skates",
            "Blade Color" => "Laces",
            "Laces Color" => "Blades",
            "Stick Color" => "Stick",
            "Stick Secondary Color" => "Tape",
            "Stick Tertiary Color" => "Writing",
            _ => key
        };
    }
    internal static string FieldLabel(string key, string role)
    {
        string label = Channel(key);
        if (label == key) return key;
        if (key.StartsWith("Helmet") && role == "skater" && !key.EndsWith("Helmet Color") && key.Contains("Secondary")) label = "Goalie design (unused on skaters)";
        if (key.StartsWith("Helmet") && role == "skater" && key.Contains("Tertiary")) label = "Goalie cage (unused on skaters)";
        if (key.StartsWith("Socks")) label = role == "goalie" ? label.Replace("Socks / pads base","Pads base").Replace("Thin stripe / pad design 1","Pad design 1").Replace("Thick stripe / pad design 2","Pad design 2") : label.Replace("Socks / pads base","Socks base").Replace("Thin stripe / pad design 1","Thin sock stripe").Replace("Thick stripe / pad design 2","Thick sock stripe");
        return label + (key.Contains("Away") ? " · Away" : "");
    }
    internal static Control Content()
    {
        var panel = new StackPanel { Spacing = 12, Width = 680 };
        foreach (var entry in Entries) panel.Children.Add(new StackPanel { Spacing = 3, Children = {
            new TextBlock { Text = entry.Part + " · " + entry.Channels, Foreground = Brush.Parse("#6FE7BA"), FontSize = 15, FontWeight = FontWeight.SemiBold },
            new TextBlock { Text = entry.Note, TextWrapping = TextWrapping.Wrap, FontSize = 13 }
        }});
        return panel;
    }
}
