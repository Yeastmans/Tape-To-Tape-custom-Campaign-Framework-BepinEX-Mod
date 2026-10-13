using System;
using System.IO;

namespace CampaignCore
{
    // Resolve only authored campaigns. Stale selections and progress-only folders
    // must never enable gameplay patches or recreate a deleted campaign.
    public static class CampaignSelection
    {
        public const string BaseGame = "default";

        public static string Resolve(string contentRoot, out string warning)
        {
            warning = "";
            try
            {
                string activePath = Path.Combine(contentRoot, "active.txt");
                if (!File.Exists(activePath)) return BaseGame;

                string selected = "";
                foreach (string line in File.ReadLines(activePath))
                {
                    string trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith("#")) continue;
                    selected = trimmed.Substring(trimmed.IndexOf('=') + 1).Trim();
                    break;
                }

                if (selected.Length == 0 ||
                    selected.Equals(BaseGame, StringComparison.OrdinalIgnoreCase) ||
                    selected.Equals("none", StringComparison.OrdinalIgnoreCase) ||
                    selected.Equals("off", StringComparison.OrdinalIgnoreCase) ||
                    selected.Equals("base game", StringComparison.OrdinalIgnoreCase))
                    return BaseGame;

                if (selected == "." || selected == ".." ||
                    selected.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                    selected.Contains("/") || selected.Contains("\\"))
                {
                    warning = $"Invalid campaign selection '{selected}' — using base game.";
                    return BaseGame;
                }

                string configPath = Path.Combine(contentRoot, "campaigns", selected, "campaign.txt");
                if (!File.Exists(configPath))
                {
                    warning = $"Selected campaign '{selected}' has no campaign.txt — using base game.";
                    return BaseGame;
                }

                return selected;
            }
            catch (Exception ex)
            {
                warning = $"Could not read the campaign selection — using base game: {ex.Message}";
                return BaseGame;
            }
        }
    }
}
