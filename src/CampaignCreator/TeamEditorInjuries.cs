using Avalonia.Controls;
using Avalonia.Layout;
using CampaignCore;

namespace CampaignCreator;

internal sealed partial class TeamEditorView
{
    private Action? resetInjuriesForTemplate;

    private Control InjuryControls()
    {
        // Keep the existing inverse key so older teams and the native squad hook agree.
        const string key = "Squad Behavior no_injuries";
        string saved = Value(key);
        bool refreshing = false;
        var injuries = new CheckBox { Name = "PlayerTeamInjuries", Content = "Injuries",
            IsChecked = !SquadBehaviors.Enabled(Value("Squad Template"), "no_injuries", _ => saved) };
        Track(key, () => saved);
        injuries.IsCheckedChanged += (_, _) => {
            if (refreshing) return;
            saved = injuries.IsChecked == true ? "Off" : "On";
            changed();
        };
        resetInjuriesForTemplate = () => {
            saved = "Inherit";
            refreshing = true;
            injuries.IsChecked = !SquadBehaviors.Enabled(Value("Squad Template"), "no_injuries", _ => saved);
            refreshing = false;
        };
        return Card(new StackPanel { Spacing = 6, MaxWidth = 700, HorizontalAlignment = HorizontalAlignment.Left,
            Children = { injuries, Label("On for every squad except Oldschool by default. Selecting a different squad restores its native default. You can change this setting and save it with the player team.", 12) } }, 12);
    }
}
