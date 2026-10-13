using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CampaignCreator;

// Home screen "Report a bug": opens GitHub's New issue page pre-filled. Nothing is sent from
// the Studio; the reporter reviews the text and submits it with their own GitHub account.
internal static class BugReport
{
    internal const string Repository = "https://github.com/Yeastmans/Tape-To-Tape-custom-Campaign-Framework-BepinEX-Mod";
    internal const string Discord = "https://discord.gg/hscxnYghb";
    // GitHub rejects very long new-issue URLs; the log excerpt is trimmed to stay well below that.
    private const int MaxEncodedBody = 6000;

    internal static string GameFolder(string root)
    {
        string file = Path.Combine(root, "game-folder.txt");
        try { return File.Exists(file) ? File.ReadAllText(file).Trim() : ""; } catch (IOException) { return ""; }
    }
    internal static string GameLog(string root) => GameFolder(root) is { Length: > 0 } game ? Path.Combine(game, "BepInEx", "LogOutput.log") : "";
    internal static string PlayerLog => Path.Combine(GameSavePaths.Root, "Player.log");

    internal static string Environment(string root)
    {
        var text = new StringBuilder();
        string active = "";
        try { active = new CampaignCore.CampaignDocument(File.ReadAllText(Path.Combine(root, "active.txt"))).Get("Active Campaign"); } catch (Exception) { }
        text.AppendLine("- Active campaign: " + (active.Length > 0 ? active : "unknown"));
        string plugin = GameFolder(root) is { Length: > 0 } game ? Path.Combine(game, "BepInEx", "plugins", "CustomCampaignFramework.dll") : "";
        text.AppendLine("- Plugin DLL: " + (File.Exists(plugin) ? File.GetLastWriteTime(plugin).ToString("yyyy-MM-dd HH:mm") : "not found"));
        string studio = typeof(BugReport).Assembly.Location;
        text.AppendLine("- Studio build: " + (File.Exists(studio) ? File.GetLastWriteTime(studio).ToString("yyyy-MM-dd HH:mm") : "unknown"));
        text.AppendLine("- OS: " + RuntimeInformation.OSDescription);
        return text.ToString();
    }

    // Warnings and errors from the end of the plugin log, with the user profile path hidden.
    internal static string RecentLogProblems(string root, int maxLines = 25)
    {
        string log = GameLog(root);
        if (log.Length == 0 || !File.Exists(log)) return "";
        string[] lines;
        try
        {
            using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            lines = reader.ReadToEnd().Split('\n');
        }
        catch (IOException) { return ""; }
        string profile = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        var problems = lines.Select(l => l.TrimEnd('\r'))
            .Where(l => l.StartsWith("[Error", StringComparison.Ordinal) || l.StartsWith("[Warning:Custom", StringComparison.Ordinal) || l.StartsWith("[Fatal", StringComparison.Ordinal))
            .TakeLast(maxLines)
            .Select(l => (profile.Length > 0 ? l.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase) : l) is var s && s.Length > 300 ? s[..300] + "…" : s)
            .ToList();
        return string.Join("\n", problems);
    }

    internal static string Body(string happened, string steps, string environment, string log)
    {
        var body = new StringBuilder();
        body.AppendLine("### What happened").AppendLine(happened.Trim().Length > 0 ? happened.Trim() : "_(not filled in)_").AppendLine();
        body.AppendLine("### Steps to reproduce").AppendLine(steps.Trim().Length > 0 ? steps.Trim() : "_(not filled in)_").AppendLine();
        if (environment.Length > 0) body.AppendLine("### Setup").Append(environment).AppendLine();
        if (log.Length > 0) body.AppendLine("### Recent plugin log warnings/errors").AppendLine("```").AppendLine(log).AppendLine("```").AppendLine();
        body.AppendLine("_Full logs: drag `BepInEx/LogOutput.log` and `Player.log` onto this issue to attach them._");
        return body.ToString();
    }

    internal static string Url(string title, string happened, string steps, string environment, string log)
    {
        // Drop log lines from the top until the encoded body fits.
        var logLines = log.Length > 0 ? log.Split('\n').ToList() : new List<string>();
        string body = Body(happened, steps, environment, string.Join("\n", logLines));
        while (Uri.EscapeDataString(body).Length > MaxEncodedBody && logLines.Count > 0)
        {
            logLines.RemoveAt(0);
            body = Body(happened, steps, environment, string.Join("\n", logLines));
        }
        return Repository + "/issues/new?title=" + Uri.EscapeDataString(title.Trim()) + "&body=" + Uri.EscapeDataString(body);
    }

    internal static async Task Show(Window owner, string root)
    {
        var dialog = new Window { Title = "Report a bug", Width = 640, Height = 640, MinWidth = 480, MinHeight = 520,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = StudioTheme.Surface, Name = "BugReportDialog" };
        TextBox Box(string name, string watermark, bool multiline) => new() { Name = name, Watermark = watermark, AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, MinHeight = multiline ? 96 : 0, MaxHeight = multiline ? 160 : double.PositiveInfinity };
        TextBlock Label(string text) => new() { Text = text, FontWeight = FontWeight.SemiBold, Foreground = StudioTheme.Muted };
        var title = Box("BugTitle", "Short summary, e.g. \"Conceding freezes the run summary\"", false);
        var happened = Box("BugHappened", "What went wrong, and what you expected instead", true);
        var steps = Box("BugSteps", "1. Open …\n2. Click …\n3. …", true);
        var include = new CheckBox { Name = "BugIncludeInfo", IsChecked = true,
            Content = new TextBlock { Text = "Include setup details and recent warnings/errors from the game's plugin log", TextWrapping = TextWrapping.Wrap } };
        var feedback = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = StudioTheme.Muted };
        var logs = new Button { Content = "Open log folders", Padding = StudioTheme.ButtonPadding };
        logs.Click += (_, _) =>
        {
            var opened = new List<string>();
            foreach (string file in new[] { GameLog(root), PlayerLog })
                if (file.Length > 0 && File.Exists(file))
                {
                    try { Process.Start(new ProcessStartInfo(Path.GetDirectoryName(file)!) { UseShellExecute = true }); opened.Add(Path.GetFileName(file)); }
                    catch (Exception ex) { feedback.Text = ex.Message; return; }
                }
            feedback.Text = opened.Count == 0 ? "No game logs found yet. Run the game once with the mod installed."
                : "Opened the folders holding " + string.Join(" and ", opened) + ". Drag them onto the GitHub issue to attach them.";
        };
        var cancel = new Button { Content = "Cancel", Padding = StudioTheme.ButtonPadding };
        cancel.Click += (_, _) => dialog.Close();
        var send = new Button { Name = "BugOpenGitHub", Content = "Open on GitHub", Padding = StudioTheme.ButtonPadding, IsEnabled = false };
        StudioTheme.Primary(send);
        title.TextChanged += (_, _) => send.IsEnabled = (title.Text ?? "").Trim().Length > 0;
        send.Click += (_, _) =>
        {
            bool details = include.IsChecked == true;
            string url = Url(title.Text ?? "", happened.Text ?? "", steps.Text ?? "", details ? Environment(root) : "", details ? RecentLogProblems(root) : "");
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                dialog.Close();
            }
            catch (Exception ex) { feedback.Text = "Could not open the browser: " + ex.Message; }
        };
        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = StudioTheme.SpaceSm };
        buttons.Children.Add(logs);
        Grid.SetColumn(cancel, 2); buttons.Children.Add(cancel);
        Grid.SetColumn(send, 3); buttons.Children.Add(send);
        dialog.Content = new ScrollViewer { Content = new StackPanel { Margin = new Thickness(20), Spacing = StudioTheme.SpaceSm, Children = {
            new TextBlock { Text = "Report a bug", FontSize = 22, FontWeight = FontWeight.Bold },
            new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = StudioTheme.Muted,
                Text = "This opens the mod's GitHub page with a new issue filled in. Nothing is sent until you review it there and press Submit (a free GitHub account is needed)." },
            Label("TITLE"), title,
            Label("WHAT HAPPENED"), happened,
            Label("STEPS TO REPRODUCE"), steps,
            include, feedback, buttons } } };
        await dialog.ShowDialog(owner);
    }
}
