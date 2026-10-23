using System.Reflection;
using CampaignCore;
using Data;
using EndlessMode;
using Tape2Tape.Customization.UI;

// Native ApplyChanges is represented only by its documented in-memory effect;
// all mod prefix/postfix/finalizer and paired persistence code is production.
static class LineupApplyChecks
{
    internal static bool Apply(TeamRosterData editor, Action native, bool nativeResult = true, bool runNative = true)
    {
        bool allowed = PlayNowRosterContext.Prefix(editor, out var state);
        bool result = false;
        Exception error = null;
        try {
            if (allowed && runNative) { native(); result = nativeResult; }
            var applied = typeof(PlayNowRosterContext).GetMethod("Applied", BindingFlags.Public | BindingFlags.Static);
            if (applied != null) {
                object[] args = { editor, result, state, allowed && runNative };
                applied.Invoke(null, args); result = (bool)args[1];
            }
        } catch (TargetInvocationException ex) { error = ex.InnerException; }
          catch (Exception ex) { error = ex; }
        error = PlayNowRosterContext.Finalizer(error, state);
        if (error != null) throw error;
        return result;
    }
    internal static TeamRosterData Editor(TeamData team)
    {
        var clone = new TeamData { id = team.id, goalie = team.goalie, forwards = team.forwards, lines = team.lines };
        var editor = new TeamRosterData { OriginalTeamData = team, TemporaryTeamData = clone, m_G = team.goalie };
        PlayNowRosterContext.Constructed(editor);
        return editor;
    }
    internal static void Run(string root, Action<bool, string> assert)
    {
        var (repo, team) = PersistenceFixtures.Create(root);
        var editor = Editor(team);
        (editor.m_LW, editor.m_BenchForwards[1]) = (editor.m_BenchForwards[1], editor.m_LW);
        editor.ApplyBody = () => Apply(editor, () => {
            PlayNowRosterContext.Prefix(editor, out var nestedUpdate);
            PlayNowRosterContext.Finalizer(null, nestedUpdate);
            assert(PlayNowRosterContext.Active, "nested temporary update retired outer apply authority");
            team.lines[0].leftWinger = editor.m_LW.id;
            team.lines[1].leftWinger = editor.m_BenchForwards[1].id;
        });
        var menu = new Tape2Tape.Hockey.UI.LineupMenu { Team = editor };
        bool result = PlayNowRosterContext.Leave(menu);
        assert(result, "successful native UI apply rejected");
        assert(PlayNowRosterCodec.TryRead(PersistenceFixtures.TeamDoc(), out var saved, out _)
            && saved.Line1[0] == "p5" && saved.Line2[0] == "p0",
            "successful LineupMenu ApplyChanges never reached persistent paired save");
        string nativePath = PlayNowSaveJournal.NativePath(root, "team", "t");
        string sidePath = PlayNowSaveJournal.CompanionPath(PlayNowRosterSave.Overrides, "team", "t");
        string nativeSaved = File.ReadAllText(nativePath), sideSaved = File.ReadAllText(sidePath);
        int writes = repo.SaveCalls;
        assert(editor.ApplyChanges() && repo.SaveCalls == writes, "native leave's second apply duplicated the disk transaction");
        assert(Apply(editor, () => {}) && repo.SaveCalls == writes, "unchanged repeated apply wrote the team again");
        assert(!Apply(editor, () => {}, nativeResult: false) && repo.SaveCalls == writes, "failed native apply saved");
        assert(!Apply(editor, () => throw new Exception("must not run"), runNative: false) && repo.SaveCalls == writes,
            "skipped native apply saved");
        var failure = new InvalidOperationException("native apply failure");
        try { Apply(editor, () => throw failure); throw new Exception("native failure swallowed"); }
        catch (InvalidOperationException ex) { assert(ReferenceEquals(ex, failure), "native exception replaced"); }
        assert(repo.SaveCalls == writes && !PlayNowRosterContext.Active, "native failure saved or leaked scope");
        var foreign = new TeamRosterData { OriginalTeamData = new TeamData() };
        assert(Apply(foreign, () => {}) && repo.SaveCalls == writes, "foreign native apply gained persistence authority");
        NativeSquadBehaviorBridge.Ready = false;
        assert(!Apply(editor, () => throw new Exception("unsupported original ran")) && repo.SaveCalls == writes,
            "unsupported bridge saved or ran original");
        NativeSquadBehaviorBridge.Ready = true;
        editor.RosterValid = false;
        assert(PlayNowRosterContext.Leave(menu) && repo.SaveCalls == writes, "invalid roster lost native discard prompt");
        editor.RosterValid = true; editor.HasChanged = false;
        assert(PlayNowRosterContext.Leave(menu) && repo.SaveCalls == writes, "unchanged close saved");
        editor.HasChanged = true; menu.m_AreSkatersLocked = true;
        assert(PlayNowRosterContext.Leave(menu) && repo.SaveCalls == writes, "locked preview saved");
        menu.m_AreSkatersLocked = false;
        assert(nativeSaved == File.ReadAllText(nativePath) && sideSaved == File.ReadAllText(sidePath), "rejected/no-op apply changed disk");
        File.AppendAllText(nativePath, " ");
        string externalEdit = File.ReadAllText(nativePath);
        (editor.m_BenchForwards[0], editor.m_BenchForwards[2]) = (editor.m_BenchForwards[2], editor.m_BenchForwards[0]);
        editor.ApplyBody = () => Apply(editor, () => {
            team.lines[1].center = editor.m_BenchForwards[0].id;
            team.lines[1].rightWinger = editor.m_BenchForwards[2].id;
        });
        assert(!PlayNowRosterContext.Leave(menu) && menu.m_Header.m_Title.text.Contains("not saved"),
            "save rejection allowed native close or provided no visible failure");
        assert(externalEdit == File.ReadAllText(nativePath) && sideSaved == File.ReadAllText(sidePath),
            "rejected save overwrote newer native or companion file");
        assert(team.lines[1].center == "p7" && !PlayNowRosterContext.Active,
            "rejected save discarded pending in-memory edit or leaked scope");
        File.WriteAllText(nativePath, nativeSaved); // Fixture-only reconciliation of the injected conflict.
        assert(editor.HasChanged && PlayNowRosterContext.Leave(menu), "failed save could not be retried from the open editor");
        assert(PlayNowRosterCodec.TryRead(PersistenceFixtures.TeamDoc(), out var retried, out _) && retried.Line2[1] == "p7",
            "retry did not commit the pending edit");
        var model = UnityEngine.JsonUtility.FromJson<Tape2Tape.Customization.TeamDataModel>(File.ReadAllText(nativePath));
        PlayNowRosterRuntime.Clear(); PlayNowTranslation.InvalidateAll();
        var models = new Il2CppSystem.Collections.Generic.List<Tape2Tape.Customization.TeamDataModel>(); models.Add(model);
        PlayNowRosterSave.TeamModelsPostfix(repo.m_customTeamRepository, models);
        var reloaded = new TeamData { id = "t", goalie = team.goalie };
        foreach (string id in new[] { model.leftWingerId, model.rightWingerId, model.centerId, model.leftDefensemenId, model.rightDefensemenId })
            reloaded.forwards.Add(repo.m_forwardRepository.Players.First(p => p.id == id));
        assert(PlayNowRosterRuntime.TryHydrate(repo, model, reloaded)
            && reloaded.lines[0].leftWinger == "p5" && reloaded.lines[1].leftWinger == "p0" && reloaded.lines[1].center == "p7",
            "UI-triggered save did not survive cold native-shaped reload");
    }
}
