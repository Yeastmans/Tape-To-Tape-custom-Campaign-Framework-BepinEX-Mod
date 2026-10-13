using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CampaignCreator;

internal static class RewardRow
{
    internal static Border ListBox(Control content, double height = double.NaN) => new() {
        BorderBrush = Brush.Parse("#40586A"), BorderThickness = new Thickness(1),
        Background = StudioTheme.Surface, CornerRadius = new CornerRadius(8),
        Padding = new Thickness(12), HorizontalAlignment = HorizontalAlignment.Stretch,
        Child = new ScrollViewer {
            Content = content, Height = height,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        }
    };

    internal static Border ImportedTitleTile(string title) => new() {
        Tag = "ImportedRewardTitleTile",
        MinHeight = 64, Padding = new Thickness(10), CornerRadius = new CornerRadius(6),
        BorderBrush = Brush.Parse("#607F94"), BorderThickness = new Thickness(1),
        Background = Brush.Parse("#203747"), HorizontalAlignment = HorizontalAlignment.Stretch,
        Child = new TextBlock {
            Text = title, FontSize = 13, FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        }
    };

    internal static Control Create(string name, string description, bool importedTitle = false)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("2*,3*"),
            HorizontalAlignment = HorizontalAlignment.Stretch,

            Margin = new Thickness(0, 5)
        };
        var title = new TextBlock
        {
            Text = name, FontWeight = FontWeight.SemiBold, FontSize = 13,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 16, 0),
            VerticalAlignment = VerticalAlignment.Top, TextAlignment = TextAlignment.Left
        };
        if (name == "Nothing selected") title.Foreground = FieldControls.EmptySelectionBrush;
        if (importedTitle)
        {
            var tile = ImportedTitleTile(name);
            tile.Margin = new Thickness(0, 0, 16, 0);
            row.Children.Add(tile);
        }
        else row.Children.Add(title);
        var reminder = new TextBlock
        {
            Text = description, FontSize = 13, TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.Parse("#BAC7CE")),
            VerticalAlignment = VerticalAlignment.Top, TextAlignment = TextAlignment.Left
        };
        Grid.SetColumn(reminder, 1);
        row.Children.Add(reminder);
        return row;
    }
}
