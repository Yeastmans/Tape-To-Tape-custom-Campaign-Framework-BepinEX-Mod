using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace CampaignCreator;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if(args.Length>0 && args[0]=="--update-mod-compatibility")
        {
            try
            {
                if(args.Length!=3) throw new ArgumentException("Expected game folder and result JSON path.");
                var result=ModCompatibility.CompatibilityUpdater.Ensure(Path.GetFullPath(args[1]),Path.Combine(AppContext.BaseDirectory,"mod-runtime"));
                File.WriteAllText(Path.GetFullPath(args[2]),System.Text.Json.JsonSerializer.Serialize(result));
                return result.State is "Current" or "Updated" ? 0 : 1;
            }
            catch(Exception ex) { Console.Error.WriteLine(ex.Message);return 1; }
        }
        if (args.Length > 0 && args[0] == "--clean-upgrade-content")
        {
            try
            {
                string root = args.Length > 1 ? Path.GetFullPath(args[1]) : CampaignCore.UserContentPaths.EnsureRoot();
                int count = new UpgradeCleanup().CleanDirectory(root);
                File.WriteAllText(Path.Combine(root, "upgrade-cleanup.log"), $"Cleaned {count} content files. No backups retained.\n");
                return 0;
            }
            catch (Exception ex)
            {
                string logRoot = args.Length > 1 ? args[1] : CampaignCore.UserContentPaths.Root;
                try { File.WriteAllText(Path.Combine(logRoot, "upgrade-cleanup-error.log"), ex.ToString()); } catch { }
                return 1;
            }
        }
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
public sealed class App : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
        Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://CampaignCreator/")) { Source = new Uri("avares://Avalonia.Controls.ColorPicker/Themes/Fluent/Fluent.xaml") });
        StudioTheme.Install(this);
        PageHelpButton.Install();
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainWindow.BuildGameArtOnStart = true;
            MainWindow.UpdateModCompatibilityOnStart = true;
            desktop.MainWindow = new MainWindow(desktop.Args ?? Array.Empty<string>());
        }
        base.OnFrameworkInitializationCompleted();
    }
}


