using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.Composition;

namespace CampaignCreator;

internal sealed class StudioLoadingIndicator : StackPanel
{
    internal StudioLoadingIndicator(string message)
    {
        Spacing = StudioTheme.SpaceSm;
        var text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetLiveSetting(text, AutomationLiveSetting.Polite);
        var progress = new ProgressBar { Height = 6, IsIndeterminate = true };
        AutomationProperties.SetName(progress, message);
        Children.Add(text);
        Children.Add(progress);
    }

    internal Task PresentAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var window = TopLevel.GetTopLevel(this) ?? throw new InvalidOperationException("The loading window was closed.");
        window.UpdateLayout();
        var visual = ElementComposition.GetElementVisual(window)!;
        // A dispatcher yield alone can hand off the window before a single pixel of the indicator is drawn.
        return visual.Compositor.RequestCompositionBatchCommitAsync().Rendered.WaitAsync(token);
    }
}
