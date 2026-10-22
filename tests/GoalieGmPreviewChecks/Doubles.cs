using System.Collections.Generic;
namespace UnityEngine {
 public class Object { static int next; public IntPtr Pointer {get;}=(IntPtr)(++next); public bool Destroyed; public static int Created,Removed; public static readonly List<TMPro.TMP_Text> Texts=new();
  public static T Instantiate<T>(T source,Transform parent,bool world) where T:Object { Created++; var t=new TMPro.TMP_Text(); t.transform.parent=parent;Texts.Add(t);return (T)(object)t; }
  public static void Destroy(Object obj){obj.Destroyed=true;Removed++;}
 }
 public struct Vector2 { public float x,y; public Vector2(float x,float y){this.x=x;this.y=y;} }
 public class Transform:Object {public Transform parent;public readonly Dictionary<string,Transform> Children=new();public object Component;public Transform Find(string name)=>Children.GetValueOrDefault(name);public T GetComponent<T>() where T:class=>Component as T; public T TryCast<T>() where T:class=>this as T;}
 public class RectTransform:Transform {public Vector2 anchorMin,anchorMax,pivot,anchoredPosition,sizeDelta;}
 public class GameObject:Object {public bool activeSelf=true;public void SetActive(bool value)=>activeSelf=value;}
 public class Component:Object {public GameObject gameObject=new();public Transform transform=new RectTransform();}
}
namespace TMPro {
 public enum TextOverflowModes {Ellipsis}
 public enum TextAlignmentOptions {MidlineLeft,MidlineRight}
 public class TMP_Text:UnityEngine.Component {public string text;public bool enableWordWrapping,enableAutoSizing,raycastTarget;public float fontSizeMin,fontSizeMax,fontSize;public int maxVisibleLines;public TextOverflowModes overflowMode;public TextAlignmentOptions alignment;public UnityEngine.RectTransform rectTransform=>(UnityEngine.RectTransform)transform;}
}
namespace Data {public class GoaltenderData:UnityEngine.Object { public int skill,catchingSkill,gloveSkill,blockerSkill,fiveHoleSkill,standingSpeed,butterflySpeed,controlSkill,recoverySkill,passPower,pokecheckSkill,depth; public float passReadSkill; public int shotPower=999;}}
namespace Tape2Tape.Customization.UI {public class SkaterStatBox:UnityEngine.Component {public TMPro.TMP_Text m_NameLabel=new(),m_MainValue=new();}}
namespace Tape2Tape.Hockey.UI {
 public class SkaterDetailsUI:UnityEngine.Object {public Tape2Tape.Customization.UI.SkaterStatBox m_ForwardSpeed=new(),m_ForwardAccuracy=new(),m_ForwardShotPower=new(),m_ForwardChecking=new();}
 public class Choice {public Data.GoaltenderData Skater;}
 public class EventChoiceItem:UnityEngine.Object {public Choice ChoiceData=new();public SkaterDetailsUI m_SkaterDetails=new();}
 public class EventChoiceMenu:UnityEngine.Object {public bool IsVisible=true,IsHiding;public List<EventChoiceItem> m_Items=new();}
}
namespace EndlessMode {
 static class Plugin {public static Logger Log=new();public class Logger {public int Warnings;public void LogWarning(string s){Warnings++;}}}
 static class GoalieRosterMenu {public static bool Enabled=true;}
 static class GoalieRecruitment {
  public class Selection {public Tape2Tape.Hockey.UI.EventChoiceMenu Menu;}
  public class Visit {public Selection Selection=new();public List<Offer> Offers=new();}
  public class Offer {public Data.GoaltenderData Candidate;public bool Taken,Unresolved;}
  public static Visit Current;public static bool ContextValid=true;public static bool IsCurrent(Visit v)=>ContextValid&&v!=null&&ReferenceEquals(Current,v);public static bool Valid(Offer o)=>o!=null&&!o.Taken&&!o.Unresolved;
 }
}


