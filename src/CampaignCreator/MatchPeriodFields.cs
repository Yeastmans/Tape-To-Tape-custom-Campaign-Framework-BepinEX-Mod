using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using CampaignCore;

namespace CampaignCreator;

public sealed class MatchPeriodFields : StackPanel
{
    public MatchPeriodFields(MapLayoutNode node, Action beforeChange, Action changed)
    {
        Spacing = StudioTheme.SpaceMd;
        Children.Add(new TextBlock { Text = "Timed match", FontWeight = FontWeight.SemiBold });
        Add("Periods", "NodePeriods", new float[] { 0, 1, 2, 3 }, node.PeriodCount,
            value => node.PeriodCount = (int)value, value => value.ToString("0"));
        Add("Period length", "NodePeriodMinutes", new float[] { 0, .5f, 1, 1.5f, 2, 2.5f, 3, 4, 5, 10, 20 }, node.PeriodLengthMinutes,
            value => node.PeriodLengthMinutes = value, value => value.ToString("0.##") + " min");
        Children.Add(new TextBlock { Text = "Use base-game setting keeps this match's normal value. Overrides apply to timed matches; goal-target rules stay unchanged.", TextWrapping = TextWrapping.Wrap });
        void Add(string label, string name, float[] values, float current, Action<float> set, Func<float, string> format)
        {
            var choices = values.Append(current).Distinct().Order().ToArray();
            var picker = new ComboBox { Name = name, ItemsSource = choices, SelectedItem = current,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                ItemTemplate = new FuncDataTemplate<float>((v, _) => new TextBlock { Text = v == 0 ? "Use base-game setting" : format(v) }) };
            picker.SelectionChanged += (_, _) =>
            {
                if (picker.SelectedItem is not float value || value == current) return;
                beforeChange(); current = value; set(value); changed();
            };
            Children.Add(new TextBlock { Text = label }); Children.Add(picker);
        }
    }
}
