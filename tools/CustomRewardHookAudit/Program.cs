using Mono.Cecil;
using System.Text.Json;

// Metadata only: never load the plugin, interop runtime or game code.
if (args.Length != 3) throw new ArgumentException("runtime.dll interop-directory report.json");
using var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(args[1]);
resolver.AddSearchDirectory(Path.GetFullPath("installer/bepinex/BepInEx/core"));
resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location));
var parameters = new ReaderParameters { AssemblyResolver = resolver };
using var runtime = AssemblyDefinition.ReadAssembly(args[0], parameters);
var checkedHooks = new List<object>(); var errors = new List<string>(); var skipped = new List<string>();
TypeReference Plain(TypeReference type) => type is ByReferenceType byref ? byref.ElementType : type;
bool Accepts(TypeReference patch, TypeReference original) {
    patch = Plain(patch); original = Plain(original);
    if (patch.FullName == original.FullName) return true;
    if (patch.FullName == "System.Object") return true;
    try { for (var type = original.Resolve()?.BaseType; type != null; type = type.Resolve()?.BaseType) if (type.FullName == patch.FullName) return true; } catch { }
    return false;
}
foreach (var type in runtime.MainModule.Types.Where(t => t.Name.StartsWith("CustomReward") || t.Name.StartsWith("CustomTalent") || t.Name.StartsWith("CustomRelic"))) {
    var attributes = type.CustomAttributes.Where(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch").ToArray();
    if (attributes.Length == 0) continue;
    var values = attributes.SelectMany(a => a.ConstructorArguments).ToArray();
    var targetType = values.FirstOrDefault(a => a.Value is TypeReference).Value as TypeReference;
    var name = values.FirstOrDefault(a => a.Type.FullName == "System.String").Value as string;
    if (targetType == null || name == null) { skipped.Add(type.FullName + ": dynamic target declaration"); continue; }
    var methodType = values.FirstOrDefault(a => a.Type.FullName == "HarmonyLib.MethodType");
    if (methodType.Type != null) {
        var definition = methodType.Type.Resolve();
        string kind = definition.Fields.First(f => f.HasConstant && Convert.ToInt32(f.Constant) == Convert.ToInt32(methodType.Value)).Name;
        name = kind switch { "Getter" => "get_" + name, "Setter" => "set_" + name, _ => name };
    }
    var target = targetType.Resolve();
    var candidates = target.Methods.Where(m => m.Name == name).ToArray();
    var argumentTypes = values.FirstOrDefault(a => a.Type.FullName == "System.Type[]").Value as CustomAttributeArgument[];
    if (argumentTypes != null) candidates = candidates.Where(m => m.Parameters.Count == argumentTypes.Length
        && m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(argumentTypes.Select(a => ((TypeReference)a.Value).FullName))).ToArray();
    if (candidates.Length != 1) { errors.Add(type.FullName + ": target resolves to " + candidates.Length + " methods: " + targetType.FullName + "." + name); continue; }
    var original = candidates[0];
    foreach (var patch in type.Methods.Where(m => m.CustomAttributes.Any(a => a.AttributeType.FullName is "HarmonyLib.HarmonyPrefix" or "HarmonyLib.HarmonyPostfix" or "HarmonyLib.HarmonyFinalizer"))) {
        foreach (var parameter in patch.Parameters) {
            TypeReference? expected = null;
            if (parameter.Name.StartsWith("__") && int.TryParse(parameter.Name.AsSpan(2), out int index)) {
                if (index < 0 || index >= original.Parameters.Count) { errors.Add(patch.FullName + ": invalid argument " + parameter.Name); continue; }
                expected = original.Parameters[index].ParameterType;
            } else if (parameter.Name == "__result") expected = original.ReturnType;
            else if (!parameter.Name.StartsWith("__")) expected = original.Parameters.FirstOrDefault(p => p.Name == parameter.Name)?.ParameterType;
            else if (parameter.Name.StartsWith("___")) expected = target.Fields.FirstOrDefault(f => f.Name == parameter.Name[3..])?.FieldType;
            if (expected != null && !Accepts(parameter.ParameterType, expected)) errors.Add(patch.FullName + ": " + parameter.Name + " expects " + expected.FullName + ", patch uses " + parameter.ParameterType.FullName);
        }
        checkedHooks.Add(new { patch = patch.FullName, native = original.FullName });
    }
}
var report = new { evidence = "Static metadata signatures only; no game execution or native detour validation", checkedHooks, errors, skipped };
File.WriteAllText(args[2], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{checkedHooks.Count} patch signatures inspected; {errors.Count} errors; {skipped.Count} dynamic target declarations listed separately.");
foreach (string error in errors) Console.WriteLine(error);
Environment.ExitCode = errors.Count == 0 ? 0 : 1;
