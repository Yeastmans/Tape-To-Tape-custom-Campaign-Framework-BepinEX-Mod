using System.Reflection;
using CampaignCore;
using Data;
using EndlessMode;
using Tape2Tape.Customization;
using Tape2Tape.Customization.UI;
using Tape2Tape.Hockey.UI;

static class NativeInitializationChecks
{
    internal static void Lineup(string root, Action<bool,string> assert)
    {
        var (_, team) = PersistenceFixtures.Create(root); team.activeLineNumber = 2;
        var metadata = new LineupMenu.Metadata { Team = team, IsHomeTeam = true };
        var roster = new TeamRosterData { OriginalTeamData = team, TemporaryTeamData = new TeamData { id = team.id, goalie = team.goalie, forwards = team.forwards, lines = team.lines } };
        var menu = new LineupMenu { Team = roster, m_IsHomeTeam = true };
        // Installed native constructor executes directly: no managed ctor postfix.
        typeof(PlayNowRosterContext).GetMethod("Initialized")?.Invoke(null, new object[] {menu, metadata, true});
        assert(roster.m_LW?.id == "p0" && roster.m_BenchForwards.Select(p=>p.id).SequenceEqual(new[]{"p6","p5","p7","p8","p9"}),
            "native LineupMenu.InitData bypassed constructor normalization");
        assert(team.activeLineNumber == 2 && roster.TemporaryTeamData.activeLineNumber == 1, "editable initialization changed source active line");
        TeamRosterData NativeRoster() => new() { OriginalTeamData = team, TemporaryTeamData = new TeamData { id = team.id, goalie = team.goalie, forwards = team.forwards, lines = team.lines } };
        roster = NativeRoster(); menu.Team = roster; menu.m_AreSkatersLocked = metadata.LockPlayers = true;
        PlayNowRosterContext.Initialized(menu, metadata, true);
        assert(roster.m_LW.id == "p5" && roster.m_BenchForwards[1].id == "p0"
            && roster.TemporaryTeamData.activeLineNumber == 2 && team.activeLineNumber == 2,
            "locked native initialization did not retain selected/inactive line");
        roster = NativeRoster(); menu.Team = roster;
        PlayNowRosterContext.Initialized(menu, metadata, false);
        assert(roster.m_BenchForwards.Count == 0, "skipped original initialized a roster");
        var wrong = new LineupMenu.Metadata { Team = new TeamData { id = team.id }, LockPlayers = true, IsHomeTeam = true };
        PlayNowRosterContext.Initialized(menu, wrong, true);
        assert(roster.m_BenchForwards.Count == 0, "same-ID foreign source gained initialization authority");
        menu.m_AreSkatersLocked = false;
        PlayNowRosterContext.Initialized(menu, metadata, true);
        assert(roster.m_BenchForwards.Count == 0, "mismatching native lock flags accepted");
        menu.m_AreSkatersLocked = true;
        PlayNowRosterContext.Initialized(menu, team, true);
        assert(roster.m_LW.id == "p5" && team.activeLineNumber == 2, "native direct-TeamData locked path failed");
    }
    internal static void Player(string root, Action<bool,string> assert)
    {
        var (repo, team) = PersistenceFixtures.Create(root);
        var player = team.forwards[0];
        var stage = new CustomizeForwardData(player) { headSkin = "native-init-confirmed-head" };
        var menu = new SkaterCustomizationMenu { CurrentSkater = stage };
        // Native creates CustomizeForwardData without a managed constructor hook.
        typeof(PlayNowPlayerSave).GetMethod("EditorInitialized")?.Invoke(null, new object[] {menu, stage, true});
        PlayNowPlayerSave.HeadConfirmed(menu); player.headSkin = stage.headSkin;
        repo.m_forwardRepository.SaveCustomForward(player);
        var saved = UnityEngine.JsonUtility.FromJson<ForwardDataModel>(File.ReadAllText(PlayNowSaveJournal.NativePath(root,"forward",player.id)));
        assert(saved.headSkin == stage.headSkin, "native typed editor Init bypassed confirmation authority");
        // Repeated initialization of this exact active edit cannot reset a confirmed choice.
        PlayNowPlayerSave.EditorInitialized(menu, stage, true);
        stage.headSkin = "confirmed-before-repeat"; PlayNowPlayerSave.HeadConfirmed(menu);
        stage.headSkin = "preview-after-confirm"; player.headSkin = stage.headSkin;
        PlayNowPlayerSave.EditorInitialized(menu, stage, true);
        repo.m_forwardRepository.SaveCustomForward(player);
        saved = UnityEngine.JsonUtility.FromJson<ForwardDataModel>(File.ReadAllText(PlayNowSaveJournal.NativePath(root,"forward",player.id)));
        assert(saved.headSkin == "confirmed-before-repeat", "repeated Init reset confirmed edit authority");
        var nextStage = new CustomizeForwardData(player) { headSkin = "next-stage-confirmed" };
        var nextMenu = new SkaterCustomizationMenu { CurrentSkater = nextStage };
        PlayNowPlayerSave.EditorInitialized(nextMenu, nextStage, true); PlayNowPlayerSave.HeadConfirmed(nextMenu);
        stage.headSkin = "stale-confirmation"; PlayNowPlayerSave.HeadConfirmed(menu); PlayNowPlayerSave.HidePrefix(menu);
        player.headSkin = nextStage.headSkin; repo.m_forwardRepository.SaveCustomForward(player);
        saved = UnityEngine.JsonUtility.FromJson<ForwardDataModel>(File.ReadAllText(PlayNowSaveJournal.NativePath(root,"forward",player.id)));
        assert(saved.headSkin == "next-stage-confirmed", "old staged callbacks/hide retired the new editor");
        PlayNowPlayerSave.HidePrefix(nextMenu);
        var goalie = team.goalie; var goalieStage = new CustomizeGoalieData(goalie) { headSkin = "goalie-native-init" };
        var goalieMenu = new SkaterCustomizationMenu { CurrentSkater = goalieStage };
        PlayNowPlayerSave.EditorInitialized(goalieMenu, goalieStage, true); PlayNowPlayerSave.HeadConfirmed(goalieMenu);
        goalie.headSkin = goalieStage.headSkin; repo.m_goalieRepository.SaveCustomGoalie(goalie);
        var goalieSaved = UnityEngine.JsonUtility.FromJson<GoalieDataModel>(File.ReadAllText(PlayNowSaveJournal.NativePath(root,"goalie",goalie.id)));
        assert(goalieSaved.headSkin == "goalie-native-init", "native goalie typed initialization failed");
        PlayNowPlayerSave.HidePrefix(goalieMenu);
        var rejectedStage = new CustomizeForwardData(player) { headSkin = "must-not-confirm" };
        var rejectedMenu = new SkaterCustomizationMenu { CurrentSkater = rejectedStage };
        PlayNowPlayerSave.EditorInitialized(rejectedMenu, rejectedStage, false);
        PlayNowPlayerSave.HeadConfirmed(rejectedMenu);
        var mismatch = new CustomizeForwardData(player);
        PlayNowPlayerSave.EditorInitialized(rejectedMenu, mismatch, true); PlayNowPlayerSave.HeadConfirmed(rejectedMenu);
        player.headSkin = rejectedStage.headSkin;
        try { repo.m_forwardRepository.SaveCustomForward(player); throw new Exception("uninitialized editor saved"); }
        catch (IOException) {}
        saved = UnityEngine.JsonUtility.FromJson<ForwardDataModel>(File.ReadAllText(PlayNowSaveJournal.NativePath(root,"forward",player.id)));
        assert(saved.headSkin == "next-stage-confirmed", "skipped or mismatched input granted confirmation authority");
        foreach (bool foreign in new[] {false, true}) {
            var retiredStage = new CustomizeForwardData(player) { headSkin = "retired-choice" };
            var retiredMenu = new SkaterCustomizationMenu { CurrentSkater = retiredStage };
            PlayNowPlayerSave.EditorInitialized(retiredMenu, retiredStage, true); PlayNowPlayerSave.HeadConfirmed(retiredMenu);
            var replacement = foreign ? new CustomizeForwardData(new ForwardData { id = "foreign" }) : null;
            retiredMenu.CurrentSkater = replacement;
            PlayNowPlayerSave.EditorInitialized(retiredMenu, replacement, true);
            player.headSkin = retiredStage.headSkin;
            try { repo.m_forwardRepository.SaveCustomForward(player); throw new Exception("retired editor saved"); }
            catch (IOException) {}
            saved = UnityEngine.JsonUtility.FromJson<ForwardDataModel>(File.ReadAllText(PlayNowSaveJournal.NativePath(root,"forward",player.id)));
            assert(saved.headSkin == "next-stage-confirmed", "null/foreign successful initialization retained old editor authority");
        }
        var ownerAStage = new CustomizeForwardData(player) {headSkin="owner-a"};
        var ownerA = new SkaterCustomizationMenu {CurrentSkater=ownerAStage};
        PlayNowPlayerSave.EditorInitialized(ownerA,ownerAStage,true);
        var ownerBStage = new CustomizeForwardData(player) {headSkin="owner-b"};
        var ownerB = new SkaterCustomizationMenu {CurrentSkater=ownerBStage};
        PlayNowPlayerSave.EditorInitialized(ownerB,ownerBStage,true); PlayNowPlayerSave.HeadConfirmed(ownerB);
        PlayNowPlayerSave.EditorInitialized(ownerA,ownerAStage,true); PlayNowPlayerSave.HeadConfirmed(ownerA);
        PlayNowPlayerSave.HidePrefix(ownerB); player.headSkin=ownerAStage.headSkin;
        repo.m_forwardRepository.SaveCustomForward(player);
        saved=UnityEngine.JsonUtility.FromJson<ForwardDataModel>(File.ReadAllText(PlayNowSaveJournal.NativePath(root,"forward",player.id)));
        assert(saved.headSkin=="owner-a", "superseded same-stage initialization failed to restore current menu ownership");
        PlayNowPlayerSave.HidePrefix(ownerA);
    }
}
