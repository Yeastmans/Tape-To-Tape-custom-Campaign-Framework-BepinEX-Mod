using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace CampaignCreator;

/// <summary>Keeps a pool's own scrollbar and controls clear of the page scrollbar.</summary>
internal static class InsetPoolPanel
{
    internal static Border Frame(Control content, string name) => new()
    {
        Name = name,
        Margin = new Thickness(0, 0, 10, 0),
        Padding = new Thickness(10),
        BorderBrush = StudioTheme.Muted,
        BorderThickness = new Thickness(1),
        Background = StudioTheme.Surface,
        CornerRadius = StudioTheme.Radius,
        Child = content
    };

    internal static ScrollViewer List(Control content, double maxHeight, string name) => new()
    {
        Name = name,
        MaxHeight = maxHeight,
        AllowAutoHide = false,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Content = new Border { Padding = new Thickness(0, 0, 10, 0), Child = content }
    };
}
