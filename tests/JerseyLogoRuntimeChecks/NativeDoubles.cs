using CampaignCore;
namespace UnityEngine {
 public static class Application { public static string persistentDataPath = System.IO.Path.GetTempPath(); }
 public class Object { public bool Destroyed; public string name; public HideFlags hideFlags; public static void Destroy(Object o){o.Destroyed=true;} public static void DontDestroyOnLoad(Object o){} }
 public class Texture:Object {}
 public class Texture2D:Texture { public Color32[] Pixels=Array.Empty<Color32>(); public Texture2D(int w,int h,TextureFormat f,bool mip){} public void SetPixels32(Color32[] p)=>Pixels=p; public void Apply(bool a,bool b){} }
 public struct Color32 { public byte a; }
 public enum TextureFormat {RGBA32} public enum HideFlags {HideAndDontSave}
 public static class ImageConversion {public static bool LoadImage(Texture2D t,byte[] b)=>b.Length>0;}
}
namespace Data {
 public class SkaterData { static long next; public IntPtr Pointer=new(++next); public string id=""; public T TryCast<T>() where T:class=>this as T; }
 public class ForwardData:SkaterData {public string bodySkin,bodyAwaySkin,headSkin;}
 public class GoaltenderData:SkaterData {public string skin,awaySkin;}
}
namespace EndlessMode {
 static class Plugin { public static string ModFolder="",ModContentRoot=""; public static Logger Log=new(); }
 class Logger { public void LogWarning(object s){} }
 static class PatchBossLaunchMatch { public static NativeTeam FindTeamByName(string s)=>null; }
 class NativeTeam { public NativeSprite logo {get;set;} } class NativeSprite {public UnityEngine.Texture texture {get;set;}}
}
