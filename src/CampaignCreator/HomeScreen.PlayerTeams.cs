using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CampaignCore;

namespace CampaignCreator;

public sealed partial class MainWindow
{
    private List<ContentChoice> PlayerTeamChoices() => ContentImport.Entries(root, "team", ContentImport.PlayerTeamLibrary)
        .Select(e => new ContentChoice(e.Name + " · Player team library", e.File))
        .Concat(CampaignChoices().SelectMany(c => ExistingItems(c, "squad")))
        .DistinctBy(e => Path.GetFullPath(e.Path), StringComparer.OrdinalIgnoreCase)
        .OrderBy(e => e.Label, StringComparer.OrdinalIgnoreCase).ToList();

    private async Task ChooseLibraryPlayerTeam(bool create, bool import, string? draftSource)
    {
        string? source = draftSource;
        if (import)
        {
            source = await ContentImport.Pick(this, root, "team");
            if (source == null) return;
        }
        if (create)
        {
            if (source == null) { await CreateFromHome("squad"); return; }
            try
            {
                var team = new CampaignDocument(File.ReadAllText(source));
                string file = CreatorContent.Create(root, "player-team", team.Get("Team Name"), Path.GetDirectoryName(source)!, null, null);
                homeDraft = null; campaignWorkspace = null; dirty = false;
                campaigns.IsEnabled = files.IsEnabled = true; OpenFile(file); status.Text = "Saved player team to library";
            }
            catch (Exception ex) { status.Text = ex.Message; }
            return;
        }
        var dialog = new Window { Title = "Open player team",
            Width = 540, Height = 350, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var entries = PlayerTeamChoices();
        var picker = new ComboBox { Name = "HomePlayerTeamPicker", ItemsSource = entries, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var feedback = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = StudioTheme.SpaceLg, Children = {
            new TextBlock { Text = dialog.Title, FontSize = 24 },
            new TextBlock { Text = "Choose a player team from your library or any saved campaign. Edits update the selected copy.", TextWrapping = TextWrapping.Wrap }
        } };
        panel.Children.Add(picker);
        panel.Children.Add(feedback);
        panel.Children.Add(Button("Open player team", async () => {
            try
            {
                string file = (picker.SelectedItem as ContentChoice)?.Path ?? throw new ArgumentException("Choose a player team.");
                homeDraft = null; campaignWorkspace = null; dirty = false;
                campaigns.IsEnabled = files.IsEnabled = true;
                var campaign = CampaignChoices().FirstOrDefault(c => Path.GetFullPath(file).StartsWith(
                    Path.GetFullPath(c.Path) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
                if (campaign != null)
                {
                    loading = true;
                    campaigns.ItemsSource = CampaignChoices().Select(c => c.Label).ToArray();
                    campaigns.SelectedItem = campaign.Label;
                    loading = false;
                    await LoadCampaign();
                }
                search.Text = "";
                OpenFile(file);
                status.Text = campaign == null ? "Editing library player team" : "Editing player team · " + campaign.Label;
                dialog.Close();
            }
            catch (Exception ex) { feedback.Text = ex.Message; }
        }));
        panel.Children.Add(Button("Cancel", () => dialog.Close()));
        dialog.Content = panel;
        await dialog.ShowDialog(this);
    }
}
