using Mono.Cecil;
using Mono.Cecil.Cil;
if(args.Length!=2)throw new ArgumentException("runtime.dll game-directory");
using var runtime=ModuleDefinition.ReadModule(Path.GetFullPath(args[0]));
using var main=ModuleDefinition.ReadModule(Path.Combine(Path.GetFullPath(args[1]),"BepInEx/interop/MainScriptsAssembly.dll"));
int passed=0,failed=0;
void Check(bool condition,string message){Console.WriteLine((condition?"PASS ":"FAIL ")+message);if(condition)passed++;else failed++;}
var repo=main.GetType("State.RunDataRepositoryV2");var property=repo.Properties.Single(p=>p.Name=="_logoRepository");
var constructors=main.GetType("State.DeserializerV2").Methods.Where(m=>m.IsConstructor&&!m.IsStatic&&m.Parameters.Count==4).ToArray();
Check(property.PropertyType.FullName=="Tape2Tape.Customization.ILogoRepository"&&constructors.Length==1&&constructors[0].Parameters[3].ParameterType.FullName==property.PropertyType.FullName,"generated repository logo dependency matches fourth deserializer constructor parameter");
var getter=repo.Properties.Single(p=>p.Name=="_fileName").GetMethod;
Check(getter.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="il2cpp_field_get_offset"),"moved repository filename uses generated native field offset lookup");
var bind=runtime.GetType("EndlessMode.GoaliePersistenceBridge").Methods.Single(m=>m.Name=="BindDeserializer");
var ctor=bind.Body.Instructions.Single(i=>i.OpCode==OpCodes.Newobj&&i.Operand is MethodReference m&&m.DeclaringType.FullName=="State.DeserializerV2");
Check(ctor.Previous.Operand is MethodReference logo&&logo.Name=="get__logoRepository"&&logo.DeclaringType.FullName==repo.FullName&&ctor.Previous.Previous.OpCode==OpCodes.Ldarg_0,"compiled goalie deserializer receives its owning repository logo dependency as fourth argument");
Console.WriteLine($"{passed} checks passed; {failed} failed; metadata and compiled IL only; no native execution.");return failed==0?0:1;
