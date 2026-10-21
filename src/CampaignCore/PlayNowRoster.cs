using System;
using System.Linq;

namespace CampaignCore;

/// <summary>Version 1 wire order: LW, C, RW, LD, RD. Native bench display is separately C, LW, RW, LD, RD.</summary>
public static class PlayNowPositions
{
    public const int LeftWing = 0, Center = 1, RightWing = 2, LeftDefense = 3, RightDefense = 4;
    public static string[] Names => new[] { "Left Wing", "Center", "Right Wing", "Left Defense", "Right Defense" };
    public static string[] NativeFields => new[] { "leftWingerId", "centerId", "rightWingerId", "leftDefensemenId", "rightDefensemenId" };
    public static T[] FromNamed<T>(T leftWing, T center, T rightWing, T leftDefense, T rightDefense)
        => new[] { leftWing, center, rightWing, leftDefense, rightDefense };
    public static bool MatchesNative(PlayNowRoster roster, Func<string, string> read)
        => NativeFields.Select(read).SequenceEqual(roster.Line1) && read("goalieId") == roster.GoalieId;
    public static T[] NativeBench<T>(T[] wire) => new[] { wire[Center], wire[LeftWing], wire[RightWing], wire[LeftDefense], wire[RightDefense] };
}

/// <summary>The two authored skater lines and active goalie in an exported Play Now team.</summary>
public sealed class PlayNowRoster
{
    public string[] Line1 { get; }
    public string[] Line2 { get; }
    public string GoalieId { get; }
    public bool StaminaEnabled { get; }

    public PlayNowRoster(string[] line1, string[] line2, string goalieId, bool staminaEnabled)
    {
        Line1 = line1;
        Line2 = line2;
        GoalieId = goalieId;
        StaminaEnabled = staminaEnabled;
    }
}

/// <summary>Versioned, mod-owned fields in the exported team sidecar.</summary>
public static class PlayNowRosterCodec
{
    public const string VersionKey = "Export Roster Version";
    public const string Line1Key = "Export Line 1";
    public const string Line2Key = "Export Line 2";
    public const string GoalieKey = "Export Goalie";
    public const string StaminaKey = "Export Line Stamina";
    private const string CurrentVersion = "1";
    private static readonly string[] Keys = { VersionKey, Line1Key, Line2Key, GoalieKey, StaminaKey };

    public static bool TryRead(CampaignDocument document, out PlayNowRoster roster, out string error)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        roster = new PlayNowRoster(Array.Empty<string>(), Array.Empty<string>(), "", false);
        error = "";
        var fields = document.Fields;
        if (!Keys.Any(fields.ContainsKey)) return false;
        string version = document.Get(VersionKey);
        if (version != CurrentVersion)
        {
            error = version.Length == 0 ? "Export roster version is missing." : $"Unknown export roster version '{version}'.";
            return false;
        }
        string stamina = document.Get(StaminaKey);
        if (stamina is not ("yes" or "no"))
        {
            error = "Export line stamina must be yes or no.";
            return false;
        }
        var candidate = new PlayNowRoster(document.Get(Line1Key).Split(','), document.Get(Line2Key).Split(','), document.Get(GoalieKey), stamina == "yes");
        if (!TryValidate(candidate, out error)) return false;
        roster = candidate;
        return true;
    }

    public static void Write(CampaignDocument document, PlayNowRoster roster)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        if (!TryValidate(roster, out string error)) throw new ArgumentException(error, nameof(roster));
        document.Set(VersionKey, CurrentVersion);
        document.Set(Line1Key, string.Join(",", roster.Line1));
        document.Set(Line2Key, string.Join(",", roster.Line2));
        document.Set(GoalieKey, roster.GoalieId);
        document.Set(StaminaKey, roster.StaminaEnabled ? "yes" : "no");
    }

    public static string[] PlayerIds(PlayNowRoster roster)
    {
        if (!TryValidate(roster, out string error)) throw new ArgumentException(error, nameof(roster));
        return roster.Line1.Concat(roster.Line2).Append(roster.GoalieId).ToArray();
    }

    public static PlayNowRoster Remap(PlayNowRoster roster, Func<string, string> map)
    {
        if (map == null) throw new ArgumentNullException(nameof(map));
        string[] ids = PlayerIds(roster).Select(map).ToArray();
        var result = new PlayNowRoster(ids.Take(5).ToArray(), ids.Skip(5).Take(5).ToArray(), ids[10], roster.StaminaEnabled);
        if (!TryValidate(result, out string error)) throw new ArgumentException(error, nameof(map));
        return result;
    }

    private static bool TryValidate(PlayNowRoster roster, out string error)
    {
        error = "";
        if (roster == null || roster.Line1 == null || roster.Line2 == null || roster.Line1.Length != 5 || roster.Line2.Length != 5)
        {
            error = "Export roster requires five skaters in each line.";
            return false;
        }
        var ids = roster.Line1.Concat(roster.Line2).Append(roster.GoalieId).ToArray();
        if (ids.Any(id => string.IsNullOrEmpty(id) || !id.All(c => c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z'
            || c >= '0' && c <= '9' || c == '-' || c == '_')))
        {
            error = "Export roster has a missing or unsafe player ID.";
            return false;
        }
        if (ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() != ids.Length)
        {
            error = "Export roster has duplicate player IDs.";
            return false;
        }
        return true;
    }
}
