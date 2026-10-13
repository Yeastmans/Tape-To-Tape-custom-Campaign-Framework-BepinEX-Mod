using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using System.Runtime.CompilerServices;

namespace CampaignCreator;

public static class UnsavedChangesGuard
{
    private sealed class State { public bool AllowClose, PromptOpen; }
    private static readonly ConditionalWeakTable<Window, State> states = new();
    public static void Attach(Window owner, Func<bool> hasChanges)
    {
        var state = states.GetOrCreateValue(owner);
        owner.Closing += async (_, e) =>
        {
            if (state.AllowClose || !hasChanges()) return;
            e.Cancel = true;
            if (state.PromptOpen) return;
            state.PromptOpen = true;
            try
            {
                var dialog = new Window { Title = "Unsaved changes", Width = 440, Height = 220, CanResize = false,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = StudioTheme.Background };
                var keep = new Button { Content = "Keep editing", IsDefault = true, Padding = new Thickness(14, 9) };
                var discard = new Button { Content = "Close anyway", Padding = new Thickness(14, 9) };
                keep.Click += (_, _) => dialog.Close(false); discard.Click += (_, _) => dialog.Close(true);
                dialog.KeyDown += (_, key) => { if (key.Key == Key.Escape) { dialog.Close(false); key.Handled = true; } };
                dialog.Content = new StackPanel { Margin = new Thickness(24), Spacing = 18, Children = {
                    new TextBlock { Text = "You have unsaved changes", FontSize = 22, FontWeight = FontWeight.Bold },
                    new TextBlock { Text = "Closing now will discard your unsaved edits.", TextWrapping = TextWrapping.Wrap },
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10, Children = { keep, discard } }
                }};
                if (await dialog.ShowDialog<bool>(owner)) { AllowTree(owner); owner.Close(); }
            }
            finally { state.PromptOpen = false; }
        };
    }
    private static void AllowTree(Window window)
    {
        if (states.TryGetValue(window, out var state)) state.AllowClose = true;
        foreach (var child in window.OwnedWindows) AllowTree(child);
    }
}
