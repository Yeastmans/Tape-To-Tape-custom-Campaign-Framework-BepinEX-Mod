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
        IEnumerable<PreviewGridPicker.Choice> Choices(string current)
        {
            var choices = data.HeadChoices(current).ToList();
            // A missing custom project remains a visible saved choice until changed.
            if (current.Length > 0 && !choices.Contains(current, StringComparer.OrdinalIgnoreCase)) choices.Add(current);
            return new[] { new PreviewGridPicker.Choice("", "Game default") }.Concat(choices.Select(id =>
                new PreviewGridPicker.Choice(id, FieldControls.Friendly(id))));
        }
        var picker = PreviewGridPicker.Create("Choose referee head", Choices(initial), initial,
            id => PreviewGridPicker.FileImage(data.HeadPreview("skater", id)),
            () => { if (HasUnsavedChanges) message.Text = "Unsaved campaign changes"; }, choicesForSelection: Choices);
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
