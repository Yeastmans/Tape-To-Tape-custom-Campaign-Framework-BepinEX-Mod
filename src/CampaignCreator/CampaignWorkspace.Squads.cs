using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CampaignCore;

namespace CampaignCreator;

public sealed partial class CampaignWorkspace
{
    private StartingPoolEditor? benchPool, superstarPool;
    private TextBlock? gmNote;
    private bool StartingPoolsDirty => benchPool?.HasChanges == true || superstarPool?.HasChanges == true;
    private void SaveStartingPools() { benchPool?.Save(); superstarPool?.Save(); }
    // Retain a legacy map preview/new-team default without forcing it onto teams.
    private string SelectedTemplate => settings.Get("Squad Template", "Basic Squad");
    private Control CustomSquadsPage(Func<Task> newTeam)
    {
        var page = new StackPanel { Spacing = StudioTheme.SpaceLg, Children = {
            new TextBlock { Text = "Choose campaign team", FontSize = 24, FontWeight = FontWeight.SemiBold }
        } };
        var useTeams = new CheckBox { Name = "UsePlayerTeams", Content = "Use player teams", IsChecked = !settings.Get("Use Player Teams", "yes").Equals("no", StringComparison.OrdinalIgnoreCase) && !settings.Get("Use Player Teams").Equals("false", StringComparison.OrdinalIgnoreCase) };
        // A missing key reads as on here but as off in the plugin, so it is always written once;
        // an equivalent saved spelling (true/false) is kept as it is.
        fields["Use Player Teams"] = () =>
        {
            string saved = settings.Get("Use Player Teams");
            bool on = useTeams.IsChecked == true;
            bool savedOn = saved.Equals("yes", StringComparison.OrdinalIgnoreCase) || saved.Equals("true", StringComparison.OrdinalIgnoreCase);
            bool savedOff = saved.Equals("no", StringComparison.OrdinalIgnoreCase) || saved.Equals("false", StringComparison.OrdinalIgnoreCase);
            return (on && savedOn) || (!on && savedOff) ? saved : on ? "yes" : "no";
        };
        useTeams.IsCheckedChanged += (_, _) => message.Text = "Unsaved player team setting";
        var only = new CheckBox { Name = "CustomSquadsOnly", Content = "Custom teams only", IsChecked = settings.Get("Custom Squads Only").Equals("yes", StringComparison.OrdinalIgnoreCase) };
        fields["Custom Squads Only"] = () =>
        {
            string saved = settings.Get("Custom Squads Only");
            return only.IsChecked == saved.Equals("yes", StringComparison.OrdinalIgnoreCase) ? saved : only.IsChecked == true ? "yes" : "no";
        };
        only.IsCheckedChanged += (_, _) => message.Text = "Unsaved custom squad settings";
        page.Children.Add(new TextBlock { Text = "Each player team has its own template, behaviors, player pools and optional map edits. Open a team to change them.", TextWrapping = TextWrapping.Wrap });
        page.Children.Add(RosterPage(true, newTeam));
        page.Children.Add(useTeams); page.Children.Add(only);
        page.Children.Add(new TextBlock { Text = "Custom teams only blocks base-game squad selection in this campaign.", TextWrapping = TextWrapping.Wrap });
        void Changed() => message.Text = "Unsaved starting choices";
        void Edit(string file) { SaveWorkingCopy(); open(file); }
        benchPool = new StartingPoolEditor(owner, root, () => directory, "draft_pool", Edit, Changed);
        superstarPool = new StartingPoolEditor(owner, root, () => directory, "superstars", Edit, Changed);
        var bench = new StackPanel { Spacing = StudioTheme.SpaceMd, HorizontalAlignment = HorizontalAlignment.Stretch, Children = {
            new TextBlock { Text = VanillaCopyData.IsPool(Path.Combine(directory,"player_teams","draft_pool")) ? "Starting bench candidates" : "Starting bench · 9 slots", FontSize = 18, FontWeight = FontWeight.SemiBold }, benchPool } };
        var stars = new StackPanel { Spacing = StudioTheme.SpaceMd, HorizontalAlignment = HorizontalAlignment.Stretch, Children = {
            new TextBlock { Text = VanillaCopyData.IsPool(Path.Combine(directory,"player_teams","superstars")) ? "Starting superstar candidates" : "Starting superstars · 3 slots", FontSize = 18, FontWeight = FontWeight.SemiBold }, superstarPool } };
        page.Children.Add(bench); page.Children.Add(stars);
        var flow = new TextBlock { Name = "TemplateFlow", TextWrapping = TextWrapping.Wrap,
            Text = "Player teams share these candidate pools. Each team's Superstar selection and Bench draft controls decide which screens appear. A selected empty pool skips its screen." };
        page.Children.Add(flow);
        return page;
    }
    private bool AnyPlayerTeamUses(string key)
    {
        string teams = Path.Combine(directory, "player_teams");
        return Directory.Exists(teams) && Directory.GetDirectories(teams)
            .Select(team => Path.Combine(team, "team.txt")).Where(File.Exists)
            .Any(file => new CampaignDocument(File.ReadAllText(file)).Get(key).Equals("yes", StringComparison.OrdinalIgnoreCase));
    }
    private Control GeneralManagerNote()
    {
        gmNote = new TextBlock { Name = "GeneralManagerNote", Text = "Add General Manager nodes to your maps so players can recruit these free agents.",
            Foreground = StudioTheme.Accent, TextWrapping = TextWrapping.Wrap,
            IsVisible = SquadTemplatePolicy.Identity(SelectedTemplate) == "generalmanager" };
        return gmNote;
    }
}

