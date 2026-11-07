using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length != 2) throw new ArgumentException("runtime.dll game-directory");
using var runtime = ModuleDefinition.ReadModule(Path.GetFullPath(args[0]));
using var main = ModuleDefinition.ReadModule(Path.Combine(Path.GetFullPath(args[1]), "BepInEx/interop/MainScriptsAssembly.dll"));
int passed = 0, failed = 0;
void Check(bool condition, string message)
{
    Console.WriteLine((condition ? "PASS " : "FAIL ") + message);
    if (condition) passed++;
    else failed++;
}

var dependencies = new[] {
    ("_talentRepository", "Rogue.Powerups.Repository.ITalentRepository"),
    ("_relicRepository", "Rogue.Relics.Repository.IRelicRepository"),
    ("_abilityRepository", "Rogue.Powerups.Repository.IAbilityRepository")
};
var repository = main.GetType("State.RunDataRepositoryV2");
var parameterTypes = dependencies.Select(d => d.Item2).ToArray();
Check(dependencies.All(d => repository.Properties.Single(p => p.Name == d.Item1).PropertyType.FullName == d.Item2),
    "installed repository exposes the reviewed talent, relic and ability interfaces");
var constructors = main.GetType("State.DeserializerV2").Methods
    .Where(m => m.IsConstructor && !m.IsStatic && m.Parameters.Count == dependencies.Length).ToArray();
Check(constructors.Length == 1 && constructors[0].Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(parameterTypes),
    "installed deserializer has the exact ordered injection signature");
var getter = repository.Properties.Single(p => p.Name == "_fileName").GetMethod;
Check(getter.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "il2cpp_field_get_offset"),
    "repository filename uses generated native field offset lookup");

var bind = runtime.GetType("EndlessMode.GoaliePersistenceBridge").Methods.Single(m => m.Name == "BindDeserializer");
var ctor = bind.Body.Instructions.Single(i => i.OpCode == OpCodes.Newobj && i.Operand is MethodReference m && m.DeclaringType.FullName == "State.DeserializerV2");
var called = (MethodReference)ctor.Operand;
Check(called.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(parameterTypes),
    "compiled goalie bridge invokes the current three-argument constructor");
var cursor = ctor.Previous;
bool gettersMatch = true;
foreach (var (name, type) in dependencies.Reverse())
{
    gettersMatch &= cursor?.Operand is MethodReference method && method.Name == "get_" + name
        && method.ReturnType.FullName == type && method.DeclaringType.FullName == repository.FullName
        && cursor.Previous?.OpCode == OpCodes.Ldarg_0;
    cursor = cursor?.Previous?.Previous;
}
Check(gettersMatch, "compiled goalie bridge supplies the three dependencies from its owning repository in order");
Console.WriteLine($"{passed} checks passed; {failed} failed; metadata and compiled IL inspected.");
return failed == 0 ? 0 : 1;
