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

if (args.Length != 2) throw new ArgumentException("Supply game-art cache and a new empty output directory.");
string output = Path.GetFullPath(args[1]);
if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
    throw new IOException("Use a new, empty output folder; existing files are never overwritten by these checks.");
Directory.CreateDirectory(output);
typeof(HeadProject).Assembly.GetType("CampaignCreator.StudioArt")!.GetMethod("Use", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [Path.GetFullPath(args[0])]);
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
int checks = 0, failures = 0;
void Check(bool value, string name) { checks++; Console.WriteLine((value ? "PASS " : "FAIL ") + name); if (!value) failures++; }
void Case(string name, Action action) { try { action(); } catch (Exception ex) { Check(false, name + ": " + ex.GetBaseException().Message); } }
byte[] Hash(string file) => SHA256.HashData(File.ReadAllBytes(file));
void Save(SKBitmap bitmap, string file)
{
    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
    using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    using var stream = File.Create(file); data.SaveTo(stream);
}
SKRectI Bounds(SKBitmap image)
{
    int l = image.Width, t = image.Height, r = 0, b = 0;
    var pixels = image.Pixels;
    for (int y = 0; y < image.Height; y++) for (int x = 0; x < image.Width; x++)
        if (pixels[y * image.Width + x].Alpha > 32) { l = Math.Min(l, x); t = Math.Min(t, y); r = Math.Max(r, x + 1); b = Math.Max(b, y + 1); }
    return new(l, t, r, b);
}
var project = HeadProject.CreateFromTemplate(Path.Combine(output, "custom-heads"), "Workflow fixture " + Guid.NewGuid().ToString("N"), HeadProject.BasicStarterSkin);
var front = project.Images.Single(i => i.Group == "Head" && i.Direction == "S");
Case("PSD quick-export filename matching", () => {
    Check(project.SlotFor("Front [S].png") == front, "Front [S].png resolves to the front head");
    foreach (var item in project.Images)
    {
        string exported = Path.ChangeExtension(project.DrawingTemplate(item), ".png");
        Check(project.SlotFor(exported) == item, "PSD export resolves its exact group/view: " + Path.GetRelativePath(project.Folder, exported));
    }
    Check(project.SlotFor("Front.png") == front && project.SlotFor("s.png") == front, "Existing front aliases still work");
    Check(project.SlotFor("unrelated [S].png") == null, "Unrelated bracketed names are not silently assigned to a head slot");
});
Case("Legacy fitted aliases", () => {
    var legacy = new HeadProject { Version = 1, Images = [new HeadImage { File = "frames/S.png", Group = "Head", Label = "Front" },
        new HeadImage { File = "helmet/S.png", Group = "Helmet", Label = "Front" }] };
    Check(legacy.SlotFor(Path.Combine("Helmet", "Front.png")) == legacy.Images[1], "Legacy fitted Front alias uses the saved helmet slot");
    Check(legacy.SlotFor(Path.Combine("Helmet", "S.png")) == legacy.Images[1], "Legacy fitted direction alias uses the saved helmet slot");
});
Case("Layered native exports", () => {
    var layered = HeadProject.CreateFromTemplate(Path.Combine(output, "layered"), "Captain fixture", "Faces/Canadians/Captain");
    foreach (var item in layered.Images)
        Check(layered.SlotFor(Path.ChangeExtension(layered.DrawingTemplate(item), ".png")) == item,
            "Extra-layer export resolves correctly: " + item.Group + "/" + item.Label);
});
Case("Trimmed replacement", () => {
    string source = Path.Combine(output, "exports", "Front [S].png"), target = project.FilePath(front.File);
    using var crop = new SKBitmap(359, 400, SKColorType.Rgba8888, SKAlphaType.Premul); crop.Erase(SKColors.Coral);
    Save(crop, source); byte[] before = Hash(target), sourceBefore = Hash(source);
    project.Replace(front, source);
    using var result = SKBitmap.Decode(target); var bounds = Bounds(result);
    Check(result.Width == 512 && result.Height == 512, "Trimmed replacement becomes a valid 512px canvas");
    Check(Math.Abs(bounds.Width / (double)bounds.Height - 359.0 / 400) < .01, "Trimmed replacement preserves proportions");
    Check(bounds.Left >= front.Left && bounds.Top >= front.Top && bounds.Right <= front.Left + front.ContentWidth && bounds.Bottom <= front.Top + front.ContentHeight,
        "Trimmed replacement stays inside the template footprint");
    Check(Hash(target + ".bak").SequenceEqual(before), "Replacement preserves the previous PNG backup");
    Check(Hash(source).SequenceEqual(sourceBefore), "Replacement leaves the Photoshop export unchanged");
    CustomHeadDefinition.Read(project.Folder); Check(true, "Imported project still satisfies the runtime contract");
});
Case("Native canvas placement round trip", () => {
    var native = HeadProject.CreateFromTemplate(Path.Combine(output, "native-placement"), "Native placement", HeadProject.BasicStarterSkin);
    Check(native.Images.Select(i => (i.NativeWidth, i.NativeHeight)).Distinct().Count() > 1, "Placement fixture includes differing original game canvas sizes");
    foreach (var item in native.Images)
    {
        using var before = SKBitmap.Decode(native.FilePath(item.File));
        native.Replace(item, native.FilePath(item.SourceFile));
        using var after = SKBitmap.Decode(native.FilePath(item.File));
        Check(before.Pixels.SequenceEqual(after.Pixels), "Native-size import restores the view's exact canvas offset: " + item.File);
        int l = before.Width, t = before.Height, r = 0, b = 0;
        var pixels = before.Pixels;
        for (int y = 0; y < before.Height; y++) for (int x = 0; x < before.Width; x++)
            if (pixels[y * before.Width + x].Alpha > 0) { l = Math.Min(l, x); t = Math.Min(t, y); r = Math.Max(r, x + 1); b = Math.Max(b, y + 1); }
        using var cropped = new SKBitmap(r - l, b - t, before.ColorType, before.AlphaType);
        before.ExtractSubset(cropped, new SKRectI(l, t, r, b));
        string file = Path.Combine(output, "trimmed-native", item.Group, Path.GetFileName(item.File)); Save(cropped, file);
        native.Replace(item, file);
        using var restored = SKBitmap.Decode(native.FilePath(item.File));
        Check(before.Pixels.SequenceEqual(restored.Pixels), "Trim-only export returns to the original per-view position: " + item.File);
    }
});
Case("Batch round trip", () => {
    string source = Path.Combine(output, "full", "Front.png");
    using var canvas = new SKBitmap(512, 512, SKColorType.Rgba8888, SKAlphaType.Premul); canvas.Erase(SKColors.Transparent);
    using (var draw = new SKCanvas(canvas)) { using var paint = new SKPaint { Color = SKColors.Teal }; draw.DrawRect(90, 70, 140, 240, paint); }
    Save(canvas, source); project.Import([source]);
    using var imported = SKBitmap.Decode(project.FilePath(front.File));
    Check(imported.Pixels.SequenceEqual(canvas.Pixels), "Batch import keeps an existing 512px canvas pixel-for-pixel");
});
Case("Batch cropped exports", () => {
    var paths = new List<string>();
    foreach (var item in project.Images)
    {
        string path = Path.Combine(output, "batch", item.Group, Path.GetFileNameWithoutExtension(project.DrawingTemplate(item)) + ".png");
        using var crop = new SKBitmap(200, 300, SKColorType.Rgba8888, SKAlphaType.Premul);
        crop.Erase(item.Group == "Helmet" ? SKColors.Blue : item.Group == "Portrait" ? SKColors.Yellow : SKColors.Red); Save(crop, path); paths.Add(path);
    }
    string summary = project.Import(paths);
    Check(!summary.Contains("Skipped"), "A full batch of named PSD exports imports without renaming");
    foreach (var item in project.Images)
    {
        using var image = SKBitmap.Decode(project.FilePath(item.File)); var pixel = image.GetPixel(256, 256);
        var expected = item.Group == "Helmet" ? SKColors.Blue : item.Group == "Portrait" ? SKColors.Yellow : SKColors.Red;
        Check(pixel == expected, "Batch writes the correct normal/fitted/portrait slot: " + item.File);
    }
});
Case("Invalid file preservation", () => {
    string source = Path.Combine(output, "not-png.png"); using var bitmap = new SKBitmap(40, 40); bitmap.Erase(SKColors.Red);
    using (var image = SKImage.FromBitmap(bitmap)) using (var data = image.Encode(SKEncodedImageFormat.Jpeg, 90)) using (var stream = File.Create(source)) data.SaveTo(stream);
    string target = project.FilePath(front.File); byte[] before = Hash(target);
    bool rejected = false; try { project.Replace(front, source); } catch (InvalidDataException) { rejected = true; }
    Check(rejected && Hash(target).SequenceEqual(before), "Non-PNG replacement is rejected without damaging artwork");
});
Case("Remove and restore head options", () => {
    var visibility = typeof(HeadProject).Assembly.GetType("CampaignCreator.HeadOptionVisibility");
    Check(visibility != null, "Head option visibility can be changed without deleting a project");
    if (visibility == null) return;
    string skin = "Faces/CustomHeads/" + project.Name;
    var gameDataType = typeof(HeadProject).Assembly.GetType("CampaignCreator.GameData")!;
    object Data() => Activator.CreateInstance(gameDataType, [output])!;
    string[] Choices(object data) => ((IEnumerable<string>)gameDataType.GetMethod("HeadChoices")!.Invoke(data, [""])!).ToArray();
    void Visible(bool show) => visibility.GetMethod("SetVisible")!.Invoke(null, [output, skin, show]);
    bool IsVisible() => (bool)visibility.GetMethod("IsVisible")!.Invoke(null, [output, skin])!;
    var before = Directory.GetFiles(project.Folder, "*", SearchOption.AllDirectories).ToDictionary(f => f, Hash);
    Check(Choices(Data()).Contains(skin), "Custom head is initially offered in the picker");
    Visible(false);
    Check(!IsVisible() && !Choices(Data()).Contains(skin), "Removed head stays absent after the picker catalog is reloaded");
    var data = Data();
    var assets = (AssetCatalog)gameDataType.GetProperty("Assets")!.GetValue(data)!;
    Check(assets.Resolve("skater", "Face", skin) == skin && assets.Resolve("skater", "Face", skin + "_Helmet") == skin + "_Helmet",
        "Existing normal and fitted head assignments still resolve");
    Check(gameDataType.GetMethod("HeadPreview")!.Invoke(data, ["skater", skin]) is string,
        "Existing saved head keeps its portrait preview");
    var fieldType = typeof(HeadProject).Assembly.GetType("CampaignCreator.FieldControls")!;
    var create = fieldType.GetMethod("Create", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
    var field = create.Invoke(null, ["Face", skin, "skater", data, (Action)(() => { }), (Action<string>)(_ => { }), false, false, false])!;
    var read = (Func<string>)field.GetType().GetField("Item2")!.GetValue(field)!;
    Check(read() == skin, "Opening an existing player does not clear its removed head assignment");
    var pickerOwner = new Window();
    try
    {
        var control = (Control)field.GetType().GetField("Item1")!.GetValue(field)!;
        pickerOwner.Content = control; pickerOwner.Show(); Dispatcher.UIThread.RunJobs();
        var open = control.GetVisualDescendants().OfType<Button>().Single();
        open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
        var dialog = pickerOwner.OwnedWindows.Single();
        var replacement = dialog.GetVisualDescendants().OfType<Button>().First(b => ToolTip.GetTip(b) is string label && label != project.Name && label != "Nothing selected");
        replacement.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
        Check(read().Length > 0 && read() != skin, "Player can change away from a removed head");
        open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
        dialog = pickerOwner.OwnedWindows.Single();
        Check(!dialog.GetVisualDescendants().OfType<Button>().Any(b => ToolTip.GetTip(b) as string == project.Name),
            "Removed head disappears from subsequent choices after changing away");
        dialog.Close(); Dispatcher.UIThread.RunJobs();
    }
    finally { foreach (var dialog in pickerOwner.OwnedWindows.ToArray()) dialog.Close(); pickerOwner.Close(); }
    Check(CustomHeadCatalog.Load(output).Any(h => h.Skin == skin), "Runtime catalog retains removed head assets for existing players");
    Check(before.All(f => Hash(f.Key).SequenceEqual(f.Value)), "Removing a head changes none of its project, image or source files");
    project.Install(output);
    Check(IsVisible() && Choices(Data()).Contains(skin), "Add to player head options restores the head");
    Visible(false);
    string external = project.Install(Path.Combine(output, "external"));
    HeadProject.Open(external).Install(output);
    Check(IsVisible() && Choices(Data()).Contains(skin), "Installing an edited external project restores its head option");
    bool rejected = false; try { visibility.GetMethod("SetVisible")!.Invoke(null, [output, HeadProject.BasicStarterSkin, false]); }
    catch (TargetInvocationException ex) when (ex.InnerException is ArgumentException) { rejected = true; }
    Check(rejected, "Native game heads cannot be removed by the custom-head action");
});
Case("Head Editor removal action", () => {
    var owner = new Window { Width = 1100, Height = 850 };
    try
    {
        var editor = new HeadEditorView(owner, output, () => { }, project); owner.Content = editor; owner.Show(); Dispatcher.UIThread.RunJobs();
        var remove = editor.GetVisualDescendants().OfType<Button>().SingleOrDefault(b => b.Name == "RemoveCustomHead");
        Check(remove != null, "Head Editor exposes Remove from head options");
        if (remove != null)
        {
            var before = Directory.GetFiles(project.Folder, "*", SearchOption.AllDirectories).ToDictionary(f => f, Hash);
            remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            var visibility = typeof(HeadProject).Assembly.GetType("CampaignCreator.HeadOptionVisibility")!;
            Check(!(bool)visibility.GetMethod("IsVisible")!.Invoke(null, [output, "Faces/CustomHeads/" + project.Name])!, "Remove button hides the head in its content library");
            Check(before.All(f => Hash(f.Key).SequenceEqual(f.Value)), "Remove button leaves all project files intact");
            Check(editor.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "InstallCustomHead"), "Removed project stays open with its Add action available");
            editor.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "InstallCustomHead").BringIntoView(); Dispatcher.UIThread.RunJobs();
            using var frame = owner.CaptureRenderedFrame(); frame!.Save(Path.Combine(output, "head-editor.png"));
        }
    }
    finally { owner.Close(); }
});
Case("Distinct blank starter views", () => {
    var art = typeof(HeadProject).Assembly.GetType("CampaignCreator.HeadStarterArt")!;
    var plain = art.GetMethod("PlainHead", BindingFlags.Static | BindingFlags.NonPublic)!;
    using var native = new SKBitmap(512, 512, SKColorType.Rgba8888, SKAlphaType.Premul); native.Erase(SKColors.Transparent);
    using (var canvas = new SKCanvas(native)) { using var paint = new SKPaint { Color = SKColors.Gray }; canvas.DrawRect(76, 56, 360, 400, paint); }
    byte[] original = native.GetPixelSpan().ToArray();
    var hashes = new Dictionary<string, byte[]>();
    using var sheet = new SKBitmap(1280, 320); using var draw = new SKCanvas(sheet); draw.Clear(new SKColor(35, 43, 55));
    using var text = new SKPaint { Color = SKColors.White, TextSize = 20, IsAntialias = true };
    string[] directions = ["S", "N", "W", "SW", "NE"];
    for (int n = 0; n < directions.Length; n++)
    {
        string direction = directions[n]; using var image = (SKBitmap)plain.Invoke(null, [native, direction])!;
        hashes[direction] = SHA256.HashData(image.GetPixelSpan());
        var bounds = Bounds(image);
        Check(image.Width == 512 && image.Height == 512 && bounds.Left >= 76 && bounds.Top >= 56 && bounds.Right <= 436 && bounds.Bottom <= 456,
            "Starter preserves its transparent native footprint: " + direction);
        draw.DrawBitmap(image, new SKRect(n * 256, 35, n * 256 + 256, 291));
        draw.DrawText(HeadProject.DirectionLabel(direction), n * 256 + 20, 26, text);
    }
    Save(sheet, Path.Combine(output, "starter-directions.png"));
    Check(hashes.Values.Select(Convert.ToHexString).Distinct().Count() == 5, "Front, back, profile and both diagonal starters have distinct anatomy");
    using var left = (SKBitmap)plain.Invoke(null, [native, "W"])!; using var right = (SKBitmap)plain.Invoke(null, [native, "E"])!;
    Check(!left.Pixels.SequenceEqual(right.Pixels), "Left and right profile starters face opposite directions");
    Check(native.GetPixelSpan().SequenceEqual(original), "Starter generation preserves the native reference image");
});
Console.WriteLine($"{checks - failures}/{checks} head workflow checks passed.");
return failures == 0 ? 0 : 1;
