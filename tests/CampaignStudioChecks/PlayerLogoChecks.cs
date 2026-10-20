using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CampaignCore;
using CampaignCreator;

internal static class PlayerLogoChecks
{
    internal static void Run(string output)
    {
        string root = Path.Combine(Path.GetFullPath(output), Guid.NewGuid().ToString("N"));
        string folder = Path.Combine(root, "campaigns", "Test", "teams", "Test");
        Directory.CreateDirectory(Path.Combine(folder, "players"));
        File.WriteAllText(Path.Combine(root, "campaigns", "Test", "campaign.txt"), "Maps = Basic\n");
        string teamFile = Path.Combine(folder, "team.txt");
        string team = "Team Name = Test\nLogo From = Winnipeg\nBody = Body/Customization/Customization_colors\n";
        File.WriteAllText(teamFile, team);
        var independent = new CampaignDocument("Override Team Settings = yes\nLogo From = personal\n");
        var kitTeam = new CampaignDocument(team);
        kitTeam.Set("Jersey Logo From", "home-kit.png"); kitTeam.Set("Jersey Logo Away From", "none");
        if (JerseyLogoOptions.Resolve(independent, kitTeam, false).Source != "personal"
            || JerseyLogoOptions.Resolve(independent, kitTeam, true).Source != "personal")
            throw new Exception("Independent personal shared logo must precede both team kit choices");
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var window = new MainWindow(["--content", root]); window.Show(); Dispatcher.UIThread.RunJobs();
        int count = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); count++; }
        foreach (string role in new[] { "skater", "goalie" })
        {
            string body = role == "goalie" ? "Skin = Body/Figure_Skaters" : "Body = Body/HockeyFC/HockeyFC";
            string file = Path.Combine(folder, "players", role + ".txt");
            var player = AppearanceContract.Resolve(new CampaignDocument("Name = Logo Test " + role + "\nPosition = " + (role == "goalie" ? "Goalie" : "Center") + "\nNumber = 7\nSize = Medium\nOverride Team Settings = yes\nFace = Faces/Toronto/Mathieu\n"), new CampaignDocument(""), new CampaignDocument(""), role);
            foreach (string stat in role == "goalie" ? new[] { "Skill", "Catching", "Glove", "Blocker", "Five Hole", "Standing Speed", "Butterfly Speed", "Control", "Recovery", "Pass Power", "Shot Power", "Poke Check", "Depth" } : new[] { "Speed", "Shot Power", "Accuracy", "Checking" }) player.Set(stat, "50");
            player.Set(role == "goalie" ? "Skin" : "Body", role == "goalie" ? "Body/Figure_Skaters" : "Body/HockeyFC/HockeyFC");
            player.Set("Logo From", "Winnipeg"); player.Set("Logo Skin", "Team_Logo/Custom_R"); player.Save(file);
            typeof(MainWindow).GetMethod("OpenFile", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [file]); Dispatcher.UIThread.RunJobs();
            var view = window.GetVisualDescendants().Single(v => v.GetType().Name == "PlayerEditorView");
            var effective = (CampaignDocument)view.GetType().GetMethod("Effective", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, null)!;
            Check(effective.Get("Logo From") == "Winnipeg" && effective.Get("Jersey Logo From") == "",
                role + " incompatible Home jersey clears its resolved crest while retaining shared identity; Body=" + effective.Get("Body") + ";Skin=" + effective.Get("Skin"));
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == "Save changes").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            var saved = new CampaignDocument(File.ReadAllText(file));
            Check(saved.Get("Logo From") == "" && saved.Get("Logo Skin") == "", role + " saved player logo cleared: " + ((TextBlock)typeof(MainWindow).GetField("status", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!).Text);
            Check(new CampaignDocument(File.ReadAllText(teamFile)).Get("Logo From") == "Winnipeg", role + " team logo retained");
        }
        File.WriteAllText(Path.Combine(root, "_game_team_logos.txt"), "Winnipeg\nHockey FC\n");
        foreach (string role in new[] { "skater", "goalie" })
        {
            string home = role == "goalie" ? "Skin" : "Body", custom = AppearanceContract.DefaultSkin(role, home);
            string file = Path.Combine(folder, "players", role + "-picker.txt");
            var player = new CampaignDocument(File.ReadAllText(Path.Combine(folder, "players", role + ".txt")));
            player.Set("Name", "Logo Picker " + role); player.Set(home, custom); player.Set(home + " Away", custom);
            player.Set("Logo From", "Winnipeg"); player.Set("Logo Skin", ""); player.Save(file);
            typeof(MainWindow).GetMethod("OpenFile", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [file]); Dispatcher.UIThread.RunJobs();
            var view = window.GetVisualDescendants().Single(v => v.GetType().Name == "PlayerEditorView");
            var slot = view.GetVisualDescendants().OfType<ContentControl>().Single(c => c.Name == "PlayerLogo");
            IEnumerable<string> Texts() => slot.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "");
            bool Has(string label) => slot.GetVisualDescendants().OfType<Button>().Any(b => b.Content?.ToString() == label);
            Check(Has("Choose logo") && Texts().Contains("Winnipeg") && Has("Use team logo"), role + " team-colour jersey offers the player logo picker");
            slot.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == "Choose logo").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            var dialog = window.OwnedWindows.Single(w => w.Title == "Choose player logo");
            Check(dialog.Title == "Choose player logo", role + " dialog is titled for a player");
            dialog.GetVisualDescendants().OfType<TabControl>().Single().SelectedIndex = 0; Dispatcher.UIThread.RunJobs();
            var list = dialog.GetVisualDescendants().OfType<ListBox>().Single();
            list.SelectedItem = list.Items.Cast<object>().Single(i => i.ToString() == "Hockey FC"); Dispatcher.UIThread.RunJobs();
            dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == "Use logo").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            var effective = (CampaignDocument)view.GetType().GetMethod("Effective", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, null)!;
            Check(effective.Get("Logo From") == "Hockey FC" && Texts().Contains("Hockey FC"), role + " chosen player logo applies: " + effective.Get("Logo From"));
            // A premade jersey on either kit switches the control off and clears the player logo.
            var jersey = view.GetVisualDescendants().OfType<StackPanel>().First(p => p.Children.FirstOrDefault() is TextBlock { Text: "JERSEY" or "GOALIE JERSEY" });
            var mode = jersey.GetVisualDescendants().OfType<ComboBox>().First();
            mode.SelectedItem = mode.Items.Cast<ComboBoxItem>().Single(i => i.Content?.ToString() == "Premade"); Dispatcher.UIThread.RunJobs();
            Check(!Has("Choose logo") && Texts().Any(t => t.StartsWith("Premade jersey")), role + " premade jersey turns the player logo off");
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == "Save changes").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            var saved = new CampaignDocument(File.ReadAllText(file));
            Check(saved.Get(home) != custom && saved.Get("Logo From") == "" && saved.Get("Logo Skin") == "", role + " premade save drops the player logo; " + home + "=" + saved.Get(home));
            Check(new CampaignDocument(File.ReadAllText(teamFile)).Get("Logo From") == "Winnipeg", role + " team logo untouched by player logo");
            view.GetVisualDescendants().OfType<ToggleSwitch>().Single(t => t.Name == "InheritFromTeam").IsChecked = true; Dispatcher.UIThread.RunJobs();
            Check(!Has("Choose logo") && Texts().Any(t => t.StartsWith("Team equipment is on")), role + " team look hides the player logo picker");
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == "Save changes").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
        }
        window.Close(); Console.WriteLine($"PASS {count} player-only logo editor checks");
    }
}
