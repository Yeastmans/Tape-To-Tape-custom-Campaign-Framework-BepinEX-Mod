using Avalonia.Controls;
using Avalonia.Media;
using CampaignCore;
using System.Globalization;

namespace CampaignCreator;

internal sealed class PlayerContractEditor : StackPanel
{
    internal PlayerContractEditor(CampaignDocument document, Action<string, Func<string>> register, Action changed, bool goalie = false)
    {
        Spacing = 6;
        var enabled = new CheckBox { Name = "PlayerCustomContract", Content = "Custom contract duration",
            IsChecked = PlayerContractOptions.Enabled(document) };
        var duration = new NumericUpDown { Name = "PlayerContractMatches", Minimum = 1, Maximum = int.MaxValue,
            Increment = 1, FormatString = "0", ParsingNumberStyle = NumberStyles.Integer, AllowSpin = true, ClipValueToMinMax = true,
            Value = PlayerContractOptions.TryMatches(document.Get(PlayerContractOptions.MatchesKey).Trim(), out int saved)
                ? saved : PlayerContractOptions.DefaultMatches, IsEnabled = enabled.IsChecked == true };
        register(PlayerContractOptions.EnabledKey, () => enabled.IsChecked == true ? "yes" : "no");
        decimal WholeMatches() => Math.Clamp(decimal.Truncate(duration.Value ?? PlayerContractOptions.DefaultMatches), 1, int.MaxValue);
        register(PlayerContractOptions.MatchesKey, () => WholeMatches().ToString(CultureInfo.InvariantCulture));
        enabled.IsCheckedChanged += (_, _) => { duration.IsEnabled = enabled.IsChecked == true; changed(); };
        duration.ValueChanged += (_, _) =>
        {
            decimal whole = WholeMatches();
            if (duration.Value != whole) { duration.Value = whole; return; }
            changed();
        };
        Children.Add(enabled);
        Children.Add(new TextBlock { Text = "Matches played before contract ends" });
        Children.Add(duration);
        Children.Add(new TextBlock { FontSize = 12, Foreground = StudioTheme.Muted, TextWrapping = TextWrapping.Wrap,
            Text = goalie
                ? "Off keeps this goalie permanent until replaced. On starts at four played matches. Applies to new runs and future recruitment. Reserve-only matches do not count. Saved contracts keep their signed duration. Departure-preventing effects still apply; an expired active goalie stays until a replacement is available."
                : "Off uses normal game rules. Applies to this skater in new runs and future recruitment, including non-GM campaigns. Bench matches do not count. Saved contracts keep their current duration; game effects that prevent departures still apply." });
    }
}
