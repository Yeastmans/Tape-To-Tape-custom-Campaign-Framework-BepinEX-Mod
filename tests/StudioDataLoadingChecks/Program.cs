using System.Collections;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CampaignCore;
using CampaignCreator;

string root = Path.Combine(Path.GetTempPath(), "t2t-data-loading-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var gameType = typeof(App).Assembly.GetType("CampaignCreator.GameData", true)!;
object NewData() => Activator.CreateInstance(gameType, root)!;
T Property<T>(object data, string name) => (T)gameType.GetProperty(name)!.GetValue(data)!;
int failures = 0;
void Test(string name, Func<bool> check)
{
    try { if (!check()) throw new InvalidOperationException("Unexpected result"); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + " — " + ex.GetBaseException().Message); }
}
try {
    typeof(App).Assembly.GetType("CampaignCreator.StudioArt")!.GetMethod("Use")!.Invoke(null, [null]);
    string skins = Path.Combine(root, "_game_skins.txt"), templates = Path.Combine(root, "_squad_templates.tsv");
    File.WriteAllText(skins, "[Body]\nBody/Mid_Cheese\n");
    File.WriteAllText(templates, "some-id\tTest lineup\tmaps\tcolor\n");
    using (var busySkins = File.Open(skins, FileMode.Open, FileAccess.Read, FileShare.None)) {
        Test("Opening the content root does not read appearance files", () => NewData() != null);
        Test("Opening the template catalog needs only the template data", () => Property<List<string>>(NewData(), "Templates").Contains("Test lineup"));
    }
    using (var busyTemplates = File.Open(templates, FileMode.Open, FileAccess.Read, FileShare.None)) {
        Test("Reading sizes or native abilities does not read templates", () =>
            Property<List<string>>(NewData(), "Sizes").Count > 0 && Property<IDictionary>(NewData(), "Abilities").Count > 0);
    }
    Test("The first appearance request still resolves the local skin", () =>
        Property<AssetCatalog>(NewData(), "Assets").Resolve("skater", "Body", "Mid_Cheese") == "Body/Mid_Cheese");
    Test("A catalog obtained before the data file is created reads it at first use", () => {
        var data = NewData();
        File.WriteAllText(templates, "different\tLate lineup\tmaps\tcolor\n");
        return Property<List<string>>(data, "Templates").SequenceEqual(["Late lineup"]);
    });
    string library = Path.Combine(root, "library", "rewards");
    var reward = new CustomRewardDefinition { Name = "Demand test", IconPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jWI8AAAAASUVORK5CYII=" };
    reward.Save(library);
    string rewardFile = Path.Combine(library, reward.Id + CustomRewardDefinition.Extension);
    Test("An unopened custom-reward catalog can be read after its file becomes available", () => {
        object data;
        using (File.Open(rewardFile, FileMode.Open, FileAccess.Read, FileShare.None)) {
            data = NewData();
            if (Property<IDictionary>(data, "Abilities").Count == 0) return false;
        }
        return Property<IDictionary>(data, "CustomRewards").Contains(reward.Id);
    });
    Test("The head picker and portrait read only needed artwork; selecting validates and can retry", () => {
        string folder = Path.Combine(root, "custom-heads", "Imported folder");
        Directory.CreateDirectory(folder);
        var head = new CustomHeadDefinition { Version = 1, Name = "Demand test head" };
        using var bitmap = new SkiaSharp.SKBitmap(512, 512);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        foreach (string direction in new[] { "S", "N", "W", "NE", "SW", "portrait" }) {
            string name = direction + ".png";
            head.Images.Add(new CustomHeadImage { Group = direction == "portrait" ? "Portrait" : "Head",
                File = name, Width = 512, Height = 512, NativeWidth = 512, NativeHeight = 512, ContentWidth = 512, ContentHeight = 512 });
            File.WriteAllBytes(Path.Combine(folder, name), png.ToArray());
        }
        File.WriteAllText(Path.Combine(folder, "head-project.json"), System.Text.Json.JsonSerializer.Serialize(head));
        var data = NewData();
        var select = gameType.GetMethod("CanSelectHead", BindingFlags.Instance | BindingFlags.NonPublic)!;
        using (File.Open(Path.Combine(folder, "N.png"), FileMode.Open, FileAccess.Read, FileShare.None)) {
            var choices = (IEnumerable<string>)gameType.GetMethod("HeadChoices")!.Invoke(data, [""])!;
            if (!choices.Contains(head.Skin)) return false;
            if ((string?)gameType.GetMethod("HeadPreview")!.Invoke(data, ["skater", head.Skin]) != Path.Combine(folder, "portrait.png")) return false;
            if ((bool)select.Invoke(data, ["skater", head.Skin])!) return false;
        }
        return (bool)select.Invoke(data, ["skater", head.Skin])!;
    });
    Test("Artwork is requested only for visible choices, including after scrolling", () => {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var picker = typeof(App).Assembly.GetType("CampaignCreator.PreviewGridPicker", true)!;
        var choice = picker.GetNestedType("Choice", BindingFlags.NonPublic)!;
        var items = Array.CreateInstance(choice, 120);
        for (int i = 0; i < items.Length; i++) items.SetValue(Activator.CreateInstance(choice, i.ToString(), "Head " + i), i);
        var seen = new HashSet<string>();
        Func<string, IImage?> load = id => { seen.Add(id); return null; };
        var owner = new Window { Width = 1000, Height = 700 };
        owner.Show();
        try {
            var show = picker.GetMethod("Show", BindingFlags.Static | BindingFlags.NonPublic)!;
            object?[] arguments = show.GetParameters().Length == 7 ? [owner, "Choose head", items, "", load, true, null] : [owner, "Choose head", items, "", load, true];
            var task = (Task<string?>)show.Invoke(null, arguments)!;
            var dialog = owner.OwnedWindows.Single();
            void Refresh() { dialog.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
            Refresh();
            if (!seen.Contains("0") || seen.Count >= 40 || seen.Contains("119")) return false;
            var scroll = dialog.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "PreviewGridScroll");
            scroll.Offset = new Vector(0, scroll.Extent.Height);
            Refresh();
            dialog.Close(); Dispatcher.UIThread.RunJobs();
            return seen.Contains("119") && seen.Count < items.Length && task.IsCompleted;
        } finally { owner.Close(); }
    });
} finally {
    Directory.Delete(root, true);
}
return failures == 0 ? 0 : 1;
