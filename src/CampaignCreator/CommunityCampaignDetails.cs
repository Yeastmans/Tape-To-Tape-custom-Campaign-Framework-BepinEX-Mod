using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace CampaignCreator;

/// <summary>Details for the selected public campaign; never fetches a ZIP to show its artwork.</summary>
internal sealed class CommunityCampaignDetails : Border, IDisposable
{
    private readonly Image icon = new() { Name = "CommunityCampaignIcon", Width = 144, Height = 144, Stretch = Stretch.Uniform };
    private readonly TextBlock fallback = new() { Name = "CommunityCampaignIconFallback", Text = "C", FontSize = 64,
        FontWeight = FontWeight.Bold, Foreground = StudioTheme.Accent, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock title = new() { Name = "CommunityCampaignTitle", FontSize = 22, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock author = new() { Name = "CommunityCampaignAuthor", Foreground = StudioTheme.Muted, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock description = new() { Name = "CommunityCampaignDescription", TextWrapping = TextWrapping.Wrap };
    private Bitmap? bitmap;
    private bool disposed;

    internal CommunityCampaignDetails()
    {
        Name = "CommunityCampaignDetails";
        Background = StudioTheme.Surface;
        CornerRadius = StudioTheme.Radius;
        Padding = new Thickness(StudioTheme.SpaceLg);
        Child = new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = new StackPanel { Spacing = StudioTheme.SpaceMd, Children = {
                new Border { Height = 164, Background = StudioTheme.Input, CornerRadius = StudioTheme.Radius,
                    Child = new Grid { Children = { fallback, icon } } }, title, author, description
            } } };
        Show(null, null);
    }

    internal void Show(string? name, CommunityPresentation? details)
    {
        if (disposed) return;
        icon.Source = null;
        bitmap?.Dispose(); bitmap = null;
        title.Text = name ?? "Choose a campaign";
        author.Text = string.IsNullOrWhiteSpace(details?.Author) ? "" : "By " + details.Author;
        author.IsVisible = author.Text.Length > 0;
        description.Text = name == null ? "Select a campaign to see its icon and description."
            : string.IsNullOrWhiteSpace(details?.Description) ? "No description provided." : details.Description;
        fallback.Text = name == null || name.Trim().Length == 0 ? "C" : System.Globalization.StringInfo.GetNextTextElement(name.Trim()).ToUpperInvariant();
        fallback.IsVisible = true;
        if (string.IsNullOrEmpty(details?.Icon)) return;
        try
        {
            // Reuse upload limits for untrusted server content before allocating a decoder.
            var validated = new CommunityPresentation("Campaign", Icon: details.Icon).ValidateForUpload("Campaign");
            using var stream = new MemoryStream(Convert.FromBase64String(validated.Icon["data:image/png;base64,".Length..]));
            bitmap = new Bitmap(stream);
            icon.Source = bitmap;
            fallback.IsVisible = false;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or FormatException or NotSupportedException or InvalidOperationException)
        {
            // A broken optional thumbnail never hides campaign information or blocks installation.
            bitmap?.Dispose(); bitmap = null;
        }
    }

    public void Dispose() { disposed = true; icon.Source = null; bitmap?.Dispose(); bitmap = null; }
}
