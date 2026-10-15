using System;
using System.IO;
using System.Linq;

namespace CampaignCore;

public static class RosterPosition
{
    // Saved roster choices take precedence over legacy position-based filenames.
    public static string Read(string file, string playersDirectory, CampaignDocument document)
    {
        string position = document.Get("Position").Trim();
        if (position.Length > 0) return position;
        position = Path.GetFileNameWithoutExtension(file).Split(new[] { " - " }, StringSplitOptions.None)[0].Trim();
        var folders = Path.GetRelativePath(playersDirectory, file).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (folders.Take(folders.Length - 1).Any(p => p.Equals("second-line", StringComparison.OrdinalIgnoreCase))
            && !position.StartsWith("Line 2 ", StringComparison.OrdinalIgnoreCase)) position = "Line 2 " + position;
        return position;
    }
}
