using Avalonia.Controls;
using CampaignCore;

namespace CampaignCreator;

internal sealed partial class TeamEditorView
{
    internal Control JerseyLogoPicker(bool slotAway)
    {
        string field = JerseyLogoOptions.Field(slotAway);
        string prefix = JerseyLogoOptions.Prefix(slotAway);
        if (!equipment.ContainsKey(field)) equipment[field] = Value(field);
        Track(field, () => equipment[field]);
        foreach (string suffix in new[] { "Mode", "ID", "SHA256", "PNG" })
        {
            string key = prefix + " " + suffix;
            if (!equipment.ContainsKey(key)) equipment[key] = Value(key);
            Track(key, () => equipment[key]);
        }

        string selected = equipment[field];
        var mode = new ComboBox {
            Name = slotAway ? "JerseyLogoModeAway" : "JerseyLogoModeHome",
            ItemsSource = new[] { "Use team logo", "No jersey logo", "Choose image" },
            SelectedIndex = selected.Length == 0 ? 0 : selected.Equals("none", StringComparison.OrdinalIgnoreCase) ? 1 : 2
        };
        void Set(string source)
        {
            if (equipment[field] == source && new[] { "Mode", "ID", "SHA256", "PNG" }.All(suffix => equipment[prefix + " " + suffix].Length == 0)) return;
            equipment[field] = source;
            foreach (string suffix in new[] { "Mode", "ID", "SHA256", "PNG" }) equipment[prefix + " " + suffix] = "";
            changed(); RefreshUniform();
        }
        Func<string> pickerRead = () => "";
        var picker = LogoPicker.Create(data.Root, selected is "" or "none" ? "" : selected,
            () => { Set(pickerRead()); mode.SelectedIndex = 2; }, "No image selected", "Choose jersey logo",
            buttonCaption: "Choose jersey image");
        pickerRead = picker.read;
        picker.control.Name = slotAway ? "JerseyLogoImageAway" : "JerseyLogoImageHome";
        mode.SelectionChanged += (_, _) => {
            if (mode.SelectedIndex == 0) Set("");
            else if (mode.SelectedIndex == 1) Set("none");
            else if (mode.SelectedIndex == 2 && equipment[field] is "" or "none")
                mode.SelectedIndex = equipment[field].Length == 0 ? 0 : 1;
        };
        return new StackPanel { Name = slotAway ? "JerseyLogoAway" : "JerseyLogoHome",
            Spacing = StudioTheme.SpaceXs,
            Children = { new TextBlock { Text = "Jersey logo" }, mode, picker.control } };
    }
}
