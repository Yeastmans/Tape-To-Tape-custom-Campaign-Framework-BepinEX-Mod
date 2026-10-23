using CampaignCore;
using EndlessMode;
using Data;
using Spine;
using Skaters;
using System.Reflection;

int checks = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
const string Head = "Faces/Toronto/Mathieu", Helmet = "Faces/Custom/Helmet_Colors";
const BindingFlags Hidden = BindingFlags.Static | BindingFlags.NonPublic;
void Before(bool home, bool headOnly, ForwardData player) => typeof(HeadAppearanceRuntime).GetMethod("BeforeSkins", Hidden)!.Invoke(null, [home, headOnly, player]);
void After(SkeletonData skeleton, SkaterType role, bool home, bool headOnly, ForwardData player, IEquipmentOwner owner, Il2CppSystem.Collections.Generic.List<Skin> skins)
    => typeof(HeadAppearanceRuntime).GetMethod("AfterSkins", Hidden)!.Invoke(null, [skeleton, role, home, headOnly, player, owner, skins]);

var harmony = new HarmonyLib.Harmony(); HeadAppearanceRuntime.Install(harmony);
Check(harmony.Target?.Name == "GetSkins" && harmony.Prefix?.Name == "BeforeSkins" && harmony.Postfix?.Name == "AfterSkins", "Both policies attach to the existing native skin helper");
var player = new ForwardData { headSkin = Head + "_Helmet", helmetSkin = "none", helmetAwaySkin = Helmet, glassesSkin = "none", id = "keep", skill = 87 };
HeadAppearanceRuntime.Configure(player);
Check(player.headSkin == Head, "Configured fitted head normalizes to base so native live equipment can choose its mesh");
Check(player.helmetSkin == "__NO_HELMET__" && player.helmetAwaySkin == Helmet, "Explicit bare Home preserves independent Away helmet intent");
Check(player.glassesSkin == "" && player.id == "keep" && player.skill == 87, "No-glasses sentinel clears without changing identity or earned stats");
var other = new ForwardData { headSkin = Head, helmetSkin = Helmet, helmetAwaySkin = Helmet, glassesSkin = "native-glasses" };
HeadAppearanceRuntime.Configure(other);
Check(other.headSkin == Head && other.helmetSkin == Helmet && other.helmetAwaySkin == Helmet && other.glassesSkin == "native-glasses", "Supported head retains native helmets and glasses without pinning fitted art");
var noVariants = HeadAppearance.NativeChoices.Where(HeadAppearance.HasNoHelmetVariant).ToArray();
Check(noVariants.Length > 0 && noVariants.Contains("Faces/Knights/Lancelov"), "Current bare-only head catalogue includes the known unsupported family");
foreach (string head in noVariants) {
    var unsupported = new ForwardData { headSkin = head, helmetSkin = Helmet, helmetAwaySkin = Helmet };
    HeadAppearanceRuntime.Configure(unsupported);
    Check(unsupported.headSkin == head && unsupported.helmetSkin == "__NO_HELMET__" && unsupported.helmetAwaySkin == "__NO_HELMET__", "Unsupported head disables both authored helmet selections: " + head);
}
var empty = new ForwardData { headSkin = "", helmetSkin = Helmet, helmetAwaySkin = "none", glassesSkin = "none" };
HeadAppearanceRuntime.Configure(empty);
Check(empty.headSkin == "" && empty.helmetSkin == Helmet && empty.helmetAwaySkin == "none" && empty.glassesSkin == "", "Empty head does not invent or rewrite helmet ownership");
var opaque = new ForwardData { headSkin = "Faces/Unregistered/Native_Transform_Helmet", helmetSkin = Helmet, helmetAwaySkin = Helmet };
HeadAppearanceRuntime.Configure(opaque);
Check(opaque.headSkin == "Faces/Unregistered/Native_Transform_Helmet" && opaque.helmetSkin == Helmet, "Unknown native head identity is not guessed or stripped");

foreach (bool home in new[] { true, false }) {
    Boundary.Reset(); Before(home, false, player);
    Check(Boundary.KitCalls == 1 && ReferenceEquals(Boundary.Player, player) && Boundary.Home == home, "Full render forwards exact requested kit to appearance binding: " + home);
    Before(!home, true, player);
    Check(Boundary.KitCalls == 1 && Boundary.Home == home, "Head-only preview does not switch full-player kit: " + home);
    foreach (bool headOnly in new[] { false, true })
    foreach (IEquipmentOwner owner in new IEquipmentOwner[] { null, new EquipmentOwner() })
    foreach (string liveEquipment in new[] { "bare", "helmet", "stack" }) {
        var nativeHead = new Skin(liveEquipment == "bare" ? Head : Head + "_Helmet");
        var result = new Il2CppSystem.Collections.Generic.List<Skin> { nativeHead, new("Body/Test") };
        if (liveEquipment != "bare") result.Add(new Skin(Helmet));
        if (liveEquipment == "stack") result.Add(new Skin("Native/SecondHelmet"));
        var before = result.ToArray(); Boundary.Reset();
        After(new SkeletonData(), SkaterType.Skater, home, headOnly, player, owner, result);
        Check(result.SequenceEqual(before) && ReferenceEquals(result[0], nativeHead), "Native head/helmet/stack result preserved for kit/headOnly/owner/equipment: " + home + "/" + headOnly + "/" + (owner != null) + "/" + liveEquipment);
        Check(Boundary.Decals == (headOnly ? 0 : 1) && Boundary.Captures == (headOnly ? 0 : 1)
            && Boundary.Referees == 0 && (headOnly || ReferenceEquals(Boundary.Skins, result) && Boundary.Home == home && ReferenceEquals(Boundary.Player, player)),
            "Full render keeps decal/logo input while head-only render excludes it");
    }
    foreach (bool headOnly in new[] { true, false }) {
        var result = new Il2CppSystem.Collections.Generic.List<Skin> { new("Referee/NativeHead"), new("Native/Helmet") };
        var before = result.ToArray(); Boundary.Reset(); After(new SkeletonData(), SkaterType.Referee, home, headOnly, player, null, result);
        Check(Boundary.Referees == 1 && Boundary.Decals == 0 && Boundary.Captures == 0 && result.SequenceEqual(before), "Referee route retains native head result and excludes player decal/logo paths");
    }
}
foreach (string missing in new[] { "player", "skeleton", "result" }) {
    Boundary.Reset(); After(missing == "skeleton" ? null : new SkeletonData(), SkaterType.Skater, true, false, missing == "player" ? null : player, null,
        missing == "result" ? null : new Il2CppSystem.Collections.Generic.List<Skin>());
    Check(Boundary.Decals + Boundary.Captures + Boundary.Referees == 0, "Incomplete native skin result has no side effects: " + missing);
}
Console.WriteLine($"{checks} source-linked head selection/preservation checks passed; no native rendering executed.");

namespace Data { public class ForwardData { public string headSkin, helmetSkin, helmetAwaySkin, glassesSkin, id; public int skill; } }
namespace EndlessMode {
    public static class Boundary {
        public static int KitCalls, Decals, Captures, Referees; public static Data.ForwardData Player; public static bool Home; public static object Skins;
        public static void Reset() { KitCalls = Decals = Captures = Referees = 0; Player = null; Skins = null; Home = false; }
    }
    public static class CampaignAppearanceRuntime { public static void ForKit(Data.ForwardData player, bool home) { Boundary.KitCalls++; Boundary.Player = player; Boundary.Home = home; } }
    public static class AppearanceDecalRuntime { public static void Forward(Spine.SkeletonData data, Data.ForwardData player, Il2CppSystem.Collections.Generic.List<Spine.Skin> skins, bool home) { Boundary.Decals++; Boundary.Player = player; Boundary.Home = home; Boundary.Skins = skins; } }
    public static class JerseyLogoNativeHooks { public static void CaptureList(Data.ForwardData player, bool home, Il2CppSystem.Collections.Generic.List<Spine.Skin> skins) { Boundary.Captures++; Boundary.Player = player; Boundary.Home = home; Boundary.Skins = skins; } }
    public static class CampaignRefereeAppearance { public static void Apply(Spine.SkeletonData data, Data.ForwardData player, Il2CppSystem.Collections.Generic.List<Spine.Skin> skins) { Boundary.Referees++; } }
}
namespace Skaters { public enum SkaterType { Skater, Referee } public interface IEquipmentOwner { } public class EquipmentOwner : IEquipmentOwner { } public class SkinHelper { public static void GetSkins() { } } }
namespace Il2CppSystem.Collections.Generic { public class List<T> : System.Collections.Generic.List<T> { } }
namespace Spine {
    public class Skin { public string Name; public Skin(string name) { Name = name; } public object GetAttachment(int slot, string name) => throw new Exception("Managed policy must not inspect native head attachments"); }
    public class SkeletonData { public object FindSkin(string name) => throw new Exception("Native skin selection must remain authoritative"); }
}
namespace HarmonyLib {
    public class Harmony { public MethodInfo Target; public HarmonyMethod Prefix, Postfix; public void Patch(MethodInfo method, HarmonyMethod prefix, HarmonyMethod postfix) { Target = method; Prefix = prefix; Postfix = postfix; } }
    public class HarmonyMethod { public string Name; public HarmonyMethod(Type type, string name) { Name = name; } }
    public static class AccessTools { public static MethodInfo Method(Type type, string name) => type.GetMethod(name); }
}
