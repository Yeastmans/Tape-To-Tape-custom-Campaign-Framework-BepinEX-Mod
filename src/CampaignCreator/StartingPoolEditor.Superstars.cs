using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CampaignCore;

namespace CampaignCreator;

public sealed partial class StartingPoolEditor
{
    private void RefreshCustomCards()
    {
        customCards.Children.Clear();
        for (int i = 0; i < customSlots.Length; i++)
        {
            int slot = i;
            string? file = customSlots[i] is string key ? Path.Combine(DirectoryPath, key) : null;
            var body = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = StudioTheme.SpaceSm };
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = StudioTheme.SpaceSm,
                VerticalAlignment = file == null ? VerticalAlignment.Center : VerticalAlignment.Bottom };
            void AddAction(Button button)
            {
                button.Padding = new Thickness(7, 4);
                actions.Children.Add(button);
            }
            if (file == null)
            {
                AddAction(Button("+ New player", async () => await AddCustomPlayer(slot, false)));
                AddAction(Button("Import player", async () => await AddCustomPlayer(slot, true)));
                Grid.SetRow(actions, 1); body.Children.Add(actions);
            }
            else
            {
                var player = new CampaignDocument(File.ReadAllText(file));
                body.Children.Add(new TextBlock { Text = player.Get("Name", "Player"), FontSize = 16, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                var details = PlayerDetails(player); Grid.SetRow(details, 1); body.Children.Add(details);
                AddAction(Button("Edit player", () => { Save(); edit(file); }));
                AddAction(Button("Import player", async () => await AddCustomPlayer(slot, true)));
                AddAction(Button("Remove", () => { customSlots[slot] = null; HasChanges = true; changed(); RefreshCustomCards(); }));
                Grid.SetRow(actions, 2); body.Children.Add(actions);
            }
            AddCard(customCards, body, i, Bench ? "CustomBenchCard" : "SuperstarCard", file != null);
        }
        UpdateCount();
    }
    private async Task AddCustomPlayer(int slot, bool import)
    {
        try
        {
            string? source = import ? await ContentImport.Pick(owner, root, "skater") : null;
            if (import && source == null) return;
            var player = source == null ? new CampaignDocument("Position = Center\n") : ContentImport.ReadDocument(source);
            if (source == null) DefaultPlayerLook.Apply(root, player);
            var dialog = new Window { Title = import ? "Import player" : "New player", Width = 430, Height = 260, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var name = new TextBox { Watermark = "Player name", Text = player.Get("Name") };
            var feedback = new TextBlock { TextWrapping = TextWrapping.Wrap };
            dialog.Content = new StackPanel { Margin = new Thickness(24), Spacing = StudioTheme.SpaceLg, Children = { name, feedback, Button(import ? "Import player" : "Create player", () => {
                try
                {
                    string label = CreatorContent.AvailablePlayerName(DirectoryPath, name.Text ?? "");
                    string key = "custom-" + Guid.NewGuid().ToString("N") + ".txt";
                    player.Set("Name", label); player.Set(BaseMarker, ""); player.Set(SourceMarker, "custom");
                    // New players start with team equipment. Imports keep their
                    // source mode so a valid own premade kit remains effective.
                    if (source == null) player.Set("Override Team Settings", "no");
                    Directory.CreateDirectory(DirectoryPath); player.Save(Path.Combine(DirectoryPath, key));
                    customSlots[slot] = key; HasChanges = true; changed(); RefreshCustomCards(); dialog.Close();
                }
                catch (Exception ex) { feedback.Text = ex.Message; }
            }), Button("Cancel", () => dialog.Close()) } };
            await dialog.ShowDialog(owner);
        }
        catch (Exception ex) { count.Text = ex.Message; }
    }
}

