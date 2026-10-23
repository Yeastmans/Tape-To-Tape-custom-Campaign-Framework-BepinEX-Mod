using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CampaignCreator;
using CampaignCore;

static class BicepsChecks
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
        var type = typeof(App).Assembly.GetType("CampaignCreator.EquipmentStylePicker")!;
        object data = Activator.CreateInstance(typeof(App).Assembly.GetType("CampaignCreator.GameData")!, Directory.GetCurrentDirectory())!;
        var assets = (AssetCatalog)data.GetType().GetProperty("Assets")!.GetValue(data)!;
        var bodies = assets.Values("skater", "Body");
        var sleeves = assets.Values("skater", "Bicep");
        string body = "Body/Customization/Customization_colors", sleeve = "";
        int writes = 0;
        var picker = (StackPanel)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null,
            [bodies, (Func<string>)(() => body), (Action<string>)(v => body = v), new Button(), true, "Custom color", false, null, false, false, null, null, null, null], null)!;
        type.GetMethod("AddSleeves", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(picker,
            [sleeves, (Func<string>)(() => sleeve), (Action<string>)(v => { sleeve = v; writes++; })]);
        var panel = (StackPanel)picker.Children.Last();
        var selector = panel.Children.OfType<ComboBox>().Single();
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS " + name); }
        void Refresh() => type.GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(picker, null);
        Check(!panel.IsVisible, "Custom jersey hides biceps");
        body = bodies.First(s => s.EndsWith("/Golfers")); Refresh();
        Check(panel.IsVisible, "Premade jersey shows biceps");
        var choices = selector.Items.OfType<string>().ToArray();
        Check(choices.Length > 1 && choices.All(s => sleeves.Contains(s)), "Choices come from base-game biceps catalog");
        selector.SelectedItem = choices[0];
        Check(sleeve == choices[0] && writes == 1, "Sleeve selection writes independently");
        Refresh(); Check(writes == 1, "Refreshing does not overwrite sleeve selection");
        body = ""; Refresh(); Check(!panel.IsVisible, "Empty jersey hides biceps");
        body = "Body/Customization/Customization_colors"; Refresh();
        Check(!panel.IsVisible && sleeve == choices[0], "Hiding control preserves sleeve value");
    }
}
