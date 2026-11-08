using Tape2Tape.Hockey.UI;
using Native = Il2CppSystem.Collections.Generic;

static RunMatchData Match(int terrain, bool boss, bool win, int self, int other)
{
    var id = Guid.NewGuid().ToString();
    return new(new object(), new MatchScore
    {
        act = terrain, IsBoss = boss, Win = win, SelfScore = self, OtherScore = other,
        Shootout = true, teamId = id, score = $"{self}-{other}", MapPoint = (3, 5),
        scorersStats = new object(), goalieStats = new object()
    });
}

static void Check(string name, int[] expected, params RunMatchData[] rows)
{
    var recordedActs = rows.Select(row => row.Score.act).ToArray();
    var played = new Native.List<int>();
    played.AddRange(recordedActs.Distinct());
    Native.IReadOnlyList<int> acts = played;
    var original = new Native.List<RunMatchData>();
    original.AddRange(rows);
    Native.IReadOnlyList<RunMatchData> matches = original;
    EndlessMode.CustomRunActSummaries.Prefix(ref acts, ref matches);

    var observedActs = matches.Select(m => m.Score.act).ToArray();
    if (!observedActs.SequenceEqual(expected) || !acts.SequenceEqual(expected.Distinct()))
        throw new Exception($"{name}: expected match acts [{string.Join(", ", expected)}], got [{string.Join(", ", observedActs)}] under headings [{string.Join(", ", acts)}]");
    if (!recordedActs.SequenceEqual(rows.Select(row => row.Score.act)))
        throw new Exception(name + ": summary altered the underlying saved scores");
    if (recordedActs.SequenceEqual(expected) && (!ReferenceEquals(original, matches) || !ReferenceEquals(played, acts)))
        throw new Exception(name + ": unnecessary changes to native summary arguments");
    for (int i = 0; i < rows.Length; i++)
    {
        var saved = rows[i].Score;
        var display = matches[i].Score;
        if (saved.Win != display.Win || saved.IsBoss != display.IsBoss || saved.Shootout != display.Shootout ||
            saved.SelfScore != display.SelfScore || saved.OtherScore != display.OtherScore || saved.score != display.score ||
            saved.teamId != display.teamId || saved.MapPoint != display.MapPoint || saved.scorersStats != display.scorersStats ||
            saved.goalieStats != display.goalieStats || rows[i].OpponentTeam != matches[i].OpponentTeam)
            throw new Exception(name + ": lost native match details for row " + i);
    }
    Console.WriteLine("PASS " + name);
}

// Stock presentation uses one boss slot per distinct MatchScore.act. A second
// boss in the same panel overwrites the first, even when both scores were saved.
Check("repeated act one with two victorious bosses", [1, 1, 2, 2],
    Match(1, false, true, 4, 2), Match(1, true, true, 3, 0), Match(1, false, true, 2, 1), Match(1, true, true, 7, 0));
Check("four-map legacy history with repeated last terrain", [1, 2, 3, 4, 4],
    Match(1, true, true, 3, 0), Match(2, true, true, 12, 7), Match(3, true, true, 10, 3),
    Match(3, false, true, 5, 0), Match(3, true, true, 7, 0));
Check("reordered terrain ending in a lost boss", [1, 2, 2],
    Match(3, true, true, 5, 2), Match(1, false, true, 2, 1), Match(1, true, false, 1, 4));
Check("conceded after second-map opener", [1, 2],
    Match(1, true, true, 3, 1), Match(1, false, false, 0, 2));
Check("no extra act after final boss", [1], Match(1, true, true, 3, 0));
Check("standard act sequence", [1, 1, 2, 3],
    Match(1, false, true, 1, 0), Match(1, true, true, 2, 1), Match(2, true, true, 3, 0), Match(3, false, false, 0, 3));
Check("concession without any matches", []);

Check("legacy partial history starting on native act two", [2], Match(2, true, true, 5, 0));
Check("legacy terrain change with a missing boss marker", [1, 2, 2, 3],
    Match(1, false, true, 4, 0), Match(2, false, true, 4, 0),
    Match(2, true, true, 5, 3), Match(2, false, true, 5, 0));
EndlessMode.Plugin.ActSequence = [2, 3];
var opening = Match(2, true, true, 3, 2);
var finale = Match(3, true, false, 1, 3);
Tape2Tape.CampaignUtil.CampaignState.runStatsState.runMatchScores.Add(opening.Score);
Tape2Tape.CampaignUtil.CampaignState.runStatsState.runMatchScores.Add(finale.Score);
Check("active custom campaign starting on native terrain two", [1, 2], opening, finale);
Check("unrelated history while a custom campaign is active", [2, 3], Match(2, true, true, 3, 2), Match(3, true, false, 1, 3));
EndlessMode.Plugin.ActSequence = [3];
var single = Match(3, true, true, 5, 4);
Tape2Tape.CampaignUtil.CampaignState.runStatsState.runMatchScores.Clear();
Tape2Tape.CampaignUtil.CampaignState.runStatsState.runMatchScores.Add(single.Score);
Check("active custom campaign with only native terrain three", [1], single);
EndlessMode.Plugin.IsDefaultMode = true;
Check("native partial run in default mode", [3], single);
EndlessMode.Plugin.IsDefaultMode = false;

if (args.Length > 1 && args[0] == "--history")
{
    foreach (string path in args.Skip(1))
    {
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        var scores = json.RootElement.GetProperty("entry").GetProperty("value").GetProperty("matchScores");
        var rows = new Native.List<RunMatchData>();
        var headings = new Native.List<int>();
        foreach (var s in scores.EnumerateArray())
        {
            var m = Match(s.GetProperty("act").GetInt32(), s.GetProperty("IsBoss").GetBoolean(),
                s.GetProperty("Win").GetBoolean(), s.GetProperty("SelfScore").GetInt32(), s.GetProperty("OtherScore").GetInt32());
            rows.Add(m);
            if (!headings.Contains(m.Score.act)) headings.Add(m.Score.act);
        }
        Native.IReadOnlyList<int> acts = headings;
        Native.IReadOnlyList<RunMatchData> display = rows;
        EndlessMode.CustomRunActSummaries.Prefix(ref acts, ref display);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
        {
            file = Path.GetFileName(path),
            acts = acts.ToArray(),
            matches = display.Select((m, i) => new { nativeAct = rows[i].Score.act, displayAct = m.Score.act,
                m.Score.IsBoss, m.Score.Win, m.Score.SelfScore, m.Score.OtherScore }).ToArray()
        }));
    }
}
