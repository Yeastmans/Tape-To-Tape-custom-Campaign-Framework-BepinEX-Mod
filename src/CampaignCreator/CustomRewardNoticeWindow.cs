using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CampaignCreator;

internal sealed class CustomRewardNoticeWindow : Window
{
    internal CustomRewardNoticeWindow()
    {
        Name = "CustomRewardNotice";
        Title = "Talent and relic creator notice";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var acknowledge = new Button {
            Name = "CustomRewardNoticeContinue", Content = "I understand",
            IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right
        };
        StudioTheme.Primary(acknowledge);
        acknowledge.Click += (_, _) => Close();
        Content = new StackPanel {
            Margin = new Thickness(24), Spacing = StudioTheme.SpaceLg,
            Children = {
                new TextBlock {
                    Text = "Experimental talents and relics", FontSize = 24,
                    FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap
                },
                new TextBlock {
                    Name = "CustomRewardNoticeMessage",
                    Text = "Most combinations and effects are untested and may cause unpredictable results.",
                    FontSize = 17, TextWrapping = TextWrapping.Wrap
                },
                acknowledge
            }
        };
    }
}
