using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CampaignCreator;

internal sealed class BetaNoticeWindow : Window
{
    internal event Func<CancellationToken, Task>? ContinueRequested;

    internal BetaNoticeWindow()
    {
        Name = "BetaNotice";
        Title = "Campaign Studio · Beta 1.0 notice";
        Width = 600;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = new WindowIcon(typeof(BetaNoticeWindow).Assembly.GetManifestResourceStream("CampaignStudioIcon")!);
        FontFamily = new FontFamily("Segoe UI");

        var linkStatus = new TextBlock { IsVisible = false, TextWrapping = TextWrapping.Wrap, Foreground = StudioTheme.Muted };
        Button Link(string text, string name, string url)
        {
            var button = new Button { Content = text, Name = name };
            button.Click += (_, _) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    linkStatus.IsVisible = false;
                }
                catch (Exception)
                {
                    linkStatus.Text = "Could not open your browser. Open this link manually: " + url;
                    linkStatus.IsVisible = true;
                }
            };
            return button;
        }

        var continueButton = new Button { Name = "BetaNoticeContinue", Content = "I understand · Open Studio", IsDefault = true };
        StudioTheme.Primary(continueButton);
        var progressHost = new StackPanel { IsVisible = false };
        var error = new TextBlock { Name = "StudioStartupError", IsVisible = false, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Assertive);
        var lifetime = new CancellationTokenSource();
        var token = lifetime.Token;
        Closed += (_, _) => { lifetime.Cancel(); lifetime.Dispose(); };
        continueButton.Click += async (_, _) =>
        {
            if (!continueButton.IsEnabled || token.IsCancellationRequested) return;
            continueButton.IsEnabled = false;
            continueButton.Content = "Opening Studio…";
            error.IsVisible = false;
            var progress = new StudioLoadingIndicator("Opening Campaign Studio…") { Name = "StudioStartupProgress" };
            progressHost.IsVisible = true;
            progressHost.Children.Add(progress);
            try
            {
                await progress.PresentAsync(token);
                if (ContinueRequested is { } open) await open(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex)
            {
                if (token.IsCancellationRequested) return;
                progressHost.Children.Remove(progress);
                progressHost.IsVisible = false;
                error.Text = "Studio could not open. " + ex.Message;
                error.IsVisible = true;
                continueButton.Content = "Try opening Studio again";
                continueButton.IsEnabled = true;
                continueButton.Focus();
            }
        };
        var exit = new Button { Name = "BetaNoticeExit", Content = "Exit", IsCancel = true };
        exit.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Thickness(24), Spacing = StudioTheme.SpaceLg,
            Children =
            {
                new TextBlock { Text = "BETA 1.0", Foreground = StudioTheme.Accent, FontWeight = FontWeight.Bold },
                new TextBlock { Text = "You're joining a beta test", FontSize = 26, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                new TextBlock
                {
                    Text = "Campaign Studio and the bundled mod are in beta. Some features may not work as described, behave unexpectedly, or be broken.",
                    FontSize = 17, TextWrapping = TextWrapping.Wrap
                },
                new TextBlock
                {
                    Text = "Reporting bugs on Discord or GitHub helps immensely. Tell us what happened, what you expected, and how to reproduce it. Screenshots or logs can help too. Thank you for helping improve the beta!",
                    FontSize = 17, TextWrapping = TextWrapping.Wrap
                },
                new WrapPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        Link("Report on Discord", "BetaNoticeDiscord", BugReport.Discord),
                        new Border { Width = StudioTheme.SpaceSm },
                        Link("Report on GitHub", "BetaNoticeGitHub", BugReport.Repository + "/issues")
                    }
                },
                linkStatus,
                new Separator(),
                progressHost,
                error,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = StudioTheme.SpaceSm, Children = { exit, continueButton }
                }
            }
        };
    }
}
