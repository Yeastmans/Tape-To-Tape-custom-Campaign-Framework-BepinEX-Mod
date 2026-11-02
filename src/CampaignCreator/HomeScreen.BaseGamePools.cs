using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CampaignCore;

namespace CampaignCreator;

public sealed partial class MainWindow
{
    private BaseGamePoolsView? baseGamePoolsView;
    private string? baseGamePoolPlayerFile;
    private async Task ShowBaseGamePools()
    {
        DiscardEditorChangesForNavigation(); campaignWorkspace = null;
        try
        {
            if (!File.Exists(Path.Combine(BaseGamePlayerPools.Folder(root), "settings.txt")) && StudioArt.Path("base-campaigns", "catalog.json") == null)
                await EnsureGameArt();
            OpenBaseGamePoolsProfile(new BaseGamePoolsContent(root, StudioArt.Folder));
        }
        catch (Exception ex) { ShowBaseGamePoolsError(ex); }
    }
    private void OpenBaseGamePoolsProfile(BaseGamePoolsContent profile)
    {
        baseGamePoolsView = new BaseGamePoolsView(profile, () => StudioArt.Folder,
            () => ContentImport.Pick(this, root, "skater"), OpenBaseGamePoolPlayer, ShowHome);
        Content = baseGamePoolsView;
    }
    private void ShowBaseGamePoolsError(Exception error)
    {
        var recover = Button("Restore base-game pool defaults", async () => await RecoverBaseGamePools());
        recover.Name = "BaseGamePoolsRecover"; StudioTheme.Primary(recover);
        Content = new StackPanel { Margin = StudioTheme.PagePadding, Spacing = StudioTheme.SpaceLg, Children = {
            new TextBlock { Text = "Base Game Pools", FontSize = 28, FontWeight = FontWeight.Bold },
            new TextBlock { Name = "BaseGamePoolsOpenError", Text = "Could not open Base Game Pools. " + error.Message, TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = "Your saved settings and player files have been kept. Restore defaults to prepare a fresh draft from your installed game and back up the previous settings. Save pools applies the recovered draft; Back leaves your profile unchanged.", TextWrapping = TextWrapping.Wrap },
            recover, Button("Back / Home", ShowHome)
        }};
    }
    private async Task RecoverBaseGamePools()
    {
        try
        {
            if (StudioArt.Path("base-campaigns", "catalog.json") == null) await EnsureGameArt();
            OpenBaseGamePoolsProfile(BaseGamePoolsContent.RecoverDefaults(root, StudioArt.Folder));
        }
        catch (Exception ex) { ShowBaseGamePoolsError(ex); }
    }
    private void OpenBaseGamePoolPlayer(string file)
    {
        baseGamePoolPlayerFile = file;
        OpenFile(file); Content = editorShell;
    }
    private void ReturnToBaseGamePools()
    {
        DiscardEditorChangesForNavigation(); baseGamePoolPlayerFile = null;
        if (baseGamePoolsView == null) { ShowHome(); return; }
        baseGamePoolsView.RefreshCandidates(); Content = baseGamePoolsView;
    }
}
