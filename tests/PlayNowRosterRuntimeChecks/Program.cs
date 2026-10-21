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
    Plugin.Log.Warnings.Clear();
    NativeSquadBehaviorBridge.Ready = true;
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
    var m = new TeamDataModel { Id = "export-"+Guid.NewGuid().ToString("N"), leftWingerId = "p0", rightWingerId = "p2", centerId = "p1", leftDefensemenId = "p3", rightDefensemenId = "p4", goalieId = "g" };
    var t = new TeamData { id = m.Id, goalie = new GoaltenderData { id = "g" } };
    for (int i = 0;
i < 10;
i++)
    {
        r.m_forwardRepository.Players.Add(new ForwardData { id = "p" + i });
        if (i < 5) t.forwards.Add(new ForwardData { id = "p" + i });
    }
    t.lines.Add(new Lineup(t) { leftWinger = "p0", rightWinger = "p2", center = "p1", leftDefensemen = "p3", rightDefensemen = "p4" });
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
Test("RejectedExtensionLogsOncePerRevisionAndAbsentIsQuiet", () =>
{
    var (r,m,t)=Fixture();var d=PlayNowTranslation.Documents[m.Id];
    d.Set(PlayNowRosterCodec.Line2Key,"p5,p5,p7,p8,p9");
    Check(!PlayNowRosterRuntime.TryHydrate(r,m,t),"malformed accepted");
    Check(Plugin.Log.Warnings.Count==1 && Plugin.Log.Warnings[0].Contains(m.Id) && Plugin.Log.Warnings[0].Contains("native") && Plugin.Log.Warnings[0].Contains("duplicate"),"actionable malformed diagnostic absent");
    PlayNowRosterRuntime.TryHydrate(r,m,t);Check(Plugin.Log.Warnings.Count==1,"same revision warning repeated");
    PlayNowRosterRuntime.CustomTeamsPostfix(r,new NList{t});
    Check(Plugin.Log.Warnings.Count==1,"native cache completion repeated the same load diagnostic");
    d.Set(PlayNowRosterCodec.VersionKey,"99");PlayNowRosterRuntime.TryHydrate(r,m,t);
    Check(Plugin.Log.Warnings.Count==2 && Plugin.Log.Warnings[1].Contains("99"),"unsupported revision diagnostic absent");
    PlayNowTranslation.Documents[m.Id]=new CampaignDocument("");PlayNowRosterRuntime.TryHydrate(r,m,t);
    Check(Plugin.Log.Warnings.Count==2,"ordinary absent extension warned");
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
    Plugin.Log.Warnings.Clear();
    NativeSquadBehaviorBridge.Ready = true;
    Check(!PlayNowRosterRuntime.TryGet(t, out _) && !PlayNowRosterRuntime.TryGet(c, out _), "lifecycle ownership retained");
});
Test("RosterScopeUnwindsOnThrow", () =>
{
    var type = typeof(PlayNowRosterRuntime).Assembly.GetType("EndlessMode.PlayNowRosterContext");
    Check(type != null, "exported roster scope is absent");
    var enter = type.GetMethod("Enter", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
    var active = type.GetProperty("Active", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
    bool IsActive() => (bool)active.GetValue(null);
    var (r,m,t) = Fixture(); Check(PlayNowRosterRuntime.TryHydrate(r,m,t), "hydrate");
    try {
        using var scope = (IDisposable)enter.Invoke(null, new object[] {t});
        Check(IsActive(), "eligible scope inactive");
        using (var nested = (IDisposable)enter.Invoke(null, new object[] {new TeamData()}))
            Check(!IsActive(), "inactive nested scope leaked outer eligibility");
        Check(IsActive(), "outer not restored");
        throw new InvalidOperationException("expected");
    } catch (InvalidOperationException) { }
    Check(!IsActive(), "scope leaked after throw");
});
Test("SameTeamBothSidesIndependent", () =>
{
    var (r,m,t) = Fixture(); Check(PlayNowRosterRuntime.TryHydrate(r,m,t), "hydrate");
    // Unity Instantiate in Team.Initialize keeps IDs, unlike TeamData.Clone.
    var a = NativeClone(t, ""); var b = NativeClone(t, "");
    a.forwards = t.forwards; b.forwards = t.forwards; // Unity Instantiate can share nested references
    foreach(var p in a.forwards) p.OriginalId = null;
    foreach(var p in b.forwards) p.OriginalId = null;
    PlayNowRosterRuntime.Clone(t,a); PlayNowRosterRuntime.Clone(t,b);
    Check(PlayNowRosterRuntime.TryGet(a,out _) && PlayNowRosterRuntime.TryGet(b,out _), "instantiated sides not registered");
    a.lines[0].leftWinger="p1"; a.lines[0].rightWinger="p0"; a.activeLineNumber=2;
    Check(b.lines[0].leftWinger=="p0" && b.activeLineNumber==1 && !ReferenceEquals(a.lines,b.lines) && !ReferenceEquals(a.forwards,b.forwards) && !ReferenceEquals(a.forwards,t.forwards), "side state shared");
});
Test("StaminaOnBeforeHudCreation", () =>
{
    var (r,m,t)=Fixture(); Check(PlayNowRosterRuntime.TryHydrate(r,m,t), "hydrate");
    var live = new Tape2Tape.Team { _teamData=NativeClone(t, "") };
    foreach(var p in live._teamData.forwards) p.OriginalId=null;
    var system = new Skaters.TeamStaminaSystem();
    PlayNowTeamInitialization.Prefix(live,t,out var scope);
    SquadStaminaRuntime.Postfix(system,live,false);
    Check(system.IsActive && system.Refills==1 && system.Rebuilds==1, "policy not active before native HUD factory");
    SquadStaminaRuntime.Postfix(system,live,false);
    Check(system.Refills==1,"routine lookup refilled fatigue");
    PlayNowTeamInitialization.Finalizer(null,scope);
});
Test("ManualSwapWithStaminaOff", () =>
{
    var (r,m,t)=Fixture(); PlayNowTranslation.Documents[m.Id].Set(PlayNowRosterCodec.StaminaKey,"no");
    Check(PlayNowRosterRuntime.TryHydrate(r,m,t), "hydrate");
    var live=new Tape2Tape.Team { _teamData=NativeClone(t, "") };
    PlayNowTeamInitialization.Prefix(live,t,out var scope);
    var system=new Skaters.TeamStaminaSystem {_isLinesTeam=true};
    SquadStaminaRuntime.Postfix(system,live,false);
    Check(!system.IsActive && live._teamData.lines.Count==2,"disabled stamina removed native switching precondition");
    live._teamData.activeLineNumber=2;
    Check(PlayNowRosterRuntime.TryGet(live._teamData,out var roster) && !roster.StaminaEnabled && roster.Line2[0]=="p5", "line selection changed ownership/policy");
    PlayNowTeamInitialization.Finalizer(null,scope);
});
Test("EditorKeepsAbsoluteLinesWhenSourceLine2", () =>
{
    var (r,m,t)=Fixture(); Check(PlayNowRosterRuntime.TryHydrate(r,m,t), "hydrate"); t.activeLineNumber=2;
    var editor=new Tape2Tape.Customization.UI.TeamRosterData { OriginalTeamData=t,TemporaryTeamData=NativeClone(t,""),m_G=t.goalie };
    PlayNowRosterContext.Constructed(editor);
    Check(t.activeLineNumber==2 && editor.TemporaryTeamData.activeLineNumber==1 && editor.m_LW.id=="p0" && editor.m_BenchForwards.Select(p=>p.id).SequenceEqual(new[] {"p6","p5","p7","p8","p9"}),"editor renumbered absolute lines");
    PlayNowRosterContext.Prefix(editor,out var state); Check(PlayNowRosterContext.Active,"valid editor scope missing");
    var error=new InvalidOperationException("native failure"); Check(ReferenceEquals(error,PlayNowRosterContext.Finalizer(error,state)),"finalizer swallowed native failure");
    PlayNowRosterContext.Finalizer(null,state); Check(!PlayNowRosterContext.Active,"double cleanup leaked");
    Check(PlayNowRosterContext.IsRosterSite(0x4501226) && PlayNowRosterContext.IsRosterSite(0x45022E0) && !PlayNowRosterContext.IsRosterSite(0x4270A6C),"scope leaked into campaign query sites");
});
Test("UnsupportedBridgeRejectsExportedWriteOnly", () =>
{
    var (r,m,t)=Fixture(); Check(PlayNowRosterRuntime.TryHydrate(r,m,t),"hydrate");
    var editor=new Tape2Tape.Customization.UI.TeamRosterData {OriginalTeamData=t,TemporaryTeamData=NativeClone(t,""),m_G=t.goalie};
    PlayNowRosterContext.Constructed(editor);
    NativeSquadBehaviorBridge.Ready=false;
    Check(!PlayNowRosterContext.Prefix(editor,out var state) && !PlayNowRosterContext.Active,"unsupported bridge allowed single-line fallback");
    PlayNowRosterContext.Finalizer(null,state);
    var foreign=new Tape2Tape.Customization.UI.TeamRosterData {OriginalTeamData=new TeamData()};
    Check(PlayNowRosterContext.Prefix(foreign,out state) && !PlayNowRosterContext.Active,"foreign native writer blocked");
    PlayNowRosterContext.Finalizer(null,state);
});
Test("ScopeDuplicateAndOutOfOrderCleanup", () =>
{
    var (r,m,t)=Fixture(); Check(PlayNowRosterRuntime.TryHydrate(r,m,t),"hydrate");
    var outer=PlayNowRosterContext.Enter(t); var inner=PlayNowRosterContext.Enter(null);
    outer.Dispose(); Check(!PlayNowRosterContext.Active,"out-of-order disposal exposed outer");
    inner.Dispose(); outer.Dispose(); Check(!PlayNowRosterContext.Active,"disposed parent restored");
    using(var eligible=PlayNowRosterContext.Enter(t)) {
        bool otherThread=Task.Run(()=>PlayNowRosterContext.Active).GetAwaiter().GetResult();
        Check(!otherThread && PlayNowRosterContext.Active,"scope leaked to another thread");
    }
});
Test("TeamInitializationIsolationAndRetirement", () =>
{
    var (r,m,t)=Fixture(); Check(PlayNowRosterRuntime.TryHydrate(r,m,t),"hydrate");
    var a=new Tape2Tape.Team(); var b=new Tape2Tape.Team();
    PlayNowTeamInitialization.Prefix(a,t,out var first);
    a._teamData=NativeClone(t,""); var staminaA=new Skaters.TeamStaminaSystem();
    SquadStaminaRuntime.Postfix(staminaA,a,false);
    var old=a._teamData;
    PlayNowTeamInitialization.Prefix(b,new TeamData(),out var nested);
    b._teamData=NativeClone(t,""); var staminaB=new Skaters.TeamStaminaSystem();
    SquadStaminaRuntime.Postfix(staminaB,b,false);
    Check(!staminaB.IsActive && !PlayNowRosterRuntime.IsRegistered(b._teamData),"nested foreign team inherited outer export");
    PlayNowTeamInitialization.Finalizer(new InvalidOperationException(),nested);
    Check(staminaA.IsActive,"other side disabled");
    PlayNowTeamInitialization.Finalizer(null,first);
    PlayNowTeamInitialization.Prefix(a,t,out var second);
    Check(!PlayNowRosterRuntime.IsRegistered(old),"old runtime clone ownership retained");
    a._teamData=NativeClone(t,""); SquadStaminaRuntime.Postfix(new Skaters.TeamStaminaSystem(),a,false);
    PlayNowTeamInitialization.Finalizer(null,second);
    Check(PlayNowRosterRuntime.IsRegistered(a._teamData) && PlayNowRosterRuntime.IsRegistered(t),"reinitialization lost source/new ownership");
});
Test("LockedPreviewUsesCurrentAndInactiveLines", () =>
{
    var (r,m,t)=Fixture(); Check(PlayNowRosterRuntime.TryHydrate(r,m,t),"hydrate"); t.activeLineNumber=2;
    var editor=new Tape2Tape.Customization.UI.TeamRosterData {OriginalTeamData=t,TemporaryTeamData=NativeClone(t,""),m_G=t.goalie};
    PlayNowRosterContext.Constructed(editor); PlayNowRosterContext.RefreshLocked(editor);
    Check(editor.m_LW.id=="p5" && editor.m_C.id=="p6" && editor.m_BenchForwards.Select(p=>p.id).SequenceEqual(new[]{"p1","p0","p2","p3","p4"}),"locked preview duplicated current line");
});
Test("UnrelatedTemporaryRosterCannotBorrowContext", () =>
{
    var (r,m,t)=Fixture(); Check(PlayNowRosterRuntime.TryHydrate(r,m,t),"hydrate");
    var editor=new Tape2Tape.Customization.UI.TeamRosterData {OriginalTeamData=t,TemporaryTeamData=NativeClone(t,""),m_G=t.goalie};
    PlayNowRosterContext.Constructed(editor);
    var other=NativeClone(t,"b"); PlayNowRosterRuntime.Clone(t,other);
    var unrelated=NativeClone(other,""); PlayNowRosterRuntime.Clone(other,unrelated);
    editor.TemporaryTeamData=unrelated;
    Check(!PlayNowRosterContext.Prefix(editor,out var scope) && !PlayNowRosterContext.Active,"unrelated temporary roster activated context");
    PlayNowRosterContext.Finalizer(null,scope);
});
Test("BothSidesReceiveTheirOwnStaminaInitialization", () =>
{
    var (r,m,t)=Fixture(); Check(PlayNowRosterRuntime.TryHydrate(r,m,t),"hydrate");
    var home=new Tape2Tape.Team(); var away=new Tape2Tape.Team();
    var homeStamina=new Skaters.TeamStaminaSystem(); var awayStamina=new Skaters.TeamStaminaSystem();
    PlayNowTeamInitialization.Prefix(home,t,out var first);
    home._teamData=NativeClone(t,""); SquadStaminaRuntime.Postfix(homeStamina,home,false);
    PlayNowTeamInitialization.Finalizer(null,first);
    PlayNowTeamInitialization.Prefix(away,t,out var second);
    away._teamData=NativeClone(t,""); SquadStaminaRuntime.Postfix(awayStamina,away,false);
    PlayNowTeamInitialization.Finalizer(null,second);
    Check(homeStamina.IsActive && awayStamina.IsActive && homeStamina.Refills==1 && awayStamina.Refills==1,"one side lacked pre-HUD initialization");
    home._teamData.activeLineNumber=2; homeStamina._isLinesTeam=false;
    Check(away._teamData.activeLineNumber==1 && awayStamina.IsActive,"per-side selection or stamina state shared");
});
Console.WriteLine($"{passed} passed; {failed} failed");
return failed == 0 ? 0 : 1;
