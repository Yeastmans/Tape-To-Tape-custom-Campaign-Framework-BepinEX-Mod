// These adapters exercise the production selection and patch-prefix code. They
// do not run native weighting, Harmony patch installation or game event menus.
using System.Reflection;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime.InteropTypes;
namespace Il2CppInterop.Runtime.InteropTypes
{
    public class Il2CppObjectBase
    {
        public T Cast<T>() => (T)(object)this;
        public Type GetIl2CppType() => GetType();
    }
}
namespace Il2CppSystem.Collections.Generic
{
    public interface IEnumerable<T> : System.Collections.Generic.IEnumerable<T> { }
    public interface IReadOnlyCollection<T> : System.Collections.Generic.IReadOnlyCollection<T> { }
    public interface IReadOnlyList<T> : System.Collections.Generic.IReadOnlyList<T>, IReadOnlyCollection<T>
    { TCast Cast<TCast>(); }
    public class List<T> : System.Collections.Generic.List<T>, IEnumerable<T>, IReadOnlyList<T>
    {
        public List() {}
        public List(System.Collections.Generic.IEnumerable<T> items) : base(items) {}
        public TCast Cast<TCast>() => (TCast)(object)this;
        public IntPtr Pointer => Il2CppInterop.Runtime.NativeCardMemory.RegisterList(this);
    }
}
namespace Il2CppInterop.Runtime
{
    // Model the current generated wrapper's boxed value-type argument boundary.
    // The production AddChoice is linked unchanged; this is not Unity execution.
    public static class Il2CppClassPointerStore<T> { public static IntPtr NativeClassPtr = (IntPtr)77; }
    public static class NativeCardMemory
    {
        private static readonly Dictionary<object, IntPtr> ListPointers = new();
        internal static readonly Dictionary<IntPtr, object> Lists = new();
        internal static readonly Dictionary<IntPtr, STS.Map.EventChoice> Boxes = new();
        public static IntPtr RegisterList(object list)
        {
            if (!ListPointers.TryGetValue(list, out var pointer))
            { pointer = (IntPtr)(1000 + ListPointers.Count); ListPointers[list] = pointer; Lists[pointer] = list; }
            return pointer;
        }
        public static IntPtr RegisterCard(STS.Map.EventChoice choice)
        {
            IntPtr box = Marshal.AllocHGlobal(0x90);
            for (int i = 0; i < 0x90; i++) Marshal.WriteByte(box, i, 0);
            Boxes[box] = choice;
            return box;
        }
        public static STS.Map.EventChoice CopyArgument(IntPtr argument)
        {
            // Native Add copies the payload at its argument, not an object.
            if (Boxes.TryGetValue(argument - 16, out var card)) return card.Copy();
            if (Boxes.TryGetValue(argument, out card))
            {
                // Exact current field offsets: the boxed interactable byte at
                // 0x30 is mistaken for payload ChoiceType at 0x30.
                var shifted = card.Copy();
                shifted.ChoiceType = (STS.Map.EEventChoiceType)Marshal.ReadInt32(argument, 0x30);
                return shifted;
            }
            throw new InvalidOperationException("Unknown native card payload");
        }
    }
    public static unsafe class IL2CPP
    {
        public static int UnboxCalls, InvokeCalls;
        public static bool MissingMethod, FailInvoke;
        public static IntPtr il2cpp_class_get_method_from_name(IntPtr klass, string name, int count)
        {
            if (klass != (IntPtr)77 || name != "Add" || count != 1) throw new InvalidOperationException("Wrong native method");
            return MissingMethod ? IntPtr.Zero : (IntPtr)88;
        }
        public static IntPtr il2cpp_object_unbox(IntPtr value) { UnboxCalls++; return value + 16; }
        public static IntPtr il2cpp_runtime_invoke(IntPtr method, IntPtr target, void** arguments, ref IntPtr exception)
        {
            if (method != (IntPtr)88) throw new InvalidOperationException("Wrong invocation");
            InvokeCalls++;
            if (FailInvoke) { exception = (IntPtr)99; return IntPtr.Zero; }
            ((Il2CppSystem.Collections.Generic.List<STS.Map.EventChoice>)NativeCardMemory.Lists[target])
                .Add(NativeCardMemory.CopyArgument((IntPtr)arguments[0]));
            return IntPtr.Zero;
        }
    }
    public static class Il2CppException
    {
        public static void RaiseExceptionIfNecessary(IntPtr exception)
        { if (exception != IntPtr.Zero) throw new InvalidOperationException("Expected native Add exception"); }
    }
}
namespace Il2CppSystem
{
    public sealed class Object(string value)
    {
        public override string ToString() => value;
        public static implicit operator Object(string value) => new(value);
    }
}
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    public class HarmonyPatch(Type type, string method) : Attribute
    { public Type Type = type; public string Method = method; }
    public class HarmonyPrefix : Attribute { }
    public class HarmonyPostfix : Attribute { }
    public class HarmonyFinalizer : Attribute { }
    public class HarmonyPriority(int value) : Attribute { public int Value = value; }
    public static class Priority { public const int First = 800; }
    public static class AccessTools
    {
        public static MethodInfo Method(Type type, string name) => type.GetMethod(name,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
    }
}
namespace UnityEngine
{
    public class Object : Il2CppObjectBase
    {
        public string name = "";
        public static T Instantiate<T>(T source) where T : Object => (T)source.MemberwiseClone();
    }
    public static class Resources
    {
        public static Object[] Assets = [];
        public static T[] FindObjectsOfTypeAll<T>() => Assets.OfType<T>().ToArray();
    }
}
namespace Rogue
{
    [Flags] public enum RelicCategory { None = 0, Chaos = 128, OnlyStats = 512 }
    [Flags] public enum TalentCategory
    { None = 0, UsedInCampaignPool = 256, Goalie = 1024, Chaos = 16384, OnlyStats = 65536, UsedInCampaignGoaliePool = 131072 }
    public class PowerUp : UnityEngine.Object
    { public string id; public int level = 1; public TalentCategory category; public float GetRewardWeight() => 0;
      public string LocalizedName; public string localizedPowerupName => LocalizedName ?? name; }
    public class Talent : PowerUp { }
    public class Ability : PowerUp { public string GetName() => name; }
    public class Relic : UnityEngine.Object
    { public string id; public int level = 1; public RelicCategory category; public float GetRewardWeight() => 0; }
    public class SkaterTalentRewards { public Il2CppSystem.Collections.Generic.IReadOnlyList<Talent> Talents; }
    public static class RogueUtils { public static SkaterTalentRewards GetRandomTalentRewards() => throw new NotImplementedException(); }
}
namespace Rogue.Relics.Repository
{
    public interface IRelicRepository { Relic GetRelic(Type type, int level, bool clone); }
    public class RelicRepository : IRelicRepository
    {
        public Il2CppSystem.Collections.Generic.List<Relic> GetRandomRelics(RelicCategory c, Il2CppSystem.Collections.Generic.List<Relic> skip, bool clone, int count, bool pool, bool any) => throw new NotImplementedException();
        public Relic GetRelic(string id, bool clone) => UnityEngine.Resources.Assets.OfType<Relic>().FirstOrDefault(t => t.id == id);
        public Relic GetRelic(Type type, int level, bool clone) => UnityEngine.Resources.Assets.OfType<Relic>().FirstOrDefault(t => t.GetType() == type && t.level == level);
        private static Il2CppSystem.Collections.Generic.List<Relic> GetWeightedRandomRelics(Il2CppSystem.Collections.Generic.IEnumerable<Relic> source, int count)
        { var list = new Il2CppSystem.Collections.Generic.List<Relic>(); list.AddRange(source.Take(count)); return list; }
    }
}
namespace Rogue.Powerups.Repository
{
    public class TalentRepository
    {
        public static int WeightedCalls;
        public static Func<Il2CppSystem.Collections.Generic.IEnumerable<Talent>, int, Data.ForwardData, Il2CppSystem.Collections.Generic.List<Talent>> WeightedOverride;
        public Il2CppSystem.Collections.Generic.List<Talent> GetRandomTalents(TalentCategory c, Il2CppSystem.Collections.Generic.List<Talent> skip, bool clone, int count, bool pool, bool any, Data.ForwardData f) => throw new NotImplementedException();
        public Talent GetTalent(string id, bool clone) => UnityEngine.Resources.Assets.OfType<Talent>().FirstOrDefault(t => t.id == id);
        public Talent GetTalent(Type type, int level, bool clone) => UnityEngine.Resources.Assets.OfType<Talent>().FirstOrDefault(t => t.GetType() == type && t.level == level);
        private static Il2CppSystem.Collections.Generic.List<Talent> GetWeightedRandomTalents(Il2CppSystem.Collections.Generic.IEnumerable<Talent> source, int count, Data.ForwardData forward)
        {
            WeightedCalls++;
            if (WeightedOverride != null) return WeightedOverride(source, count, forward);
            var list = new Il2CppSystem.Collections.Generic.List<Talent>(); list.AddRange(source.Take(count)); return list;
        }
    }
    public class AbilityRepository
    {
        public static bool ThrowWeighted;
        public static int WeightedCalls, ThrowWeightedAt;
        public Il2CppSystem.Collections.Generic.List<Ability> GetRandomAbilities(TalentCategory c, Il2CppSystem.Collections.Generic.List<Ability> skip, bool clone, int count, bool pool, bool any, int level) => throw new NotImplementedException();
        public Ability GetAbility(string id, bool clone) => UnityEngine.Resources.Assets.OfType<Ability>().FirstOrDefault(t => t.id == id);
        private static Il2CppSystem.Collections.Generic.List<Ability> GetWeightedRandomAbilities(Il2CppSystem.Collections.Generic.IEnumerable<Ability> source, int count)
        {
            WeightedCalls++;
            if (ThrowWeighted || (ThrowWeightedAt > 0 && WeightedCalls == ThrowWeightedAt)) throw new IOException("expected ability picker failure");
            var list = new Il2CppSystem.Collections.Generic.List<Ability>(); list.AddRange(source.Take(count)); return list;
        }
    }
}
namespace Rogue.NPC.CoachNPC { public class CoachEvent { } }
namespace Data
{
    public class ForwardData : UnityEngine.Object
    { public string FullName => name; public Rogue.Ability ability; }
}
namespace State { public class CampaignState { public RunData runData; } public class RunData { public string squadId; } }
namespace STS.Map
{
    public enum NodeType { Blademaster = 6 }
    public enum EEventChoiceType { Invalid = 0, Skater = 1, PowerUp = 2, PowerUpSwap = 3, PowerUpUpgrade = 4 }
    public struct EventTermParam { }
    public class EventChoice
    {
        private IntPtr pointer;
        public IntPtr Pointer
        {
            get
            {
                if (pointer == IntPtr.Zero) pointer = Il2CppInterop.Runtime.NativeCardMemory.RegisterCard(this);
                Marshal.WriteInt32(pointer, 16 + 0x20, interactable ? 1 : 0);
                Marshal.WriteInt32(pointer, 16 + 0x30, (int)ChoiceType);
                return pointer;
            }
        }
        public EventChoice Copy() { var copy = (EventChoice)MemberwiseClone(); copy.pointer = IntPtr.Zero; return copy; }
        public string buttonName, translatedButtonName;
        public System.Action onSelection;
        public bool interactable;
        public NodeType nodeType;
        public Il2CppSystem.Collections.Generic.List<EventTermParam> choicesTermParams;
        public EEventChoiceType ChoiceType;
        public Data.ForwardData Skater;
        public UnityEngine.Object PowerUpOrRelic, PowerUpOrRelic2;
        public string[] CustomLocalizedTexts;
        public bool SuppressRewardTracking;
    }
}
namespace Rogue.NPC.BlademasterNPC
{
    public class BlademasterEvent
    {
        public readonly Il2CppSystem.Collections.Generic.List<Data.ForwardData> Eligible = new();
        public readonly Il2CppSystem.Collections.Generic.List<STS.Map.EventChoice> eventChoices = new();
        public int EligibilityCalls;
        public State.CampaignState LastState;
        public Il2CppSystem.Collections.Generic.List<Data.ForwardData> GetPlayersEligibleForNewAbility(State.CampaignState state)
        { EligibilityCalls++; LastState = state; return Eligible; }
    }
}
namespace I2.Loc
{
    public static class LocalizationManager
    {
        public static string LastKey;
        public static string GetTermTranslation(string key) { LastKey = key; return "Learn {0}: {1}"; }
    }
}
namespace UtilsMono
{
    public static class SafeStringFormatter
    {
        public static string SafeFormat(string format, Il2CppSystem.Object[] values)
            => string.Format(format, values.Cast<object>().ToArray());
    }
}
namespace Tape2Tape
{
    public static class CampaignUtil { public static State CampaignState; }
    public class State { public Run runData; }
    public class Run { public Team teamData; }
    public class Team { public Data.TeamData _teamData = new(); public Il2CppSystem.Collections.Generic.List<Rogue.Relic> relics; public Team otherTeam; }
}
namespace EndlessMode
{
    public static class Plugin
    {
        public static bool IsDefaultMode;
        public static string ModFolder;
        public static HashSet<string> ExcludedRewardRelicIds = [], ExcludedRewardTalentIds = [], ExcludedRewardAbilityIds = [];
        public static Logger Log = new();
        public static Dictionary<string, TeamConfig> PlayerTeamConfigs = new();
    }
    public class Logger
    {
        public readonly List<string> Lines = [];
        public void LogInfo(string text) => Lines.Add(text);
        public void LogWarning(string text) => Lines.Add(text);
        public void LogError(string text) => Lines.Add(text);
    }
    public static class PatchBossLaunchMatch
    { public static Rogue.Relic FindRelic(string id) => UnityEngine.Resources.Assets.OfType<Rogue.Relic>().FirstOrDefault(r => r.id == id); }
    public sealed class TeamConfig
    {
        public string SquadTemplate = "Basic Squad", StartingBenchPositions = "";
        public int BenchSize;
        public object LW, RW, C, LD, RD;
        public Dictionary<string, string> SquadBehaviors = new();
    }
    internal static class NativeSquadBehaviorBridge { internal static bool Ready => true; internal static string Status => "test"; }
    internal static class PatchChooseMetaUI { internal static bool SlotIsConfigured(object slot) => slot != null; }
}

namespace HarmonyLib { public class Harmony { public void Patch(MethodInfo method, HarmonyMethod prefix) {} } public class HarmonyMethod(Type type, string name) {} }
