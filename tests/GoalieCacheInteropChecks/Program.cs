using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;

if (args.Length != 2) throw new ArgumentException("runtime.dll game-directory");
string game = Path.GetFullPath(args[1]);
using var runtime = ModuleDefinition.ReadModule(Path.GetFullPath(args[0]));
using var wrappers = ModuleDefinition.ReadModule(Path.Combine(game, "BepInEx/interop/Il2Cppmscorlib.dll"));
using var main = ModuleDefinition.ReadModule(Path.Combine(game, "BepInEx/interop/MainScriptsAssembly.dll"));
using var provider = ModuleDefinition.ReadModule(Path.Combine(game, "BepInEx/core/Il2CppInterop.Runtime.dll"));
int passed = 0, failed = 0;
void Check(bool ok, string label) { Console.WriteLine((ok ? "PASS " : "FAIL ") + label); if (ok) passed++; else failed++; }
IEnumerable<MethodReference> Calls(MethodDefinition method) => method.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>();
bool CallsNamed(MethodDefinition method, string name) => Calls(method).Any(m => m.Name == name);
bool Constant(Instruction instruction, bool value) => instruction.OpCode == (value ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
var data = main.GetType("ES3Internal.ES3Data");
Check(data.BaseType.FullName == "Il2CppSystem.ValueType" && !data.IsValueType, "installed ES3Data is a managed class representing a native value type");
Check(data.Properties.Single(p => p.Name == "type").PropertyType.FullName == "ES3Types.ES3Type"
    && data.Properties.Single(p => p.Name == "bytes").PropertyType.FullName.Contains("Il2CppStructArray`1<System.Byte>"), "installed ES3Data carries two native reference fields");
var dictionary = wrappers.GetType("Il2CppSystem.Collections.Generic.Dictionary`2");
var legacyRead = dictionary.Methods.Single(m => m.Name == "TryGetValue");
Check(CallsNamed(legacyRead, "get_IsValueType") && legacyRead.Body.Variables.Any(v => v.VariableType.MetadataType == MetadataType.IntPtr), "installed out wrapper chooses pointer storage using managed value-type classification");
var legacyConversion = legacyRead.Body.Instructions.Single(i => i.Operand is MethodReference m && m.Name == "PointerToValueGeneric");
Check(Constant(legacyConversion.Previous, false) && Constant(legacyConversion.Previous.Previous, false), "installed out wrapper re-boxes first returned pointer as an unboxed value");
var getter = dictionary.Properties.Single(p => p.Name == "Item").GetMethod;
var getConversion = getter.Body.Instructions.Single(i => i.Operand is MethodReference m && m.Name == "PointerToValueGeneric");
Check(CallsNamed(getter, "il2cpp_runtime_invoke") && Constant(getConversion.Previous, true) && Constant(getConversion.Previous.Previous, false), "installed generic getter retains runtime-invoke boxed return");
var converter = provider.GetType("Il2CppInterop.Runtime.IL2CPP").Methods.Single(m => m.Name == "PointerToValueGeneric");
Check(CallsNamed(converter, "il2cpp_class_is_valuetype") && CallsNamed(converter, "il2cpp_value_box"), "installed converter distinguishes native structs and supports boxed returns");
var objectSetter = wrappers.GetType("Il2CppSystem.Collections.IDictionary").Properties.Single(p => p.Name == "Item").SetMethod;
Check(objectSetter.Parameters.All(p => p.ParameterType.FullName == "Il2CppSystem.Object")
    && CallsNamed(objectSetter, "il2cpp_object_get_virtual_method") && CallsNamed(objectSetter, "il2cpp_runtime_invoke")
    && !CallsNamed(objectSetter, "get_IsValueType"), "installed IDictionary setter dispatches two boxed object references without generic-value marshalling");
var genericSetter = dictionary.Properties.Single(p => p.Name == "Item").SetMethod;
Check(CallsNamed(genericSetter, "get_IsValueType") && !CallsNamed(genericSetter, "il2cpp_object_unbox"), "installed generic setter has the related boxed-header-as-payload hazard");
var checkpoint = runtime.GetType("EndlessMode.GoalieNativeCheckpoint");
var methods = checkpoint.Methods.Where(m => m.HasBody).ToArray();
bool StructDictionary(MethodReference m) => m.DeclaringType is GenericInstanceType t && t.ElementType.FullName == dictionary.FullName && t.GenericArguments.Last().FullName == data.FullName;
Check(!methods.SelectMany(Calls).Any(m => StructDictionary(m) && m.Name == "TryGetValue"), "compiled checkpoint never calls native ES3Data out wrapper");
Check(!methods.SelectMany(Calls).Any(m => StructDictionary(m) && m.Name == "set_Item"), "compiled rollback never calls generic native ES3Data setter");
var read = methods.SingleOrDefault(m => m.Name == "ReadEntry");
Check(read != null && Calls(read).Any(m => StructDictionary(m) && m.Name == "ContainsKey")
    && Calls(read).Any(m => StructDictionary(m) && m.Name == "get_Item"), "compiled typed entry reader uses reviewed presence and boxed getter path");
Check(read != null && methods.SelectMany(Calls).Count(m => m.DeclaringType.FullName == checkpoint.FullName && m.Name == "ReadEntry") == 3,
    "all three compiled cache read sites use the typed safe entry reader");
Check(Calls(methods.Single(m => m.Name == "Inject")).Any(m => m.DeclaringType.FullName == "Il2CppSystem.Collections.IDictionary" && m.Name == "set_Item"),
    "compiled precise rollback uses native boxed-object setter");
// Overload presence alone does not prove C# selected it. String's generated
// wrapper initializes the System.String native-class store, so inferring the
// Il2CppSystem.String generic argument reaches a different uninitialized store.
var markerWrite = Calls(methods.Single(m => m.Name == "Inject")).Single(m => m.DeclaringType.FullName == "ES3" && m.Name == "Save");
Check(markerWrite is not GenericInstanceMethod && !markerWrite.HasGenericParameters
    && markerWrite.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(new[] { "System.String", "Il2CppSystem.Object", "ES3Settings" }),
    "compiled marker write selects exact non-generic object Save overload");
Console.WriteLine("MARKER_WRITE " + markerWrite.FullName);
var concreteSave = main.GetType("ES3").Methods.Single(m => m.Name == "Save" && !m.HasGenericParameters
    && m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(new[] { "System.String", "Il2CppSystem.Object", "ES3Settings" }));
Check(CallsNamed(concreteSave, "il2cpp_runtime_invoke") && !CallsNamed(concreteSave, "MakeGenericMethod")
    && concreteSave.Body.Instructions.Select(i => i.Operand).OfType<FieldReference>().Any(f => f.Name == "NativeMethodInfoPtr_Save_Public_Static_Void_String_Object_ES3Settings_0")
    && !concreteSave.Body.Instructions.Select(i => i.Operand).OfType<FieldReference>().Any(f => f.DeclaringType is GenericInstanceType),
    "installed concrete marker writer invokes direct native method without generic method store");
var card = main.Types.Single(t => t.Name == "SkaterDataForUI");
Check(card.BaseType.FullName == "Il2CppSystem.ValueType" && !card.IsValueType, "installed lineup card is another native struct represented as managed class");
var list = wrappers.GetType("Il2CppSystem.Collections.Generic.List`1");
var genericAdd = list.Methods.Single(m => m.Name == "Add");
Check(CallsNamed(genericAdd, "get_IsValueType") && !CallsNamed(genericAdd, "il2cpp_object_unbox"), "installed generic Add has boxed-header-as-payload hazard for lineup cards");
var boxedAdd = wrappers.GetType("Il2CppSystem.Collections.IList").Methods.Single(m => m.Name == "Add");
Check(boxedAdd.Parameters.Single().ParameterType.FullName == "Il2CppSystem.Object"
    && CallsNamed(boxedAdd, "il2cpp_object_get_virtual_method") && CallsNamed(boxedAdd, "il2cpp_runtime_invoke")
    && !CallsNamed(boxedAdd, "get_IsValueType"), "installed IList Add dispatches boxed object without generic-value marshalling");
var configure = runtime.GetType("EndlessMode.GoalieRosterMenu").Methods.Single(m => m.Name == "Configure");
Check(!Calls(configure).Any(m => m.DeclaringType is GenericInstanceType t && t.ElementType.FullName == list.FullName && m.Name == "Add"),
    "compiled goalie cards never call generic native struct Add");
Check(Calls(configure).Count(m => m.DeclaringType.FullName == "Il2CppSystem.Collections.IList" && m.Name == "Add") == 2,
    "both compiled active and reserve card additions use native boxed-object Add");
IEnumerable<TypeDefinition> Descendants(TypeDefinition type) => new[] { type }.Concat(type.NestedTypes.SelectMany(Descendants));
var unsafeValueMethods = new[] { "TryGetValue", "set_Item", "Add", "Insert", "Remove", "Contains", "IndexOf" };
var unsafeCalls = runtime.Types.Where(t => t.Namespace == "EndlessMode" && t.Name.StartsWith("Goalie"))
    .SelectMany(Descendants).SelectMany(t => t.Methods).Where(m => m.HasBody).SelectMany(Calls)
    .Where(m => unsafeValueMethods.Contains(m.Name) && m.DeclaringType is GenericInstanceType t
        && t.Namespace == "Il2CppSystem.Collections.Generic"
        && m.Parameters.Any(p => {
            var argument = p.ParameterType is ByReferenceType reference ? reference.ElementType : p.ParameterType;
            return argument is GenericParameter generic && generic.Type == GenericParameterType.Type
                && main.GetType(t.GenericArguments[generic.Position].FullName)?.BaseType?.FullName == "Il2CppSystem.ValueType";
        }))
    .Select(m => m.FullName).Distinct().ToArray();
Check(unsafeCalls.Length == 0, "compiled goalie routes contain no wrapped-native-value generic out/input collection calls");
foreach (string call in unsafeCalls) Console.WriteLine("UNSAFE " + call);
foreach (var module in new[] { runtime, wrappers, main, provider }) Console.WriteLine("SHA256 " + Path.GetFileName(module.FileName) + " " + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(module.FileName))));
Console.WriteLine($"{passed} checks passed; {failed} failed. Actual installed wrapper and compiled production IL inspected; no game/native execution.");
Environment.ExitCode = failed == 0 ? 0 : 1;
