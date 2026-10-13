using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CampaignCreator;

internal sealed class TeamTransitionPreview : Control
{
    internal Func<string, string> Read { get; set; } = _ => "";
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        double w=Bounds.Width, h=Bounds.Height;
        IBrush BrushFor(string key) => CampaignCore.FieldSchema.TryRgb(Read(key),out byte r,out byte g,out byte b)
            ? new SolidColorBrush(Color.FromRgb(r,g,b)) : UnsetColor.Brush("");
        context.DrawRectangle(BrushFor("Transition Primary"),null,new Rect(0,0,w,h));
        void Band(string key,double start,double end)
        {
            var shape=new StreamGeometry();
            using(var path=shape.Open()) { path.BeginFigure(new Point(w*start,0),true); path.LineTo(new Point(w*end,0)); path.LineTo(new Point(w*(end-.18),h)); path.LineTo(new Point(w*(start-.18),h)); path.EndFigure(true); }
            context.DrawGeometry(BrushFor(key),null,shape);
        }
        Band("Transition Secondary",.48,.82); Band("Transition Tertiary",.82,1.18);
    }
}
