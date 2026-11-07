using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using CampaignCore;
using EndlessMode;
using Movements.MovementStates;

int checks=0,failures=0;
void Check(bool condition,string name){checks++;if(!condition)failures++;Console.WriteLine((condition?"PASS ":"FAIL ")+name);}
void Reject(Action action,string name){try{action();Check(false,name);}catch(InvalidDataException){Check(true,name);}}
string game=args.ElementAtOrDefault(0)??@"C:\Steam\steamapps\common\Tape to Tape\GameAssembly.dll";
string dobby=args.ElementAtOrDefault(1)??@"C:\Steam\steamapps\common\Tape to Tape\BepInEx\core\dobby.dll";
byte[] image=File.ReadAllBytes(game);
Check(Convert.ToHexString(SHA256.HashData(image))=="0709D469AE6C47B695BBA71664BF5619982EE6C3E17EAEF6DC8B98FDA0A61CEC","Exact audited native image");
Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dobby)))=="8015DE7D867245A1095D13947A63763878E4CF5FD3D3089B63CC39200B055DED","Actual pinned Dobby backend");
using var pe=new PEReader(new MemoryStream(image,false));const int rva=0x3c55450;
var section=pe.PEHeaders.SectionHeaders.Single(s=>rva>=s.VirtualAddress&&rva<s.VirtualAddress+s.SizeOfRawData);
var body=image.AsSpan(section.PointerToRawData+rva-section.VirtualAddress,96).ToArray();
Check(Convert.ToHexString(SHA256.HashData(body))=="F8E630EED796F25F0827060FFAC0EAE87318755BD5A7612A349E47B267BEDD81","Exact ReleaseShot body");
IntPtr page=Native.VirtualAlloc(IntPtr.Zero,4096,0x3000,0x40),state=Marshal.AllocHGlobal(256);
if(page==IntPtr.Zero)throw new System.ComponentModel.Win32Exception();
Marshal.Copy(new byte[256],0,state,256);
IntPtr source=page+128;Marshal.Copy(body,0,source,body.Length);
// Wrong RET+4 is a safe adjacent sentinel in this PRIVATE executable fixture.
Marshal.Copy(new byte[]{0x90,0x90,0x90,0xc6,0x41,0x60,1,0xc3},0,source+96,8);
var library=NativeLibrary.Load(dobby);
var prepare=Marshal.GetDelegateForFunctionPointer<Native.Prepare>(NativeLibrary.GetExport(library,"DobbyPrepare"));
Native.Observer observer=()=>{};
Check(prepare(source,Marshal.GetFunctionPointerForDelegate(observer),out var trampoline)==0,"Real DobbyPrepare on private native bytes");
var before=new byte[24];Marshal.Copy(trampoline,before,0,24);
Check(trampoline.ToInt64()+10+BitConverter.ToInt32(before,6)==source.ToInt64()+99,"Reproduces captured Dobby RET+4 relocation");
const ulong direction=0xbf7a95f53e517c73UL,canary=0x1928374655aabbccUL;
// Private native caller: preserves argument slots and records stack/canary
// across the call. No game function or process is invoked.
var caller=new List<byte>();void Emit(params byte[] bytes)=>caller.AddRange(bytes);
Emit(0x48,0x83,0xec,0x48,0x8a,0x44,0x24,0x70,0x88,0x44,0x24,0x20);
Emit(0x4c,0x8b,0x54,0x24,0x78,0x4c,0x89,0x54,0x24,0x28,0x48,0x89,0x4c,0x24,0x30);
Emit(0x49,0xba);caller.AddRange(BitConverter.GetBytes(canary));Emit(0x4c,0x89,0x54,0x24,0x38,0x48,0x89,0x61,0x68);
Emit(0x48,0xb8);caller.AddRange(BitConverter.GetBytes(trampoline.ToInt64()));Emit(0xff,0xd0);
Emit(0x48,0x8b,0x4c,0x24,0x30,0x48,0x89,0x61,0x70,0x4c,0x8b,0x54,0x24,0x38,0x4c,0x89,0x51,0x78,0x48,0x83,0xc4,0x48,0xc3);
Marshal.Copy(caller.ToArray(),0,page+512,caller.Count);
var invoke=Marshal.GetDelegateForFunctionPointer<Native.Release>(page+512);
Marshal.WriteByte(state,0x28,1);
invoke(state,direction,1.25f,1,1,(IntPtr)0x12345678);
Check(Marshal.ReadByte(state,0x60)==1,"Unrepaired released branch enters adjacent sentinel (safe crash reproduction)");
Check(ChargedReleaseTrampoline.Apply(trampoline,source),"Production protected-memory writer applies the proven correction");
Marshal.WriteByte(state,0x60,0);
invoke(state,direction,1.25f,1,1,(IntPtr)0x12345678);
Check(Marshal.ReadByte(state,0x60)==0,"Corrected released=true returns without adjacent sentinel");
Check(Marshal.ReadInt64(state,0x68)==Marshal.ReadInt64(state,0x70)&&unchecked((ulong)Marshal.ReadInt64(state,0x78))==canary,"Released=true preserves caller stack/canary");
Marshal.WriteByte(state,0x28,0);Marshal.WriteByte(state,0x4c,1);
invoke(state,direction,1.25f,1,1,(IntPtr)0x12345678);
Check(Marshal.ReadByte(state,0x28)==1&&unchecked((ulong)Marshal.ReadInt64(state,0x2c))==direction&&Marshal.ReadInt32(state,0x34)==BitConverter.SingleToInt32Bits(1.25f)
    &&Marshal.ReadByte(state,0x38)==1&&Marshal.ReadByte(state,0x39)==1,"Released=false executes native body with exact vector/float/register/stack argument bits");
Check(Marshal.ReadInt64(state,0x68)==Marshal.ReadInt64(state,0x70)&&unchecked((ulong)Marshal.ReadInt64(state,0x78))==canary,"Released=false preserves caller stack/canary");
var after=new byte[24];Marshal.Copy(trampoline,after,0,24);
Check(ChargedReleaseTrampoline.Correction(after,trampoline.ToInt64(),source.ToInt64())==null,"Already-correct trampoline is idempotent");
Check(!ChargedReleaseTrampoline.Apply(trampoline,source),"Applying the production correction twice makes no further write");
Check(before.Take(6).SequenceEqual(after.Take(6))&&before.Skip(10).SequenceEqual(after.Skip(10)),"Repair changes only the four displacement bytes");
var invalid=(byte[])after.Clone();invalid[0]=0x90;Reject(()=>ChargedReleaseTrampoline.Correction(invalid,trampoline.ToInt64(),source.ToInt64()),"Unknown instruction shape rejected");
invalid=(byte[])after.Clone();BitConverter.GetBytes(17).CopyTo(invalid,6);Reject(()=>ChargedReleaseTrampoline.Correction(invalid,trampoline.ToInt64(),source.ToInt64()),"Unknown branch destination rejected");
invalid=(byte[])after.Clone();invalid[16]^=1;Reject(()=>ChargedReleaseTrampoline.Correction(invalid,trampoline.ToInt64(),source.ToInt64()),"Wrong native fallthrough rejected");
bool owned=false,other=true,reported=false;
bool installed=ChargedReleaseTrampoline.InstallChecked(()=>{},()=>owned=true,()=>throw new InvalidDataException("unknown trampoline"),()=>owned=false,_=>reported=true);
Check(!installed&&!owned&&other&&reported,"Rejected post-install validation removes only owned hook and reports error");
owned=false;reported=false;
installed=ChargedReleaseTrampoline.InstallChecked(()=>throw new InvalidDataException("unknown provider"),()=>owned=true,()=>{},()=>throw new Exception("No rollback expected"),_=>reported=true);
Check(!installed&&!owned&&reported,"Unknown provider fails before hook installation");
var owners=new HashSet<string>{"ours","other"};bool detached=false,nativeVerified=false;
ChargedReleaseTrampoline.RemoveOwned(()=>owners.Remove("ours"),()=>owners.Contains("ours"),()=>owners.Any(),()=>detached=true,()=>nativeVerified=true);
Check(owners.SetEquals(new[]{"other"})&&!detached&&!nativeVerified,"Production rollback policy preserves another owner's bridge");
owners.Clear();owners.Add("ours");
ChargedReleaseTrampoline.RemoveOwned(()=>owners.Remove("ours"),()=>owners.Contains("ours"),()=>owners.Any(),()=>detached=true,()=>nativeVerified=true);
Check(owners.Count==0&&detached&&nativeVerified,"Production rollback policy detaches and verifies the unowned bridge");
var commit=Marshal.GetDelegateForFunctionPointer<Native.AddressAction>(NativeLibrary.GetExport(library,"DobbyCommit"));
var destroy=Marshal.GetDelegateForFunctionPointer<Native.AddressAction>(NativeLibrary.GetExport(library,"DobbyDestroy"));
owned=false;reported=false;
installed=ChargedReleaseTrampoline.InstallChecked(()=>{},()=>{
    if(commit(source)!=0)throw new Exception("Private Dobby commit failed");owned=true;
},()=>throw new InvalidDataException("Refused private installed bridge"),()=>{
    if(destroy(source)!=0)throw new Exception("Private Dobby disposal failed");owned=false;
},_=>reported=true);
var restored=new byte[96];Marshal.Copy(source,restored,0,96);
Check(!installed&&!owned&&reported&&restored.SequenceEqual(body),"Post-install refusal disposes actual private Dobby hook and restores every native byte");
Marshal.WriteByte(state,0x28,0);Marshal.WriteInt64(state,0x2c,0);
Marshal.GetDelegateForFunctionPointer<Native.Release>(source)(state,direction,1.25f,1,1,(IntPtr)0x12345678);
Check(Marshal.ReadByte(state,0x28)==1&&unchecked((ulong)Marshal.ReadInt64(state,0x2c))==direction,"Refusal leaves executable native shot behavior, not an empty bridge");
var menu=new SlapshotState{ThrowOnRead=true};CustomRewardRuntime.InMatch=false;
try{CustomRewardChargedRelease.Prefix(menu,1);Check(menu.Reads==0,"Inactive guard precedes native forward getter");}catch(InvalidOperationException){Check(false,"Inactive guard precedes native forward getter");}
menu.ThrowOnRead=false;CustomRewardRuntime.InMatch=true;
Check(CustomRewardChargedShots.Bonus(menu.Player,"Speed")==0 && menu.Player.PowerChecks==0,"No pending charge bypasses the native power check on stat getters");
var rule=new CustomRewardDefinition{Id="charge",Effect=CustomRewardEffects.ChargePower,Amount=7,Seconds=2};
CustomRewardRuntime.Conditions.Add(rule);menu.Player._skaterData.powerups.Add(new Rogue.Talent{id="charge"});UnityEngine.Time.time=10;
CustomRewardChargedRelease.Prefix(menu,1.25f);CustomRewardRuntime.Conditions.Clear();
Check(CustomRewardChargedShots.Bonus(menu.Player,"Shot Power")==8,"Active authored charge snapshot retains native integer truncation");
UnityEngine.Time.time=10.5f;Check(CustomRewardChargedShots.Bonus(menu.Player,"Shot Power")==0,"Native half-second bonus window retained");
GC.KeepAlive(observer);Marshal.FreeHGlobal(state);
Console.WriteLine($"{checks} charged-release checks; {failures} failures. Private native fixture only.");
return failures==0?0:1;
static class Native{
 [DllImport("kernel32",SetLastError=true)]internal static extern IntPtr VirtualAlloc(IntPtr address,nuint size,uint type,uint protection);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate int Prepare(IntPtr source,IntPtr target,out IntPtr trampoline);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate int AddressAction(IntPtr source);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate void Observer();
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate void Release(IntPtr state,ulong direction,float duration,byte headshot,byte redirect,IntPtr method);
}
