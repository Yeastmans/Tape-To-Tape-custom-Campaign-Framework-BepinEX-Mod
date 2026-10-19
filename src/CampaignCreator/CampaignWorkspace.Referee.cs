using Avalonia.Controls;
using Avalonia.Media;
using CampaignCore;

namespace CampaignCreator;

public sealed partial class CampaignWorkspace
{
    private Control RefereeHeadPicker()
    {
        string original = settings.Get(FieldSchema.RefereeHeadKey);
        string initial = HeadAppearance.Base(original);
        var choices = HeadAppearance.Choices(data.Assets.Values("skater", "Face")).ToList();
        // A missing custom project remains a visible saved choice until changed.
        if (initial.Length > 0 && !choices.Contains(initial, StringComparer.OrdinalIgnoreCase)) choices.Add(initial);
        var picker = PreviewGridPicker.Create("Choose referee head",
            new[] { new PreviewGridPicker.Choice("", "Game default") }.Concat(choices.Select(id =>
                new PreviewGridPicker.Choice(id, FieldControls.Friendly(id)))), initial,
            id => PreviewGridPicker.FileImage(data.HeadPreview("skater", id)),
            () => { if (HasUnsavedChanges) message.Text = "Unsaved campaign changes"; });
        picker.control.Name = "CampaignRefereeHead";
        picker.control.Tag = FieldSchema.RefereeHeadKey;
        fields[FieldSchema.RefereeHeadKey] = () => picker.read() == initial ? original : picker.read();
        return new StackPanel { Spacing = StudioTheme.SpaceSm, Children = {
            new TextBlock { Text = "Referee head" }, picker.control,
            new TextBlock { Text = "Choose the match officials' head for this campaign. Game default keeps the game's chosen heads. This also applies when a second referee is present.",
                Foreground = StudioTheme.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap }
        } };
    }
}
