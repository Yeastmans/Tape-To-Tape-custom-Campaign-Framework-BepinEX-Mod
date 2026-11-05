using EndlessMode;
using Rogue;
using Skaters;
using UnityEngine;
using CampaignCore;
try {
int passed=0;
void Check(bool value,string name){if(!value)throw new Exception("FAIL "+name);Console.WriteLine("PASS "+name);passed++;}
Forward Skater(bool withBody=true){var s=new Forward();s.Rules.Add(new(){Id="Bridge",Effect=CustomRewardEffects.RepelPuck,Amount=1});if(withBody)s._rigidbody=new GameObject().AddComponent<Rigidbody>();return s;}
var forward=Skater();
CustomRewardEquipmentEffects.Refresh(forward);
var repellent=forward._rigidbody.gameObject.GetComponent<PuckRepellentComponent>();
Check(repellent!=null,"custom repellent uses native Rigidbody host in split skater hierarchy");
Check(forward.gameObject.GetComponent<PuckRepellentComponent>()==null,"skater root remains free of invalid repellent component");
Check(repellent._rigidbody==forward._rigidbody&&repellent.FixedUpdate()==10,"native same-object Rigidbody initialization supports FixedUpdate");
CustomRewardEquipmentEffects.Refresh(forward);
Check(forward._rigidbody.gameObject.Components.OfType<PuckRepellentComponent>().Count()==1,"refresh does not duplicate custom component");
forward.Benched=true;CustomRewardEquipmentEffects.Refresh(forward);
Check(!repellent.enabled&&repellent.Destroyed,"bench transition removes owned repellent like native controller");
forward.Benched=false;CustomRewardEquipmentEffects.Refresh(forward);
var resumed=forward._rigidbody.gameObject.GetComponent<PuckRepellentComponent>();
Check(resumed!=null&&resumed!=repellent&&resumed.FixedUpdate()==10,"return from bench reinstates valid component");
forward.Rules.Clear();CustomRewardEquipmentEffects.Refresh(forward);
Check(!resumed.enabled&&resumed.Destroyed,"condition expiry disables and destroys owned repellent");
var unready=Skater(false);CustomRewardEquipmentEffects.Refresh(unready);
Check(unready.gameObject.GetComponent<PuckRepellentComponent>()==null,"missing Rigidbody defers component creation");
unready._rigidbody=new GameObject().AddComponent<Rigidbody>();CustomRewardEquipmentEffects.Refresh(unready);
Check(unready._rigidbody.gameObject.GetComponent<PuckRepellentComponent>()?.FixedUpdate()==10,"ready Rigidbody permits deferred effect");
var nativeOwner=Skater();nativeOwner.NativeRepellent=true;
var nativeComponent=nativeOwner._rigidbody.gameObject.AddComponent<PuckRepellentComponent>();
CustomRewardEquipmentEffects.Refresh(nativeOwner);
Check(nativeOwner._rigidbody.gameObject.Components.OfType<PuckRepellentComponent>().Count()==1,"native talent ownership avoids custom duplicate");
CustomRewardEquipmentEffects.Clear();
Check(!nativeComponent.Destroyed&&nativeComponent.enabled,"cleanup preserves native-owned component");
Check(unready._rigidbody.gameObject.GetComponent<PuckRepellentComponent>()==null,"match cleanup removes custom-owned repellent");
var acquired=Skater();CustomRewardEquipmentEffects.Refresh(acquired);
var shared=acquired._rigidbody.gameObject.GetComponent<PuckRepellentComponent>();
acquired.AcquireNativeRepellent();CustomRewardEquipmentEffects.Refresh(acquired);
Check(!shared.Destroyed&&shared.enabled,"native acquisition preserves the active shared-host component during refresh");
Check(acquired.NativeController._component==shared,"native controller receives ownership for later teardown");
CustomRewardEquipmentEffects.Clear();
Check(!shared.Destroyed&&shared.enabled,"custom cleanup preserves the transferred native component");
acquired.RemoveNativeRepellent();
Check(shared.Destroyed,"native talent removal tears down the transferred component");
CustomRewardEquipmentEffects.Refresh(acquired);
var reactivated=acquired._rigidbody.gameObject.GetComponent<PuckRepellentComponent>();
Check(reactivated!=null&&reactivated!=shared&&reactivated.FixedUpdate()==10,"native removal permits custom reactivation with a valid Rigidbody");
CustomRewardEquipmentEffects.Clear();
Check(reactivated.Destroyed&&!reactivated.enabled,"reactivated custom component retains normal cleanup");
var clearFirst=Skater();CustomRewardEquipmentEffects.Refresh(clearFirst);
var clearShared=clearFirst._rigidbody.gameObject.GetComponent<PuckRepellentComponent>();
clearFirst.AcquireNativeRepellent();CustomRewardEquipmentEffects.Clear();
Check(!clearShared.Destroyed&&clearShared.enabled&&clearFirst.NativeController._component==clearShared,"cleanup before the next refresh transfers newly native-owned component");
Console.WriteLine($"{passed} checks passed.");

} catch(Exception ex) { Console.Error.WriteLine(ex.Message); Environment.ExitCode=1; }
