using CampaignCore;
using EndlessMode;
using HarmonyLib;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

string gameDir = args.Length > 0 ? args[0] : @"C:\Steam\steamapps\common\Tape to Tape";
var files = Directory.GetFiles(Path.Combine(gameDir, "BepInEx", "interop"), "*.dll")
    .Concat(Directory.GetFiles(Path.Combine(gameDir, "BepInEx", "core"), "*.dll"))
    .Concat(Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll"));
using var metadata = new MetadataLoadContext(new PathAssemblyResolver(files), "System.Private.CoreLib");
AccessTools.Game = metadata.LoadFromAssemblyPath(Path.Combine(gameDir, "BepInEx", "interop", "MainScriptsAssembly.dll"));
AccessTools.Other = metadata.LoadFromAssemblyPath(Path.Combine(gameDir, "BepInEx", "interop", "Assembly-CSharp.dll"));
var raw = JsonSerializer.Deserialize<RawNativeHookContract[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "jersey-logo-targets.json")));
var allRaw = NativeCompatibilityProfile.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "native-compatibility.json"))).RawHooks;
var consumers = OpponentEffects.All.SelectMany(e => e.Consumers).Distinct().ToArray();
string Key(string value) => NativeOpponentScopes.Identity(value);
MethodInfo[] Resolve(string consumer)
{
    int dot = consumer.LastIndexOf('.'); string name = consumer[..dot]; int slash = name.IndexOf('/');
    var type = AccessTools.TypeByName(slash < 0 ? name : name[..slash]); if (type == null) throw new Exception("Missing type for " + consumer);
    if (slash >= 0) type = type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
        .Single(t => Key(t.Name) == Key(name[(slash + 1)..]));
    return type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
        .Where(m => m.Name == consumer[(dot + 1)..]).ToArray();
}
var methods = consumers.SelectMany(c => Resolve(c).Select(m => (Consumer: c, Method: m))).ToArray();
var shared = methods.Where(x => allRaw.Any(c => Key(c.Identity) == Key(x.Consumer))).ToArray();
var module = new IntPtr(0x180000000);
Dictionary<string, int> auditedAddresses = null;
if (args.Length > 1) {
    using var audit = JsonDocument.Parse(File.ReadAllText(args[1]));
    auditedAddresses = audit.RootElement.GetProperty("rows").EnumerateArray().ToDictionary(
        row => Key(row.GetProperty("consumer").GetString()),
        row => Convert.ToInt32(row.GetProperty("native").EnumerateArray().Single().GetProperty("rva").GetString(), 16));
    AssertAudit(auditedAddresses.Count == 91);
}
void AssertAudit(bool value) { if (!value) throw new Exception("Native address audit must cover all 91 consumers."); }
// Supplied addresses let us exercise the production installer using real game
// metadata without initializing IL2CPP or modifying native memory in this process.
IntPtr Address(MethodInfo method) => module + (auditedAddresses != null ? auditedAddresses[Key(method.DeclaringType.FullName + "." + method.Name)] : raw.SingleOrDefault(c => Key(c.Identity) == Key(method.DeclaringType.FullName + "." + method.Name))?.Rva
    ?? (0x1000 + method.MetadataToken));
NativeOpponentScopes Registry(bool own = true)
{
    var registry = new NativeOpponentScopes(raw) { Module = module };
    if (own) foreach (var contract in raw) registry.Own(contract);
    return registry;
}
int passed = 0, failed = 0;
void Assert(bool value, string reason = "Assertion failed") { if (!value) throw new Exception(reason); }
void Check(string name, Action test)
{
    try { test(); Console.WriteLine("PASS " + name); passed++; }
    catch (Exception ex) { Console.WriteLine("FAIL " + name + ": " + ex.Message); failed++; }
}
Check("All production consumers resolve and exactly two collide across every raw contract", () => {
    Assert(methods.Length == 91 && consumers.All(c => Resolve(c).Length > 0), $"methods={methods.Length}, unresolved={string.Join(",", consumers.Where(c => Resolve(c).Length == 0))}");
    Assert(allRaw.Count == 21 && raw.Length == 21, $"allRaw={allRaw.Count}, raw={raw.Length}");
    Assert(shared.Length == 2);
    Assert(shared.Select(x => raw.Single(c => Key(c.Identity) == Key(x.Consumer)).Id).Order().SequenceEqual(new[] { "2-move", "7-move" }));
    Console.WriteLine($"AUDIT {consumers.Length} consumers / {methods.Length} methods / {allRaw.Count} raw contracts: 2 raw, 89 Harmony.");
});
Check("Production installer makes no duplicate hook request", () => {
    JerseyLogoNativeHooks.Scopes = Registry(); JerseyLogoNativeHooks.Address = Address;
    Plugin.Log.Warnings.Clear(); var harmony = new Harmony(); OpponentEffectScope.Install(harmony);
    Assert(harmony.Patched.Count == 89 && Plugin.Log.Warnings.Count == 0, $"patched={harmony.Patched.Count}, warnings={string.Join(" | ", Plugin.Log.Warnings)}");
    Assert(!harmony.Patched.Any(m => raw.Any(c => Key(c.Identity) == Key(m.DeclaringType.FullName + "." + m.Name))));
    foreach (var item in shared) Assert(JerseyLogoNativeHooks.Scopes.Consumer(raw.Single(c => Key(c.Identity) == Key(item.Consumer)).Id) == item.Consumer);
});
Check("Blocked install reserves both entries and leaves unrelated consumers intact", () => {
    JerseyLogoNativeHooks.Scopes = Registry(false); JerseyLogoNativeHooks.Address = Address;
    Plugin.Log.Warnings.Clear(); var harmony = new Harmony(); OpponentEffectScope.Install(harmony);
    Assert(harmony.Patched.Count == 89 && Plugin.Log.Warnings.Count == 2, $"patched={harmony.Patched.Count}, warnings={string.Join(" | ", Plugin.Log.Warnings)}");
});
Check("Rollback retains ownership only for detours whose removal failed", () => {
    var registry = Registry(); var removed = raw.Single(c => c.Id == "2-move"); registry.Release(removed);
    foreach (var item in shared) {
        var route = registry.Register(item.Consumer, item.Method, Address, out _);
        Assert(route == (Key(item.Consumer) == Key(removed.Identity) ? OpponentScopeRoute.Blocked : OpponentScopeRoute.Raw));
    }
});
Check("Stable compiler ordinal mapping retains original consumer route", () => {
    var item = shared.Single(x => x.Consumer.Contains("ReapplySkin"));
    Assert(item.Consumer.Contains("105") && item.Method.DeclaringType.Name.Contains("115"));
    var registry = Registry(); Assert(registry.Register(item.Consumer, item.Method, Address, out _) == OpponentScopeRoute.Raw);
    Assert(registry.Consumer("7-move") == item.Consumer);
});
Check("Unknown native addresses block instead of falling through to Harmony", () => {
    var registry = Registry(); var item = methods[0];
    Assert(registry.Register(item.Consumer, item.Method, _ => IntPtr.Zero, out _) == OpponentScopeRoute.Blocked);
    Assert(registry.Register(item.Consumer, item.Method, _ => throw new InvalidOperationException("unreadable"), out _) == OpponentScopeRoute.Blocked);
    registry.Module = IntPtr.Zero;
    Assert(registry.Register(item.Consumer, item.Method, Address, out _) == OpponentScopeRoute.Blocked);
});
Check("Aliased native address cannot acquire another raw owner", () => {
    var registry = Registry(); var unrelated = methods.First(x => !shared.Contains(x));
    Assert(registry.Register(unrelated.Consumer, unrelated.Method, _ => module + raw.Single(c => c.Id == "2-move").Rva, out _) == OpponentScopeRoute.Blocked);
});
Check("Reserved name with changed address or resolved method blocks", () => {
    var registry = Registry(); var item = shared[0];
    Assert(registry.Register(item.Consumer, item.Method, _ => module + 123, out _) == OpponentScopeRoute.Blocked);
    Assert(registry.Register(item.Consumer, methods[0].Method, _ => Address(item.Method), out _) == OpponentScopeRoute.Blocked);
});
Check("Repeated registration is idempotent and release removes activation", () => {
    var registry = Registry(); var item = shared[0]; var contract = raw.Single(c => Key(c.Identity) == Key(item.Consumer));
    for (int i = 0; i < 2; i++) Assert(registry.Register(item.Consumer, item.Method, Address, out _) == OpponentScopeRoute.Raw);
    registry.Release(contract); Assert(registry.Consumer(contract.Id) == null);
});
Check("Native scopes nest and restore parent on every slice", () => {
    var registry = Registry(); foreach (var item in shared) registry.Register(item.Consumer, item.Method, Address, out _);
    OpponentEffectScope.Current = "outer-managed"; int count = 0;
    for (int slice = 0; slice < 2; slice++) {
        OpponentEffectScope.RunNative(registry.Consumer("2-move"), () => {
            Assert(OpponentEffectScope.Current == registry.Consumer("2-move")); count++;
            OpponentEffectScope.RunNative(registry.Consumer("7-move"), () => { Assert(OpponentEffectScope.Current == registry.Consumer("7-move")); count++; });
            Assert(OpponentEffectScope.Current == registry.Consumer("2-move"));
        });
        Assert(OpponentEffectScope.Current == "outer-managed");
    }
    Assert(count == 4); OpponentEffectScope.Current = null;
});
Check("Throwing native original executes once and restores exact prior scope", () => {
    OpponentEffectScope.Current = "parent"; var expected = new InvalidOperationException("native-call"); int count = 0;
    try { OpponentEffectScope.RunNative(shared[0].Consumer, () => { count++; throw expected; }); throw new Exception("Exception swallowed"); }
    catch (InvalidOperationException ex) { Assert(ReferenceEquals(ex, expected)); }
    Assert(count == 1 && OpponentEffectScope.Current == "parent"); OpponentEffectScope.Current = null;
});
Check("Unregistered raw entries preserve caller scope and pass through once", () => {
    var registry = Registry(); OpponentEffectScope.Current = "parent"; int count = 0;
    OpponentEffectScope.RunNative(registry.Consumer("1-move"), () => { count++; Assert(OpponentEffectScope.Current == "parent"); });
    Assert(count == 1 && OpponentEffectScope.Current == "parent"); OpponentEffectScope.Current = null;
});
Check("Native scopes remain thread local", () => {
    OpponentEffectScope.Current = "main"; Exception failure = null;
    var thread = new Thread(() => { try { Assert(OpponentEffectScope.Current == null); OpponentEffectScope.RunNative("worker", () => Assert(OpponentEffectScope.Current == "worker")); Assert(OpponentEffectScope.Current == null); } catch (Exception ex) { failure = ex; } });
    thread.Start(); thread.Join(); Assert(failure == null && OpponentEffectScope.Current == "main"); OpponentEffectScope.Current = null;
});
Check("Shipped pointer resolver finds all actual generated MethodInfo fields without native initialization", () => {
    System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (context, name) => {
        foreach (string directory in new[] { "interop", "core" }) {
            string path = Path.Combine(gameDir, "BepInEx", directory, name.Name + ".dll");
            if (File.Exists(path)) return context.LoadFromAssemblyPath(Path.GetFullPath(path));
        }
        return null;
    };
    var loaded = new Dictionary<string, Assembly>();
    foreach (var item in methods) {
        string name = item.Method.DeclaringType.Assembly.GetName().Name;
        if (!loaded.TryGetValue(name, out var assembly)) loaded[name] = assembly = Assembly.LoadFrom(Path.Combine(gameDir, "BepInEx", "interop", name + ".dll"));
        var method = assembly.ManifestModule.ResolveMethod(item.Method.MetadataToken);
        var field = Il2CppInterop.Common.Il2CppInteropUtils.GetIl2CppMethodInfoPointerFieldForGeneratedMethod(method);
        Assert(field != null && field.FieldType == typeof(IntPtr), item.Consumer + " has no generated MethodInfo field");
        // Never GetValue: that would execute the generated native type initializer.
    }
    Console.WriteLine($"Verified shipped resolver for {methods.Length} generated wrappers; no native fields read.");
});
Console.WriteLine($"{passed} passed; {failed} failed.");
return failed == 0 ? 0 : 1;
