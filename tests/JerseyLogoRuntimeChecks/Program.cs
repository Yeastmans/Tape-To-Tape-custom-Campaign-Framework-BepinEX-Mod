using CampaignCore;
using EndlessMode;
using Data;
using UnityEngine;
int passed=0, failed=0;
void Check(string name, Action test) { try { test(); passed++; Console.WriteLine("PASS "+name); } catch(Exception e) { failed++; Console.WriteLine("FAIL "+name+": "+e.Message); } }
void Assert(bool value, string reason = "Assertion failed") { if(!value) throw new Exception(reason); }
CampaignDocument Doc(string s) => new(s.Replace(";", "\n"));
string root=Path.Combine(Path.GetTempPath(), "jersey-runtime-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(root,"logos"));
Plugin.ModFolder=root; Plugin.ModContentRoot=root;
File.WriteAllBytes(Path.Combine(root,"logos","home.png"),new byte[]{1});
File.WriteAllBytes(Path.Combine(root,"logos","away.png"),new byte[]{2});
var team=Doc("Jersey Logo From = home;Jersey Logo Away From = away");
ForwardData Forward()=>new(){bodySkin="Body/Customization/Customization_colors",bodyAwaySkin="Body/Customization/Customization_colors"};
GoaltenderData Goalie()=>new(){skin=AppearanceContract.DefaultSkin("goalie","Skin"),awaySkin=AppearanceContract.DefaultSkin("goalie","Skin")};
void Bind(SkaterData p,CampaignDocument t,CampaignDocument own=null)=>JerseyLogoRuntime.Bind(p,t,own??Doc(""),Doc(""),root);
try {
Check("ForwardHomeAwayTexturesDiffer",()=>{var p=Forward(); Bind(p,team); Assert(JerseyLogoRuntime.Resolve(p,false).Override && JerseyLogoRuntime.Resolve(p,true).Override && JerseyLogoRuntime.Resolve(p,false).Texture!=JerseyLogoRuntime.Resolve(p,true).Texture);});
Check("GoalieHomeAwayTexturesDiffer",()=>{var p=Goalie(); Bind(p,team); Assert(JerseyLogoRuntime.Resolve(p,false).Override && JerseyLogoRuntime.Resolve(p,true).Override && JerseyLogoRuntime.Resolve(p,false).Texture!=JerseyLogoRuntime.Resolve(p,true).Texture);});
Check("ExplicitNoneUsesBlankTexture",()=>{var p=Goalie(); Bind(p,Doc("Jersey Logo From = none")); var c=JerseyLogoRuntime.Resolve(p,false); Assert(c.Override && c.Texture is Texture2D t && t.Pixels.Length==4 && t.Pixels.All(x=>x.a==0));});
Check("CampaignSameNameIsolation",()=>{var p=Forward(); Bind(p,team); var a=JerseyLogoRuntime.Resolve(p,false).Texture; string other=Path.Combine(root,"other");Directory.CreateDirectory(Path.Combine(other,"logos"));File.WriteAllBytes(Path.Combine(other,"logos","home.png"),new byte[]{3});var b=Forward();JerseyLogoRuntime.Bind(b,team,Doc(""),Doc(""),other);Assert(JerseyLogoRuntime.Resolve(b,false).Override && JerseyLogoRuntime.Resolve(b,false).Texture!=a);});
Check("SnapshotRestoresBothKits",()=>{var p=Forward();Bind(p,team);var snapshot=JerseyLogoRuntime.Snapshot(p);var b=Forward();JerseyLogoRuntime.Restore(b,snapshot);Assert(JerseyLogoRuntime.Resolve(p,false).Override && JerseyLogoRuntime.Resolve(b,true).Override && JerseyLogoRuntime.Resolve(p,false).Texture==JerseyLogoRuntime.Resolve(b,false).Texture && JerseyLogoRuntime.Resolve(p,true).Texture==JerseyLogoRuntime.Resolve(b,true).Texture);});
Check("OwnPlayerUnaffectedByTeamChange",()=>{var p=Forward();var own=Doc("Override Team Settings = yes;Logo From = home");Bind(p,team,own);var a=JerseyLogoRuntime.Resolve(p,true).Texture;Bind(p,Doc("Jersey Logo From = none;Jersey Logo Away From = none"),own);Assert(a!=null && JerseyLogoRuntime.Resolve(p,true).Texture==a);});
Check("UnboundVanillaPlayerPassesThrough",()=>Assert(!JerseyLogoRuntime.Resolve(Forward(),false).Override));
Check("MixedCompatiblePremadeKits",()=>{var p=Forward();p.bodyAwaySkin="Body/HockeyFC/HockeyFC";Bind(p,team);Assert(JerseyLogoRuntime.Resolve(p,false).Override && !JerseyLogoRuntime.Resolve(p,true).Override);});
Check("FrozenEmbeddedKitsResolveIndependently",()=>{var p=Forward();var doc=Doc("Jersey Logo From = home;Jersey Logo Away From = away");foreach(bool away in new[]{false,true}){byte[] bytes=new byte[]{away?(byte)2:(byte)1};string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(), prefix=JerseyLogoOptions.Prefix(away);doc.Set(prefix+" Mode","embedded");doc.Set(prefix+" ID","custom_t2t_"+hash+".png");doc.Set(prefix+" SHA256",hash);doc.Set(prefix+" PNG",Convert.ToBase64String(bytes));}JerseyLogoRuntime.Bind(p,Doc(""),doc,Doc(""),root);Assert(JerseyLogoRuntime.Resolve(p,false).Override&&JerseyLogoRuntime.Resolve(p,true).Override&&JerseyLogoRuntime.Resolve(p,false).Texture!=JerseyLogoRuntime.Resolve(p,true).Texture);});
Check("MalformedEmbeddedPayloadFailsClosed",()=>{var p=Forward();Bind(p,Doc("Jersey Logo From = home;Export Jersey Logo Mode = embedded;Export Jersey Logo SHA256 = invalid;Export Jersey Logo PNG = AQ=="));Assert(!JerseyLogoRuntime.Resolve(p,false).Override);});
Check("ForeignAndAmbiguousCloneRejected",()=>{var p=Forward();p.id="same";Bind(p,team);var clone=Forward();clone.id="same";Assert(!JerseyLogoRuntime.CopyUniqueClone(clone,Array.Empty<SkaterData>()));Assert(!JerseyLogoRuntime.CopyUniqueClone(clone,new[]{p,p}));Assert(!JerseyLogoRuntime.Resolve(clone,false).Override);Assert(JerseyLogoRuntime.CopyUniqueClone(clone,new[]{p}));Assert(JerseyLogoRuntime.Resolve(clone,false).Override);});

JerseyLogoRequests.Request Request(int renderer, int skin, Texture texture=null, JerseyLogoRequests.Request parent=null)
    => JerseyLogoRequests.Begin(new(renderer),new(skin),new(1),new(2),false,JerseyLogoRuntime.Context,new(texture),new object(),parent);
JerseyLogoRequests.Operation Operation(int renderer=100, int kind=1, Texture texture=null)
{ var o=JerseyLogoRequests.Start(kind,Request(renderer,5,texture));o.SyncEligible=true;return o; }
void Error(Exception e) { }
Check("InitialSynchronousCompletionAndHashSkip",()=>{var o=Operation();int calls=0,writes=0;JerseyLogoRequests.RunSlice(()=>o,new(1000),()=>calls++,r=>writes++,Error);Assert(calls==1 && writes==1 && o.Completed && JerseyLogoRequests.Current==null);});
Check("SynchronousFaultAndCancellationNeverRefresh",()=>{foreach(bool cancellation in new[]{false,true}){var o=Operation();int calls=0,writes=0;JerseyLogoRequests.RunSlice(()=>o,new(1000),()=>{calls++;JerseyLogoRequests.MarkBuilderFault(new(1008));},r=>writes++,Error);Assert(calls==1 && writes==0 && o.Completed && o.Faulted);}});
Check("CaptureLoggerFailureStillCallsOriginal",()=>{int calls=0;try{JerseyLogoRequests.RunSlice(()=>throw new Exception("capture"),new(1000),()=>calls++,r=>{},e=>throw new Exception("logger"));}catch{}Assert(calls==1);});
Check("CaptureAndOriginalFailuresPreserveExactlyOnce",()=>{int calls=0,writes=0;JerseyLogoRequests.RunSlice(()=>throw new Exception("capture"),new(1000),()=>calls++,r=>writes++,Error);Assert(calls==1&&writes==0);var o=Operation();try {JerseyLogoRequests.RunSlice(()=>o,new(1000),()=>{calls++;throw new Exception("native proxy");},r=>writes++,Error);}catch{}Assert(calls==2&&writes==0&&JerseyLogoRequests.Current==null);});
Check("SuspensionResumeRetiresBeforeCompletion",()=>{var o=Operation();int writes=0;JerseyLogoRequests.RunSlice(()=>o,new(1000),()=>JerseyLogoRequests.Publish(o,()=>{},()=>new(2000),Error),r=>writes++,Error);Assert(writes==0&&!o.Completed&&JerseyLogoRequests.Resume(1,new(2000))==o);JerseyLogoRequests.RunSlice(()=>JerseyLogoRequests.Resume(1,new(2000)),new(2032),()=>JerseyLogoRequests.Finish(1,new(2000),false,r=>writes++,()=>Assert(JerseyLogoRequests.Resume(1,new(2000))==null),Error),r=>writes++,Error);Assert(writes==1&&o.Completed);});
Check("ImmediateContinuationAndPooledRunnerReuse",()=>{var o=Operation();JerseyLogoRequests.Operation reused=null;JerseyLogoRequests.RunSlice(()=>o,new(1000),()=>{JerseyLogoRequests.Publish(o,()=>{},()=>new(2000),Error);JerseyLogoRequests.Finish(1,new(2000),false,r=>{},()=>{reused=Operation();JerseyLogoRequests.Bridge(reused,new(2000));},Error);},r=>{},Error);Assert(JerseyLogoRequests.Resume(1,new(2000))==reused);JerseyLogoRequests.Complete(o,false);Assert(JerseyLogoRequests.Resume(1,new(2000))==reused);JerseyLogoRequests.Terminal(1,new(2000),true);});
Check("RegistrationFailureAfterAllocationRetires",()=>{var o=Operation();int writes=0;JerseyLogoRequests.RunSlice(()=>o,new(1000),()=>{JerseyLogoRequests.Publish(o,()=>{},()=>new(2000),Error);JerseyLogoRequests.MarkBuilderFault(new(1008));JerseyLogoRequests.Finish(1,new(2000),true,r=>writes++,()=>{},Error);},r=>writes++,Error);Assert(o.Completed&&o.Faulted&&writes==0&&JerseyLogoRequests.Resume(1,new(2000))==null);});
Check("PublicationObservationFailureIsIndeterminate",()=>{foreach(bool throws in new[]{false,true}){var o=Operation();int writes=0;JerseyLogoRequests.RunSlice(()=>o,new(1000),()=>JerseyLogoRequests.Publish(o,()=>{},()=>throws?throw new Exception("read failed"):IntPtr.Zero,Error),r=>writes++,Error);Assert(writes==0&&!o.PublicationObserved);}});
Check("NestedOwnerWorldScopesAndExactBuilder",()=>{var a=Operation();using(var outer=JerseyLogoRequests.Enter(a,new(1000))){var child=JerseyLogoRequests.Start(3,Request(100,6,null,a.Request));using(var inner=JerseyLogoRequests.Enter(child,new(3000))){Assert(child.Request==a.Request);JerseyLogoRequests.MarkBuilderFault(new(9008));Assert(!a.Faulted&&!child.Faulted);JerseyLogoRequests.MarkBuilderFault(new(1008));Assert(a.Faulted&&!child.Faulted);JerseyLogoRequests.MarkBuilderFault(new(3008));Assert(child.Faulted);}Assert(JerseyLogoRequests.Current==outer);}Assert(JerseyLogoRequests.Current==null);});
Check("SupersededOwnerCannotReclaimThroughNestedWorld",()=>{var a=Operation();var b=Operation();var child=Request(100,6,null,a.Request);Assert(child==a.Request&&!JerseyLogoRequests.IsCurrent(child)&&JerseyLogoRequests.IsCurrent(b.Request));JerseyLogoRequests.Complete(a,false);JerseyLogoRequests.Complete(b,false);});
Check("AuthoredAThenNativeBRejectsLateOwnerA",()=>{var p=Forward();Bind(p,team);var authored=JerseyLogoRuntime.Resolve(p,false).Texture;var a=Operation(texture:authored);var b=Operation();Texture incoming=new Texture2D(2,2,TextureFormat.RGBA32,false);using(var scope=JerseyLogoRequests.Enter(a,new(1000)))Assert(!JerseyLogoRequests.FinalLogo(a.Request,new(100),new(5),false,ref incoming));using(var scope=JerseyLogoRequests.Enter(b,new(3000)))Assert(JerseyLogoRequests.FinalLogo(b.Request,new(100),new(5),false,ref incoming)&&incoming!=authored);});
Check("ExplicitNoneRejectsGoalieInlineOverwrite",()=>{var p=Goalie();Bind(p,Doc("Jersey Logo From = none"));var blank=JerseyLogoRuntime.Resolve(p,false).Texture;var r=JerseyLogoRequests.Begin(new(150),new(5),new(1),new(2),true,JerseyLogoRuntime.Context,new(blank),new object());var o=JerseyLogoRequests.Start(2,r);Texture incoming=new Texture2D(2,2,TextureFormat.RGBA32,false);using(var scope=JerseyLogoRequests.Enter(o,new(1000)))Assert(JerseyLogoRequests.FinalLogo(r,new(150),new(5),true,ref incoming)&&incoming==blank);});
Check("MismatchedSkinNeverReceivesLatestArtwork",()=>{var p=Forward();Bind(p,team);var o=Operation(texture:JerseyLogoRuntime.Resolve(p,false).Texture);Texture native=new Texture2D(2,2,TextureFormat.RGBA32,false), incoming=native;using(var scope=JerseyLogoRequests.Enter(o,new(1000)))Assert(!JerseyLogoRequests.CanApply(o.Request,new(9))&&JerseyLogoRequests.FinalLogo(o.Request,new(100),new(9),false,ref incoming)&&incoming==native);});
Check("UnknownResumeSupersedesAuthoredWithoutSuppressingNative", () =>
{
    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
    var authored = Request(700, 5, texture);
    var unknown = JerseyLogoRequests.BeginNative(new(700), new(6), false, JerseyLogoRuntime.Context, new object());
    Texture native = new Texture2D(2, 2, TextureFormat.RGBA32, false), incoming = native;
    Assert(JerseyLogoRequests.FinalLogo(unknown, new(700), new(6), false, ref incoming), "Unknown native B must invoke SetLogo");
    Assert(incoming == native && !unknown.Choice.Override, "Unknown B must never acquire authored selection");
    Assert(!JerseyLogoRequests.FinalLogo(authored, new(700), new(5), false, ref incoming), "Late authored A must be rejected after unknown B");
});
Check("SelectedPreviewLogoPreservesPendingAndCompletedImageAndNone", () =>
{
    foreach (bool none in new[] { false, true })
    foreach (bool pending in new[] { false, true })
    {
        var player = Forward();
        Bind(player, none ? Doc("Jersey Logo From = none") : team);
        var choice = JerseyLogoRuntime.Resolve(player, false);
        var request = JerseyLogoRequests.Begin(new(701), new(15), player.Pointer, new(702), false,
            JerseyLogoRuntime.Context, choice, new object());
        var saved = JerseyLogoRequests.FindOwner(new(701), new(702), player.Pointer);
        Assert(saved == request, "Exact preview owner/player must recover its request even before the new composite is installed");
        Assert(JerseyLogoRequests.FindOwner(new(701), new(703), player.Pointer) == null, "Another preview owner must not recover this request");
        Assert(JerseyLogoRequests.FindOwner(new(701), new(702), new(9999)) == null, "Changed selected player must not recover this request");
        Texture native = new Texture2D(2, 2, TextureFormat.RGBA32, false), incoming = native;
        Assert(JerseyLogoRequests.FinalLogo(saved, new(701), new(pending ? 14 : 15), false, ref incoming), "Exact preview logo call must remain valid");
        Assert(incoming == (pending ? native : choice.Texture), "Only the matching composite may receive image or explicit none");
        Assert(JerseyLogoRequests.IsCurrent(request) && JerseyLogoRequests.CanApply(request, new(15)), "InitializeLogo must preserve pending completion ownership");
        JerseyLogoRequests.BeginNative(new(701), new(15), false, JerseyLogoRuntime.Context, new object());
        Assert(JerseyLogoRequests.FindOwner(new(701), new(702), player.Pointer) == null, "Real unscoped clear must invalidate saved preview ownership");
        Assert(!JerseyLogoRequests.CanApply(request, new(15)), "Late completion after native clear must be rejected");
    }
});
Check("ReentrantPreviewStartsNewGenerationForSameOwnerAndPlayer", () =>
{
    var old = Operation(renderer: 705, texture: new Texture2D(2, 2, TextureFormat.RGBA32, false));
    using (JerseyLogoRequests.Enter(old, new(3000)))
    {
        JerseyLogoRequests.BeginNative(new(705), new(5), false, JerseyLogoRuntime.Context, new object());
        var next = Request(705, 16, new Texture2D(2, 2, TextureFormat.RGBA32, false));
        Assert(next != old.Request && JerseyLogoRequests.IsCurrent(next), "Explicit new preview must not inherit the executing old generation");
        Texture incoming = old.Request.Choice.Texture;
        Assert(!JerseyLogoRequests.FinalLogo(old.Request, new(705), new(5), false, ref incoming), "Old scoped SetLogo must remain rejected after reentrant preview selection");
    }
});
Check("ContextRetirementPreservesInflightAndDisplayedTextures",()=>{var p=Forward();Bind(p,team);var texture=JerseyLogoRuntime.Resolve(p,false).Texture;var o=Operation(texture:texture);JerseyLogoRequests.Bridge(o,new(2000));JerseyLogoRequests.DidDisplay(new(100),texture);JerseyLogoRequests.RetireContext(JerseyLogoRuntime.Context+1);Assert(!JerseyLogoRequests.IsCurrent(o.Request)&&JerseyLogoRequests.RetainedTextures().Contains(texture));JerseyLogoRequests.Complete(o,true);Assert(JerseyLogoRequests.RetainedTextures().Contains(texture));JerseyLogoRequests.ForgetRenderer(new(100));});

Check("ContentChangeInvalidatesSamePathCache",()=>{var p=Forward();Bind(p,team);var before=JerseyLogoRuntime.Resolve(p,false).Texture;File.WriteAllBytes(Path.Combine(root,"logos","home.png"),new byte[]{9});Assert(JerseyLogoRuntime.Resolve(p,false).Texture!=before);});
Check("FrozenBindingsIgnoreLaterDocumentMutation",()=>{var p=Forward();var doc=Doc("Jersey Logo From = home");Bind(p,doc);var before=JerseyLogoRuntime.Resolve(p,false).Texture;doc.Set("Jersey Logo From","none");Assert(JerseyLogoRuntime.Resolve(p,false).Texture==before);});
Check("ContextCollectionWaitsForRendererDestruction",()=>{var p=Forward();Bind(p,team);var texture=JerseyLogoRuntime.Resolve(p,false).Texture;JerseyLogoRequests.DidDisplay(new(999),texture);Plugin.ModFolder=Path.Combine(root,"newcontext");JerseyLogoRuntime.EnsureContext();Assert(!texture.Destroyed&&!JerseyLogoRuntime.Resolve(p,false).Override);JerseyLogoRequests.ForgetRenderer(new(999));JerseyLogoRuntime.Collect();Assert(texture.Destroyed);});
Check("UnintegratedAndChangedNativeContractsFailClosed",()=>{var expected=System.Text.Json.JsonSerializer.Deserialize<RawNativeHookContract[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"jersey-logo-targets.json")));bool denied=false;try{RawNativeHookContract.RequireExactSet(expected,Array.Empty<RawNativeHookContract>(),"jersey-logo");}catch(InvalidDataException){denied=true;}Assert(denied);RawNativeHookContract.RequireExactSet(expected,expected,"jersey-logo");Assert(expected.Length==21&&expected.Single(c=>c.Kind=="builder-fault").AliasCount==0);var copy=System.Text.Json.JsonSerializer.Deserialize<RawNativeHookContract[]>(System.Text.Json.JsonSerializer.Serialize(expected));copy[0].Sha256="changed";denied=false;try{RawNativeHookContract.RequireExactSet(expected,copy,"jersey-logo");}catch(InvalidDataException){denied=true;}Assert(denied);});
Check("RawCompleteNamedMissingOrAlteredInstallsNothing", () =>
{
    var raw = System.Text.Json.JsonSerializer.Deserialize<RawNativeHookContract[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "jersey-logo-targets.json")));
    var required = JerseyLogoManagedTargets.Required;
    foreach (var approved in new[] { Array.Empty<NamedManagedHookContract>(), required.Skip(1).ToArray(),
        System.Text.Json.JsonSerializer.Deserialize<NamedManagedHookContract[]>(System.Text.Json.JsonSerializer.Serialize(required)) })
    {
        if (approved.Length == required.Length) approved[0].ReturnType = "System.Int32";
        int installs = 0;
        try
        {
            JerseyLogoManagedTargets.ValidateBeforeInstall(raw, raw, approved);
            installs++;
        }
        catch (InvalidDataException) { }
        Assert(installs == 0, "Complete raw contracts with missing/altered named contracts must install zero callbacks");
    }
    JerseyLogoManagedTargets.ValidateBeforeInstall(raw, raw, required);
    Assert(required.Length == 11 && required.Select(c => c.Id).Distinct().Count() == 11, "All nine existing and two selected-preview targets must be required exactly once");
});
if (args.Length == 2)
{
    Check("NamedManifestResolvesExactFreshInteropSignatures", () =>
    {
        var assemblies = Directory.GetFiles(args[0], "*.dll").Concat(Directory.GetFiles(args[1], "*.dll"))
            .Concat(Directory.GetFiles(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "*.dll"));
        using var metadata = new System.Reflection.MetadataLoadContext(new System.Reflection.PathAssemblyResolver(assemblies), "System.Private.CoreLib");
        var game = metadata.LoadFromAssemblyPath(Path.Combine(Path.GetFullPath(args[0]), "MainScriptsAssembly.dll"));
        foreach (var contract in JerseyLogoManagedTargets.Required)
        {
            var target = JerseyLogoManagedTargets.Resolve(contract, new[] { game });
            Assert(target != null, "Fresh metadata target must match: " + contract.Id);
        }
        Console.WriteLine("Resolved all 11 exact managed contracts against fresh interop without executing game code.");
    });
}
} finally { Directory.Delete(root,true); }
Console.WriteLine($"{passed} passed; {failed} failed.");
return failed==0?0:1;
