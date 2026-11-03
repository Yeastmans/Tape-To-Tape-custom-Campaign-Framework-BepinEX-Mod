using System.Reflection;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CampaignCore;
using CampaignCreator;
using SkiaSharp;

string cache = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
    throw new IOException("Use a new, empty output folder; existing files are never overwritten by these checks.");
Directory.CreateDirectory(output);
string root = Path.Combine(output, "fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
typeof(App).Assembly.GetType("CampaignCreator.StudioArt")!.GetMethod("Use", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [cache]);
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
int checks = 0;
void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks++; Console.WriteLine("PASS " + label); }
void Pump() { Dispatcher.UIThread.RunJobs(); }
T Find<T>(Control parent, string name) where T : Control => parent.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
void Click(Control parent, string name) { Find<Button>(parent, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(); }
bool HasArt(string path) { using var bitmap = SKBitmap.Decode(path); return bitmap != null && bitmap.Pixels.Any(p => p.Alpha > 32); }
byte[] PreviewHash(HeadEditorView editor)
{
    using var stream = new MemoryStream();
    ((Avalonia.Media.Imaging.Bitmap)Find<Image>(editor, "HeadArtworkPreview").Source!).Save(stream);
    return SHA256.HashData(stream.ToArray());
}
Dictionary<string, byte[]> Files(HeadProject p) => Directory.GetFiles(p.Folder, "*", SearchOption.AllDirectories).ToDictionary(f => f, f => SHA256.HashData(File.ReadAllBytes(f)));
void Snapshot(Window window, string file) { Pump(); using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(output, file)); }
var owner = new Window { Width = 1100, Height = 850 }; owner.Show(); Pump();
try
{
    string library = Path.Combine(root, "custom-heads");
    var cancelled = new HeadProjectStartWindow(root); _ = cancelled.ShowDialog(owner); Pump();
    Find<RadioButton>(cancelled, "HeadSetupScratch").IsChecked = true; Pump();
    var options = cancelled.GetVisualDescendants().OfType<ComboBox>().SingleOrDefault(c => c.Name == "HeadBlankTemplateStyle");
    Check(options != null && options.IsEffectivelyVisible, "New head > Blank template exposes starter options");
    Check(options!.Items.Count == 3, "blank starter offers plain head, transparent with guides and transparent-only options");
    Find<TextBox>(cancelled, "HeadSetupName").Text = "Cancelled blank"; Pump();
    Check(Find<Button>(cancelled, "SaveHeadSetup").IsEnabled, "blank starter needs a valid name without forcing a native-character selection");
    Snapshot(cancelled, "blank-template-options.png");
    Click(cancelled, "CancelHeadSetup"); Check(!Directory.Exists(library), "cancelling blank setup creates no project or drawing files");

    for (int mode = 0; mode < 3; mode++)
    {
        var setup = new HeadProjectStartWindow(root); _ = setup.ShowDialog(owner); Pump();
        Find<RadioButton>(setup, "HeadSetupScratch").IsChecked = true; Pump();
        Find<ComboBox>(setup, "HeadBlankTemplateStyle").SelectedIndex = mode;
        Find<TextBox>(setup, "HeadSetupName").Text = "Blank option " + mode; Pump();
        Click(setup, "SaveHeadSetup");
        var project = setup.Result;
        Check(project != null && project.StartedFromScratch && project.Version == 2, "blank option creates a saved current-format project: " + mode + " / " + string.Join(" | ", setup.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text)));
        var definition = CustomHeadDefinition.Read(project!.Folder);
        Check(definition.Images.Count(i => i.Group == "Head") == 5 && definition.SupportsHelmet && definition.Bindings.Count == 18,
            "blank starter preserves five main views and all native fitted-head bindings: " + mode);
        foreach (var image in project.Images)
        {
            using var bitmap = SKBitmap.Decode(project.FilePath(image.File));
            Check(bitmap is { Width: 512, Height: 512 } && HasArt(project.FilePath(image.File)) == (mode == 0), "starter PNG has the selected visible/transparent state: " + mode + "/" + image.File);
            Check(!bitmap!.Pixels.Any(p => p.Alpha > 0 && p.Red == 105 && p.Green == 217 && p.Blue == 192), "guide colour is absent from editable artwork: " + mode + "/" + image.File);
        }
        var psds = Directory.GetFiles(Path.Combine(project.Folder, "drawing"), "*.psd", SearchOption.AllDirectories);
        Check(psds.Length == project.Images.Count, "each required view has a layered external-editor file: " + mode);
        foreach (string psd in psds) Check(File.ReadAllBytes(psd).Take(4).SequenceEqual("8BPS"u8.ToArray()), "drawing file has a PSD signature: " + Path.GetFileName(psd));
        Check(HeadProject.Open(project.Folder).Name == project.Name, "blank project reopens without changing its identity: " + mode);
        if (mode == 0 || mode == 2)
        {
            var editor = new HeadEditorView(owner, root, () => { }, project); owner.Content = editor; Pump();
            Check(editor.GetVisualDescendants().OfType<CheckBox>().Any(c => c.Name == "HeadPreviewHelmet"), "Head Editor exposes a separate helmet preview toggle");
            Check(editor.GetVisualDescendants().OfType<Button>().Any(c => c.Name == "OpenHeadDrawingTemplates"), "blank project exposes its layered external-editor files");
            Check(editor.GetVisualDescendants().OfType<ComboBox>().Any(c => c.Name == "HeadPreviewDirection"), "head preview lets the user inspect the required directions");
            Check(editor.GetVisualDescendants().OfType<CheckBox>().Where(c => c.Name == "ShowNativeOutline").All(c => c.IsChecked == (mode == 0)), "drawing-guide visibility matches the chosen starter: " + mode);
            var files = Files(project);
            bool supported = project.SupportsHelmet, separate = project.SeparateHelmetArtwork;
            byte[] bare = PreviewHash(editor);
            Find<CheckBox>(editor, "HeadPreviewHelmet").IsChecked = true; Pump();
            Check(!PreviewHash(editor).SequenceEqual(bare), "actual UI toggle adds the helmet overlay: " + mode);
            if (mode == 0)
            {
                Snapshot(owner, "head-editor-top.png");
                Find<Image>(editor, "HeadArtworkPreview").BringIntoView(); Pump(); Snapshot(owner, "head-editor-preview.png");
                owner.Width = 800; owner.Height = 680; Pump(); Snapshot(owner, "head-editor-small.png");
                owner.Width = 1100; owner.Height = 850; Pump();
                byte[] front = PreviewHash(editor);
                Find<ComboBox>(editor, "HeadPreviewDirection").SelectedIndex = 2; Pump();
                Check(!PreviewHash(editor).SequenceEqual(front), "actual UI direction selector changes the fitted preview");
                Find<ComboBox>(editor, "HeadPreviewDirection").SelectedIndex = 0; Pump();
            }
            Find<CheckBox>(editor, "HeadPreviewHelmet").IsChecked = false; Pump();
            Check(PreviewHash(editor).SequenceEqual(bare), "turning the helmet off restores the unmodified bare preview: " + mode);
            Check(project.SupportsHelmet == supported && project.SeparateHelmetArtwork == separate && files.All(f => SHA256.HashData(File.ReadAllBytes(f.Key)).SequenceEqual(f.Value)), "preview controls leave equipment flags and every project file unchanged: " + mode);
            Click(editor, "RefreshHeadPngs");
            Check(PreviewHash(editor).SequenceEqual(bare), "Refresh PNGs reloads the same external artwork: " + mode);
            owner.Content = null; Pump();
        }
        string first = psds[0]; File.AppendAllText(first, "user edit sentinel"); byte[] before = SHA256.HashData(File.ReadAllBytes(first));
        typeof(HeadProject).GetMethod("EnsureDrawingTemplates")!.Invoke(project, null);
        Check(SHA256.HashData(File.ReadAllBytes(first)).SequenceEqual(before), "opening drawing templates preserves existing PSD edits: " + mode);
        File.WriteAllText(Path.Combine(project.Folder, "drawing", "unrelated.txt"), "must not be installed");
        string installed = project.Install(Path.Combine(root, "installed"));
        var installedProject = HeadProject.Open(installed);
        Check(SHA256.HashData(File.ReadAllBytes(Path.Combine(installed, Path.GetRelativePath(project.Folder, first)))).SequenceEqual(before), "install preserves edited source PSDs: " + mode);
        Check(installedProject.BlankStyle == project.BlankStyle && Directory.GetFiles(Path.Combine(installed, "drawing-layers"), "*.png", SearchOption.AllDirectories).Length == project.Images.Count * (mode == 2 ? 3 : 4), "install preserves starter options and PNG layers: " + mode);
        Check(!File.Exists(Path.Combine(installed, "drawing", "unrelated.txt")), "install excludes unrelated drawing-folder files: " + mode);
        var originalFiles = Files(project);
        bool duplicateRejected = false;
        try { HeadProject.CreateBlank(library, project.Name, (HeadBlankStyle)mode); } catch (IOException) { duplicateRejected = true; }
        Check(duplicateRejected && originalFiles.All(f => SHA256.HashData(File.ReadAllBytes(f.Key)).SequenceEqual(f.Value)), "duplicate starter names preserve the existing project: " + mode);
        if (mode == 0) File.Copy(psds.Skip(1).First(), Path.Combine(output, "plain-starter.psd"), true);
        if (mode == 1) File.Copy(psds.Skip(1).First(), Path.Combine(output, "guided-transparent-starter.psd"), true);
        if (mode == 2) File.Copy(psds.Skip(1).First(), Path.Combine(output, "empty-starter.psd"), true);
    }
    var nativeSetup = new HeadProjectStartWindow(root); _ = nativeSetup.ShowDialog(owner); Pump();
    Find<TextBox>(nativeSetup, "HeadSetupName").Text = "Native still needs layout"; Pump();
    Check(!Find<Button>(nativeSetup, "SaveHeadSetup").IsEnabled && Find<ComboBox>(nativeSetup, "HeadSetupTemplate").IsEffectivelyVisible, "native mode still requires a selected native layout");
    Click(nativeSetup, "CancelHeadSetup");
    var studioArt = typeof(App).Assembly.GetType("CampaignCreator.StudioArt")!.GetMethod("Use", BindingFlags.Public | BindingFlags.Static)!;
    studioArt.Invoke(null, [null]);
    var noCache = new HeadProjectStartWindow(root); _ = noCache.ShowDialog(owner); Pump();
    Check(!Find<RadioButton>(noCache, "HeadSetupScratch").IsEnabled && !Find<Button>(noCache, "SaveHeadSetup").IsEnabled, "missing game artwork cannot create an incomplete starter");
    Click(noCache, "CancelHeadSetup"); studioArt.Invoke(null, [cache]);
    if (args.Length > 2)
    {
        studioArt.Invoke(null, [Path.GetFullPath(args[2])]);
        var maskTemplate = NativeHeadTemplates.Read(goalie: true).First(t => t.Available);
        var mask = HeadProject.CreateFromTemplate(library, "Mask regression", maskTemplate.Skin, fromScratch: true, goalie: true);
        var maskEditor = new HeadEditorView(owner, root, () => { }, mask); owner.Content = maskEditor; Pump();
        Check(mask.IsGoalie && mask.Version == 3 && !maskEditor.GetVisualDescendants().OfType<CheckBox>().Any(c => c.Name == "HeadPreviewHelmet"), "goalie mask blank layout remains separate from skater helmet previews");
        Check(mask.Images.All(i => !HasArt(mask.FilePath(i.File))), "goalie mask blank artwork is unchanged");
        owner.Content = null; studioArt.Invoke(null, [cache]);
    }
    Console.WriteLine("PASS " + checks + " blank head template checks");
    return 0;
}
catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
finally { foreach (var window in owner.OwnedWindows.ToArray()) window.Close(); owner.Close(); }
