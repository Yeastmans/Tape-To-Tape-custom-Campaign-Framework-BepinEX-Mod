using System;
using System.IO;

namespace CampaignCore
{
    // Shared by the desktop editor and plugin; never write user files to Program Files.
    public static class UserContentPaths
    {
        public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "T2T Campaign Studio");
        public static string EnsureRoot()
        {
            Directory.CreateDirectory(Path.Combine(Root, "campaigns"));
            Directory.CreateDirectory(Path.Combine(Root, "library"));
            return Root;
        }
    }
}
