using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
namespace CampaignCreator;
internal sealed class EditorAttention : Border
{
    private readonly TextBlock text = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brush.Parse("#FFD18C") };
    private Button? save;
    internal EditorAttention() { Child = new ScrollViewer { Content = text, MaxHeight = 115 }; Background = Brush.Parse("#382B19"); Padding = new Thickness(12); Margin = new Thickness(20, 0, 20, 8); IsVisible = false; }
    internal Control Wrap(Control content, Button saveButton) { save = saveButton; var dock = new DockPanel(); DockPanel.SetDock(this, Dock.Top); dock.Children.Add(this); dock.Children.Add(content); return dock; }
    internal void Update(string[] missing)
    {
        IsVisible = missing.Length > 0;
        if (save != null) { save.IsEnabled = missing.Length == 0; ToolTip.SetTip(save, missing.Length == 0 ? "Save changes" : "Complete the fields listed under Needs attention."); }
        text.Text = "Needs attention · " + missing.Length + " required field(s). Save and export are blocked.\n"
            + string.Join("\n", missing.GroupBy(EditorRequirements.Zone).Select(g => g.Key + ": " + string.Join(", ", g)))
            + "\nNothing selected means unfinished. Choose None only where it is an available, intentional option.";
    }
}
