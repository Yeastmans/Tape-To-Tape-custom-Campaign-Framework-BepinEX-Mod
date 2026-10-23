using System.Reflection;
using System.Runtime.Loader;
using CampaignCore;
using LoaderBoundary;

if (args.Length != 3) throw new ArgumentException("runtime.dll interop-directory evidence-fixture-directory");
string runtime = Path.GetFullPath(args[0]), interop = Path.GetFullPath(args[1]);
string core = Path.GetFullPath("installer/bepinex/BepInEx/core");
AssemblyLoadContext.Default.Resolving += (context, name) => {
    foreach (string directory in new[] { interop, core }) {
        string path = Path.Combine(directory, name.Name + ".dll");
        if (File.Exists(path)) return context.LoadFromAssemblyPath(path);
    }
    return null;
};
int passed = 0, failed = 0;
void Check(bool condition, string label) { if (!condition) throw new Exception(label); passed++; Console.WriteLine("PASS " + label); }
void Run(string label, Action test) {
    try { test(); } catch (Exception ex) { failed++; Console.WriteLine("FAIL " + label + ": " + (ex is TargetInvocationException ? ex.InnerException : ex)); }
}
Run("production wrapper preflight accepts installed interop", () => {
    var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(runtime);
    var contracts = assembly.GetType("EndlessMode.GoaliePersistenceContracts", true)!;
    contracts.GetMethod("ValidateWrapperApi", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null);
    Check(true, "production wrapper preflight accepts installed interop without invoking native methods");
});
Run("production aggregate lifecycle plan", () => {
    var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(runtime);
    var type = assembly.GetType("EndlessMode.GoalieLifecycleActivation", true)!;
    const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
    var resolve = type.GetMethod("ResolvePlan", flags) ?? throw new Exception("Startup aggregate plan is not independently verifiable");
    var validate = type.GetMethod("ValidatePlan", flags)!;
    var plan = (System.Collections.IList)resolve.Invoke(null, null)!;
    using var stream = assembly.GetManifestResourceStream("NativeCompatibility.json")!;
    using var json = System.Text.Json.JsonDocument.Parse(stream);
    int expected = json.RootElement.GetProperty("NamedHooks").EnumerateArray()
        .Count(h => h.GetProperty("Feature").GetString()!.StartsWith("goalie-", StringComparison.Ordinal));
    Check(plan.Count == expected, "compiled aggregate plan covers every embedded goalie hook");
    void Reject(System.Collections.IList changed, string label) {
        try { validate.Invoke(null, new object[] { changed }); throw new Exception("Accepted " + label); }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { Check(true, label); }
    }
    var duplicate = (System.Collections.IList)resolve.Invoke(null, null)!; duplicate[1] = duplicate[0];
    Reject(duplicate, "aggregate plan rejects duplicate native targets");
    var missing = (System.Collections.IList)resolve.Invoke(null, null)!; missing.RemoveAt(missing.Count - 1);
    Reject(missing, "aggregate plan rejects missing native target");
    var extra = (System.Collections.IList)resolve.Invoke(null, null)!; extra.Add(extra[0]);
    Reject(extra, "aggregate plan rejects extra target");
    var nullEntry = (System.Collections.IList)resolve.Invoke(null, null)!; nullEntry[0] = null;
    Reject(nullEntry, "aggregate plan rejects null target");
});
Run("strict wrapper overload resolution", () => {
    var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(runtime);
    var resolver = assembly.GetType("EndlessMode.GoalieWrapperContract", true)!.GetMethod("Resolve", BindingFlags.Static | BindingFlags.NonPublic)!;
    MethodInfo Resolve(Type type, string name = "Load", bool isStatic = false, Type? result = null, Type[]? parameters = null)
        => (MethodInfo)resolver.Invoke(null, new object[] { type, name, isStatic, result ?? typeof(object), parameters ?? new[] { typeof(string) } })!;
    void Reject(Type type, string label, bool isStatic = false, Type? result = null, Type[]? parameters = null) {
        try { Resolve(type, isStatic: isStatic, result: result, parameters: parameters); throw new Exception("Accepted " + label); }
        catch (TargetInvocationException ex) when (ex.InnerException is MissingMethodException) { Check(true, label); }
    }
    Check(!Resolve(typeof(WrapperFixtures.Overloads)).IsGenericMethod, "non-generic overload selected even with same-argument generic sibling");
    Reject(typeof(WrapperFixtures.GenericOnly), "generic-only API rejected");
    Reject(typeof(WrapperFixtures.StaticOnly), "wrong staticness rejected");
    Reject(typeof(WrapperFixtures.Overloads), "wrong result rejected", result: typeof(string));
    Reject(typeof(WrapperFixtures.CoercibleOnly), "binder-coercible parameter rejected");
    Reject(typeof(WrapperFixtures.Overloads), "wrong argument count rejected", parameters: Type.EmptyTypes);
    Reject(typeof(WrapperFixtures.InheritedOnly), "inherited alternative rejected");
    Reject(new WrapperFixtures.DuplicateMethods(), "ambiguous exact metadata candidates rejected");
    var installed = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "MainScriptsAssembly");
    var es3 = installed.GetType("ES3File", true)!;
    var methods = es3.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        .Where(m => m.Name == "Load" && m.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(string) })).ToArray();
    Check(methods.Any(m => m.IsGenericMethod) && methods.Any(m => !m.IsGenericMethod), "installed ES3File has the generic/non-generic collision");
    foreach (var method in methods) Console.WriteLine("INSTALLED " + method);
});
Run("pool folder classification", () => {
    string root = Path.Combine(Path.GetFullPath(args[2]), Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(GoalieFreeAgents.DirectoryPath(root));
    File.WriteAllText(Path.Combine(GoalieFreeAgents.DirectoryPath(root), "one.txt"), "Position = Goalie\nName = One\n");
    File.WriteAllText(Path.Combine(GoalieFreeAgents.DirectoryPath(root), "two.txt"), "Position = Goalie\nName = Two\n");
    Directory.CreateDirectory(Path.Combine(root, "player_teams", "actual_squad"));
    Directory.CreateDirectory(Path.Combine(root, "player_teams", "Goalie_Free_Agents"));
    Directory.CreateDirectory(Path.Combine(root, "player_teams", "free_agents"));
    File.WriteAllText(Path.Combine(root, "player_teams", "free_agents", "skater.txt"), "Position = Center\n");
    Plugin.ModFolder = root;
    Plugin.LoadPlayerTeamsFolders(true, true);
    Check(!Plugin.PlayerTeamConfigs.ContainsKey("goalie_free_agents"), "goalie pool never registers as a custom squad");
    Check(Plugin.PlayerTeamConfigs.Keys.SequenceEqual(new[] { "actual_squad" }), "ordinary custom squad still registers");
    Check(Plugin.FreeAgentPoolList.Count == 1, "skater free-agent loading remains intact");
    Check(GoalieFreeAgents.SelectedFiles(root).Length == 2, "both authored goalies remain available to recruitment");
});
Console.WriteLine($"{passed} checks passed; {failed} groups failed");
Environment.ExitCode = failed == 0 ? 0 : 1;
