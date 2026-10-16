using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace CampaignCreator;

public sealed class HeadProjectStartWindow : Window
{
    readonly string library;
    readonly bool goalie;
    readonly TextBox name = new() { Name = "HeadSetupName", Watermark = "Name your head", MaxLength = 80 };
    readonly RadioButton scratch = new() { Name = "HeadSetupScratch", GroupName = "HeadStartingArtwork", Content = "From scratch · blank PNGs" };
    readonly ComboBox template = new() { Name = "HeadSetupTemplate", PlaceholderText = "Choose a native head layout…", HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly TextBlock layoutLabel = Text("Native template", 16);
    readonly TextBlock description = Text("");
    readonly TextBlock error = Text("");
    readonly Button save = new() { Name = "SaveHeadSetup", Content = "Save head and show previews", Padding = StudioTheme.ButtonPadding };
    public HeadProject? Result { get; private set; }

    public HeadProjectStartWindow(string contentRoot, bool goalie = false)
    {
        this.goalie = goalie;
        library = Path.Combine(contentRoot, "custom-heads");
        if (goalie) { name.Watermark = "Name your mask"; save.Content = "Save mask and show previews"; template.PlaceholderText = "Choose a native mask layout…"; }
        Name = "HeadProjectSetupDialog"; Title = goalie ? "Create or open a goalie mask" : "Create or open a head";
        StudioTheme.Primary(save);
        Width = 650; MaxHeight = 720; SizeToContent = SizeToContent.Height; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = StudioTheme.Surface;
        var templates = NativeHeadTemplates.Read(goalie).Where(t => t.Available).ToArray();
        template.ItemsSource = templates.Select(t => new LayoutChoice(t.Skin, t.Label)).ToArray();
        var native = new RadioButton { Name = "HeadSetupNative", GroupName = "HeadStartingArtwork", Content = "Use a native template", IsChecked = true };
        var modes = new WrapPanel { Orientation = Orientation.Horizontal };
        native.Margin = new Thickness(0, 0, 24, 8); scratch.Margin = new Thickness(0, 0, 0, 8);
        modes.Children.Add(native); modes.Children.Add(scratch);
        var cancel = new Button { Name = "CancelHeadSetup", Content = "Cancel", Padding = StudioTheme.ButtonPadding };
        var open = new Button { Name = "OpenSavedHead", Content = goalie ? "Open saved mask…" : "Open saved head…", Padding = StudioTheme.ButtonPadding };
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var button in new[] { open, cancel, save }) { button.Margin = new Thickness(0, 8, 8, 0); actions.Children.Add(button); }
        error.Foreground = Brush.Parse("#FFD18C"); error.IsVisible = false;
        var instructions = Text(goalie ? "Name your goalie mask and choose native artwork or a blank layout. Save first to open all required views, layers and guides." : "Name your head and choose how to start. Save the project first; its artwork previews and size guides open next.");
        var heading = new DockPanel();
        var help = PageHelpButton.Create(this); help.HorizontalAlignment = HorizontalAlignment.Right; DockPanel.SetDock(help, Dock.Right);
        heading.Children.Add(help); heading.Children.Add(Text(goalie ? "Create your goalie mask" : "Create your head", 26));
        var body = new StackPanel { Margin = new Thickness(24), Spacing = StudioTheme.SpaceMd, Children = {
            heading, instructions,
            Text(goalie ? "1. Name your mask" : "1. Name your head", 18), name,
            Text("2. Choose starting artwork", 18), modes,
            layoutLabel, template, description,
            Text("3. Save and open the previews", 18),
            Text("The project is saved in your custom-heads folder under this name. Edit its PNGs in your image app, or import your own artwork after saving."),
            error, actions
        } };
        Content = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        if (templates.Length == 0)
        {
            native.IsEnabled = scratch.IsEnabled = template.IsEnabled = false;
            instructions.Text = "Use Read game artwork on Home to load native templates and attachment layouts. You can still open a saved project here.";
        }
        native.IsCheckedChanged += (_, _) => Refresh(); scratch.IsCheckedChanged += (_, _) => Refresh();
        name.TextChanged += (_, _) => Refresh(); template.SelectionChanged += (_, _) => Refresh();
        save.Click += (_, _) => SaveAndOpen();
        cancel.Click += (_, _) => Close();
        open.Click += async (_, _) =>
        {
            try
            {
                var start = Directory.Exists(library) ? await StorageProvider.TryGetFolderFromPathAsync(library) : null;
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions {
                    Title = goalie ? "Open a saved goalie mask project" : "Open a saved Head Editor project", AllowMultiple = false, SuggestedStartLocation = start });
                if (folders.FirstOrDefault()?.TryGetLocalPath() is not { } folder) return;
                OpenSaved(folder);
            }
            catch (Exception ex) { ShowError(ex.Message); }
        };
        Opened += (_, _) => name.Focus();
        KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Escape) { Close(); e.Handled = true; } };
        Refresh();
    }

    void Refresh()
    {
        bool blank = scratch.IsChecked == true;
        layoutLabel.Text = blank ? "Attachment layout · keeps the native size and placement" : "Native template";
        description.Text = blank
            ? "Starts with blank 512 × 512 PNGs, including every direction, extra layer and portrait. Your selected layout supplies native outlines, original sizes and collar guides."
            : "Starts with artwork extracted from your installed game. Includes the matching fitted helmet variant when available, plus every direction, layer, portrait and size guide.";
        if (goalie) description.Text = blank
            ? "Starts with blank 512 × 512 PNGs for the mask views, extra layers and portrait. The native layout supplies outlines and placement guides."
            : "Starts with a native mask from your game. Includes front, back, four diagonal views, any extra layers and a portrait. Mirrored views share artwork where the game does.";
        bool validName; error.IsVisible = false;
        try
        {
            CreatorContent.ValidateName(name.Text ?? "");
            if ((name.Text ?? "").Trim().EndsWith("_Helmet", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Choose a name without the reserved _Helmet ending. Fitted artwork is saved inside the same head project.");
            validName = true;
        }
        catch (ArgumentException ex) { validName = false; if (!string.IsNullOrWhiteSpace(name.Text)) ShowError(ex.Message); }
        save.IsEnabled = validName && template.SelectedItem is LayoutChoice;
    }

    void SaveAndOpen()
    {
        try
        {
            if (template.SelectedItem is not LayoutChoice choice) { ShowError("Choose a native template or attachment layout first."); return; }
            Result = HeadProject.CreateFromTemplate(library, name.Text ?? "", choice.Skin, scratch.IsChecked == true, goalie);
            Close(Result);
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    public void OpenSaved(string folder)
    {
        var opened = HeadProject.Open(folder);
        if (opened.IsGoalie != goalie) throw new InvalidDataException(goalie ? "Choose a goalie mask project. Skater heads open in Head Editor." : "Choose a skater head project. Goalie masks open in Goalie Mask Editor.");
        Result = opened; Close(Result);
    }
    internal IReadOnlyList<PageHelpPopup.Step> HelpSteps() => goalie ? [
        new("Name your mask", "Enter a unique project name, then choose a native mask layout.", ["HeadSetupName"]),
        new("Starting artwork", "Use the native artwork or start with blank PNGs. Both retain all six views, mirrored images and any top layers.", ["HeadSetupNative", "HeadSetupScratch"]),
        new("Native layout", "The selected mask supplies the original attachment sizes, outlines and front/back placement guides.", ["HeadSetupTemplate"]),
        new("Save mask and show previews", "Save first, then edit each required PNG and portrait in your image app or import artwork. Cancel creates no files.", ["SaveHeadSetup"]),
        new("Open saved mask", "Open a mask project folder previously saved by Goalie Mask Editor.", ["OpenSavedHead"])
    ] : [
        new("Name your head", "Enter a unique name for its project folder. Save is enabled after a valid name and a native layout are selected.", ["HeadSetupName"]),
        new("Use a native template", "Start with head artwork extracted from your installed game, including its fitted variant when available. Choosing a mode does not create files.", ["HeadSetupNative"]),
        new("From scratch", "Create blank 512 × 512 PNGs for every direction, extra layer and portrait. The selected native layout supplies the size, native outlines and collar guides.", ["HeadSetupScratch"]),
        new("Native template or attachment layout", "Choose an extracted native skater head or fitted variant. Either variant keeps the family's native placement and matching bare/fitted artwork layouts.", ["HeadSetupTemplate"]),
        new("Save head and show previews", "Creates and saves the named project before opening its artwork previews. Duplicate names are rejected without overwriting the existing head. Draw or import your own PNGs after saving.", ["SaveHeadSetup"]),
        new("Open saved head", "Choose an existing folder containing head-project.json to open its saved artwork directly.", ["OpenSavedHead"]),
        new("Cancel", "Close setup and return to the current page. Cancel creates no files and opens no previews.", ["CancelHeadSetup"])
    ];
    void ShowError(string message) { error.Text = message; error.IsVisible = true; }
    static TextBlock Text(string text, double size = 14) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
    sealed record LayoutChoice(string Skin, string Label) { public override string ToString() => Label; }
}
