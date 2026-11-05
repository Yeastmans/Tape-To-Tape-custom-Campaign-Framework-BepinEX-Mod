using System.Reflection;
using EndlessMode;
using HarmonyLib;
int passed=0;
void Check(bool value,string name) {if(!value)throw new Exception("FAIL "+name);passed++;Console.WriteLine("PASS "+name);}
var defaultMode=args.Length==0||args[0]=="default";
// Reproduce production's mutually exclusive default early-return/PatchAll routes.
if(defaultMode) typeof(TeamGoalSound).GetMethod("InstallForDefault",BindingFlags.Static|BindingFlags.NonPublic)?.Invoke(null,null);
else new Harmony("com.mods.customcampaign").PatchAll(typeof(TeamGoalSound).Assembly);
new CampaignOnly().Run();
Check(CampaignOnly.Calls==(defaultMode?0:1),"default audio installation does not activate campaign-only patches");
byte[] wave;
using(var ms=new MemoryStream()) {using(var w=new BinaryWriter(ms,System.Text.Encoding.ASCII,true)) {w.Write("RIFF"u8);w.Write(40);w.Write("WAVEfmt "u8);w.Write(16);w.Write((short)1);w.Write((short)1);w.Write(8000);w.Write(16000);w.Write((short)2);w.Write((short)16);w.Write("data"u8);w.Write(4);w.Write((short)1000);w.Write((short)-1000);}wave=ms.ToArray();}
TeamGoalSound.Register("export-team",Convert.ToBase64String(wave),"90");
var manager=new SoundManager();
Events.GoalScoredArg Goal(string id,NHL94.GoalType type=NHL94.GoalType.GOOD)=>new(){goalType=type,scorer=new(){Team=new(){_teamData=new(){id=id}}}};
manager.OnGoalScored(Goal("export-team"));
Check(FMOD.AudioState.Channels.Count==1,"registered exported goal reaches custom playback exactly once");
var first=FMOD.AudioState.Channels[0];
Check(!first.Paused&&!first.Stopped&&Math.Abs(first.Volume-.36f)<.0001,"authored/master/SFX volume applied before playback");
Check(manager.NativeVolume==0&&manager.CrowdUpdates==1,"custom audio replaces native event while crowd handler runs");
Check(TeamGoalSoundScope.Active==null,"handler scope cleared by finalizer");
Settings.SystemSettings.Instance.SetMasterVolume(.25f);
Check(Math.Abs(first.Volume-.18f)<.0001,"master volume refresh reaches active channel");
Settings.SystemSettings.Instance.SetSfxVolume(.4f);
Check(Math.Abs(first.Volume-.09f)<.0001,"SFX volume refresh reaches active channel");
manager.OnGoalScored(Goal("native-team"));
Check(first.Stopped&&FMOD.AudioState.Channels.Count==1&&manager.NativeVolume==1,"unconfigured opposing goal stops custom clip and keeps native event");
manager.OnGoalScored(Goal("export-team",NHL94.GoalType.NO_GOAL));
Check(FMOD.AudioState.Channels.Count==1&&manager.NativeVolume==1,"no-goal event keeps native playback");
manager.OnGoalScored(Goal("export-team"));
Check(FMOD.AudioState.Channels.Count==2&&FMOD.AudioState.Samples.Count==1,"second goal starts once and reuses loaded samples");
manager.OnDestroy();
Check(FMOD.AudioState.Channels[1].Stopped&&FMOD.AudioState.Samples[0].Released,"sound-manager destruction stops and releases samples");
Check(Plugin.Log.Warnings.Count==0,"no playback warnings");
Console.WriteLine($"{passed} checks passed ({(defaultMode?"default":"campaign")}).");
