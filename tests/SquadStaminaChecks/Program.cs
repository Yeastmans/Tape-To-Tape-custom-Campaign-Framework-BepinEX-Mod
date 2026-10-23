using EndlessMode;
using Data;
using Skaters;
using Tape2Tape;
using UnityEngine;

int checks = 0;
Team liveTeam = new();
void Check(bool ok, string what) { if (!ok) throw new Exception(what); checks++; Console.WriteLine("PASS " + what); }
void Reset(string squad = "Custom_test", bool? enabled = true)
{
    liveTeam = new Team { _teamData = new TeamData() };
    PlayNowRosterRuntime.Policies.Clear();
    CampaignUtil.CampaignState = new() { Squad = new() { id = squad } };
    SquadBehaviorRuntime.Policy = enabled; Resources.Config = new StaminaConfig(); Resources.Loads = 0;
}
Reset();
var system = new TeamStaminaSystem(); // native Initialize rejects the Custom_* ID when bridge is unavailable
SquadStaminaRuntime.Postfix(system, liveTeam, true);
Check(system.IsActive && system._config == Resources.Config, "custom stamina activates without native comparison bridge");
Check(system.Refills == 1 && system.Rebuilds == 1, "native initialization creates stamina and on-ice membership before HUD factory");
SquadStaminaRuntime.Postfix(system, liveTeam, true);
Check(system.Refills == 1 && system.Rebuilds == 1 && Resources.Loads == 1, "already active native system is not refilled twice");
foreach (var enabled in new bool?[] { true, false, null })
foreach (bool nativeActive in new[] { true, false })
{
    Reset(enabled: enabled);
    system = new() { _isLinesTeam = nativeActive };
    SquadStaminaRuntime.Postfix(system, liveTeam, false);
    Check(system.IsActive == nativeActive && system.Refills == 0 && Resources.Loads == 0, "opponent or noncampaign initialization unchanged");
}
foreach (string squad in new[] { "lines", "vanilla", "Custom_missing", null })
foreach (bool nativeActive in new[] { true, false })
{
    Reset(squad, null); system = new() { _isLinesTeam = nativeActive };
    SquadStaminaRuntime.Postfix(system, liveTeam, true);
    Check(system.IsActive == nativeActive && system.Refills == 0 && Resources.Loads == 0, "unhandled native or missing squad unchanged");
}
Reset(enabled:false); system = new() { _isLinesTeam = true };
SquadStaminaRuntime.Postfix(system, liveTeam, true);
Check(!system.IsActive && system.Refills == 0 && Resources.Loads == 0, "explicit stamina Off suppresses native activation");
Reset(); Resources.Config = null; system = new();
SquadStaminaRuntime.Postfix(system, liveTeam, true);
Check(!system.IsActive && system.Refills == 0 && system.Rebuilds == 0, "missing game configuration safely disables fatigue and HUD");
Reset(); CampaignUtil.CampaignState = null; system = new();
SquadStaminaRuntime.Postfix(system, liveTeam, true);
Check(!system.IsActive && Resources.Loads == 0, "no active campaign leaves native state unchanged");
foreach (bool enabled in new[] { true, false })
{
    Reset(enabled: null); system = new() { _isLinesTeam = !enabled };
    PlayNowRosterRuntime.Policies[liveTeam._teamData] = new ExportedStaminaPolicy { StaminaEnabled = enabled };
    SquadStaminaRuntime.Postfix(system, liveTeam, false);
    Check(system.IsActive == enabled && system.Refills == (enabled ? 1 : 0), "exported team policy applies independently of campaign mode");
}
Console.WriteLine($"{checks} checks passed");

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)] public class HarmonyPatch : Attribute { public HarmonyPatch(Type t,string n) {} public HarmonyPatch(Type t,string n,Type[] args) {} }
    [AttributeUsage(AttributeTargets.Method)] public class HarmonyPostfix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public class HarmonyPrefix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public class HarmonyFinalizer : Attribute { }
}
namespace Skaters
{
    public class StaminaConfig { }
    public class TeamStaminaSystem
    {
        public bool _isLinesTeam; public StaminaConfig _config; public int Refills, Rebuilds;
        public bool IsActive => _isLinesTeam;
        public void Initialize() { }
        public void RefillAll() => Refills++;
        public void RebuildOnIce() => Rebuilds++;
    }
}
namespace UnityEngine
{
    public static class Resources
    {
        public static Skaters.StaminaConfig Config; public static int Loads;
        public static T Load<T>(string name) where T:class { if (name != "StaminaConfig") throw new Exception(name); Loads++; return Config as T; }
    }
}
namespace Data { public class TeamData { static int next; public IntPtr Pointer = (IntPtr)(++next); } }
namespace Tape2Tape
{
    public class Team { static int next; public IntPtr Pointer = (IntPtr)(++next); public TeamData _teamData; public void Initialize(TeamData data, bool first, bool second) {} }
    public class Squad { public string id; }
    public class State { public Squad Squad; public Squad GetCurrentSquad() => Squad; }
    public static class CampaignUtil { public static State CampaignState; }
}
namespace EndlessMode
{
    public class ExportedStaminaPolicy { public bool StaminaEnabled; }
    public static class PlayNowRosterRuntime
    {
        public static readonly Dictionary<TeamData, ExportedStaminaPolicy> Policies = new();
        public static bool TryGet(TeamData team, out ExportedStaminaPolicy policy) { policy = null; return team != null && Policies.TryGetValue(team, out policy); }
        public static bool IsRegistered(TeamData team) => team != null && Policies.ContainsKey(team);
        public static void Forget(TeamData team) { if (team != null) Policies.Remove(team); }
        public static void Clone(TeamData source, TeamData clone) { if (clone != null && TryGet(source, out var policy)) Policies[clone] = policy; }
    }
    public static class SquadBehaviorRuntime
    {
        public static bool? Policy;
        public static bool TryEnabled(string squad, string behavior, out bool enabled)
        { if (behavior != "line_stamina") throw new Exception(behavior); enabled=Policy??false; return squad!=null && Policy.HasValue; }
    }
    public static class Plugin { public static Logger Log = new(); }
    public class Logger { public void LogWarning(string s) {} }
}
