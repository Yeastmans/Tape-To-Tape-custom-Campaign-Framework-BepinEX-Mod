using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using CampaignCore;

namespace CampaignCreator;

public sealed partial class MainWindow
{
    private async Task BulkImportRosterPlayers(string teamFile, string[] slots)
    {
        if (dirty) { status.Text = "Save your team before importing players."; return; }
        try
        {
            if (await ContentImport.PickPlayers(this, root, slots.Length,
                sources => PlayerPoolImport.IntoRoster(root, teamFile, slots, sources)))
            {
                OpenFile(teamFile);
                status.Text = "Imported players into the team roster. Original players are unchanged.";
            }
        }
        catch (Exception ex) { status.Text = ex.Message; }
    }

    private async Task AddRosterPlayer(string teamFile, string position, bool import)
    {
        try
        {
            string source = "";
            if (import)
            {
                source = await ContentImport.Pick(this, root, position == "Goalie" ? "goalie" : "skater") ?? "";
                if (source.Length == 0) return;
                var imported = new CampaignDocument(File.ReadAllText(source));
                bool goalie = imported.Get("Position").Equals("Goalie", StringComparison.OrdinalIgnoreCase) || imported.Fields.ContainsKey("Goalie Skill") || imported.Fields.ContainsKey("Catching") || Path.GetFileName(source).Contains("goalie", StringComparison.OrdinalIgnoreCase);
                if (goalie != (position == "Goalie")) throw new ArgumentException("Choose a " + (position == "Goalie" ? "goalie" : "skater") + " file for this slot.");
            }
            var dialog = new Window { Title = (import ? "Import · " : "New · ") + position, Width = 420, Height = 250, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var name = new TextBox { Watermark = "Player name", Text = import ? new CampaignDocument(File.ReadAllText(source)).Get("Name") : "" };
            var feedback = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            var panel = new StackPanel { Margin = new Thickness(24), Spacing = StudioTheme.SpaceLg, Children = { new TextBlock { Text = position, FontSize = 22 }, name, feedback } };
            panel.Children.Add(Button(import ? "Import player" : "Create player", () => {
                try
                {
                    string playerName = CreatorContent.ValidateName(name.Text ?? "");
                    CreatorContent.ValidatePlayerName(teamFile, playerName);
                    string target = CreatorContent.Create(root, "player", playerName, source,
                        CreatorContent.PlayerCampaign(teamFile), Path.GetDirectoryName(teamFile), initialPlayerPosition: position);
                    var player = new CampaignDocument(File.ReadAllText(target)); player.Set("Position", position);
                    if (position == "Goalie" && !player.Get("Override Team Settings").Equals("yes", StringComparison.OrdinalIgnoreCase))
                        TeamGoalieEquipment.ApplyDefaults(teamFile, player);
                    player.Save(target);
                    dialog.Close(); OpenFile(target); Content = editorShell;
                }
                catch (Exception ex) { feedback.Text = ex.Message; }
            }));
            dialog.Content = panel; await dialog.ShowDialog(this);
        }
        catch (Exception ex) { status.Text = ex.Message; }
    }
}
