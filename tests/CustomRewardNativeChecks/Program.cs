using Mono.Cecil;
using Mono.Cecil.Cil;

// Read-only validation of the ACTUAL compiled positive hook list against the
// actual generated wrappers. Does not load/execute the mod or game assemblies.
if (args.Length != 2) throw new ArgumentException("compiled-runtime.dll current-interop-directory");
using var runtime = AssemblyDefinition.ReadAssembly(Path.GetFullPath(args[0]));
var opened = new Dictionary<string, AssemblyDefinition>(StringComparer.OrdinalIgnoreCase);
IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> roots)
    => roots.SelectMany(t => new[] { t }.Concat(Types(t.NestedTypes)));
int checks = 0, failures = 0;
try
{
    var scope = runtime.MainModule.Types.Single(t => t.FullName == "EndlessMode.CustomRewardLiveQueryScope");
    var iterator = scope.NestedTypes.Single(t => t.Name.StartsWith("<TargetMethods>", StringComparison.Ordinal));
    var move = iterator.Methods.Single(m => m.Name == "MoveNext");
    TypeReference owner = null; string name = null;
    foreach (var instruction in move.Body.Instructions)
    {
        if (instruction.OpCode == OpCodes.Ldtoken && instruction.Operand is TypeReference type) owner = type;
        if (instruction.OpCode == OpCodes.Ldstr) name = (string)instruction.Operand;
        if (instruction.OpCode != OpCodes.Call || instruction.Operand is not MethodReference method
            || method.DeclaringType.FullName != "HarmonyLib.AccessTools" || method.Name != "Method") continue;
        if (owner == null || name == null) throw new InvalidDataException("Unexpected compiled hook declaration shape.");
        string assemblyName = owner.Scope.Name;
        if (!opened.TryGetValue(assemblyName, out var interop))
            opened[assemblyName] = interop = AssemblyDefinition.ReadAssembly(Path.Combine(args[1], assemblyName + ".dll"));
        var target = Types(interop.MainModule.Types).SingleOrDefault(t => t.FullName == owner.FullName);
        var matches = target?.Methods.Where(m => m.Name == name).ToArray() ?? Array.Empty<MethodDefinition>();
        bool valid = matches.Length == 1;
        Console.WriteLine($"{(valid ? "PASS" : "FAIL")} {owner.FullName}.{name}: {matches.Length} exact wrapper targets");
        checks++; if (!valid) failures++;
        owner = null; name = null;
    }
    if (checks != 13) { failures++; Console.WriteLine($"FAIL expected all 13 positive hooks, found {checks}"); }
    Console.WriteLine($"{checks} compiled native hook checks; {failures} failures. No game code executed.");
    return failures == 0 ? 0 : 1;
}
finally { foreach (var interop in opened.Values) interop.Dispose(); }
