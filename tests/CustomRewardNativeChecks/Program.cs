using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Buffers.Binary;
using System.Reflection.PortableExecutable;

// Read-only validation of the ACTUAL compiled positive hook list against the
// actual generated wrappers. Does not load/execute the mod or game assemblies.
if (args.Length is not (2 or 3)) throw new ArgumentException("compiled-runtime.dll current-interop-directory [GameAssembly.dll]");
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
    if (checks != 12) { failures++; Console.WriteLine($"FAIL expected all 12 inlined live-query hooks, found {checks}"); }
    // Build 26471720 emits a fixed one-argument query for both injury-stat paths.
    // Check the actual compiled registrations as well as the managed scope list.
    var fixedMethods = runtime.MainModule.Types.Single(t => t.FullName == "EndlessMode.CustomRewardCapabilities")
        .Methods.Single(m => m.Name == "InstallFixedQueries").Body.Instructions;
    using var image = args.Length == 3 ? new PEReader(File.OpenRead(args[2])) : null;
    var entries = new Dictionary<(string type, string query), int>();
    foreach (var instruction in fixedMethods)
    {
        if (instruction.OpCode != OpCodes.Call || instruction.Operand is not GenericInstanceMethod call || call.Name != "Fixed") continue;
        var prologue = instruction.Previous;
        var query = prologue.Previous;
        var address = query.Previous;
        if (prologue.OpCode != OpCodes.Ldstr || query.OpCode != OpCodes.Ldstr || address.OpCode != OpCodes.Ldc_I4)
            throw new InvalidDataException("Unexpected compiled fixed-query declaration shape.");
        var key = (call.GenericArguments.Single().FullName, (string)query.Operand);
        int rva = (int)address.Operand;
        if (!entries.TryAdd(key, rva)) throw new InvalidDataException("Duplicate fixed query " + key);
        if (image == null) continue;
        var expected = Convert.FromHexString((string)prologue.Operand);
        bool matches = expected.Length == 24 && image.GetSectionData(rva).GetContent(0, expected.Length).SequenceEqual(expected);
        Check(matches, $"native prologue {key} at 0x{rva:X}");
    }
    Check(entries.Count == 20 && entries.Values.Distinct().Count() == 20, "20 distinct fixed-query registrations and native addresses");
    bool hasInjuryQuery = entries.TryGetValue(("Rogue.SadoMaso", "HaveTalentCached"), out int injuryRva);
    Check(hasInjuryQuery, "SadoMaso fixed live query supplies injury immunity");
    if (image != null && hasInjuryQuery)
    {
        foreach (var (rva, label) in new[] { (0x40fc4c9, "normal"), (0x40fc5c4, "balanced") })
        {
            var site = image.GetSectionData(rva).GetContent(0, 5).AsSpan();
            Check(site[0] == 0xe8 && rva + 5 + BinaryPrimitives.ReadInt32LittleEndian(site[1..]) == injuryRva,
                $"native {label} injury calculation calls the registered query");
        }
    }
    Console.WriteLine($"{checks} compiled native hook checks; {failures} failures. No game code executed.");
    return failures == 0 ? 0 : 1;
}
finally { foreach (var interop in opened.Values) interop.Dispose(); }

void Check(bool passes, string detail)
{
    checks++;
    if (!passes) failures++;
    Console.WriteLine($"{(passes ? "PASS" : "FAIL")} {detail}");
}
