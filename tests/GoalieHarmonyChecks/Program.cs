using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using HarmonyLib;
using HarmonyLib.Public.Patching;
using MonoMod.Cil;
using MonoMod.Utils;

if (args.Length != 2) throw new ArgumentException("runtime.dll game-directory");
string game = Path.GetFullPath(args[1]);
AssemblyLoadContext.Default.Resolving += (context, name) => {
    foreach (string directory in new[] { Path.Combine(game, "BepInEx/core"), Path.Combine(game, "BepInEx/interop") }) {
        string path = Path.Combine(directory, name.Name + ".dll");
        if (File.Exists(path)) return context.LoadFromAssemblyPath(path);
    }
    return null;
};
// Load metadata only. Never call a game wrapper or patch/detour an installed method.
AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(game, "BepInEx/interop/MainScriptsAssembly.dll"));
var runtime = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(args[0]));
const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
int passed=0, failed=0;
void Check(bool condition,string label) { if(!condition) throw new Exception(label); passed++; Console.WriteLine("PASS "+label); }
void Run(string label,Action test) { try { test(); } catch(Exception ex) { failed++; Console.WriteLine("FAIL "+label+": "+ex); } }
Run("production wrapper API contract",()=> { runtime.GetType("EndlessMode.GoaliePersistenceContracts",true)!.GetMethod("ValidateWrapperApi",flags)!.Invoke(null,null); Check(true,"production wrapper API contract resolves against current interop"); });
MethodInfo Generate(MethodInfo original, PatchInfo patches) {
    using var dmd = new DynamicMethodDefinition(original);
    using var context = new ILContext(dmd.Definition);
    HarmonyManipulator.Manipulate(original, patches, context);
    return dmd.Generate();
}
PatchInfo Plan(MethodInfo? prefix, MethodInfo? finalizer, MethodInfo? postfix = null) {
    var plan = new PatchInfo();
    if(prefix != null) plan.AddPrefix(prefix,"EndlessMode.GoalieLifecycle",Priority.First,Array.Empty<string>(),Array.Empty<string>(),false);
    if(postfix != null) plan.AddPostfix(postfix,"EndlessMode.GoalieLifecycle",Priority.Normal,Array.Empty<string>(),Array.Empty<string>(),false);
    if(finalizer != null) plan.AddFinalizer(finalizer,"EndlessMode.GoalieLifecycle",Priority.First,Array.Empty<string>(),Array.Empty<string>(),false);
    return plan;
}
foreach(var assembly in new[] { typeof(Harmony).Assembly, typeof(DynamicMethodDefinition).Assembly, typeof(Mono.Cecil.AssemblyDefinition).Assembly })
    Run("installed provider identity "+assembly.GetName().Name,()=> {
        var installed=Path.Combine(game,"BepInEx/core",Path.GetFileName(assembly.Location));
        Check(SHA256.HashData(File.ReadAllBytes(assembly.Location)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(installed))),
            "exact installed provider bytes: "+assembly.GetName().Name);
    });
foreach(string owner in new[] { "FreshRunActivation", "GoaliePersistenceContracts", "GoalieRosterMenu", "GoalieMatchLaunchGate", "GoalieMatchCompletion" }) Run("approved production plan " + owner,()=> {
    using var stream=runtime.GetManifestResourceStream("NativeCompatibility.json")!;
    var profileType=runtime.GetType("CampaignCore.NativeCompatibilityProfile",true)!;
    var profile=profileType.GetMethod("Read")!.Invoke(null,new object[]{new StreamReader(stream).ReadToEnd()})!;
    var type=runtime.GetType("EndlessMode." + owner,true)!;
    var required=(Array)type.GetField("Required",flags)!.GetValue(null)!;
    var first=required.GetValue(0)!;
    var feature=first.GetType().GetProperty("Feature")!.GetValue(first)!;
    runtime.GetType("CampaignCore.NamedManagedHookContract",true)!.GetMethod("RequireExactSet")!.Invoke(null,new[]{required,profileType.GetProperty("NamedHooks")!.GetValue(profile),feature});
    Check(true,"production " + owner + " plan matches approved embedded contracts");
});
foreach (string owner in new[] { "FreshRunActivation", "GoaliePersistenceContracts", "GoalieRosterMenu", "GoalieMatchLaunchGate", "GoalieMatchCompletion" }) {
    var type=runtime.GetType("EndlessMode."+owner,true)!;
    var targets=(Array)type.GetMethod("Resolve",flags)!.Invoke(null,null)!;
    foreach(object tuple in targets) {
        var tupleType=tuple.GetType();
        var original=(MethodInfo)tupleType.GetField("Item1")!.GetValue(tuple)!;
        var prefix=(MethodInfo?)tupleType.GetField("Item2")!.GetValue(tuple);
        var third=(MethodInfo?)tupleType.GetField("Item3")!.GetValue(tuple);
        Run(owner+" "+original.DeclaringType!.FullName+"."+original.Name,()=> {
            var result=Generate(original,Plan(prefix,owner=="GoalieMatchCompletion"?third:null,owner!="GoalieMatchCompletion"?third:null));
            Check(result!=null,"actual provider compiles "+owner+" "+original.DeclaringType!.FullName+"."+original.Name);
        });
    }
}
Run("provider execution provenance",()=> {
    var completion=runtime.GetType("EndlessMode.GoalieMatchCompletion",true)!;
    var prefix=completion.GetMethod("ExecutionPrefix",flags) ?? throw new Exception("Missing production execution-observation prefix");
    var target=typeof(ExecutionProbe).GetMethod(nameof(ExecutionProbe.Original))!;
    var witness=typeof(ExecutionProbe).GetMethod(nameof(ExecutionProbe.Witness))!;
    foreach(string scenario in new[] { "normal", "body-throws", "foreign-before", "foreign-after", "prefix-throws" }) {
        var plan=Plan(prefix,witness);
        if(scenario.StartsWith("foreign-")) plan.AddPrefix(typeof(ExecutionProbe).GetMethod(nameof(ExecutionProbe.Skip))!,"foreign",scenario=="foreign-before"?Priority.First+1:Priority.Last,Array.Empty<string>(),Array.Empty<string>(),false);
        if(scenario=="prefix-throws") plan.AddPrefix(typeof(ExecutionProbe).GetMethod(nameof(ExecutionProbe.ThrowPrefix))!,"foreign",Priority.Last,Array.Empty<string>(),Array.Empty<string>(),false);
        var generated=Generate(target,plan);
        ExecutionProbe.Reset(scenario=="body-throws");
        Exception? observed=null;
        try { generated.Invoke(null,null); } catch(TargetInvocationException ex) { observed=ex.InnerException; }
        Check(ExecutionProbe.Finalizations==1,"one finalization: "+scenario);
        Check(ExecutionProbe.Calls==(scenario is "normal" or "body-throws"?1:0),"original execution count: "+scenario);
        if(scenario.StartsWith("foreign-")) Check(!ExecutionProbe.RanOriginal && observed==null,"foreign skip stays false: "+scenario);
        if(scenario=="normal") Check(ExecutionProbe.RanOriginal && observed==null,"successful body provenance retained");
        if(scenario.EndsWith("throws")) Check(ReferenceEquals(observed,ExecutionProbe.Failure) && ReferenceEquals(ExecutionProbe.Error,ExecutionProbe.Failure),"native/prefix exception preserved: "+scenario);
    }
});
Console.WriteLine($"{passed} checks passed; {failed} groups failed; installed Harmony {typeof(Harmony).Assembly.Location}; no native execution or detours.");
Environment.ExitCode=failed==0?0:1;

public static class ExecutionProbe {
    public static int Calls,Finalizations;
    public static bool ThrowBody,RanOriginal;
    public static Exception? Error;
    public static readonly Exception Failure=new InvalidOperationException("execution probe failure");
    public static void Reset(bool throwBody) { Calls=Finalizations=0;ThrowBody=throwBody;RanOriginal=false;Error=null; }
    public static void Original() { Calls++;if(ThrowBody) throw Failure; }
    public static bool Skip()=>false;
    public static void ThrowPrefix()=>throw Failure;
    public static Exception? Witness(bool __runOriginal,Exception? __exception) { Finalizations++;RanOriginal=__runOriginal;Error=__exception;return __exception; }
}
