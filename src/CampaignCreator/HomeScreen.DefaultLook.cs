using Avalonia.Controls;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Media;

namespace CampaignCreator;

public sealed partial class MainWindow
{
    private bool defaultLookPromptShown;

    private async Task PromptForDefaultLook()
    {
        if (defaultLookPromptShown || File.Exists(DefaultPlayerLook.FilePath(root))) return;
        defaultLookPromptShown = true;
        var prompt = new Window { Name = "DefaultLookPrompt", Title = "Set your default team look",
            Width = 490, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = StudioTheme.Background };
        var setup = Button("Set default", () => prompt.Close(true));
        setup.Name = "SetDefaultNow";
        var later = Button("Later", () => prompt.Close(false));
        later.Name = "DefaultLookLater";
        prompt.Content = new StackPanel { Margin = new Thickness(24), Spacing = StudioTheme.SpaceLg, Children = {
            new TextBlock { Text = "Set your default team look", FontSize = 24, Foreground = Brushes.White },
            new TextBlock { Text = "You haven't saved a default look yet. New player teams and opponents start with this look: logo, kit colors, equipment styles and goalie gear. New players start with its styles and its stick. You can change it from the home screen anytime.",
                TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White },
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = StudioTheme.SpaceMd, Children = { setup, later } }
        }};
        if (await prompt.ShowDialog<bool>(this)) ShowDefaultLook();
    }

    private void ShowDefaultLook()
    {
        var feedback = new TextBlock { Text = "Choose the team logo and home and away equipment and colors. New player teams and opponents start with this look; new players start with its styles and the Stick (new players) choice. Save default keeps it for future sessions.",
            TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White };
        try
        {
            TeamEditorView? editor = null;
            editor = new TeamEditorView(this, DefaultPlayerLook.Load(root), data, root, DefaultPlayerLook.FilePath(root),
                () => { }, (_, _) => { }, () => {
                    try { DefaultPlayerLook.Save(root, editor!.DefaultLookValue); ShowHome(); }
                    catch (Exception ex) { feedback.Text = "Could not save the default look: " + ex.Message; }
                }, ShowHome, () => { }, _ => { }, (_, _) => { }, () => { }, feedback, defaultsOnly: true);
            Content = editor;
        }
        catch (Exception ex) { status.Text = "Could not open the default look: " + ex.Message; }
    }
}
