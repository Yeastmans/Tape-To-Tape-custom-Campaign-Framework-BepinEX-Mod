using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace CampaignCreator;

// UI chrome only: game artwork, authored team colours and map-node colours retain their own palettes.
internal static class StudioTheme
{
    internal const double SpaceXs = 4, SpaceSm = 8, SpaceMd = 12, SpaceLg = 16, SpaceXl = 24;
    internal static readonly Thickness ButtonPadding = new(SpaceMd, SpaceSm);
    internal static readonly Thickness PagePadding = new(SpaceXl, SpaceLg);
    internal static readonly CornerRadius Radius = new(8);
    internal static readonly IBrush Background = Brush.Parse("#141B25"), Surface = Brush.Parse("#1D2836"),
        Input = Brush.Parse("#101923"), Hover = Brush.Parse("#304357"), Border = Brush.Parse("#40566D"),
        Text = Brush.Parse("#EDF3FA"), Muted = Brush.Parse("#A7B8CB"), Accent = Brush.Parse("#69D9C0"),
        AccentHover = Brush.Parse("#8DE6D2"), OnAccent = Brush.Parse("#10252C"), Selected = Brush.Parse("#243E45"),
        Danger = Brush.Parse("#FF8585");

    internal static void Primary(Button button) => button.Classes.Add("primary");
    internal static void Header(Control control) { control.ClearValue(Layoutable.MarginProperty); control.Classes.Add("page-header"); }
    internal static void Footer(Control control) { control.ClearValue(Layoutable.MarginProperty); control.Classes.Add("page-footer"); }

    internal static void Install(Application app)
    {
        app.Resources["SystemAccentColor"] = Color.Parse("#69D9C0");
        app.Resources["SystemControlHighlightAccentBrush"] = Accent;
        var styles = new Styles();
        void Add(Func<Selector?, Selector> selector, params Setter[] setters)
        {
            var style = new Style(selector);
            foreach (var setter in setters) style.Setters.Add(setter);
            styles.Add(style);
        }
        Add(s => s.OfType<Window>(), new Setter(Window.BackgroundProperty, Background),
            new Setter(TemplatedControl.ForegroundProperty, Text), new Setter(TemplatedControl.FontSizeProperty, 14d));
        Add(s => s.OfType<Button>(), new Setter(TemplatedControl.BackgroundProperty, Surface), new Setter(TemplatedControl.ForegroundProperty, Text),
            new Setter(TemplatedControl.BorderBrushProperty, Border), new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)),
            new Setter(TemplatedControl.CornerRadiusProperty, Radius), new Setter(TemplatedControl.PaddingProperty, ButtonPadding),
            new Setter(ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
        Add(s => s.OfType<Button>().Class("primary"), new Setter(TemplatedControl.BackgroundProperty, Accent),
            new Setter(TemplatedControl.ForegroundProperty, OnAccent), new Setter(TemplatedControl.FontWeightProperty, FontWeight.SemiBold));
        // Fluent paints pointer states on the presenter, so style that layer as well.
        Add(s => s.OfType<Button>().Class(":pointerover").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"),
            new Setter(ContentPresenter.BackgroundProperty, Hover), new Setter(ContentPresenter.ForegroundProperty, Text));
        Add(s => s.OfType<Button>().Class("primary").Class(":pointerover").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"),
            new Setter(ContentPresenter.BackgroundProperty, AccentHover), new Setter(ContentPresenter.ForegroundProperty, OnAccent));
        Add(s => s.OfType<Button>().Class(":pressed").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"),
            new Setter(ContentPresenter.BackgroundProperty, Selected), new Setter(ContentPresenter.ForegroundProperty, Text));
        foreach (var type in new[] { typeof(TextBox), typeof(ComboBox), typeof(NumericUpDown) })
            Add(s => s.Is(type), new Setter(TemplatedControl.BackgroundProperty, Input), new Setter(TemplatedControl.BorderBrushProperty, Border),
                new Setter(TemplatedControl.CornerRadiusProperty, Radius), new Setter(TemplatedControl.MinHeightProperty, 32d));
        Add(s => s.OfType<TabItem>(), new Setter(TemplatedControl.FontSizeProperty, 16d), new Setter(TemplatedControl.PaddingProperty, new Thickness(SpaceLg, SpaceSm)),
            new Setter(TemplatedControl.ForegroundProperty, Muted), new Setter(TemplatedControl.FontWeightProperty, FontWeight.SemiBold));
        Add(s => s.OfType<TabItem>().Class(":selected"), new Setter(TemplatedControl.ForegroundProperty, Accent));
        // TabStrip is exclusively a compact preview selector (Home/Away, Front/Back).
        Add(s => s.OfType<TabStripItem>(), new Setter(TemplatedControl.FontSizeProperty, 14d), new Setter(TemplatedControl.PaddingProperty, ButtonPadding),
            new Setter(TemplatedControl.BackgroundProperty, Input), new Setter(TemplatedControl.ForegroundProperty, Muted),
            new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(4)));
        Add(s => s.OfType<TabStripItem>().Class(":selected"), new Setter(TemplatedControl.BackgroundProperty, Selected),
            new Setter(TemplatedControl.ForegroundProperty, Accent), new Setter(TemplatedControl.FontWeightProperty, FontWeight.Bold));
        Add(s => s.OfType<ToggleButton>().Class("segment"), new Setter(TemplatedControl.BackgroundProperty, Input),
            new Setter(TemplatedControl.ForegroundProperty, Muted));
        Add(s => s.OfType<ToggleButton>().Class("segment").Class(":checked"), new Setter(TemplatedControl.BackgroundProperty, Selected),
            new Setter(TemplatedControl.ForegroundProperty, Accent), new Setter(TemplatedControl.BorderBrushProperty, Accent));
        Add(s => s.OfType<ToggleButton>().Class("segment").Class(":checked").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"),
            new Setter(ContentPresenter.BackgroundProperty, Selected), new Setter(ContentPresenter.ForegroundProperty, Accent),
            new Setter(ContentPresenter.BorderBrushProperty, Accent));
        Add(s => s.OfType<ToggleButton>().Class("segment").Class(":checked").Class(":pointerover").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"),
            new Setter(ContentPresenter.BackgroundProperty, Hover), new Setter(ContentPresenter.ForegroundProperty, Accent));
        Add(s => s.OfType<Button>().Class("map-select"), new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent),
            new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0)));
        Add(s => s.OfType<Button>().Class("map-close"), new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent),
            new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0)), new Setter(TemplatedControl.PaddingProperty, new Thickness(SpaceSm)));
        Add(s => s.OfType<Button>().Class("map-close").Class(":pointerover").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"),
            new Setter(ContentPresenter.ForegroundProperty, Danger));
        Add(s => s.OfType<Border>().Class("map-tab"), new Setter(Avalonia.Controls.Border.BackgroundProperty, Surface), new Setter(Avalonia.Controls.Border.BorderBrushProperty, Border),
            new Setter(Avalonia.Controls.Border.BorderThicknessProperty, new Thickness(1)), new Setter(Avalonia.Controls.Border.CornerRadiusProperty, Radius));
        Add(s => s.OfType<Border>().Class("map-tab").Class("active"), new Setter(Avalonia.Controls.Border.BackgroundProperty, Selected), new Setter(Avalonia.Controls.Border.BorderBrushProperty, Accent));
        Add(s => s.Is<Control>().Class("page-header"), new Setter(Layoutable.MarginProperty, new Thickness(0, 0, 0, SpaceLg)));
        Add(s => s.Is<Control>().Class("page-footer"), new Setter(Layoutable.MarginProperty, new Thickness(0, SpaceLg, 0, 0)));
        app.Styles.Add(styles);
    }
}
