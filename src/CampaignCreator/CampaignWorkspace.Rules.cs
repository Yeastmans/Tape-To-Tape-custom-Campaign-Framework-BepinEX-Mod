using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CampaignCreator;

/// <summary>Campaign-wide match rules shown on the overview.</summary>
public sealed partial class CampaignWorkspace
{
    // Offside uses temporary native settings reads; blank penalty keeps the player's choice.
    private static readonly string[] OffsidePenalties = ["Player's setting", "Whistle", "Lose Puck", "Knockout"];
    private Border? matchRulesCard;

    private static bool IsYes(string value) => value.Equals("yes", StringComparison.OrdinalIgnoreCase) || value.Equals("true", StringComparison.OrdinalIgnoreCase);

    // Same spellings the plugin accepts (Plugin.ParsePenalty); anything else reads as the player's setting.
    private static int PenaltyIndex(string value) => value.Trim().ToLowerInvariant().Replace("-", " ").Replace("_", " ") switch
    {
        "whistle" or "0" => 1,
        "lose puck" or "losepuck" or "loose puck" or "1" => 2,
        "knockout" or "knock out" or "ko" or "2" => 3,
        _ => 0
    };

    private Border MatchRulesCard()
    {
        // Built once: the overview is rebuilt on every visit and must not lose unsaved choices.
        if (matchRulesCard != null) { (matchRulesCard.Parent as Panel)?.Children.Remove(matchRulesCard); return matchRulesCard; }
        var offside = new CheckBox { Name = "CampaignOffside", Content = "Offside in every match", IsChecked = IsYes(settings.Get("Offside")) };
        var penalty = new ComboBox { Name = "CampaignOffsidePenalty", ItemsSource = OffsidePenalties, SelectedIndex = PenaltyIndex(settings.Get("Offside Penalty")),
            IsEnabled = offside.IsChecked == true, HorizontalAlignment = HorizontalAlignment.Stretch };
        // Readers return the saved text while a value is unchanged, so an untouched campaign stays clean.
        fields["Offside"] = () =>
        {
            string saved = settings.Get("Offside");
            bool on = offside.IsChecked == true;
            return on == IsYes(saved) ? saved : on ? "yes" : "no";
        };
        fields["Offside Penalty"] = () =>
        {
            string saved = settings.Get("Offside Penalty");
            int index = Math.Max(0, penalty.SelectedIndex);
            return index == PenaltyIndex(saved) ? saved : index == 0 ? "" : OffsidePenalties[index];
        };
        void Changed() { penalty.IsEnabled = offside.IsChecked == true; if (HasUnsavedChanges) message.Text = "Unsaved campaign changes"; }
        offside.IsCheckedChanged += (_, _) => Changed();
        penalty.SelectionChanged += (_, _) => Changed();
        var body = new StackPanel { Spacing = StudioTheme.SpaceSm, Children = {
            new TextBlock { Text = "Game options", FontSize = 20, FontWeight = FontWeight.SemiBold, Foreground = StudioTheme.Accent },
            RefereeHeadPicker(),
            offside,
            new TextBlock { Text = "Uses the game's own offside rule, the one Play Now has in Advanced Settings, for every match of a run. Your saved settings are not changed.", Foreground = StudioTheme.Muted, TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = "Offside penalty" }, penalty,
            new TextBlock { Text = "Player's setting keeps the penalty chosen in Advanced Settings. Other choices apply only during this campaign's matches. Native Tchel squad rules and the Linesman talent still force Knockout.", Foreground = StudioTheme.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = "Player-team injuries are in Custom teams → Team settings (On by default except Oldschool). Tchel offside, Eliminator and Sin Bin are in Squad behaviors. Team relics and player abilities keep their own native effects. Rinks, balls and opponent triggers are in Opponent settings.", Foreground = StudioTheme.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap }
        } };
        return matchRulesCard = new Border {
            Name = "CampaignMatchRules", Background = StudioTheme.Surface, BorderBrush = StudioTheme.Border, BorderThickness = new Thickness(1),
            CornerRadius = StudioTheme.Radius, Padding = new Thickness(StudioTheme.SpaceLg), Child = body
        };
    }
}
