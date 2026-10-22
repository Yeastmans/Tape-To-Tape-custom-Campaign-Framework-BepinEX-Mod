using Mono.Cecil;
static class InteropChecks {
 internal static void Run(string[] args,Action<bool,string> check) {
  using var runtime=ModuleDefinition.ReadModule(Path.GetFullPath(args[0]));
  using var native=ModuleDefinition.ReadModule(Path.Combine(args[1],"BepInEx/interop/MainScriptsAssembly.dll"));
  IEnumerable<MethodReference> Calls(MethodDefinition m)=>m.Body.Instructions.Select(i=>i.Operand).OfType<MethodReference>();
  var preview=runtime.GetType("EndlessMode.GoalieGmPreview");
  var stats=Calls(preview.Methods.Single(m=>m.Name=="Stats")).ToArray();
  string[] fields={"skill","catchingSkill","gloveSkill","blockerSkill","fiveHoleSkill","standingSpeed","butterflySpeed","controlSkill","recoverySkill","passPower","pokecheckSkill","depth","passReadSkill"};
  check(fields.All(f=>stats.Any(m=>m.Name=="get_"+f))&&!stats.Any(m=>m.Name=="get_shotPower"||m.Name=="GetStat"),"CompiledStatsUseAllThirteenNamedNativeGetters");
  check(Calls(runtime.GetType("EndlessMode.MatchRuleRunner").Methods.Single(m=>m.Name=="Update")).Any(m=>m.DeclaringType.FullName==preview.FullName&&m.Name=="Tick"),"CompiledManagedUpdateCallsPreviewWithoutNewNativeHooks");
  var choiceCalls=Calls(runtime.GetType("EndlessMode.GoalieRecruitment").Methods.Single(m=>m.Name=="Choices")).ToArray();
  check(choiceCalls.Any(m=>m.Name=="set_FreeAgentLeavingConditions")&&choiceCalls.Any(m=>m.DeclaringType.FullName=="Rogue.FreeAgents.MaxGamesCondition"&&m.Name=="set_maxMatches")&&choiceCalls.Any(m=>m.DeclaringType.FullName=="Rogue.FreeAgents.MaxGamesCondition"&&m.Name=="set_matches"),"CompiledChoicesSupplyNativeDetachedContractConditions");
  check(native.GetType("Rogue.FreeAgents.MaxGamesCondition").BaseType.FullName=="Rogue.FreeAgents.LeavingCondition"&&native.GetType("Rogue.FreeAgents.LeavingCondition").BaseType.FullName!="Il2CppSystem.ValueType","NativeLeavingConditionsAreReferenceTypesSafeForListAdd");
  var conditions=native.GetType("STS.Map.EventChoice").Properties.Single(p=>p.Name=="FreeAgentLeavingConditions");
  check(conditions.PropertyType is GenericInstanceType list&&list.GenericArguments.Single().FullName=="Rogue.FreeAgents.LeavingCondition","NativeEventChoiceOwnsExpectedConditionList");
  var create=preview.Methods.Single(m=>m.Name=="Create");
  check(create.Body.Instructions.Any(i=>i.Operand is string name&&name=="LabelBar")&&Calls(create).Any(m=>m.Name=="GetComponent"&&m is GenericInstanceMethod g&&g.GenericArguments.Single().FullName=="TMPro.TMP_Text"),"CompiledPreviewResolvesActualNullWiredLabelBarChild");
  var clone=Calls(preview.Methods.Single(m=>m.Name=="AddText")).Single(m=>m.Name=="Instantiate");
  check(clone is GenericInstanceMethod generic&&generic.GenericArguments.Single().FullName=="TMPro.TMP_Text","CompiledPreviewClonesTextOnlyWithoutNativeStatBehavior");
 }
}
