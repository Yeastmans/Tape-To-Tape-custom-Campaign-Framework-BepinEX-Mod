using CampaignCore;
using Data;
using Rogue;

namespace Il2CppSystem.Collections.Generic
{
    public sealed class List<T> : System.Collections.Generic.IEnumerable<T>
    {
        public static readonly Dictionary<IntPtr, List<T>> Lists = new();
        private static int serial;
        private sealed class Storage
        {
            internal readonly System.Collections.Generic.List<T> Items = new();
            internal int Version, Reads;
        }
        private readonly Storage storage;
        public int _version => storage.Version;
        public int Reads => storage.Reads;
        public int Count => storage.Items.Count;
        public IntPtr Pointer { get; }
        public List() { Pointer = ++serial; storage = new(); Lists.Add(Pointer, this); }
        public List(IntPtr pointer) { Pointer = pointer; storage = Lists[pointer].storage; }
        public void Add(T item) { storage.Items.Add(item); storage.Version++; }
        public void Replace(int index, T item) { storage.Items[index] = item; storage.Version++; }
        public T this[int index] { get { storage.Reads++; return storage.Items[index]; } }
        public IEnumerator<T> GetEnumerator() { for (int i = 0; i < Count; i++) yield return this[i]; }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
namespace Il2CppInterop.Runtime.Runtime
{
    public static class Il2CppObjectPool
    {
        public static T Get<T>(IntPtr pointer) where T : class
        {
            object result = typeof(T) == typeof(Tape2Tape.Team) ? Tape2Tape.Team.All[pointer]
                : typeof(T) == typeof(TeamData) ? TeamData.All[pointer]
                : typeof(T) == typeof(SkaterData) ? SkaterData.All[pointer]
                : Il2CppSystem.Collections.Generic.List<Talent>.Lists[pointer];
            return (T)result;
        }
    }
}
namespace UnityEngine
{
    public enum HideFlags { DontUnloadUnusedAsset = 1 }
    public class ScriptableObject
    {
        public HideFlags hideFlags;
        public static T CreateInstance<T>() where T : new() => new T();
        public T Cast<T>() where T : class => (T)(object)this;
        public T TryCast<T>() where T : class => this as T;
    }
}
namespace Rogue
{
    public class Talent : UnityEngine.ScriptableObject
    {
        private static int serial;
        public IntPtr Pointer = ++serial;
        public string id, name, description, powerupName;
        public int level;
        public bool PayoffSuppressed { get; set; }
    }
    public class GoldenHelmetTalent : Talent {}
    public class Relic : UnityEngine.ScriptableObject { public string id; }
    public class RandomTalentBenchBonus : Relic {}
    public class StatRewardRelic : Relic {}
    public class GoldenHelmetSecondScorerRelic : Relic {}
    public class SorestLoser : Relic {}
    public class Needle : Relic {}
    public class QuantumSextant : Relic {}
    public class LastStand : Relic {}
    public class TieDomi : Relic {}
    public class RiggedFaceoff : Relic {}
}
namespace Rogue.Relics { public class TrainingWhistle : Relic {} }
namespace Rogue.Relics.ChaosRelics { public class YipsRelic : Relic {} }
namespace Rogue.Powerups.Chaos
{
    public class StatLockTalent : Talent { public int m_LockedValue; }
    public class Railgun : StatLockTalent {}
    public class TopGear : StatLockTalent {}
    public class Bedrock : StatLockTalent {}
}
namespace Data
{
    public class SkaterData
    {
        public static readonly Dictionary<IntPtr, SkaterData> All = new();
        public SkaterData(IntPtr pointer) { if (!All.TryGetValue(pointer, out var other)) All.Add(pointer, this); else powerups = other.powerups; }
        public Il2CppSystem.Collections.Generic.List<Talent> powerups;
    }
    public class TeamData
    {
        public static readonly Dictionary<IntPtr, TeamData> All = new();
        public TeamData(IntPtr pointer) { if (!All.TryGetValue(pointer, out var other)) All.Add(pointer, this); else relics = other.relics; }
        public Il2CppSystem.Collections.Generic.List<Relic> relics;
    }
}
namespace Tape2Tape
{
    public class Team
    {
        public static readonly Dictionary<IntPtr, Team> All = new();
        public Team(IntPtr pointer) { if (!All.TryGetValue(pointer, out var other)) All.Add(pointer, this); else { _teamData = other._teamData; otherTeam = other.otherTeam; } }
        public TeamData _teamData;
        public Team otherTeam;
    }
}
namespace BepInEx.Unity.IL2CPP.Hook
{
    public interface INativeDetour : IDisposable
    { public static INativeDetour CreateAndApply<T>(IntPtr pointer, T callback, out T original) => throw new NotSupportedException(); }
}
namespace EndlessMode
{
    public static class Plugin
    {
        public static bool IsDefaultMode;
        public static LogSink Log = new();
        public sealed class LogSink
        {
            public void LogError(string message) => throw new Exception(message);
            public void LogInfo(string message) {}
        }
    }
    internal static partial class CustomRewardRuntime
    {
        internal static readonly Dictionary<string, CustomRewardDefinition> Definitions = new(StringComparer.OrdinalIgnoreCase);
        private static bool loaded = true;
        private static long definitionGeneration;
        internal static long DefinitionQueryGeneration { get => definitionGeneration; set => definitionGeneration = value; }
        internal static int AssetQueries;
        internal static bool InMatch = true;
        internal static void EnsureAssets() => AssetQueries++;
        internal static bool HasDefinedEffect(string effect) => DefinitionsForEffect(effect).Length != 0;
        internal static bool IsCustom(string id) => id != null && Definitions.ContainsKey(id);
        internal static bool HasMatch<T>(this Il2CppSystem.Collections.Generic.List<T> list, Func<T,bool> predicate) => list.Any(predicate);
    }
    internal static class CustomRewardCapabilities
    {
        [ThreadStatic] internal static int NativeQueryDepth;
        internal static byte FromData(byte native, IntPtr data, IntPtr method) => native;
    }
    internal static class CustomRewardLifecycle
    {
        internal static bool Rule(string id, string effect, out CustomRewardDefinition d)
            => CustomRewardRuntime.Definitions.TryGetValue(id ?? "", out d) && !d.NeedsRedesign && d.Effect == effect;
    }
    internal static class CustomRewardTrainingGrowth
    {
        public sealed class Scope { public bool Used; }
        public static Scope Current;
        public static bool Preview;
        internal static void Query(TeamData team) { if (Current != null && !Current.Used && !Preview) Current.Used = true; }
    }
    internal sealed class NativeCompatibility
    {
        public string GameHash => "";
        public static NativeCompatibility Load(string path) => throw new NotSupportedException();
    }
}
