using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CampaignCore;

namespace CampaignCreator;

public sealed partial class CampaignWorkspace
{
    private bool goalieAgentsDirty;
    private HashSet<string> selectedGoalieAgents = new(StringComparer.OrdinalIgnoreCase);

    private Control GoalieFreeAgentsPanel()
    {
        selectedGoalieAgents = GoalieFreeAgents.SelectedFiles(directory).Select(p => Path.GetFileName(p)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var list = new StackPanel { Name = "GoalieFreeAgents", Spacing = StudioTheme.SpaceSm };
        void Refresh()
        {
            list.Children.Clear();
            foreach (string file in GoalieFreeAgentContent.Files(directory))
            {
                string key = Path.GetFileName(file);
                var doc = new CampaignDocument(File.ReadAllText(file));
                var check = new CheckBox { Name = "GoalieFreeAgentSelection", Content = doc.Get("Name", doc.Get("Import Player", key)), Tag = key, IsChecked = selectedGoalieAgents.Contains(key) };
                check.IsCheckedChanged += (_, _) => {
                    if (check.IsChecked == true) selectedGoalieAgents.Add(key); else selectedGoalieAgents.Remove(key);
                    goalieAgentsDirty = true; message.Text = "Unsaved goalie free-agent selection";
                };
                var edit = ActionButton("Edit goalie", () => { if (CanLeave(true)) open(Path.Combine(GoalieFreeAgents.DirectoryPath(directory), key)); });
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
                row.Children.Add(check); Grid.SetColumn(edit, 1); row.Children.Add(edit); list.Children.Add(row);
            }
            if (list.Children.Count == 0) list.Children.Add(new TextBlock { Text = "No goalie free agents yet.", TextWrapping = TextWrapping.Wrap });
        }
        var import = ActionButton("Import goalie", async () => {
            try {
                if (!CanLeave(true)) return;
                string? source = await ContentImport.Pick(owner, root, "goalie"); if (source == null) return;
                var doc = new CampaignDocument(File.ReadAllText(source));
                string file = GoalieFreeAgentContent.Create(root, directory, doc.Get("Name", doc.Get("Import Player")), source);
                selectedGoalieAgents.Add(Path.GetFileName(file)); Refresh(); open(file);
            } catch (Exception ex) { message.Text = ex.Message; }
        });
        var create = ActionButton("Make New goalie", async () => {
            try {
                if (!CanLeave(true)) return;
                var dialog = new Window { Title = "Add goalie free agent", Width = 460, Height = 260, WindowStartupLocation = WindowStartupLocation.CenterOwner };
                var name = new TextBox { Watermark = "Goalie name" };
                var error = new TextBlock { TextWrapping = TextWrapping.Wrap }; string? created = null;
                dialog.Content = new StackPanel { Margin = new Thickness(24), Spacing = StudioTheme.SpaceMd, Children = {
                    new TextBlock { Text = "Add goalie free agent", FontSize = 24 }, name, error,
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = StudioTheme.SpaceMd, Children = {
                        ActionButton("Create goalie", () => {
                            try { created = GoalieFreeAgentContent.Create(root, directory, name.Text ?? ""); selectedGoalieAgents.Add(Path.GetFileName(created)); dialog.Close(); }
                            catch (Exception ex) { error.Text = ex.Message; }
                        }), ActionButton("Cancel", () => dialog.Close())
                    } }
                } };
                await dialog.ShowDialog(owner); Refresh(); if (created != null) open(created);
            } catch (Exception ex) { message.Text = ex.Message; }
        });
        refreshRosters.Add(Refresh); Refresh();
        return InsetPoolPanel.Frame(new StackPanel { Spacing = StudioTheme.SpaceMd, Children = {
            new TextBlock { Text = "Goalies", FontSize = 20 },
            new TextBlock { Text = "Set a General Manager node's Recruit role to Goalies to use this campaign pool. Recruited goalies replace your current goalie and stay until replaced again. Edit each goalie's stats and talents in the goalie editor. Check candidates, then Save campaign; an empty pool offers no goalies. Unchecked goalies remain editable.", TextWrapping = TextWrapping.Wrap },
            new WrapPanel { Children = { import, create } }, InsetPoolPanel.List(list, 440, "GoalieFreeAgentPoolScroll")
        } }, "GoalieFreeAgentPoolPanel");
    }
}
