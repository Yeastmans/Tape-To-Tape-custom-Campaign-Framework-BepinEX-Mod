using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using System.Diagnostics;

namespace CampaignCreator;

public sealed class HeadEditorView : UserControl
{
    readonly Window owner;
    readonly string library;
    readonly StackPanel body = new() { Spacing = StudioTheme.SpaceLg };
    readonly TextBlock feedback = Text("", 14);
    readonly List<Bitmap> bitmaps = [];
    HeadProject project;
    readonly IReadOnlyList<NativeHeadTemplate> templates;
    public HeadProject Project => project;
    public HeadEditorView(Window owner, string root, Action back, HeadProject savedProject)
    {
        this.owner = owner; library = Path.Combine(root, "custom-heads"); project = savedProject;
        templates = NativeHeadTemplates.Read(savedProject.IsGoalie);
        var layout = new Grid { Margin = new Thickness(24), RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), RowSpacing = StudioTheme.SpaceLg };
        var navigation = new StackPanel { Orientation = Orientation.Horizontal, Spacing = StudioTheme.SpaceLg };
        navigation.Children.Add(ActionButton("Home", back)); navigation.Children.Add(Text(project.IsGoalie ? "Goalie Mask Editor" : "Head Editor", 28)); navigation.Children.Add(PageHelpButton.Create(owner));
        layout.Children.Add(navigation);
        var tools = new WrapPanel { Orientation = Orientation.Horizontal };
        tools.Children.Add(ActionButton(project.IsGoalie ? "New mask…" : "New head…", async () =>
        {
            var setup = new HeadProjectStartWindow(root, project.IsGoalie);
            if (await setup.ShowDialog<HeadProject?>(owner) is { } saved)
            {
                project = saved; Refresh(); feedback.Text = "Saved " + saved.Name + " · " + saved.Folder;
            }
        }, "HeadNewProject"));
        tools.Children.Add(ActionButton("Import PNGs…", async () =>
        {
            try
            {
                var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = project.IsGoalie ? "Import mask PNGs: Front, Back, Front left, Front right, Back left, Back right, Portrait" : "Import head PNGs (name them Front, Back, Side, Back diagonal, Front diagonal, Portrait)", AllowMultiple = true, FileTypeFilter = [new FilePickerFileType("PNG image") { Patterns = ["*.png"] }] });
                var paths = files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList(); if (paths.Count == 0) return;
                ImportInto(paths);
            }
            catch (Exception ex) { feedback.Text = ex.Message; }
        }, "ImportHeadPngs"));
        tools.Children.Add(ActionButton("Save", () => Try(() =>
        {
            project.Save(); Refresh(); feedback.Text = "Saved " + project.Name + " · " + project.Folder;
        }), "SaveHead"));
        tools.Children.Add(ActionButton(project.IsGoalie ? "Open saved mask…" : "Open saved head…", async () =>
        {
            try
            {
                var start = Directory.Exists(library) ? await owner.StorageProvider.TryGetFolderFromPathAsync(library) : null;
                var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = project.IsGoalie ? "Open a goalie mask project" : "Open a Head Editor project", AllowMultiple = false, SuggestedStartLocation = start });
                string? folder = folders.FirstOrDefault()?.TryGetLocalPath();
                if (folder != null) { var opened = HeadProject.Open(folder); if (opened.IsGoalie != project.IsGoalie) throw new InvalidDataException("Open this project from the matching editor on Home."); project = opened; Refresh(); feedback.Text = "Opened " + folder; }
            }
            catch (Exception ex) { feedback.Text = ex.Message; }
        }, "OpenHeadProject"));
        tools.Children.Add(ActionButton("Refresh PNGs", () => Try(Refresh), "RefreshHeadPngs"));
        Grid.SetRow(tools, 1); layout.Children.Add(tools);
        var scroll = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 2); layout.Children.Add(scroll);
        feedback.Foreground = Brush.Parse("#FFD18C"); Grid.SetRow(feedback, 3); layout.Children.Add(feedback);
        Content = layout; Refresh();
        DetachedFromVisualTree += (_, _) => ClearImages();
    }
    static TextBlock Text(string text, double size = 14) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
    static Button ActionButton(string text, Action action, string? name = null)
    {
        var button = new Button { Content = text, Name = name, Padding = new Thickness(12, 8), Margin = new Thickness(0, 0, 8, 8) };
        button.Click += (_, _) => action(); return button;
    }
    public void ImportInto(IReadOnlyList<string> paths)
    {
        string summary = project.Import(paths); Refresh(); feedback.Text = summary;
    }
    void Try(Action action) { try { action(); } catch (Exception ex) { feedback.Text = ex.Message; } }
    void OpenFile(string path) => Try(() => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }));
    void ClearImages() { foreach (var bitmap in bitmaps) bitmap.Dispose(); bitmaps.Clear(); }
    public void Refresh()
    {
        body.Children.Clear(); ClearImages();
        var p = project;
        body.Children.Add(Text(p.Name, 26));
        body.Children.Add(Text("Saved project · " + p.Folder));
        body.Children.Add(Text("Native template: " + (templates.FirstOrDefault(t => t.Skin == p.TemplateSkin)?.Label ?? p.TemplateSkin)));
        body.Children.Add(Text("1. Edit your artwork · 512 × 512 transparent PNGs", 20));
        body.Children.Add(Text(p.StartedFromScratch
            ? p.IsGoalie ? "This mask started with blank PNGs. Use the native outlines to draw or import each view and layer, then edit the separate portrait. Expand the native examples to check placement." : "This project started with blank PNGs. Use each native head outline to draw the head and neck in the original position, or import your artwork. Edit every direction, extra layer and portrait. Expand the native examples below to see the collar placement."
            : "Open each PNG in your image app or use Replace PNG / Import PNGs to add your own artwork. Edit every direction, extra layer and portrait."));
        body.Children.Add(Text("2. Check the previews · use Refresh PNGs after editing in another app. Imports and replacements save immediately and keep the previous PNG as a .bak file."));
        body.Children.Add(new Border { Padding = new Thickness(12), CornerRadius = new CornerRadius(6), Background = Brush.Parse("#293749"), Child = Text(p.IsGoalie ? "3. Add to goalie mask options, return Home, then choose Goalie mask > Premade in a goalie or team look editor. Restart the game after installing or editing artwork. Check front, back and diagonal views in game." : "3. Add to player head options, then return Home and choose this head under Player > Head. Restart the game after installing or changing artwork. The outline follows the original head, including its neck edge. Check changed artwork and helmet fit in game.") });
        var actions = new WrapPanel();
        actions.Children.Add(ActionButton(p.IsGoalie ? "Add to goalie mask options" : "Add to player head options", () => Try(() =>
        {
            string installed = p.Install(CampaignCore.UserContentPaths.EnsureRoot());
            // Also register in a custom --content workspace when one is in use.
            string workspaceRoot = Path.GetDirectoryName(library)!;
            if (!Path.GetFullPath(workspaceRoot).Equals(Path.GetFullPath(CampaignCore.UserContentPaths.Root), StringComparison.OrdinalIgnoreCase)) p.Install(workspaceRoot);
            feedback.Text = "Installed " + p.Name + (p.IsGoalie ? ". Return Home and select Goalie mask > Premade. Restart the game. " : ". Return Home to select it under Player > Head. Restart the game. ") + installed;
        }), "InstallCustomHead"));
        actions.Children.Add(ActionButton("Open project folder", () => OpenFile(p.Folder)));
        actions.Children.Add(ActionButton("Open outline guides", () => Try(() => OpenFile(p.ExportOutlineGuides()))));
        actions.Children.Add(ActionButton("Open attachment data", () => OpenFile(p.FilePath(p.Version >= 2 ? "reference/Bare/head-assets.json" : "reference/head-assets.json"))));
        body.Children.Add(actions);
        AddGroup("Head", p.IsGoalie ? "Mask views and layers · required" : "Head directions · required"); AddGroup("Portrait", "Player portrait · required");
        if (p.SupportsHelmet && p.SeparateHelmetArtwork) AddGroup("Helmet", "Head under helmet · required when using separate artwork");
        if (p.Version >= 2)
        {
            AddNativeExample(p.FilePath("reference/Bare/views"), p.Bindings.Count(b => !b.Helmet));
            if (p.TemplateHelmetSkin.Length > 0) AddNativeExample(p.FilePath("reference/Helmet/views"), p.Bindings.Count(b => b.Helmet), "under-helmet");
        }
        var helmet = new CheckBox { Name = "HeadSupportsHelmet", Content = "Supports a selectable helmet", IsChecked = p.SupportsHelmet,
            IsEnabled = p.Version == 1 || p.TemplateHelmetSkin.Length > 0 };
        var separate = new CheckBox { Name = "HeadSeparateHelmetArtwork", Content = p.Version >= 2 ? "Separate native artwork under helmets" : "Use separate artwork under helmets (for tucked hair, etc.)",
            IsChecked = p.SeparateHelmetArtwork, IsEnabled = p.SupportsHelmet && p.Version == 1 };
        helmet.IsCheckedChanged += (_, _) => Try(() => { p.SupportsHelmet = helmet.IsChecked == true; p.Save(); Refresh(); });
        separate.IsCheckedChanged += (_, _) => Try(() => { if (separate.IsChecked == true) p.EnsureHelmetImages(); p.SeparateHelmetArtwork = separate.IsChecked == true; p.Save(); Refresh(); });
        if (!p.IsGoalie) { body.Children.Add(helmet); body.Children.Add(separate); }
        if (!p.IsGoalie) body.Children.Add(Text(!p.SupportsHelmet ? "Helmetless head: the project records that helmet selection should be disabled." : p.SeparateHelmetArtwork ? "Edit the fitted head artwork below. The game supplies the helmet itself." : "Both variants share these PNGs. The template has separate normal and helmet attachment layouts."));
        if (p.Images.Any(i => i.Group == "Support"))
        {
            var support = new WrapPanel();
            foreach (var item in p.Images.Where(i => i.Group == "Support")) support.Children.Add(Card(p, item));
            body.Children.Add(new Expander { Header = "Inherited hand images · keep unless changing the template’s hands", Content = support });
        }
        body.Children.Add(Text("The original artwork, native sizes, attachment data and placement examples are preserved in reference/. The game retains the selected template's geometry and fits visible replacement artwork to its native size and position, ignoring transparent padding. Edit every required direction and layer. The portrait is a separate image and does not update automatically.") );
        void AddGroup(string group, string title)
        {
            body.Children.Add(Text(title, 20)); var cards = new WrapPanel();
            foreach (var item in p.Images.Where(i => i.Group == group)) cards.Children.Add(Card(p, item));
            body.Children.Add(cards);
        }
    }
    void AddNativeExample(string folder, int attachments, string kind = "head")
    {
        if (project.IsGoalie) kind = "mask";
        var row = new StackPanel { Spacing = StudioTheme.SpaceSm };
        row.Children.Add(Text($"Native placement and size example · {attachments} {kind} attachments", 18));
        row.Children.Add(Text(project.IsGoalie ? "These original mask examples show placement on the goalie in all six views. Edit each required PNG and use the outline guides; check changed artwork in game." : "This example shows the original head against the body's collar. It keeps the same scale across all templates. The editable PNGs above have native outline guides; check changed artwork in game."));
        var direction = new ComboBox { Name = kind != "under-helmet" ? "HeadExampleDirection" : "HeadHelmetExampleDirection", ItemsSource = (project.IsGoalie ? new[] { "S", "N", "NE", "NW", "SW", "SE" } : new[] { "S", "N", "W", "E", "NE", "NW", "SW", "SE" })
            .Select(d => new ExampleDirection(d, HeadProject.DirectionLabel(d))).ToArray(), SelectedIndex = 0, Width = 160 };
        var picture = new Image { Height = 330, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
        void Show()
        {
            try { var bitmap = new Bitmap(Path.Combine(folder, (direction.SelectedItem is ExampleDirection choice ? choice.Id : "S") + ".png")); bitmaps.Add(bitmap); picture.Source = bitmap; }
            catch { picture.Source = null; }
        }
        direction.SelectionChanged += (_, _) => Show(); Show(); row.Children.Add(direction); row.Children.Add(picture);
        body.Children.Add(new Expander { Name = kind != "under-helmet" ? "HeadNativeReference" : "HeadHelmetNativeReference",
            Header = "See native " + kind + " size and collar placement", Content = row, IsExpanded = false });
    }
    sealed record ExampleDirection(string Id, string Label) { public override string ToString() => Label; }
    Control Card(HeadProject p, HeadImage item)
    {
        var panel = new StackPanel { Spacing = StudioTheme.SpaceSm };
        panel.Children.Add(Text(item.Label, 18));
        var picture = new Image { Height = 170, Stretch = Stretch.Uniform };
        var outline = new Image { Name = "HeadNativeOutline", Height = 170, Stretch = Stretch.Uniform, IsVisible = true };
        bool valid = true;
        try
        {
            using var stream = File.OpenRead(p.FilePath(item.File)); var bitmap = new Bitmap(stream); bitmaps.Add(bitmap); picture.Source = bitmap;
            valid = bitmap.PixelSize.Width == item.Width && bitmap.PixelSize.Height == item.Height;
        }
        catch { valid = false; }
        string? outlineError = null;
        try
        {
            // Regenerate from the preserved original, so older projects also get
            // silhouettes without rewriting their artwork or rectangle files.
            using var native = p.NativeOutline(item); using var image = SkiaSharp.SKImage.FromBitmap(native);
            using var encoded = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100); using var stream = encoded.AsStream();
            var guideBitmap = new Bitmap(stream); bitmaps.Add(guideBitmap); outline.Source = guideBitmap;
        }
        catch (Exception ex) { outlineError = ex.Message; outline.IsVisible = false; }
        var images = new Grid { Background = Brush.Parse("#354358") }; images.Children.Add(picture); images.Children.Add(outline); panel.Children.Add(images);
        panel.Children.Add(Text(valid ? $"{item.Width} × {item.Height} · PNG" : "Missing or invalid image — replace PNG"));
        panel.Children.Add(Text($"Original game artwork: {item.NativeWidth} × {item.NativeHeight} px", 12));
        if (p.Version >= 2)
        {
            var binding = p.Bindings.FirstOrDefault(b => b.File == item.File);
            if (binding != null) panel.Children.Add(Text($"Native attachment: {binding.NativeWidth:0.#} × {binding.NativeHeight:0.#}", 12));
        }
        panel.Children.Add(Text(item.File, 12));
        var showOutline = new CheckBox { Name = "ShowNativeOutline", Content = "Show native outline", IsChecked = outlineError == null, IsEnabled = outlineError == null };
        showOutline.IsCheckedChanged += (_, _) => outline.IsVisible = showOutline.IsChecked == true; panel.Children.Add(showOutline);
        if (outlineError != null) panel.Children.Add(Text("Native outline unavailable: " + outlineError, 12));
        panel.Children.Add(ActionButton("Replace PNG…", async () =>
        {
            try
            {
                var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Replace " + item.Label, AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("PNG image") { Patterns = ["*.png"] }] });
                string? file = files.FirstOrDefault()?.TryGetLocalPath(); if (file == null) return;
                p.Replace(item, file); Refresh(); feedback.Text = "Saved " + item.File;
            }
            catch (Exception ex) { feedback.Text = ex.Message; }
        }));
        var links = new WrapPanel(); links.Children.Add(ActionButton("Open PNG", () => OpenFile(p.FilePath(item.File)))); links.Children.Add(ActionButton("Outline PNG", () => Try(() => OpenFile(p.ExportOutline(item))))); panel.Children.Add(links);
        return new Border { Width = 245, Margin = new Thickness(0, 0, 12, 12), Padding = new Thickness(14), CornerRadius = new CornerRadius(8), Background = StudioTheme.Surface, BorderBrush = StudioTheme.Border, BorderThickness = new Thickness(1), Child = panel };
    }
}
