using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace CampaignCore;

/// <summary>Mod counters only; the native game remains responsible for the run save.</summary>
public sealed class CampaignProgress
{
    public int ActsCompleted { get; }
    public int GamesPlayed { get; }
    public int MapStartGamesPlayed { get; }

    public CampaignProgress(int actsCompleted, int gamesPlayed, int mapStartGamesPlayed)
    {
        if (actsCompleted < 0 || gamesPlayed < 0 || mapStartGamesPlayed < 0 || mapStartGamesPlayed > gamesPlayed)
            throw new ArgumentOutOfRangeException(nameof(mapStartGamesPlayed), "Invalid campaign counters.");
        ActsCompleted = actsCompleted;
        GamesPlayed = gamesPlayed;
        MapStartGamesPlayed = mapStartGamesPlayed;
    }

    public static CampaignProgress Parse(string text)
    {
        var parts = text.Trim().Split(',');
        if (parts.Length != 2 && parts.Length != 3) throw new FormatException("Expected two or three campaign counters.");
        int Read(int i)
        {
            if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value < 0)
                throw new FormatException("Invalid campaign counter at position " + i);
            return value;
        }
        int acts = Read(0), games = Read(1);
        // Older versions stored only the first two counters.
        int start = parts.Length == 3 ? Read(2) : games;
        if (start > games) throw new FormatException("Map start exceeds the match count.");
        return new CampaignProgress(acts, games, start);
    }

    public static CampaignProgress Load(string path, out bool recovered)
    {
        recovered = false;
        if (!File.Exists(path) && !File.Exists(path + ".bak")) return new CampaignProgress(0, 0, 0);
        try { return Parse(File.ReadAllText(path)); }
        catch (Exception ex) when (ex is IOException || ex is FormatException || ex is UnauthorizedAccessException)
        {
            if (!File.Exists(path + ".bak")) throw;
            var backup = Parse(File.ReadAllText(path + ".bak"));
            recovered = true;
            return backup;
        }
    }

    public void Save(string path)
    {
        string folder = Path.GetDirectoryName(Path.GetFullPath(path));
        Directory.CreateDirectory(folder);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(string.Join(",", ActsCompleted.ToString(CultureInfo.InvariantCulture),
                GamesPlayed.ToString(CultureInfo.InvariantCulture), MapStartGamesPlayed.ToString(CultureInfo.InvariantCulture)));
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            if (File.Exists(path))
            {
                // Never rotate a damaged primary over a usable recovery copy.
                bool valid = false;
                try { Parse(File.ReadAllText(path)); valid = true; }
                catch (FormatException) { }
                File.Replace(temp, path, valid ? path + ".bak" : null);
            }
            else File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
