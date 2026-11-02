using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using CampaignCore;
using CampaignCreator;

int checks = 0, failures = 0;
void Check(bool value, string label) { checks++; Console.WriteLine((value ? "PASS " : "FAIL ") + label); if (!value) failures++; }
const string Legacy = "Faces/Anyteam/Bench_Stumple", Stu = "Faces/Anyteam/Bench_Stumple_Helmet";
const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
var assembly = typeof(App).Assembly;
string root = Path.Combine(Path.GetFullPath(args[0]), "editor-fixture-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
string cache = Path.Combine(root, "cache"), portraits = Path.Combine(cache, "head-preview");
Directory.CreateDirectory(portraits);
Environment.SetEnvironmentVariable("T2T_GAME_ART_CACHE", cache);
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
// Distinct dimensions make the fixture's old/wrong cache hit observable without
// redistributing game pixels. The real cache contains both exact skin keys too.
foreach (var (file, size) in new[] { ("placeholder.png", new PixelSize(2, 3)), ("stu.png", new PixelSize(4, 5)) })
    using (var png = new RenderTargetBitmap(size)) png.Save(Path.Combine(portraits, file));
File.WriteAllText(Path.Combine(portraits, "catalog.tsv"), $"skater\t{Legacy}\tplaceholder.png\nskater\t{Stu}\tstu.png\n");
assembly.GetType("CampaignCreator.StudioArt")!.GetMethod("Use", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [cache]);
var gameType = assembly.GetType("CampaignCreator.GameData")!;
var data = Activator.CreateInstance(gameType, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, [root], null)!;
string? preview = (string?)gameType.GetMethod("HeadPreview")!.Invoke(data, ["skater", Legacy]);
Check(preview == Path.Combine(portraits, "stu.png"), "Existing cache lookup for an old saved Stu identity selects Stu artwork");

string filePath = Path.Combine(root, "players", "Stu.txt"); Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
var doc = new CampaignDocument($"Name = Stu regression\nPosition = Center\nFace = {Legacy}\nOverride Team Settings = yes\nHelmet = none\nHelmet Away = none\n");
doc.Save(filePath);
var readers = new Dictionary<string, Func<string>>();
Action nothing = () => { };
Action<string, Func<string>> register = (key, reader) => readers[key] = reader;
var owner = new Window();
var editorType = assembly.GetType("CampaignCreator.PlayerEditorView")!;
var editor = (Control)Activator.CreateInstance(editorType, Hidden, null,
    [owner, doc, data, filePath, "skater", nothing, register, nothing, null, nothing, nothing, new TextBlock(), null], null)!;
Check(readers["Face"]() == Stu, "Opening a saved skater registers the real Stu identity for Save changes");
var head = (Image)editorType.GetField("head", Hidden)!.GetValue(editor)!;
Check(head.Source is Bitmap bitmap && bitmap.PixelSize == new PixelSize(4, 5), "Actual skater editor renders Stu's cached image rather than the placeholder");
foreach (var (key, read) in readers) doc.Set(key, read());
doc.Save(filePath);
var saved = new CampaignDocument(File.ReadAllText(filePath));
Check(saved.Get("Face") == Stu, "Saving the existing player persists Stu's correct native identity");
var exportType = assembly.GetType("CampaignCreator.PlayerExport")!;
var model = (Dictionary<string, object>)exportType.GetMethod("Convert", BindingFlags.Static | BindingFlags.NonPublic)!
    .Invoke(null, [saved, data, "skater", "stu-regression", null])!;
Check((string)model["headSkin"] == Stu, "Actual Play Now export preserves Stu's complete native head identity");
var fieldType = assembly.GetType("CampaignCreator.FieldControls")!;
string label = (string)fieldType.GetMethod("Friendly", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [Stu])!;
Check(label == "Bench Stumple", "Skater head picker retains the familiar Bench Stumple label");
Console.WriteLine($"{checks - failures}/{checks} actual Studio load/preview/save/export checks passed; images are dimension-coded test fixtures.");
return failures == 0 ? 0 : 1;
