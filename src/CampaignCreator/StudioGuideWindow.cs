using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CampaignCreator;

public sealed class StudioGuideWindow : Window
{
    private readonly TextBox search = new() { Name = "GuideSearch", Watermark = "Search features, saving, game behavior…", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ListBox results = new() { Name = "GuideResults", Background = StudioTheme.Surface };
    private readonly StackPanel article = new() { Name = "GuideArticle", Spacing = StudioTheme.SpaceLg, Margin = new Thickness(0, 0, 12, 0) };
    private readonly ScrollViewer articleScroll;
    private readonly TextBlock count = new() { Foreground = StudioTheme.Muted };
    private readonly Stack<string> history = new();
    private readonly Button previous = new() { Content = "Back to previous topic", IsEnabled = false };
    private string? current;
    private bool selecting;
    public StudioGuideWindow()
    {
        Title = "Studio guide"; Width = 1120; Height = 800; MinWidth = 760; MinHeight = 560; Background = StudioTheme.Background;
        var shell = new Grid { Margin = new Thickness(24), RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), RowSpacing = StudioTheme.SpaceMd };
        var heading = new DockPanel(); var help = PageHelpButton.Create(this); DockPanel.SetDock(help, Dock.Right); heading.Children.Add(help);
        heading.Children.Add(Text("Studio guide", 28)); shell.Children.Add(heading);
        var searchRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = StudioTheme.SpaceMd };
        searchRow.Children.Add(search); var clear = Action("Clear search", () => search.Text = ""); Grid.SetColumn(clear, 1); searchRow.Children.Add(clear);
        Grid.SetRow(searchRow, 1); shell.Children.Add(searchRow);
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("260,*"), ColumnSpacing = StudioTheme.SpaceLg };
        var navigation = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = StudioTheme.SpaceSm }; navigation.Children.Add(count);
        Grid.SetRow(results, 1); navigation.Children.Add(results); columns.Children.Add(navigation);
        results.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<GuideTopic>((topic, _) => Text(topic?.Title ?? "", 15));
        articleScroll = new ScrollViewer { Content = article, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetColumn(articleScroll, 1); columns.Children.Add(articleScroll); Grid.SetRow(columns, 2); shell.Children.Add(columns);
        var footer = new WrapPanel { Orientation = Orientation.Horizontal };
        previous.Click += (_, _) => { if (history.TryPop(out var id)) Navigate(id, false); };
        footer.Children.Add(previous);
        footer.Children.Add(Action("Close guide", Close)); foreach (Control c in footer.Children) c.Margin = new Thickness(0, 0, 10, 0);
        Grid.SetRow(footer, 3); shell.Children.Add(footer); Content = shell;
        ToolTip.SetTip(search, "Search every topic's feature description, save timing, game behavior and limitations. All entered words must match; search ignores case.");
        ToolTip.SetTip(results, "Select a result to read it. Related topics and Back to previous topic keep navigation inside the guide.");
        search.TextChanged += (_, _) => Filter();
        results.SelectionChanged += (_, _) => { if (!selecting && results.SelectedItem is GuideTopic topic) ShowTopic(topic, true); };
        Filter();
    }
    private void Filter()
    {
        var words = (search.Text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var matches = StudioGuideContent.Topics.Where(t => words.All(w => t.SearchText.Contains(w, StringComparison.OrdinalIgnoreCase))).ToArray();
        selecting = true; results.ItemsSource = matches;
        var topic = matches.FirstOrDefault(t => t.Id == current) ?? matches.FirstOrDefault(); results.SelectedItem = topic; selecting = false;
        count.Text = $"{matches.Length} topic{(matches.Length == 1 ? "" : "s")}";
        if (topic != null) ShowTopic(topic, false);
        else { article.Children.Clear(); article.Children.Add(Text("No matching topics", 24)); article.Children.Add(Text("Try a shorter phrase such as colors, save, template or Play Now. Clear search to show every topic.")); }
    }
    private void Navigate(string id, bool remember = true)
    {
        string? old = current;
        selecting = true; search.Text = ""; selecting = false;
        if (remember && old != null && old != id) history.Push(old);
        var topic = StudioGuideContent.Topics.Single(t => t.Id == id);
        selecting = true; results.SelectedItem = topic; selecting = false;
        ShowTopic(topic, false);
    }
    private void ShowTopic(GuideTopic topic, bool remember)
    {
        if (remember && current != null && current != topic.Id) history.Push(current);
        current = topic.Id; previous.IsEnabled = history.Count > 0;
        article.Children.Clear(); article.Children.Add(Text(topic.Title, 25));
        Add("What changes", topic.Changes);
        article.Children.Add(Text("Controls and options", 19));
        foreach (var entry in StudioGuideReference.For(topic.Id))
            article.Children.Add(new StackPanel { Spacing = 4, Children = {
                new TextBlock { Text = entry.Label, FontSize = 15, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                Text(entry.Description)
            } });
        foreach (var screen in StudioGuideScreens.For(topic.Id)) article.Children.Add(StudioGuideScreens.Card(screen));
        Add("When it saves", topic.Saves); Add("How it reaches the game", topic.Game); Add("Known limitations", topic.Limits);
        article.Children.Add(Text("Related topics", 19));
        foreach (string id in topic.Related) article.Children.Add(Action(StudioGuideContent.Topics.Single(t => t.Id == id).Title, () => Navigate(id)));
        articleScroll.Offset = default;
        results.ScrollIntoView(topic);
        void Add(string heading, string body) { article.Children.Add(new StackPanel { Spacing = 6, Children = { Text(heading, 19), Text(body) } }); }
    }
    internal static TextBlock Text(string text, double size = 15) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Foreground = size >= 19 ? StudioTheme.Accent : Brushes.White };
    internal static Button Action(string label, Action action) { var b = new Button { Content = label, Padding = StudioTheme.ButtonPadding }; b.Click += (_, _) => action(); return b; }
}

