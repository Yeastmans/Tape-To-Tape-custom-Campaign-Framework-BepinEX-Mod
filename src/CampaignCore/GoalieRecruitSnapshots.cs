using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CampaignCore;

// Native saves retain the recruited goalie; this stores only the supplemental
// authored fields their native model omits. Callers choose which fields to restore.
public static class GoalieRecruitSnapshots
{
    private const string RunKey = "Recruit Snapshot Run", PlayerKey = "Recruit Snapshot Player";
    private static string PathFor(string campaign, string runId, string playerId)
    {
        if (string.IsNullOrWhiteSpace(campaign) || string.IsNullOrWhiteSpace(runId) || string.IsNullOrWhiteSpace(playerId))
            throw new ArgumentException("A goalie recruit snapshot requires campaign, run and player identities.");
        using var hash = SHA256.Create();
        string key = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(runId.Length + ":" + runId + playerId))).Replace("-", "");
        return Path.Combine(campaign, "_goalie_recruits", key + ".txt");
    }

    public static void Save(string campaign, string runId, string playerId, CampaignDocument document)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        string path = PathFor(campaign, runId, playerId);
        var snapshot = new CampaignDocument(document.ToString());
        snapshot.Set(RunKey, runId); snapshot.Set(PlayerKey, playerId);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        snapshot.Save(path);
    }

    public static CampaignDocument Read(string campaign, string runId, string playerId)
    {
        string path = PathFor(campaign, runId, playerId);
        if (!File.Exists(path)) return null;
        var snapshot = new CampaignDocument(File.ReadAllText(path));
        if (snapshot.Get(RunKey) != runId || snapshot.Get(PlayerKey) != playerId)
            throw new InvalidDataException("The goalie recruit snapshot belongs to another run or player.");
        snapshot.Remove(RunKey); snapshot.Remove(PlayerKey);
        return snapshot;
    }
}
