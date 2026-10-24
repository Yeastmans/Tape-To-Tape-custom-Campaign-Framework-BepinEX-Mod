using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CampaignCore;

namespace CampaignCreator;

internal sealed class SquadMapsEditor : UserControl
{
    internal SquadMapsEditor(Window owner, string root, string campaign, string squad, CampaignDocument document,
        Action changed, Action<string, Func<string>> register, Func<Task<bool>> saveTeam)
    {
        var own = new CheckBox { Name = "UseOwnMaps", Content = "Use own maps", IsChecked = SquadMapOverrides.Own(document) };
        register(SquadMapOverrides.ModeKey, () => own.IsChecked == SquadMapOverrides.Own(document)
            ? document.Get(SquadMapOverrides.ModeKey) : own.IsChecked == true ? "yes" : "no");
        var hint = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = StudioTheme.Muted };
        var message = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var edit = new Button { Name = "EditTeamMaps", Content = "Edit team maps", HorizontalAlignment = HorizontalAlignment.Left, Padding = StudioTheme.ButtonPadding };
        void Refresh() {
            bool retained = SquadMapOverrides.Files(squad).Any();
            edit.IsVisible = own.IsChecked == true || retained;
            edit.Content = own.IsChecked == true ? "Edit team maps" : "Edit saved team maps";
            hint.Text = own.IsChecked == true
                ? "Maps follow the campaign until you edit them for this squad. Each edited map stays independent. Pending player team changes must save before opening its maps."
                : "This squad uses the campaign maps. Saved team map edits are kept and can be edited or reset before enabling them again.";
        }
        own.IsCheckedChanged += (_, _) => { changed(); Refresh(); };
        edit.Click += async (_, _) => {
            edit.IsEnabled = false;
            try {
                if (!await saveTeam()) {
                    message.Text = "Save the player team successfully before editing its maps. Check the team's save message."
                        + (own.IsChecked == true && SquadMapOverrides.Files(squad).Any() ? " To repair saved maps before activation, turn Use own maps off and choose Edit saved team maps." : "");
                    return;
                }
                if (!SquadMapOverrides.Own(new CampaignDocument(File.ReadAllText(Path.Combine(squad, "team.txt"))))) {
                    if (own.IsChecked == true) throw new InvalidOperationException("Save Use own maps before opening team maps.");
                    if (!SquadMapOverrides.Files(squad).Any()) throw new InvalidOperationException("There are no saved team maps to edit. Enable Use own maps to create one.");
                }
                message.Text = "";
                await new MapEditorWindow(root, campaign, squad).ShowDialog(owner);
            }
            catch (Exception ex) { message.Text = ex.Message; }
            finally { edit.IsEnabled = true; Refresh(); }
        };
        Refresh();
        Content = new StackPanel { Margin = new Thickness(16), Spacing = StudioTheme.SpaceMd, Children = {
            new TextBlock { Text = "Team maps", FontSize = 24, FontWeight = FontWeight.SemiBold }, own, hint, edit, message
        } };
    }
}
