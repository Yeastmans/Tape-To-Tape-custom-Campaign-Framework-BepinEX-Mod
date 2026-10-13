using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CampaignCore;

namespace CampaignCreator;

public sealed record StadiumChoice(string Id, string Name, string Image);
public sealed record NodeSettingsResult(string Team, string Stadium);

public sealed class NodeSettingsWindow : Window
{
    // IDs are the animation names from the game's extracted map skeleton.
    public static readonly StadiumChoice[] Stadiums =
    [
        new("Stadium_Calaveras", "Calaveras", "Stadium_Calaveras"),
        new("Stadium_Cheese", "Cheese", "Stadium_Cheese"),
        new("Stadium_Cultists", "Cultists", "Stadium_Cultists"),
        new("Stadium_Shooting", "Shooting Stars", "ShootingStars"),
        new("Stadium_Golfers", "Golfers", "Stadium_Golfers"),
        new("Stadium_HockeyFC", "Hockey FC", "Stadium_HockeyFC"),
        new("Stadium_Knights", "Knights", "Stadium_Knights"),
        new("Stadium_Lettuce", "Lettuce", "Stadium_Lettuce"),
        new("Stadium_Meatballs", "Meatballs", "Stadium_Meatball"),
        new("Stadium_Meatballs_Alt", "Meatballs (alternate)", "Stadium_Meatball"),
        new("Stadium_Mountaineers", "Mountaineers", "Stadium_Mountaineers"),
        new("Stadium_Princess", "Princess", "Stadium_Princess"),
        new("Stadium_Prisoners", "Prisoners", "Stadium_Prisonners"),
        new("Stadium_Ref", "Referees", "Stadium_Ref"),
        new("Stadium_Tycoons", "Tycoons", "Tycoons_Arena"),
        new("Stadium_Canada", "Canada", "Stadium_Canada")
    ];

    public NodeSettingsWindow(MapLayoutNode node, IReadOnlyDictionary<string, string> teamNames, MapTeamPreview? teamPreview = null)
    {
        Title = "Node settings"; Width = 460; Height = 660; CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = StudioTheme.Background;
        var teams = teamNames.ToList();
        if (node.Team.Length > 0 && !teams.Any(t => t.Key.Equals(node.Team, StringComparison.OrdinalIgnoreCase)))
            teams.Add(new(node.Team, "Missing team: " + node.Team));
        var teamPicker = new ComboBox
        {
            Name = "NodeTeam", ItemsSource = teams,
            SelectedIndex = teams.FindIndex(t => t.Key.Equals(node.Team, StringComparison.OrdinalIgnoreCase)),
            PlaceholderText = teams.Count == 0 ? "Add an opponent team first" : "Choose an opponent team",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemTemplate = new FuncDataTemplate<KeyValuePair<string, string>>((t, _) => new TextBlock { Text = t.Value })
        };
        var stadiums = Stadiums.ToList();
        if (node.Stadium.Length > 0 && !stadiums.Any(s => s.Id == node.Stadium)) stadiums.Add(new(node.Stadium, "Unavailable stadium: " + node.Stadium, ""));
        var stadiumPicker = new ComboBox
        {
            Name = "NodeStadium", ItemsSource = stadiums, SelectedItem = stadiums.FirstOrDefault(s => s.Id == node.Stadium),
            PlaceholderText = "Choose a stadium",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemTemplate = new FuncDataTemplate<StadiumChoice>((s, _) => new TextBlock { Text = s?.Name })
        };
        var image = new Image { Height = 200, Stretch = Stretch.Uniform };
        var logo = new Image { Name = "TeamLogoPreview", Width = 64, Height = 64, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        var arena = new Grid { Children = { image, logo } };
        var description = new TextBlock { Name = "TeamDescriptionPreview", TextWrapping = TextWrapping.Wrap };
        void PreviewTeam()
        {
            string key = teamPicker.SelectedItem is KeyValuePair<string, string> choice ? choice.Key : "";
            logo.Source = teamPreview?.Logo(key);
            description.Text = teamPreview?.Description(key) ?? "";
        }
        teamPicker.SelectionChanged += (_, _) => PreviewTeam();
        var previewLabel = new TextBlock { TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center };
        Bitmap? bitmap = null;
        void Preview()
        {
            image.Source = null; bitmap?.Dispose(); bitmap = null;
            if (stadiumPicker.SelectedItem is not StadiumChoice choice) return;
            previewLabel.Text = choice.Id.Length == 0 ? "Keep this node's original stadium" : choice.Image.Length == 0 ? "Preview unavailable" : choice.Name;
            if (choice.Image.Length == 0 && choice.Id.Length > 0) return;
            bitmap = StudioArt.Bitmap("node-images", "regions", (choice.Image.Length == 0 ? "Outdoor_Rink" : choice.Image) + ".png"); image.Source = bitmap;
        }
        stadiumPicker.SelectionChanged += (_, _) => Preview();
        Closed += (_, _) => { image.Source = null; bitmap?.Dispose(); };
        var apply = new Button { Content = "Apply", IsDefault = true, Padding = new Thickness(20, 9) }; StudioTheme.Primary(apply);
        void UpdateApply() => apply.IsEnabled = teamPicker.SelectedItem is KeyValuePair<string, string> choice && teamNames.ContainsKey(choice.Key);
        teamPicker.SelectionChanged += (_, _) => UpdateApply();
        UpdateApply();
        apply.Click += (_, _) =>
        {
            if (teamPicker.SelectedItem is KeyValuePair<string, string> team && teamNames.ContainsKey(team.Key))
                Close(new NodeSettingsResult(team.Key, (stadiumPicker.SelectedItem as StadiumChoice)?.Id ?? node.Stadium));
        };
        var cancel = new Button { Content = "Cancel", Padding = new Thickness(20, 9) };
        cancel.Click += (_, _) => Close();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
        Content = new ScrollViewer { Content = new StackPanel
        {
            Margin = new Thickness(24), Spacing = StudioTheme.SpaceMd,
            Children =
            {
                new TextBlock { Text = $"{node.Type} node settings", FontSize = 24, FontWeight = FontWeight.Bold },
                new TextBlock { Text = "Team" }, teamPicker,
                new TextBlock { Text = "Stadium" }, stadiumPicker,
                arena, previewLabel, description,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = StudioTheme.SpaceMd, Children = { cancel, apply } }
            }
        } };
        Preview();
        PreviewTeam();
    }
}
