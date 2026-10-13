namespace CampaignCreator;

internal static class GameSavePaths
{
    public static string Root
    {
        get
        {
            string? custom = Environment.GetEnvironmentVariable("T2T_GAME_SAVE_PATH");
            if (!string.IsNullOrWhiteSpace(custom)) return Path.GetFullPath(custom);
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (OperatingSystem.IsWindows())
                return Path.Combine(home, "AppData", "LocalLow", "Excellent Rectangle", "Tape to Tape");
            if (OperatingSystem.IsMacOS())
            {
                string support = Path.Combine(home, "Library", "Application Support");
                string legacy = Path.Combine(support, "Excellent Rectangle", "Tape to Tape");
                return Directory.Exists(legacy) ? legacy : Path.Combine(support, "unity.Excellent Rectangle.Tape to Tape");
            }
            string config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ?? Path.Combine(home, ".config");
            return Path.Combine(config, "unity3d", "Excellent Rectangle", "Tape to Tape");
        }
    }
}
