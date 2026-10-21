using System.Reflection;
namespace CampaignCore {
    internal class NamedManagedHookContract {
        internal string Feature;
        internal static void RequireExactSet(NamedManagedHookContract[] required, object approved, string feature) {
            if (TestState.Missing == feature) throw new InvalidOperationException("missing " + feature);
        }
    }
    internal class NativeCompatibilityProfile { internal object NamedHooks = new(); }
}
internal static class TestState {
    internal static int Failure = -1, Calls; internal static string Missing; internal static bool Subscribed; internal static List<string> Errors = new();
    internal static MethodInfo Method(int i) => typeof(Targets).GetMethod(i==32 ? "HandleFreeAgentsLeaving" : "M" + i);
    internal static MethodInfo Patch => typeof(Targets).GetMethod("Callback");
    internal static void Reset() { Failure=-1;Calls=0;Missing=null;Subscribed=false;Errors.Clear();HarmonyLib.Harmony.State.Clear();EndlessMode.GoaliePersistenceBridge.SetEnabled(false);EndlessMode.GoalieRosterMenu.SetInstalled(false);
        var target=Method(32); HarmonyLib.Harmony.State[target]=new();
        HarmonyLib.Harmony.State[target].Prefixes.Add(new() { owner="com.mods.customcampaign", priority=HarmonyLib.Priority.Normal, PatchMethod=typeof(EndlessMode.CustomRewardContractRetention).GetMethod("Prefix",BindingFlags.Static|BindingFlags.NonPublic) });
    }
}
public static class Targets {
    public static void Callback() {}
    public static void M0() {}
    public static void M1() {}
    public static void M2() {}
    public static void M3() {}
    public static void M4() {}
    public static void M5() {}
    public static void M6() {}
    public static void M7() {}
    public static void M8() {}
    public static void M9() {}
    public static void M10() {}
    public static void M11() {}
    public static void M12() {}
    public static void M13() {}
    public static void M14() {}
    public static void M15() {}
    public static void M16() {}
    public static void M17() {}
    public static void M18() {}
    public static void M19() {}
    public static void M20() {}
    public static void M21() {}
    public static void M22() {}
    public static void M23() {}
    public static void M24() {}
    public static void M25() {}
    public static void M26() {}
    public static void M27() {}
    public static void M28() {}
    public static void M29() {}
    public static void M30() {}
    public static void M31() {}
    public static void HandleFreeAgentsLeaving() {}
    public static void M33() {}
    public static void M34() {}
}
namespace HarmonyLib {
    public static class Priority { public const int First=800, Normal=400, Last=0; }
    public sealed class HarmonyMethod { public MethodInfo method; public int priority; public HarmonyMethod(MethodInfo m) { method=m; } }
    public sealed class Patch { public string owner; public MethodInfo PatchMethod; public int priority; }
    public sealed class Patches {
        public List<Patch> Prefixes=new(),Postfixes=new(),Finalizers=new();
        public IEnumerable<string> Owners => Prefixes.Concat(Postfixes).Concat(Finalizers).Select(p=>p.owner);
    }
    public sealed class Harmony {
        public static Dictionary<MethodInfo,Patches> State=new(); private string owner;
        public Harmony(string id) { owner=id; }
        public static Patches GetPatchInfo(MethodInfo m) => State.GetValueOrDefault(m);
        public void Patch(MethodInfo m,HarmonyMethod prefix,HarmonyMethod postfix,HarmonyMethod finalizer=null) {
            var p=State.GetValueOrDefault(m) ?? (State[m]=new());
            void Add(List<Patch> list,HarmonyMethod patch) { if(patch!=null) list.Add(new Patch { owner=owner,PatchMethod=patch.method,priority=patch.priority }); }
            Add(p.Prefixes,prefix);Add(p.Postfixes,postfix);Add(p.Finalizers,finalizer);
            if(TestState.Calls++==TestState.Failure) throw new InvalidOperationException("partial Patch failure");
        }
        public void UnpatchSelf() { foreach(var p in State.Values) { p.Prefixes.RemoveAll(p=>p.owner==owner);p.Postfixes.RemoveAll(p=>p.owner==owner);p.Finalizers.RemoveAll(p=>p.owner==owner); } }
    }
}
namespace EndlessMode {
    internal static class CustomRewardContractRetention { private static void Prefix() {} }
    internal static class Plugin { internal static LogSink Log=new(); }
    internal class LogSink { internal void LogInfo(string m) {} internal void LogWarning(string m) {TestState.Errors.Add(m);} }
    internal static class MatchRuleRunner { internal static void Install() {} }
    internal static class GoaliePersistenceBridge { internal static bool Enabled {get;private set;} internal static void SetEnabled(bool b) { Enabled=b; } }
    internal static class GoaliePersistenceContracts {
        internal static CampaignCore.NativeCompatibilityProfile Preflight() {if(TestState.Missing=="checkpoint")throw new InvalidOperationException();return new();}
        internal static (MethodInfo Native,MethodInfo Prefix,MethodInfo Postfix)[] Resolve() => Enumerable.Range(0,8).Select(i=>(TestState.Method(i),TestState.Patch,(MethodInfo)null)).ToArray();
    }
    internal static class GoalieRosterMenu {
        internal static bool Installed; internal static void SetInstalled(bool b) {Installed=b;}
        internal static CampaignCore.NamedManagedHookContract[] Required={new(){Feature="menu"}};
        internal static (MethodInfo Native,MethodInfo Prefix,MethodInfo Postfix)[] Resolve() => Enumerable.Range(8,7).Select(i=>(TestState.Method(i),TestState.Patch,(MethodInfo)null)).ToArray();
    }
    internal static class GoalieMatchLaunchGate {
        internal static CampaignCore.NamedManagedHookContract[] Required={new(){Feature="launch"}};
        internal static (MethodInfo Native,MethodInfo Prefix,MethodInfo Postfix)[] Resolve() => Enumerable.Range(15,6).Select(i=>(TestState.Method(i),TestState.Patch,i==17?TestState.Patch:null)).ToArray();
    }
    internal static class GoalieMatchCompletion {
        internal static CampaignCore.NamedManagedHookContract[] Required={new(){Feature="completion"}};
        internal static (MethodInfo Native,MethodInfo Prefix,MethodInfo Finalizer)[] Resolve() => Enumerable.Range(21,14).Select(i=>(TestState.Method(i),i<29?TestState.Patch:null,TestState.Patch)).ToArray();
        internal static void Subscribe(){TestState.Subscribed=true;}
        internal static void Unsubscribe(){TestState.Subscribed=false;}
    }
}
internal static class CoordinatorChecks {
    internal static void Run() {
        int count=0;
        void Check(bool ok,string label) {if(!ok)throw new Exception(label);count++;}
        for(int fail=0;fail<35;fail++) {
            TestState.Reset();TestState.Failure=fail;EndlessMode.GoalieLifecycleActivation.Install();
            Check(!EndlessMode.GoaliePersistenceBridge.Enabled&&!EndlessMode.GoalieRosterMenu.Installed&&!TestState.Subscribed,"partial failure disabled");
            Check(HarmonyLib.Harmony.State.Values.All(p=>!p.Owners.Any(o=>o=="EndlessMode.GoalieLifecycle")),"remove including failing Patch mutation");
        }
        foreach(string feature in new[]{"checkpoint","menu","launch","completion"}) {
            TestState.Reset();TestState.Missing=feature;EndlessMode.GoalieLifecycleActivation.Install();
            Check(!EndlessMode.GoaliePersistenceBridge.Enabled&&TestState.Calls==0&&!TestState.Subscribed,"missing prerequisite installs zero");
        }
        TestState.Reset();HarmonyLib.Harmony.State.Clear();EndlessMode.GoalieLifecycleActivation.Install();
        Check(!EndlessMode.GoaliePersistenceBridge.Enabled&&TestState.Calls==0,"missing retention observer blocks activation");
        TestState.Reset();HarmonyLib.Harmony.State[TestState.Method(32)].Prefixes.Single().priority=HarmonyLib.Priority.First;EndlessMode.GoalieLifecycleActivation.Install();
        Check(!EndlessMode.GoaliePersistenceBridge.Enabled&&TestState.Calls==0,"misordered retention observer blocks activation");
        TestState.Reset();EndlessMode.GoalieLifecycleActivation.Install();
        Check(EndlessMode.GoaliePersistenceBridge.Enabled&&EndlessMode.GoalieRosterMenu.Installed&&TestState.Subscribed&&TestState.Calls==35,"combined activation");
        Check(HarmonyLib.Harmony.State.Values.Sum(p=>p.Finalizers.Count)==14,"all finalizers installed as finalizers");
        Check(HarmonyLib.Harmony.State.Values.SelectMany(p=>p.Finalizers).All(p=>p.priority==HarmonyLib.Priority.First),"finalizer exception observation first");
        Check(HarmonyLib.Harmony.State[TestState.Method(17)].Postfixes.Single().priority==HarmonyLib.Priority.Last,"preview postfix after custom display");
        EndlessMode.GoalieLifecycleActivation.Install();Check(TestState.Calls==35,"repeat activation is inert");
        TestState.Reset();HarmonyLib.Harmony.State[TestState.Method(21)]=new HarmonyLib.Patches();HarmonyLib.Harmony.State[TestState.Method(21)].Prefixes.Add(new(){owner="foreign",priority=800});
        EndlessMode.GoalieLifecycleActivation.Install();Check(!EndlessMode.GoaliePersistenceBridge.Enabled&&TestState.Calls==0,"foreign provenance patch blocks capability");
        Console.WriteLine($"{count} production coordinator checks passed (Harmony doubles; no native execution)");
    }
}
