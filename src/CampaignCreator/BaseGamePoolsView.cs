using Avalonia.Controls;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Media;
using CampaignCore;

namespace CampaignCreator;

public sealed class BaseGamePoolsView : UserControl
{
    public bool HasUnsavedChanges { get; private set; }
    private readonly BaseGamePoolsContent profile;
    private readonly Dictionary<string, StackPanel> lists = [];
    private readonly Dictionary<string, TextBlock> counts = [];
    private readonly Dictionary<string, List<(string File, Button Up, Button Down)>> moveActions = [];
    private readonly Func<Task<string?>> pick;
    private readonly Action<string> edit;
    private readonly TextBlock feedback = new() { Name = "BaseGamePoolsFeedback", TextWrapping = TextWrapping.Wrap, Foreground = StudioTheme.Muted };
    public BaseGamePoolsView(BaseGamePoolsContent profile, Func<string?> cache, Func<Task<string?>> pick, Action<string> edit, Action back)
    {
        this.profile = profile; this.pick = pick; this.edit = edit;
        HasUnsavedChanges = profile.IsRecoveryDraft;
        Name = "BaseGamePoolsEditor"; Background = StudioTheme.Background;
        var shell = new Grid { Margin = StudioTheme.PagePadding, RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = StudioTheme.SpaceLg };
        var enabled = new ToggleSwitch { Name = "BaseGamePoolsEnabled", Content = "Use these pools with None (base game)", IsChecked = profile.Selection.Enabled, OnContent = "Enabled", OffContent = "Disabled" };
        enabled.IsCheckedChanged += (_, _) => { profile.Selection.Enabled = enabled.IsChecked == true; Changed(); };
        shell.Children.Add(new StackPanel { Spacing = 8, Children = {
            new TextBlock { Text = "TAPE TO TAPE · Campaign Studio", Foreground = StudioTheme.Accent, FontWeight = FontWeight.Bold },
            new TextBlock { Text = "Base Game Pools", FontSize = 28, FontWeight = FontWeight.Bold },
            new TextBlock { Text = "Share bench candidates across base-game teams and customize free agents and superstars. Team capacities, eligibility and teams without a bench keep their normal rules.", Foreground = StudioTheme.Muted, TextWrapping = TextWrapping.Wrap }, enabled
        }});
        var sections = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = StudioTheme.SpaceMd };
        int column = 0;
        foreach (var (kind, title) in new[] { ("draft_pool", "Bench / Draft candidates"), ("free_agents", "Free agents"), ("superstars", "Superstars") })
        {
            var card = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 12 };
            counts[kind] = new TextBlock { Foreground = StudioTheme.Muted };
            card.Children.Add(new StackPanel { Spacing = 6, Children = { new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap }, counts[kind] } });
            lists[kind] = new StackPanel { Spacing = 8 };
            var scroll = new ScrollViewer { Name = "BaseGamePoolList_" + kind, Content = lists[kind], HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, AllowAutoHide = false };
            Grid.SetRow(scroll, 1); card.Children.Add(scroll);
            var add = Button("Add / import candidate", "BaseGamePoolAdd_" + kind, () => { });
            add.Click += async (_, _) => await Import(kind, null, add);
            Grid.SetRow(add, 2); card.Children.Add(add);
            var frame = new Border { Background = StudioTheme.Surface, BorderBrush = StudioTheme.Border, BorderThickness = new Thickness(1), CornerRadius = StudioTheme.Radius, Padding = new Thickness(14), Child = card };
            Grid.SetColumn(frame, column++); sections.Children.Add(frame);
        }
        Grid.SetRow(sections, 1); shell.Children.Add(sections);
        feedback.Text = profile.IsRecoveryDraft
            ? "Recovered defaults are an unsaved draft. Save pools to apply them. A backup of your previous settings was kept; existing player files are unchanged."
            : "Choose at least one candidate in every pool. Save applies all selections together; player edits save separately in the player editor.";
        var save = Button("Save pools", "BaseGamePoolsSave", () => { profile.Save(); HasUnsavedChanges = false; feedback.Text = profile.Selection.Enabled ? "Saved · Base Game pools enabled." : "Saved · Base Game pools disabled."; }); StudioTheme.Primary(save);
        var restore = Button("Restore base-game defaults", "BaseGamePoolsRestore", () => { profile.RestoreDefaults(cache()); Changed(); RefreshCandidates(); feedback.Text = "Base-game defaults restored in this draft. Save pools to apply them."; });
        var footer = new StackPanel { Spacing = 10, Children = { feedback, new WrapPanel { Orientation = Orientation.Horizontal, Children = { Button("Back / Home", "BaseGamePoolsBack", back), restore, save } } } };
        foreach (var button in ((WrapPanel)footer.Children[1]).Children.OfType<Button>()) button.Margin = new Thickness(0, 0, 12, 0);
        Grid.SetRow(footer, 2); shell.Children.Add(footer); Content = shell;
        AttachedToVisualTree += (_, _) => { RefreshCandidates(); enabled.Focus(); }; RefreshCandidates();
    }
    private Button Button(string text, string name, Action action)
    {
        var button = new Button { Content = text, Name = name, Padding = StudioTheme.ButtonPadding };
        button.Click += (_, _) => { try { action(); } catch (Exception ex) { feedback.Text = ex.Message; } }; return button;
    }
    private void Changed() { HasUnsavedChanges = true; feedback.Text = "Unsaved pool selections · Save pools to apply."; }
    private void UpdateCount(string kind) => counts[kind].Text = profile.Selection.Pools[kind].Length + " candidates selected";
    private async Task Import(string kind, string? replace, Button button)
    {
        button.IsEnabled = false;
        try
        {
            string? source = await pick(); if (source == null) return;
            profile.ImportCandidate(kind, source, replace); Changed(); RefreshCandidates();
        }
        catch (Exception ex) { feedback.Text = ex.Message; }
        finally { button.IsEnabled = true; }
    }
    public void RefreshCandidates()
    {
        foreach (string kind in BaseGamePlayerPools.Kinds)
        {
            var list = lists[kind]; list.Children.Clear(); moveActions[kind] = []; int index = 0;
            foreach (string file in profile.Candidates(kind))
            {
                string path = Path.Combine(BaseGamePlayerPools.PoolDirectory(profile.Root, kind), file);
                var doc = new CampaignDocument(File.Exists(path) ? File.ReadAllText(path) : "");
                string name = doc.Get("Name", doc.Get("Import Player", file));
                var check = new CheckBox { Name = "BaseGameCandidate_" + kind + "_" + index, Content = new TextBlock { Text = name, TextWrapping = TextWrapping.Wrap }, IsChecked = profile.Selection.Pools[kind].Contains(file, StringComparer.OrdinalIgnoreCase), HorizontalAlignment = HorizontalAlignment.Stretch };
                check.IsCheckedChanged += (_, _) => { profile.SetSelected(kind, file, check.IsChecked == true); Changed(); UpdateCount(kind); UpdateMoveActions(kind); };
                var editButton = Button("Edit", "BaseGamePoolEdit_" + kind + "_" + index, () => edit(path));
                var replace = Button("Replace…", "BaseGamePoolReplace_" + kind + "_" + index, () => { }); replace.Click += async (_, _) => await Import(kind, file, replace);
                var remove = Button("Remove…", "BaseGamePoolRemove_" + kind + "_" + index, () => { });
                remove.Click += async (_, _) =>
                {
                    try
                    {
                        if (TopLevel.GetTopLevel(this) is not Window owner || !await ContentRemoval.Confirm(owner, name,
                            "Remove this pool entry and any unused older copies of the same base-game player? Current pool selections will be saved.")) return;
                        profile.RemoveCandidate(kind, file); HasUnsavedChanges = false; RefreshCandidates(); feedback.Text = "Removed “" + name + "” and saved pools.";
                    }
                    catch (Exception ex) { feedback.Text = ex.Message; }
                };
                var up = Button("↑", "BaseGamePoolUp_" + kind + "_" + index, () => { profile.Move(kind, file, -1); Changed(); RefreshCandidates(); });
                var down = Button("↓", "BaseGamePoolDown_" + kind + "_" + index, () => { profile.Move(kind, file, 1); Changed(); RefreshCandidates(); });
                ToolTip.SetTip(up, "Move selected candidate up"); ToolTip.SetTip(down, "Move selected candidate down");
                moveActions[kind].Add((file, up, down));
                var actions = new WrapPanel { Orientation = Orientation.Horizontal, Children = { editButton, replace, remove, up, down } };
                foreach (var action in actions.Children.OfType<Button>()) { action.Padding = new Thickness(8, 5); action.Margin = new Thickness(0, 0, 6, 4); }
                list.Children.Add(new Border { Padding = new Thickness(4, 3, 4, 8), BorderBrush = StudioTheme.Border, BorderThickness = new Thickness(0, 0, 0, 1), Child = new StackPanel { Spacing = 4, Children = { check, actions } } }); index++;
            }
            UpdateCount(kind);
            UpdateMoveActions(kind);
        }
    }
    private void UpdateMoveActions(string kind)
    {
        foreach (var (file, up, down) in moveActions[kind])
        {
            int index = Array.FindIndex(profile.Selection.Pools[kind], f => f.Equals(file, StringComparison.OrdinalIgnoreCase));
            up.IsEnabled = index > 0;
            down.IsEnabled = index >= 0 && index < profile.Selection.Pools[kind].Length - 1;
        }
    }
}
