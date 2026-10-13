using System;
using System.Security.Cryptography;
using System.Text;

namespace CampaignCore;

public static class CustomTeamIdentity
{
    public const string Prefix = "CCF_Team_";

    // Configuration identity, never the donor's ID, display name or node position.
    public static string Create(string campaign, string teamKey)
    {
        if (string.IsNullOrWhiteSpace(campaign) || string.IsNullOrWhiteSpace(teamKey))
            throw new ArgumentException("Campaign and team key are required.");
        string Normalize(string value) => value.Trim().Replace('\\', '/').ToLowerInvariant();
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(Normalize(campaign) + "\n" + Normalize(teamKey)));
        return Prefix + BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    public static bool IsCustom(string id) => id != null && id.StartsWith(Prefix, StringComparison.Ordinal);
    public static bool IsFinalBoss(bool boss, bool terminal, int mapIndex, int totalMaps) =>
        boss && terminal && totalMaps > 0 && mapIndex == totalMaps;
}
