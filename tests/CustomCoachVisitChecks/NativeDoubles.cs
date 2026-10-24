using CampaignCore;
namespace UnityEngine {
 public class Object {
  static long next; public IntPtr Pointer = (IntPtr)(++next); public string name; public bool Destroyed;
  public static T Instantiate<T>(T source) where T:Object { var clone=(T)source.MemberwiseClone(); clone.Pointer=(IntPtr)(++next); return clone; }
  public static void Destroy(Object obj) => obj.Destroyed=true;
  public T TryCast<T>() where T:class => this as T;
 }
}
namespace Il2CppSystem.Collections.Generic { public class List<T>:System.Collections.Generic.List<T> {} }
namespace STS.Map {
 public class Map:UnityEngine.Object { public MapLayout Layout; }
 public class MapManager { public Map CurrentMap; }
 public class MapObject:UnityEngine.Object { public MapManager mapManager = new(); }
 public class Node { public int LayerIndex,NodeIndex; }
 public class MapNode:UnityEngine.Object { public Node Node=new(); public MapObject Parent; public T GetComponentInParent<T>() where T:class => Parent as T; }
 public class CoachMapNode:MapNode {}
 public class EventChoice {}
 public class EventNode:UnityEngine.Object { public bool canSkip; public Il2CppSystem.Collections.Generic.List<EventChoice> eventChoices = new(); }
 public class CoachBlueprint { public void showEventPanel(CoachMapNode node,EventNode source) {} }
}
namespace Rogue.NPC.CoachNPC { public class CoachEvent:STS.Map.EventNode { public void Initialize() {} } }
namespace Tape2Tape {
 public class Campaign { public Run runData = new(); }
 public class Run:UnityEngine.Object {}
 public static class CampaignUtil { public static Campaign CampaignState=new(); }
}
namespace EndlessMode {
 public static class Plugin { public static bool IsDefaultMode; public static string ModFolder="campaign"; public static Logger Log=new(); }
 public class Logger { public void LogError(string text) {} }
 public static class CustomMapLayout { public static MapLayout LayoutFor(STS.Map.Map map) => map.Layout; }
 public static class CustomRewardRuntime { public static bool Reject; public static void RegisterCoach(CustomCoachDefinition coach) { if(Reject)throw new ArgumentException("Conflicting identity"); } }
 public static class CustomCoachPresentation { public static void Release(STS.Map.MapObject map) {} }
}
namespace HarmonyLib {
 [AttributeUsage(AttributeTargets.Class)] public class HarmonyPatch:Attribute { public HarmonyPatch(Type type,string name) {} }
 public class HarmonyPrefix:Attribute {} public class HarmonyPostfix:Attribute {} public class HarmonyFinalizer:Attribute {}
}
