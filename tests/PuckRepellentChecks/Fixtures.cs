// The fixture models the native same-object Rigidbody contract, not puck physics.
// Steam 26372689: PuckRepellentController.AddEffect uses forward._rigidbody.gameObject;
// PuckRepellentComponent.Awake caches GetComponent<Rigidbody>() on that object.
namespace HarmonyLib {
 [AttributeUsage(AttributeTargets.Class)] public class HarmonyPatch:Attribute {public HarmonyPatch(Type t,string name){}}
 [AttributeUsage(AttributeTargets.Method)] public class HarmonyPostfix:Attribute {}
}namespace UnityEngine {
 public class Object {public bool Destroyed;public static void Destroy(Object o){o.Destroyed=true;}}
 public class Component:Object {public GameObject gameObject;public bool enabled=true;public virtual void Awake(){}}
 public class Rigidbody:Component {public float position=10;}
 public class GameObject:Object {
  public readonly List<Component> Components=new();
  public T AddComponent<T>() where T:Component,new(){var c=new T{gameObject=this};Components.Add(c);c.Awake();return c;}
  public T GetComponent<T>() where T:Component=>Components.OfType<T>().FirstOrDefault(c=>!c.Destroyed);
 }
}
namespace Il2CppSystem { public class Object{} }
namespace Rogue {
 public class PuckRepellent{} public class RefHands{}
 public class PuckRepellentComponent:UnityEngine.Component {
  public UnityEngine.Rigidbody _rigidbody;
  public override void Awake(){_rigidbody=gameObject.GetComponent<UnityEngine.Rigidbody>();}
  // Mirrors native Awake -> cached local Rigidbody -> FixedUpdate.position.
  public float FixedUpdate()=>_rigidbody.position;
 }
}
namespace Rogue.Powerups.Controllers.Talents {
 public class PuckRepellentController {
  public Skaters.Forward Skater; public Rogue.PuckRepellentComponent _component;
  // Native AddEffect skips an existing same-host component without claiming it.
  public void AddEffect() {
   if(Skater.IsBenched() || Skater._rigidbody==null) return;
   var host=Skater._rigidbody.gameObject;
   if(host.GetComponent<Rogue.PuckRepellentComponent>()==null) _component=host.AddComponent<Rogue.PuckRepellentComponent>();
  }
  public void RemoveEffect() {if(_component!=null) UnityEngine.Object.Destroy(_component);_component=null;}
 }
}
namespace Skaters {
 public class Skater {public List<CampaignCore.CustomRewardDefinition> Rules=new();}
 public class Forward:Skater {
  private static int next;public IntPtr Pointer=new(++next);public UnityEngine.GameObject gameObject=new();public UnityEngine.Rigidbody _rigidbody;
  public bool HasStick=true,actAsReferee,Benched,NativeRepellent,NativeRefHands;
  public Rogue.Powerups.Controllers.Talents.PuckRepellentController NativeController;
  public bool IsBenched()=>Benched;
  public bool HaveTalentCached<T>()=>typeof(T)==typeof(Rogue.PuckRepellent)?NativeRepellent:typeof(T)==typeof(Rogue.RefHands)&&NativeRefHands;
  public T GetTalentController<T>() where T:class=>NativeController as T;
  public void AcquireNativeRepellent() {NativeRepellent=true;NativeController=new(){Skater=this};NativeController.AddEffect();}
  public void RemoveNativeRepellent() {NativeRepellent=false;NativeController.RemoveEffect();NativeController=null;}
  public void AddForbidStickSource(string s){} public void RemoveForbidStickSource(string s){}
  public void AddStickCountContributor(int n,string s){} public void RemoveStickCountContributor(string s){}
 }
}
namespace Tape2Tape {public static class OffsideController {public static void AddForceSource(Il2CppSystem.Object o){}public static void RemoveForceSource(Il2CppSystem.Object o){}}}
namespace Movements {public class MovementManager {public Skaters.Skater _skater;public bool CanBodycheck()=>true;}}
namespace CampaignCore {
 public class CustomRewardDefinition {public string Effect,Id;public int Amount;}
 public static class CustomRewardEffects {
  public const string ForbidStick="Cannot use sticks",ExtraSticks="Extra reserve sticks",ForbidCheck="Cannot bodycheck",RepelPuck="Repel nearby puck",HandPuck="hand",ForceOffside="offside";
  public static IEnumerable<string> ForwardConditionEffects=>new[]{ForbidStick,ExtraSticks,ForbidCheck,RepelPuck,HandPuck,ForceOffside};
 }
}
namespace EndlessMode {public static class CustomRewardRuntime {public static IEnumerable<CampaignCore.CustomRewardDefinition> ActiveConditions(Skaters.Skater s)=>s?.Rules??Enumerable.Empty<CampaignCore.CustomRewardDefinition>();}}
