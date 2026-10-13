using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CampaignCreator;

/// <summary>Shared visual shell for roster positions and starting player choices.</summary>
internal static class RosterSelectionCard
{
    internal static Border Badge(string title) => new()
    {
        BorderBrush = StudioTheme.Accent, BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(14), Padding = new Thickness(13, 4),
        HorizontalAlignment = HorizontalAlignment.Center,
        Child = new TextBlock { Text = title.ToUpperInvariant(), FontSize = 11, Foreground = StudioTheme.Accent }
    };

    internal static Border Shell(Control content, bool empty) => new()
    {
        Height = 210, Padding = new Thickness(12),
        CornerRadius = new CornerRadius(9), BorderBrush = StudioTheme.Border,
        BorderThickness = new Thickness(1), Background = Brush.Parse(empty ? "#111E29" : "#0C1822"),
        Child = content
    };

    internal static Border Portrait(Control content) => new()
    {
        Height = 68, Width = 68, Padding = new Thickness(7),
        CornerRadius = new CornerRadius(10), Background = Brush.Parse("#182936"), Child = content
    };
}
