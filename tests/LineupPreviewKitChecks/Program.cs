using System;
using System.Collections.Generic;
using System.Linq;
using Data;
using EndlessMode;
using Tape2Tape;
using Tape2Tape.Customization.UI;
using Tape2Tape.Hockey.UI;

int count = 0;
void Check(bool pass, string name) { if (!pass) throw new Exception(name); count++; Console.WriteLine("PASS " + name); }
var data = new TeamData { id = "qa", Pointer = (IntPtr)1 };
var opponent = new TeamData { id = "opponent", Pointer = (IntPtr)2 };
var live = new Team { _teamData = data, UsesHomeUniform = false };
GameManager.HomeTeam = live;
GameManager.VisitorTeam = new Team { _teamData = opponent, UsesHomeUniform = true };
var clone = new TeamData { id = "qa", Pointer = (IntPtr)3 };
var menu = new LineupMenu { Team = new TeamRosterData { OriginalTeamData = data }, IsHomeTeam = true };
var preview = new SkaterPreviewInUI { Parent = menu, m_IsHome = true };
LineupPreviewKit.Prefix(preview, null, ref clone);
Check(!preview.m_IsHome, "Home-side roster previews its selected away kit");
Check(menu.IsHomeTeam && ReferenceEquals(menu.Team.OriginalTeamData, data), "Roster side and original team stay unchanged");
live.UsesHomeUniform = true; LineupPreviewKit.Prefix(preview, null, ref clone);
Check(preview.m_IsHome, "Changing match back to home kit updates preview");
GameManager.HomeTeam = null; GameManager.VisitorTeam = live; live.UsesHomeUniform = false;
LineupPreviewKit.Prefix(preview, null, ref clone);
Check(!preview.m_IsHome, "Visitor-side roster uses its actual away kit");
live.UsesHomeUniform = true; LineupPreviewKit.Prefix(preview, null, ref clone);
Check(preview.m_IsHome, "Visitor-side roster may wear its home kit");
Check(LineupPreviewKit.TryResolve(clone, null, live, out bool kit) && kit, "Unique saved/cloned roster ID resolves to live team");
var mirror = new Team { _teamData = clone, UsesHomeUniform = false };
Check(LineupPreviewKit.TryResolve(data, live, mirror, out kit) && kit, "Pointer identity wins when both sides share an ID");
Check(!LineupPreviewKit.TryResolve(new TeamData { id = "qa", Pointer = (IntPtr)4 }, live, mirror, out kit), "Ambiguous cloned IDs leave selection untouched");
preview.Parent = null; preview.m_IsHome = false; LineupPreviewKit.Prefix(preview, null, ref clone);
Check(!preview.m_IsHome, "Non-lineup previews retain their own selection");
preview.Parent = menu; Plugin.IsDefaultMode = true; LineupPreviewKit.Prefix(preview, null, ref clone);
Check(!preview.m_IsHome, "Base game mode stays unchanged");
Plugin.IsDefaultMode = false; live.gameObject.activeInHierarchy = false; LineupPreviewKit.Prefix(preview, null, ref clone);
Check(!preview.m_IsHome, "Inactive match objects cannot recolor map lineup");
live.gameObject.activeInHierarchy = true; live.gameObject.scene.isLoaded = false;
Check(!LineupPreviewKit.TryResolve(data, live, null, out kit), "Unloaded match is ignored");
Check(!LineupPreviewKit.TryResolve(null, live, null, out kit), "Null roster is safe");
Check(!LineupPreviewKit.TryResolve(opponent, null, null, out kit), "No match keeps native preview behavior");
GameManager._instance = null; preview.m_IsHome = false; LineupPreviewKit.Prefix(preview, null, ref clone);
Check(!preview.m_IsHome, "Map lineup without a game manager is safe");
LineupPreviewKit.Prefix(null, null, ref data); TeamData absentTeam = null; LineupPreviewKit.Prefix(preview, null, ref absentTeam);
Check(true, "Null render inputs are safe");
data.isMultiLineup = true;
data.benchSize = 0;
data.benchPlayers = Enumerable.Range(1, 5).Select(i => new ForwardData { Pointer = (IntPtr)(100 + i) }).ToList();
menu.m_AreSkatersLocked = true;
LineupPreviewRoster.Prefix(menu);
Check(menu.Team.Bench.SequenceEqual(data.benchPlayers), "Locked two-line preview contains all five inactive-line players despite zero reserve slots");
Check(data.benchSize == 0 && menu.m_AreSkatersLocked && !menu.Team.HasChanged, "Preview fill leaves reserves, editing locks, and roster change tracking untouched");
LineupPreviewRoster.Prefix(menu);
Check(menu.Team.Bench.Count == 5, "Repeated preview refresh does not duplicate the inactive line");
var previous = menu.Team.Bench.ToArray();
data.benchPlayers = Enumerable.Range(1, 5).Select(i => new ForwardData { Pointer = (IntPtr)(200 + i) }).ToList();
LineupPreviewRoster.Prefix(menu);
Check(menu.Team.Bench.SequenceEqual(data.benchPlayers), "Reopening after a line change displays the new inactive line");
data.benchPlayers[2] = null; LineupPreviewRoster.Prefix(menu);
Check(menu.Team.Bench.Count == 5 && menu.Team.Bench[2] == null, "Missing authored player retains its own empty slot rather than shifting positions");
menu.Team = new TeamRosterData { OriginalTeamData = data };
menu.m_AreSkatersLocked = false;
LineupInitialRoster.Postfix(menu);
Check(menu.Team.Bench.SequenceEqual(data.benchPlayers), "Editable two-line menu starts with the saved inactive line despite zero reserve slots");
Check(!menu.m_AreSkatersLocked && !menu.Team.HasChanged && data.benchSize == 0, "Initializing an editable menu does not lock it, add reserves, or mark the roster changed");
var pending = new ForwardData { Pointer = (IntPtr)999 };
menu.Team.Bench[0] = pending;
menu.Team.HasChanged = true;
LineupPreviewRoster.Prefix(menu);
Check(ReferenceEquals(menu.Team.Bench[0], pending) && menu.Team.HasChanged, "Refreshing an editable menu preserves an unsaved player swap");
Check(!ReferenceEquals(data.benchPlayers[0], pending), "Changing the menu model does not change the saved inactive lineup");
menu.Team.Bench[0] = null;
LineupPreviewRoster.Prefix(menu);
Check(menu.Team.Bench[0] == null, "Editable refresh preserves a deliberately empty pending slot");
menu.Team = new TeamRosterData { OriginalTeamData = data };
LineupInitialRoster.Postfix(menu);
Check(menu.Team.Bench.SequenceEqual(data.benchPlayers) && !menu.Team.HasChanged, "Reopening with a fresh model discards unapplied edits and reads the saved lineup");
LineupInitialRoster.Postfix(menu);
Check(menu.Team.Bench.Count == 5, "Initial population does not duplicate inactive skaters");
foreach (var exclusion in new[] { "native", "single", "default" }) {
 menu.Team.Bench.Clear(); menu.m_AreSkatersLocked = true;
 data.id = exclusion == "native" ? "native" : "qa"; data.isMultiLineup = exclusion != "single"; Plugin.IsDefaultMode = exclusion == "default";
 LineupInitialRoster.Postfix(menu);
 LineupPreviewRoster.Prefix(menu);
 Check(menu.Team.Bench.Count == 0, exclusion + " lineup retains native preview behavior");
}
Plugin.IsDefaultMode = false; data.id = "qa"; data.isMultiLineup = true;
data.benchPlayers.RemoveAt(0); LineupInitialRoster.Postfix(menu); LineupPreviewRoster.Prefix(menu);
Check(menu.Team.Bench.Count == 0, "Incomplete native inactive-line result is left unchanged");
LineupPreviewRoster.Prefix(null);
LineupInitialRoster.Postfix(null);
Check(true, "Missing lineup menu is safe");
Console.WriteLine($"{count} lineup preview policy checks passed; native rendering requires a live view.");

namespace HarmonyLib {
    [AttributeUsage(AttributeTargets.Class)] public class HarmonyPatch : Attribute { public HarmonyPatch(Type t, string name) { } }
    [AttributeUsage(AttributeTargets.Method)] public class HarmonyPrefix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public class HarmonyPostfix : Attribute { }
}
namespace EndlessMode {
    // Other runtime owners are outside this ordinary-lineup policy fixture.
    internal static class PlayNowRosterRuntime { internal static bool TryGet(TeamData team, out object registration) { registration = null; return false; } }
    internal static class VanillaCampaignRuntime { internal static bool Owns(TeamData team) => false; }
    internal static class PlayNowRosterContext { internal static void RefreshLocked(TeamRosterData roster) { } }
    internal static class GoalieRosterMenu {
        internal static void PreviewTeamFor(SkaterPreviewInUI owner, SkaterData skater, ref TeamData team) { }
        internal static void EntryLabel(LineupMenu menu) { }
    }
    internal static class Plugin { internal static bool IsDefaultMode; internal static Logger Log = new(); }
    internal class Logger { internal void LogWarning(string value) { } }
    internal static class CustomTeamRules { internal static bool IsCustom(TeamData team) => team?.id == "qa"; }
    internal static class AuditTrace { internal static bool Enabled => false; internal static void Record(string a, string b, string c, string d) { } }
}
namespace Data {
 public class SkaterData { }
 public class TeamData { public string id, teamName; public IntPtr Pointer; public bool isMultiLineup; public int benchSize; public List<ForwardData> benchPlayers = new(); }
 public class ForwardData { public IntPtr Pointer; }
}
namespace Tape2Tape {
    public class Scene { public bool isLoaded = true; }
    public class GameObject { public bool activeInHierarchy = true; public Scene scene = new(); }
    public class Team { public TeamData _teamData; public bool UsesHomeUniform; public GameObject gameObject = new(); }
}
static class GameManager { internal static object _instance = new(); internal static Team HomeTeam, VisitorTeam; }
namespace Tape2Tape.Hockey.UI {
    public class LineupMenu { public TeamRosterData Team; public bool IsHomeTeam, m_AreSkatersLocked; }
}
namespace Tape2Tape.Customization.UI {
    public class TeamRosterData { public TeamData OriginalTeamData; public List<ForwardData> m_BenchForwards = new(); public List<ForwardData> Bench => m_BenchForwards; public bool HasChanged; }
    public class SkaterPreviewInUI { public bool m_IsHome; public object Parent; public T GetComponentInParent<T>(bool includeInactive) where T : class => Parent as T; }
}
