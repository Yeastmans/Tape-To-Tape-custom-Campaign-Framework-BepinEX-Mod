using Avalonia;
using Avalonia.Media;

namespace CampaignCreator;

internal static class UnsetColor
{
    internal static readonly Color Purple = Color.FromRgb(190, 0, 255);
    internal static IBrush Brush(string value)
    {
        if (Color.TryParse(value, out var color)) return new SolidColorBrush(color);
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing { Brush = Brushes.Black, Geometry = new RectangleGeometry(new Rect(0, 0, 16, 16)) });
        foreach (var rect in new[] { new Rect(0, 0, 8, 8), new Rect(8, 8, 8, 8) })
            drawing.Children.Add(new GeometryDrawing { Brush = new SolidColorBrush(Purple), Geometry = new RectangleGeometry(rect) });
        return new DrawingBrush { Drawing = drawing, TileMode = TileMode.Tile, DestinationRect = new RelativeRect(0, 0, 16, 16, RelativeUnit.Absolute), Stretch = Stretch.None };
    }
}
