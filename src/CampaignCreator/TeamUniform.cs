using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace CampaignCreator;

// Native atlas artwork flattened into the existing separated equipment layout.
internal sealed class TeamUniform : Control
{
    internal Func<string, string> ColorValue { get; set; } = _ => "#FFFFFF";
    internal Func<string, string> SkinValue { get; set; } = _ => "";
    private readonly FlatEquipmentArtwork artwork = new();
    internal bool Back { get; set; }
    internal bool Goalie { get; set; }
    internal string Abbreviation { get; set; } = "TEAM";
    internal string TeamName { get; set; } = "TEAM";
    internal Func<string> NumberText { get; set; } = () => "27";
    internal Func<Bitmap?>? Logo { get; set; }
    internal Func<Bitmap?>? HeadPreview { get; set; }
    internal Func<string>? PlayerName { get; set; }

    public TeamUniform()
    {
        Width = 460; Height = 770; IsHitTestVisible = false;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
        DetachedFromVisualTree += (_, _) => artwork.Dispose();
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        RenderFlat(context);
    }

    private void RenderFlat(DrawingContext context)
    {
        string role=Goalie ? "goalie" : "skater";
        double x=Goalie ? 0 : 100;
        var head = HeadPreview?.Invoke();
        void Draw(string part, Rect rect)
        {
            string skin=SkinValue(part);
            if (part == "helmet" && skin.Equals("none", StringComparison.OrdinalIgnoreCase)) return;
            var image=artwork.Get(role,part,skin,Back,ColorValue);
            if (image!=null)
            {
                context.DrawImage(image,rect);
                if (artwork.UsesBackArtwork(role,part,skin,Back))
                {
                    var note = new FormattedText("Back-view artwork",CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Arial"),11,Brush.Parse("#BDCADB"));
                    context.DrawText(note,new Point(rect.X+(rect.Width-note.Width)/2,rect.Y));
                }
            }
            else if(part!="sleeves")
            {
                var text=new FormattedText(part+" preview unavailable",CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Arial"),11,Brush.Parse("#8FA3B4"));
                context.DrawText(text,new Point(rect.X,rect.Y+rect.Height/2));
            }
        }
        Draw("helmet",new Rect(x+94,0,132,110));
        if (head != null)
        {
            double scale = Math.Min(132d / head.Size.Width, 105d / head.Size.Height);
            double width = head.Size.Width * scale, height = head.Size.Height * scale;
            context.DrawImage(head, new Rect(x+160-width/2,110+(105-height)/2,width,height));
        }
        // Reserve a face-sized gap below the helmet in the player diagram.
        using var bodyOffset = context.PushTransform(Matrix.CreateTranslation(0, HeadPreview != null ? 105 : 0));
        Draw("sleeves",new Rect(x,90,320,320));
        Draw("jersey",new Rect(x,90,320,320));
        if (EquipmentStylePicker.IsCustom(SkinValue("jersey")))
        {
            void Label(string value,double y,double size,string colour)
            {
                var text=new FormattedText(value,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Arial",FontStyle.Normal,FontWeight.Bold),size,UnsetColor.Brush(colour));
                if(text.Width>140)text=new FormattedText(value,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Arial"),size*140/text.Width,UnsetColor.Brush(colour));
                var point=new Point(x+160-text.Width/2,y);
                if(size>40 && text.BuildGeometry(point) is { } geometry) context.DrawGeometry(UnsetColor.Brush(colour),new Pen(UnsetColor.Brush(ColorValue("numbers1")),2),geometry);
                else context.DrawText(text,point);
            }
            if(Back) { Label((PlayerName?.Invoke() ?? TeamName).ToUpperInvariant(),170,16,ColorValue("numbers0")); Label(NumberText(),220,68,ColorValue("numbers2")); }
            else if (Logo?.Invoke() is { } crest)
            {
                double scale = Math.Min(96d / crest.PixelSize.Width, 96d / crest.PixelSize.Height);
                double w = crest.PixelSize.Width * scale, h = crest.PixelSize.Height * scale;
                context.DrawImage(crest, new Rect(x+160-w/2, 238-h/2, w, h));
            }
        }
        Draw("gloves",new Rect(x,326,320,110));
        if(Goalie) Draw("blocker",new Rect(x,326,90,115));
        Draw("pants",new Rect(x+45,432,230,130));
        Draw("socks",new Rect(x+45,574,230,Goalie?190:90));
        if(!Goalie) Draw("skates",new Rect(x+45,656,230,110));
        Draw("stick",new Rect(Goalie?345:6,90,100,630));
    }

}

