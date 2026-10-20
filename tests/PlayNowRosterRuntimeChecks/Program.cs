using CampaignCore;
using Data;
using EndlessMode;
using Tape2Tape.Customization;
using NList = Il2CppSystem.Collections.Generic.List<Data.TeamData>;
int passed = 0, failed = 0;
void Check(bool value, string message)
{
    if (!value) throw new Exception(message);
}
void Test(string name, Action run)
{
    PlayNowRosterRuntime.Clear();
    PlayNowTranslation.Documents.Clear();
    try
    {
        run();
        Console.WriteLine("PASS " + name);
        passed++;
    }
    catch (Exception ex)
    {
        Console.WriteLine("FAIL " + name + ": " + ex.Message);
        failed++;
    }
}
(TeamRepository repo, TeamDataModel model, TeamData team) Fixture()
{
    var r = new TeamRepository();
    var m = new TeamDataModel { Id = "export", leftWingerId = "p0", rightWingerId = "p1", centerId = "p2", leftDefensemenId = "p3", rightDefensemenId = "p4", goalieId = "g" };
    var t = new TeamData { id = m.Id, goalie = new GoaltenderData { id = "g" } };
    for (int i = 0;
i < 10;
i++)
    {
        r.m_forwardRepository.Players.Add(new ForwardData { id = "p" + i });
        if (i < 5) t.forwards.Add(new ForwardData { id = "p" + i });
    }
    t.lines.Add(new Lineup(t) { leftWinger = "p0", rightWinger = "p1", center = "p2", leftDefensemen = "p3", rightDefensemen = "p4" });
    var doc = new CampaignDocument("");
    PlayNowRosterCodec.Write(doc, new PlayNowRoster(Enumerable.Range(0, 5).Select(i => "p" + i).ToArray(), Enumerable.Range(5, 5).Select(i => "p" + i).ToArray(), "g", true));
    PlayNowTranslation.Documents[m.Id] = doc;
    r.m_customTeamRepository.Models.Add(m);
    return (r, m, t);
}
TeamData NativeClone(TeamData source, string suffix)
{
    var c = new TeamData { id = source.id, goalie = source.goalie };
    foreach (var p in source.forwards) c.forwards.Add(new ForwardData { id = p.id + suffix, OriginalId = string.IsNullOrEmpty(p.OriginalId) ? p.id : p.OriginalId });
    foreach (var l in source.lines) c.lines.Add(new Lineup(c) { leftWinger = l.leftWinger + suffix, rightWinger = l.rightWinger + suffix, center = l.center + suffix, leftDefensemen = l.leftDefensemen + suffix, rightDefensemen = l.rightDefensemen + suffix });
    return c;
}
Test("HydrateAllOrNothing", () =>
{
    var (r, m, t) = Fixture();
    var old = t.forwards;
    var g = t.goalie;
    Check(PlayNowRosterRuntime.TryHydrate(r, m, t), "hydrate rejected");
    Check(t.forwards.Count == 10 && t.lines.Count == 2 && ReferenceEquals(t.goalie, g), "complete roster/goalie");
    Check(ReferenceEquals(old[0], t.forwards[0]), "existing player lost");
    Check(PlayNowRosterRuntime.TryGet(t, out var roster) && roster.Line2[4] == "p9" && roster.StaminaEnabled, "owner missing");
});
Test("RepeatedHydrationIsIdempotent", () =>
{
    var (r, m, t) = Fixture();
    Check(PlayNowRosterRuntime.TryHydrate(r, m, t), "initial");
    var lines = t.lines;
    t.lines[0].leftWinger = "p1";
    t.lines[0].rightWinger = "p0";
    int calls = r.Calls;
    Check(PlayNowRosterRuntime.TryHydrate(r, m, t), "repeat");
    Check(ReferenceEquals(lines, t.lines) && t.lines[0].leftWinger == "p1" && r.Calls == calls, "edits overwritten");
});
Test("CloneTwiceDoesNotShareLines", () =>
{
    var (r, m, t) = Fixture();
    Check(PlayNowRosterRuntime.TryHydrate(r, m, t), "initial");
    var a = NativeClone(t, "a");
    PlayNowRosterRuntime.Clone(t, a);
    var b = NativeClone(a, "b");
    PlayNowRosterRuntime.Clone(a, b);
    Check(PlayNowRosterRuntime.TryGet(b, out var roster) && roster.Line2[0] == "p5ab", "clone owner remap");
    Check(b.forwards[0].id == "p0ab", "native IDs restored");
    Check(PlayNowRosterRuntime.TryGetSourceId(b, "p5ab", out var saved) && saved == "p5", "source provenance lost");
    Check(ReferenceEquals(b.lines[0].teamData, b), "clone line owner binding");
    b.lines[0].leftWinger = "changed";
    Check(a.lines[0].leftWinger == "p0a" && t.lines[0].leftWinger == "p0", "shared lines");
});
Test("ForeignTeamUnchanged", () =>
{
    var (r, m, t) = Fixture();
    PlayNowTranslation.Documents.Clear();
    var old = t.lines;
    Check(!PlayNowRosterRuntime.TryHydrate(r, m, t) && ReferenceEquals(old, t.lines) && !PlayNowRosterRuntime.TryGet(t, out _), "foreign team changed");
    var c = NativeClone(t, "c");
    PlayNowRosterRuntime.Clone(t, c);
    Check(!PlayNowRosterRuntime.TryGet(c, out _), "foreign clone owned");
});
Test("MissingPlayerKeepsNativeFallback", () =>
{
    var (r, m, t) = Fixture();
    r.m_forwardRepository.Players.RemoveAt(9);
    var f = t.forwards;
    var l = t.lines;
    Check(!PlayNowRosterRuntime.TryHydrate(r, m, t) && ReferenceEquals(f, t.forwards) && ReferenceEquals(l, t.lines) && !PlayNowRosterRuntime.TryGet(t, out _), "partial apply");
});
Test("ResolutionExceptionKeepsFallback", () =>
{
    var (r, m, t) = Fixture();
    r.FailId = "p8";
    var f = t.forwards;
    var l = t.lines;
    Check(!PlayNowRosterRuntime.TryHydrate(r, m, t) && ReferenceEquals(f, t.forwards) && ReferenceEquals(l, t.lines), "partial failure");
});
Test("StaleModelKeepsFallback", () =>
{
    var (r, m, t) = Fixture();
    m.centerId = "new";
    Check(!PlayNowRosterRuntime.TryHydrate(r, m, t) && t.forwards.Count == 5, "stale sidecar applied");
});
Test("ReusedPointerNotOwned", () =>
{
    var (r, m, t) = Fixture();
    Check(PlayNowRosterRuntime.TryHydrate(r, m, t), "initial");
    var other = new TeamData { id = t.id, Pointer = t.Pointer };
    Check(!PlayNowRosterRuntime.TryGet(other, out _), "reused pointer inherited");
});
Test("CompletedLoadAndCacheReset", () =>
{
    var (r, m, t) = Fixture();
    var teams = new NList { t };
    PlayNowRosterRuntime.CustomTeamsPostfix(r, teams);
    Check(PlayNowRosterRuntime.TryGet(t, out _), "completed load missing");
    t.lines[0].leftWinger = "p1";
    t.lines[0].rightWinger = "p0";
    PlayNowRosterRuntime.CustomTeamsPostfix(r, teams);
    Check(t.lines[0].leftWinger == "p1", "warm cache overwritten");
    PlayNowRosterRuntime.CustomTeamsPostfix(new TeamRepository(), new NList());
    Check(!PlayNowRosterRuntime.TryGet(t, out _), "old repository ownership retained");
});
Test("MalformedExtensionKeepsFallback", () =>
{
    var (r, m, t) = Fixture();
    PlayNowTranslation.Documents[m.Id].Set(PlayNowRosterCodec.Line2Key, "p5,p5,p7,p8,p9");
    var lines = t.lines;
    Check(!PlayNowRosterRuntime.TryHydrate(r, m, t) && ReferenceEquals(lines, t.lines) && !PlayNowRosterRuntime.TryGet(t, out _), "malformed extension applied");
});
Test("MissingGoalieKeepsFallback", () =>
{
    var (r, m, t) = Fixture();
    t.goalie = null;
    var lines = t.lines;
    Check(!PlayNowRosterRuntime.TryHydrate(r, m, t) && ReferenceEquals(lines, t.lines) && t.forwards.Count == 5, "incomplete goalie applied");
});
Test("LifecycleClearDropsCloneOwnership", () =>
{
    var (r, m, t) = Fixture();
    Check(PlayNowRosterRuntime.TryHydrate(r, m, t), "initial");
    var c = NativeClone(t, "c");
    PlayNowRosterRuntime.Clone(t, c);
    Check(PlayNowRosterRuntime.TryGet(c, out _), "clone initial");
    PlayNowRosterRuntime.Clear();
    Check(!PlayNowRosterRuntime.TryGet(t, out _) && !PlayNowRosterRuntime.TryGet(c, out _), "lifecycle ownership retained");
});
Console.WriteLine($"{passed} passed; {failed} failed");
return failed == 0 ? 0 : 1;
