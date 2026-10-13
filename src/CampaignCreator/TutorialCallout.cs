using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CampaignCreator;

internal readonly record struct TutorialCallout(Rect Card, Point Start, Point Tip)
{
    // Coordinates are DIPs in the owning window, including space outside small dialogs.
    internal static TutorialCallout Place(Rect area, Rect target, Size preferred)
    {
        const double gap = 36;
        double width = Math.Min(preferred.Width, area.Width), height = Math.Min(preferred.Height, area.Height);
        double X(double x, double w) => Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - w));
        double Y(double y, double h) => Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - h));
        var options = new[] {
            new Rect(target.Right + gap, Y(target.Center.Y - height / 2, height), width, height),
            new Rect(target.Left - gap - width, Y(target.Center.Y - height / 2, height), width, height),
            new Rect(X(target.Center.X - width / 2, width), target.Bottom + gap, width, height),
            new Rect(X(target.Center.X - width / 2, width), target.Top - gap - height, width, height)
        };
        Rect card = options.FirstOrDefault(r => area.Contains(r));
        if (card.Width == 0)
        {
            var spaces = new[] {
                new Rect(Math.Min(area.Right, target.Right + gap), area.Top, Math.Max(0, area.Right - target.Right - gap), area.Height),
                new Rect(area.Left, area.Top, Math.Max(0, target.Left - gap - area.Left), area.Height),
                new Rect(area.Left, Math.Min(area.Bottom, target.Bottom + gap), area.Width, Math.Max(0, area.Bottom - target.Bottom - gap)),
                new Rect(area.Left, area.Top, area.Width, Math.Max(0, target.Top - gap - area.Top))
            };
            var space = spaces.OrderByDescending(r => Math.Min(width, r.Width) * Math.Min(height, r.Height)).First();
            double w = Math.Min(width, space.Width), h = Math.Min(height, space.Height);
            card = w >= 220 && h >= 130
                ? new Rect(Math.Clamp(target.Center.X - w / 2, space.Left, space.Right - w), Math.Clamp(target.Center.Y - h / 2, space.Top, space.Bottom - h), w, h)
                : new Rect(X(area.Right - width, width), Y(area.Bottom - height, height), width, height);
        }
        Point start, tip;
        if (card.Left >= target.Right)
        {
            double y = Math.Clamp(target.Center.Y, card.Top + 18, card.Bottom - 18);
            start = new Point(card.Left, y); tip = new Point(target.Right + 10, Math.Clamp(y, target.Top, target.Bottom));
        }
        else if (card.Right <= target.Left)
        {
            double y = Math.Clamp(target.Center.Y, card.Top + 18, card.Bottom - 18);
            start = new Point(card.Right, y); tip = new Point(target.Left - 10, Math.Clamp(y, target.Top, target.Bottom));
        }
        else if (card.Top >= target.Bottom)
        {
            double x = Math.Clamp(target.Center.X, card.Left + 18, card.Right - 18);
            start = new Point(x, card.Top); tip = new Point(Math.Clamp(x, target.Left, target.Right), target.Bottom + 10);
        }
        else
        {
            double x = Math.Clamp(target.Center.X, card.Left + 18, card.Right - 18);
            start = new Point(x, card.Bottom); tip = new Point(Math.Clamp(x, target.Left, target.Right), target.Top - 10);
        }
        return new(card, start, tip);
    }
}

internal sealed class TutorialArrow : Control
{
    internal Point Start, Tip;
    public override void Render(DrawingContext context)
    {
        var direction = new Vector(Tip.X - Start.X, Tip.Y - Start.Y);
        if (direction.Length < 1) return;
        var unit = direction / direction.Length;
        var perpendicular = new Vector(-unit.Y, unit.X);
        var accent = StudioTheme.Accent;
        context.DrawLine(new Pen(Brush.Parse("#0B151E"), 7), Start, Tip);
        context.DrawLine(new Pen(accent, 3), Start, Tip);
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(Tip, true);
            path.LineTo(Tip - unit * 12 + perpendicular * 6);
            path.LineTo(Tip - unit * 12 - perpendicular * 6);
            path.EndFigure(true);
        }
        context.DrawGeometry(accent, new Pen(Brush.Parse("#0B151E"), 1), geometry);
    }
}
