using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CampaignCreator;

public sealed partial class MainWindow
{
    private void OpenCampaignDraft(string directory, string name = "")
    {
        campaignWorkspace = new CampaignWorkspace(this, root, directory, ShowHome,
            file => { OpenFile(file); Content = editorShell; }, async () => await ChooseContent("team", true),
            isNew: true, created: saved =>
            {
                loading = true; campaigns.ItemsSource = CampaignChoices().Select(c => c.Label).ToArray();
                campaigns.SelectedItem = Path.GetFileName(saved); loading = false; _ = LoadCampaign();
            }, initialName: name);
        Content = campaignWorkspace;
    }
    private async Task CopyVanillaFromHome()
    {
        if (StudioArt.Path("base-campaigns","catalog.json") == null) await EnsureGameArt();
        var dialog = new Window { Name="CopyVanillaCampaignDialog",Title="Copy vanilla campaign",Width=600,Height=370,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=StudioTheme.Background };
        var choices=SquadTemplateCatalog.All.Where(s=>s.Id!="noeffects").ToArray();
        var picker=new ComboBox {Name="CopyVanillaSquad",ItemsSource=choices,SelectedItem=choices.First(s=>s.Id=="vanilla"),HorizontalAlignment=HorizontalAlignment.Stretch};
        var feedback=new TextBlock {TextWrapping=TextWrapping.Wrap,Foreground=Brushes.Orange};
        var copy=Button("Copy campaign",()=>
        {
            try
            {
                if (picker.SelectedItem is not SquadTemplateOption selected) throw new InvalidOperationException("Choose a squad.");
                string cache=StudioArt.Folder ?? throw new InvalidOperationException("Read game artwork before copying a campaign.");
                string draft=VanillaCampaignCopy.CreateDraft(root,cache,CampaignCore.SquadTemplatePolicy.RuntimeIdentity(selected.Id));
                dialog.Close((draft,selected.Name+" Copy"));
            }
            catch (Exception ex) {feedback.Text=ex.Message;}
        });
        copy.Name="CreateVanillaCopy";StudioTheme.Primary(copy);
        dialog.Content=new StackPanel {Margin=new Thickness(24),Spacing=StudioTheme.SpaceLg,Children={
            new TextBlock {Text="Copy vanilla campaign",FontSize=25,FontWeight=FontWeight.SemiBold},
            new TextBlock {Text="Choose a squad to copy its players, opponents, candidate pools and settings. Each act gets one editable map with the game's random node choices.",TextWrapping=TextWrapping.Wrap},
            picker,feedback,new StackPanel {Orientation=Orientation.Horizontal,Spacing=12,Children={copy,Button("Cancel",()=>dialog.Close())}}
        }};
        if (await dialog.ShowDialog<(string Draft,string Name)?>(this) is {} result) OpenCampaignDraft(result.Draft,result.Name);
    }
}
