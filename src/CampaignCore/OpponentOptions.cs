using System;
using System.Collections.Generic;
using System.Linq;

namespace CampaignCore;

public static class OpponentOptions
{
    // TeamData classifiers in the current game. These do not replace team IDs.
    public static readonly IReadOnlyDictionary<string, string> Presets = new Dictionary<string, string>
    {
        ["Standard"] = "", ["Hockey FC"] = "IsSoccerTeam", ["Golfers"] = "IsGolferTeam",
        ["Referees"] = "IsRefereeTeam", ["Tycoons"] = "IsTycoonsTeam", ["Prisoners"] = "IsPrisonerTeam",
        ["Cheese"] = "IsCheeseTeam", ["Calaveras"] = "IsCalaverasTeam", ["Meatballs"] = "IsMeatballsTeam",
        ["Lettuce"] = "IsLettuceTeam", ["Princess"] = "IsPrincessTeam", ["Disco"] = "IsDiscoTeam",
        ["Shooting Stars"] = "IsShootingStars", ["Crusaders"] = "IsCrusaderTeam",
        ["Cultists"] = "IsCultistTeam", ["Mountaineers"] = "IsMountaineerTeam", ["Spartans"] = "IsSpartans"
    };
    public static readonly string[] Pucks = { "Hockey puck", "Soccer ball", "Golf ball" };
    public static readonly string[] Surfaces = { "Standard ice", "Soccer field", "Golf ice", "Tycoons ice" };
    public static string Normalize(string value, IEnumerable<string> options, string fallback) =>
        options.FirstOrDefault(x => x.Equals(value?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? fallback;
    public static string Puck(string choice) => Normalize(choice, Pucks, "Hockey puck");
    public static string Surface(string choice) => Normalize(choice, Surfaces, "Standard ice");
    public static bool CanadaCursesEnabled(string choice) => !string.IsNullOrWhiteSpace(choice) &&
        (choice.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase)
            || choice.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) || choice.Trim() == "1");
    // Missing values follow the selected arena; an explicit opt-out survives reload.
    public static bool GolfFairwayEnabled(string surface, string choice) => Surface(surface) == "Golf ice" &&
        (string.IsNullOrWhiteSpace(choice) || choice.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase)
            || choice.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) || choice.Trim() == "1");
    public static bool TycoonsSponsorsEnabled(string surface, string choice) => Surface(surface) == "Tycoons ice" &&
        (string.IsNullOrWhiteSpace(choice) || choice.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase)
            || choice.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) || choice.Trim() == "1");
}
