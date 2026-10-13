using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace CampaignCreator;

/// <summary>A contextual popup beside the real control. Advancing never edits or saves content.</summary>
internal sealed class PageHelpPopup
{
    internal sealed record Step(string Title, string Instructions, string[] Targets)
    {
        internal Control? Target { get; init; }
    }
    internal Func<bool>? ContextValid { get; init; }
    private readonly Window owner;
    private readonly IReadOnlyList<Step> steps;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly TextBlock title = new() { FontSize = 20, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock instructions = new() { TextWrapping = TextWrapping.Wrap, FontSize = 14 };
    private readonly TextBlock progress = new() { FontSize = 12, Foreground = StudioTheme.Muted };
    private readonly Button next = new() { Content = "Continue", Name = "PageHelpContinue" };
    private readonly Button locate = new() { Content = "Point again" };
    private readonly Button back = new() { Content = "Previous" };
    private readonly Canvas surface = new() { Name = "PageHelpPopupSurface" };
    private readonly TutorialArrow arrow = new() { IsHitTestVisible = false };
    private readonly Border card;
    private readonly Popup popup;
    private bool active;
    private int index, review, locatedStep = -1;
    private Control? highlighted;
    private Window? host;
    private Panel? popupParent;
    internal Rect TargetBounds { get; private set; }
    internal Rect CardBounds { get; private set; }
    internal bool Finished => index == steps.Count;
    internal int Index => index;
    internal PageHelpPopup(Window owner, IReadOnlyList<Step> steps)
    {
        this.owner = owner; this.steps = steps;
        var pause = new Button { Content = "Close", Name = "PageHelpClose" };
        pause.Click += (_, _) => Pause();
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var button in new[] { next, back, locate, pause }) { button.Margin = new Thickness(0, 0, 6, 6); actions.Children.Add(button); }
        StudioTheme.Primary(next);
        var body = new StackPanel { Spacing = StudioTheme.SpaceMd, Children = { progress, title, instructions,
            new TextBlock { Text = "Continue explains the next feature. You can use the page while Help is open.", FontSize = 11, Foreground = StudioTheme.Muted, TextWrapping = TextWrapping.Wrap } } };
        var layout = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = StudioTheme.SpaceMd };
        layout.Children.Add(new ScrollViewer { Content = body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, ClipToBounds = true }); Grid.SetRow(actions, 1); layout.Children.Add(actions);
        card = new Border { Name = "PageHelpCard", Background = StudioTheme.Surface, BorderBrush = StudioTheme.Accent, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(12), Padding = new Thickness(18), Child = layout };
        surface.Children.Add(arrow); surface.Children.Add(card);
        popup = new Popup { Child = surface, IsLightDismissEnabled = false, Placement = PlacementMode.AnchorAndGravity,
            PlacementAnchor = PopupAnchor.TopLeft, PlacementGravity = PopupGravity.BottomRight, PlacementConstraintAdjustment = PopupPositionerConstraintAdjustment.None };
        timer.Tick += (_, _) => Refresh(); next.Click += (_, _) => Advance();
        back.Click += (_, _) => { if (review > 0) { review--; Refresh(); } };
        locate.Click += (_, _) => { locatedStep = -1; Refresh(); };
        owner.Closed += (_, _) => Pause();
    }
    internal void Show() { active = true; review = index; locatedStep = -1; Refresh(); timer.Start(); }
    internal void Pause() { active = false; timer.Stop(); popup.IsOpen = false; popupParent?.Children.Remove(popup); popupParent = null; highlighted = null; host = null; }
    private IEnumerable<Control> EditorControls(Window window) => window.GetVisualDescendants().OfType<Control>()
        .Where(c => c.IsEffectivelyVisible && c != surface && !c.GetVisualAncestors().Contains(surface));
    internal static bool Matches(Control control, string target) => control.Name == target || Equals(control.Tag, target)
        || control is Button c && Equals(c.Content, target) || control is HeaderedContentControl h && Equals(h.Header, target)
        || control is TextBox t && t.Watermark == target;
    internal void Advance()
    {
        if (review < index) { review++; Refresh(); return; }
        if (Finished) return;
        if (index == steps.Count - 1) { index++; Pause(); return; }
        index++; review = index; Refresh();
    }
    private void Refresh()
    {
        if (!active || !owner.IsVisible) return;
        if (ContextValid?.Invoke() == false) { Pause(); return; }
        if (review < steps.Count && steps[review].Target is { } target && (!target.IsEffectivelyVisible || TopLevel.GetTopLevel(target) != owner)) { Pause(); return; }
        bool complete = review == steps.Count;
        progress.Text = complete ? "Help complete" : $"HELP {review + 1} / {steps.Count}";
        title.Text = complete ? "Page help complete" : steps[review].Title;
        instructions.Text = complete ? "Open Help again whenever you need it." : steps[review].Instructions;
        next.Content = review == steps.Count - 1 ? "Done" : "Continue";
        next.IsEnabled = !complete; locate.IsEnabled = !complete; back.IsEnabled = review > 0;
        Position();
    }
    private Control? FindTarget(Window window)
    {
        if (review >= steps.Count) return null;
        return steps[review].Target is { IsEffectivelyVisible: true } exact && TopLevel.GetTopLevel(exact) == window ? exact : null;
    }
    private void Position()
    {
        var currentHost = owner;
        var target = FindTarget(currentHost);
        var parent = EditorControls(currentHost).OfType<Panel>().FirstOrDefault();
        if (host != currentHost || popupParent != parent)
        {
            popup.IsOpen = false; popupParent?.Children.Remove(popup);
            host = currentHost; popupParent = parent;
            popupParent?.Children.Add(popup);
            popup.PlacementTarget = parent; popup.ShouldUseOverlayLayer = host == owner;
        }
        if (target != highlighted || locatedStep != review)
        {
            highlighted = target; locatedStep = review;
            target?.BringIntoView(); // Reveal once; never repeatedly steal focus or fight manual scrolling.
        }
        var size = host!.ClientSize;
        if (size.Width < 1 || size.Height < 1) return;
        var area = new Rect(size).Deflate(14);
        var screen = host.Screens.ScreenFromWindow(host);
        if (screen != null)
        {
            var origin = host.PointToClient(screen.WorkingArea.Position);
            area = new Rect(origin, new Size(screen.WorkingArea.Width / host.RenderScaling, screen.WorkingArea.Height / host.RenderScaling)).Deflate(14);
        }
        var targetPosition = target?.TranslatePoint(default, host);
        var targetEnd = target?.TranslatePoint(new Point(target.Bounds.Width, target.Bounds.Height), host);
        Rect rect = targetPosition.HasValue && targetEnd.HasValue ? new Rect(targetPosition.Value, targetEnd.Value).Normalize() : new Rect(size.Width / 2, size.Height / 2, 1, 1);
        if (target != null && targetPosition.HasValue)
            foreach (var ancestor in target.GetVisualAncestors().OfType<Control>().Where(c => c.ClipToBounds || c is ScrollViewer))
                if (ancestor.TranslatePoint(default, host) is { } p && ancestor.TranslatePoint(new Point(ancestor.Bounds.Width, ancestor.Bounds.Height), host) is { } end)
                    rect = rect.Intersect(new Rect(p, end).Normalize());
        bool visibleTarget = target != null && rect.Width > 1 && rect.Height > 1;
        TargetBounds = visibleTarget ? rect : default;
        if (!visibleTarget) rect = new Rect(size.Width / 2, size.Height / 2, 1, 1);
        var plan = TutorialCallout.Place(new Rect(size).Deflate(14), rect, new Size(350, 360));
        if (plan.Card.Intersects(rect) || plan.Card.Width < 220 || plan.Card.Height < 150)
            plan = TutorialCallout.Place(area, rect, new Size(350, 360));
        CardBounds = plan.Card;
        var bounds = visibleTarget ? plan.Card.Union(new Rect(plan.Start, plan.Tip).Normalize()).Inflate(8) : plan.Card.Inflate(8);
        bool overlay = host == owner && new Rect(size).Contains(bounds);
        if (popup.ShouldUseOverlayLayer != overlay) { popup.IsOpen = false; popup.ShouldUseOverlayLayer = overlay; }
        card.Width = plan.Card.Width; card.Height = plan.Card.Height;
        Canvas.SetLeft(card, plan.Card.X - bounds.X); Canvas.SetTop(card, plan.Card.Y - bounds.Y);
        surface.Width = bounds.Width; surface.Height = bounds.Height;
        arrow.Width = bounds.Width; arrow.Height = bounds.Height; arrow.IsVisible = visibleTarget;
        arrow.Start = new Point(plan.Start.X - bounds.X, plan.Start.Y - bounds.Y); arrow.Tip = new Point(plan.Tip.X - bounds.X, plan.Tip.Y - bounds.Y); arrow.InvalidateVisual();
        var parentOrigin = popupParent?.TranslatePoint(default, host) ?? default;
        popup.PlacementRect = new Rect(0, 0, 1, 1); popup.HorizontalOffset = bounds.X - parentOrigin.X; popup.VerticalOffset = bounds.Y - parentOrigin.Y;
        if (!popup.IsOpen) popup.IsOpen = true;
    }
}
