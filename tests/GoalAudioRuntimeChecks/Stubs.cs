using System.Runtime.CompilerServices;
namespace UnityEngine { public class Object { public static T FindObjectOfType<T>() where T:class => Settings.SystemSettings.Instance as T; } }
namespace Settings {
 public class SystemSettings:UnityEngine.Object {
  public static SystemSettings Instance = new(); public float master=.5f,sfx=.8f;
  [MethodImpl(MethodImplOptions.NoInlining)] public void SetMasterVolume(float value) {master=value;}
  [MethodImpl(MethodImplOptions.NoInlining)] public void SetSfxVolume(float value) {sfx=value;}
 }
}
namespace NHL94 { public enum GoalType {GOOD=0,NO_GOAL=1} }
namespace Events { public class GoalScoredArg { public NHL94.GoalType goalType; public Scorer scorer; } public class Scorer { public Team Team; } public class Team { public TeamData _teamData; } public class TeamData { public string id; } }
public class SoundManager:UnityEngine.Object {
 public FMODUnity.EventReference goalScoredEvent = new(){Guid=Guid.Parse("8fe1a940-7657-49ce-8548-bccff3ef3f89")};
 public int CrowdUpdates; public float NativeVolume;
 [MethodImpl(MethodImplOptions.NoInlining)] public void OnGoalScored(Events.GoalScoredArg arg) { CrowdUpdates++; NativeVolume=FMODUnity.RuntimeManager.CreateInstance(goalScoredEvent).State.Volume; }
 [MethodImpl(MethodImplOptions.NoInlining)] public void OnDestroy() {}
}
namespace EndlessMode { public class Logger { public List<string> Warnings=new(); public void LogWarning(string v)=>Warnings.Add(v); public void LogError(string v)=>Warnings.Add(v); } public static class Plugin { public static Logger Log=new(); } }
namespace FMOD {
 public enum RESULT {OK,ERR_INTERNAL} [Flags] public enum MODE {CREATESAMPLE=1,LOOP_OFF=2,_2D=4}
 public class Sample {public bool Released;}
 public class ChannelState {public bool Paused=true,Stopped;public float Volume;}
 public static class AudioState {public static List<Sample> Samples=new(); public static List<ChannelState> Channels=new();}
 public struct Sound { public Sample State;public RESULT release(){State.Released=true;return RESULT.OK;} }
 public struct ChannelGroup {}
 public struct Channel {
  public ChannelState State;
  public RESULT setVolume(float v){State.Volume=v;return RESULT.OK;}
  public RESULT setPaused(bool v){State.Paused=v;return RESULT.OK;}
  public RESULT stop(){State.Stopped=true;return RESULT.OK;}
 }
 public struct System {
  public RESULT createSound(string file,MODE mode,out Sound sound) {
   CampaignCore.GoalSoundWave.Decode(global::System.IO.File.ReadAllBytes(file));
   sound=new Sound{State=new Sample()}; AudioState.Samples.Add(sound.State); return RESULT.OK;
  }
  public RESULT playSound(Sound sound,ChannelGroup group,bool paused,out Channel channel) {
   channel=new Channel{State=new ChannelState{Paused=paused}}; AudioState.Channels.Add(channel.State); return RESULT.OK;
  }
 }
}
namespace FMOD.Studio { public class EventState {public float Volume=1;} public struct EventInstance {public EventState State;public FMOD.RESULT setVolume(float v){State.Volume=v;return FMOD.RESULT.OK;}} }
namespace FMODUnity {
 public struct EventReference {public Guid Guid;}
 public static class RuntimeManager {
  public static FMOD.System CoreSystem;
  [MethodImpl(MethodImplOptions.NoInlining)] public static FMOD.Studio.EventInstance CreateInstance(EventReference reference)=>new(){State=new FMOD.Studio.EventState()};
 }
}
public class CampaignOnly { [MethodImpl(MethodImplOptions.NoInlining)] public void Run() {} public static int Calls; }
[HarmonyLib.HarmonyPatch(typeof(CampaignOnly),nameof(CampaignOnly.Run))]
public class CampaignOnlyPatch { [HarmonyLib.HarmonyPrefix] private static void Prefix()=>CampaignOnly.Calls++; }
