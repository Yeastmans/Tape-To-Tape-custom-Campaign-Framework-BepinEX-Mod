using System.Reflection;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Threading;
using CampaignCore;
using CampaignCreator;

internal static class HelmetModeChecks
{
    internal static void Run(string sandbox, Action<bool, string> check)
    {
        const BindingFlags hidden = BindingFlags.NonPublic | BindingFlags.Instance;
        var assembly = typeof(App).Assembly;
        string root = Path.Combine(sandbox, "helmet-mode"), folder = Path.Combine(root, "teams", "Test"), file = Path.Combine(folder, "players", "skater.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        const string helmet = "Faces/Custom/Helmet_Colors", body = "Body/HockeyFC/HockeyFC";
        File.WriteAllText(Path.Combine(folder, "team.txt"), "Team Name = Test\nHelmet = " + helmet + "\nHelmet Away = " + helmet + "\nBody = " + body + "\n");
        var data = Activator.CreateInstance(assembly.GetType("CampaignCreator.GameData")!, BindingFlags.Public | hidden, null, [root], null)!;
        var editorType = assembly.GetType("CampaignCreator.PlayerEditorView")!;
        var readers = new Dictionary<string, Func<string>>();
        var doc = new CampaignDocument("Name = Team helmet test\nPosition = Center\nFace = Faces/Toronto/Mathieu\nOverride Team Settings = no\n");
        var owner = new Window(); Action nothing = () => { };
        Control Editor(string role = "skater") => (Control)Activator.CreateInstance(editorType, hidden, null,
            [owner, doc, data, file, role, nothing, (Action<string, Func<string>>)((key, read) => readers[key] = read), nothing, null, nothing, nothing, new TextBlock(), null], null)!;
        var editor = Editor(); owner.Content = editor; owner.Width = 1280; owner.Height = 900; owner.Show(); Dispatcher.UIThread.RunJobs();
        var choice = editor.GetVisualDescendants().OfType<ComboBox>().SingleOrDefault(c => c.Name == "PlayerTeamHelmetMode");
        check(choice != null && choice.IsEffectivelyEnabled, "Team equipment exposes an enabled skater helmet choice");
        check(choice!.Items.Cast<string>().SequenceEqual(["Team equipment", "No helmet"]), "team helmet choice offers team equipment or no helmet");
        choice.SelectedIndex = 1;
        var effective = (CampaignDocument)editorType.GetMethod("Effective", hidden)!.Invoke(editor, null)!;
        check(effective.Get("Helmet") == "none" && effective.Get("Helmet Away") == "none" && effective.Get("Body") == body,
            "No helmet removes both kit helmets while preserving the team's jersey");
        check(readers["Override Team Settings"]() == "no" && readers["Helmet"]() == "none" && readers["Helmet Away"]() == "none",
            "No helmet saves independently of the Team equipment toggle");
        foreach (var (key, read) in readers) doc.Set(key, read()); doc.Save(file);
        doc = new CampaignDocument(File.ReadAllText(file)); readers.Clear(); editor = Editor(); owner.Content = editor; Dispatcher.UIThread.RunJobs();
        choice = editor.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "PlayerTeamHelmetMode");
        check(choice.SelectedIndex == 1, "reopening the player retains No helmet with Team equipment on");
        var export = new CampaignDocument(doc.ToString());
        editorType.GetMethod("ApplyExportAppearance", hidden)!.Invoke(editor, [export]);
        check(export.Get("Helmet") == "none" && export.Get("Helmet Away") == "none" && export.Get("Body") == body,
            "Play Now appearance export preserves helmetless team equipment");
        choice.SelectedIndex = 0;
        effective = (CampaignDocument)editorType.GetMethod("Effective", hidden)!.Invoke(editor, null)!;
        check(effective.Get("Helmet") == helmet && effective.Get("Helmet Away") == helmet && readers["Override Team Settings"]() == "no",
            "switching back to Team equipment restores inherited helmets without changing other equipment mode");
        check(readers["Helmet"]() == "" && readers["Helmet Away"]() == "", "returning to team helmet clears the explicit no-helmet choice");
        readers.Clear(); editor = Editor("goalie"); owner.Content = editor; Dispatcher.UIThread.RunJobs();
        check(!editor.GetVisualDescendants().OfType<ComboBox>().Any(c => c.Name == "PlayerTeamHelmetMode"), "goalie mask controls do not gain a skater no-helmet choice");
        owner.Close();
    }
}
