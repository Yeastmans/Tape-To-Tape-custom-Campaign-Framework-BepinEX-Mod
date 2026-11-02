// Native/Unity boundary doubles. These do not model native gameplay acceptance.
using CampaignCore;
using EndlessMode;
using Data;
using State;
using Pool = Il2CppSystem.Collections.Generic.List<Data.ForwardData>;

namespace Harness
{
    public static class Frames
    {
        private static readonly List<System.Collections.IEnumerator> pending = new();
        public static readonly List<UnityEngine.Animator> Animators = new();
        public static int Count, NativeCallbackDepth;
        public static void Start(System.Collections.IEnumerator routine)
        {
            if (routine.MoveNext()) pending.Add(routine);
        }
        public static void Advance(int count)
        {
            for (int i = 0; i < count; i++)
            {
                Count++;
                foreach (var routine in pending.ToArray())
                    if (!routine.MoveNext()) { pending.Remove(routine); (routine as IDisposable)?.Dispose(); }
                foreach (var animator in Animators) animator.Advance();
            }
        }
    }
    public class NativeObject
    {
        static long next = 10;
        public IntPtr Pointer = (IntPtr)Interlocked.Increment(ref next);
        public T Cast<T>() => (T)(object)this;
    }
}
namespace HarmonyLib
{
    public class Harmony { public Harmony(string id) { } public void Patch(object method, HarmonyMethod prefix = null, HarmonyMethod finalizer = null) { } public void UnpatchSelf() { } }
    public class HarmonyMethod { public HarmonyMethod(Type type, string name) { } }
    public static class AccessTools { public static object Method(Type type, string name, Type[] args = null) => new object(); }
}
namespace Il2CppSystem.Collections.Generic
{
    public interface IReadOnlyCollection<T> { int Count { get; } }
    public interface IReadOnlyList<T> : IReadOnlyCollection<T> { T this[int i] { get; } IntPtr Pointer { get; } TCast Cast<TCast>(); }
    public class List<T> : System.Collections.Generic.List<T>, IReadOnlyList<T>
    {
        private readonly Harness.NativeObject owner = new();
        public IntPtr Pointer => owner.Pointer;
        public TCast Cast<TCast>() => (TCast)(object)this;
    }
}
namespace UnityEngine
{
    public class Coroutine { }
    public static class Time { public static float realtimeSinceStartup => Harness.Frames.Count / 60f; }
    public readonly struct AnimatorStateInfo
    {
        private readonly string name;
        public AnimatorStateInfo(string name) { this.name = name; }
        public bool IsName(string value) => name == value;
    }
    public class Animator : Object
    {
        public string State = "PowerUp";
        public bool BlockTransitions;
        private readonly HashSet<int> pending = new();
        public Animator() { Harness.Frames.Animators.Add(this); }
        public void SetTrigger(int value) => pending.Add(value);
        public AnimatorStateInfo GetCurrentAnimatorStateInfo(int layer) => new(State);
        public void Reset() { State = "PowerUp"; pending.Clear(); }
        public void Advance()
        {
            // Actual shipped controller has no AnyState or PowerUp->Relic edge.
            if (BlockTransitions) return;
            if (State == "PowerUp" && pending.Remove(Tape2Tape.Hockey.UI.OldSquadMenu.SUPER_STAR)) State = "SuperStar";
            else if (State == "SuperStar" && pending.Remove(Tape2Tape.Hockey.UI.OldSquadMenu.RELIC)) State = "Relic";
        }
    }
    public class Object : Harness.NativeObject
    {
        public string name = "native";
        public bool Destroyed;
        // Unity keeps a managed wrapper after its native object is destroyed.
        public static bool operator ==(Object left, Object right)
        {
            bool a = ReferenceEquals(left, null) || left.Destroyed;
            bool b = ReferenceEquals(right, null) || right.Destroyed;
            return a || b ? a && b : ReferenceEquals(left, right);
        }
        public static bool operator !=(Object left, Object right) => !(left == right);
        public override bool Equals(object other) => ReferenceEquals(this, other);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
        public static T Instantiate<T>(T source) where T : Object => (T)(Object)((ForwardData)(Object)source).Clone();
    }
    public static class Resources
    {
        public static ForwardData[] Players = { new ForwardData { id = "native", name = "native", firstName = "Native" } };
        public static T[] FindObjectsOfTypeAll<T>() => Players.Cast<T>().ToArray();
    }
    public static class Random { public static int Range(int min, int max) => max - 1; }
}
namespace Data
{
    public class ForwardData : UnityEngine.Object
    {
        public string id, firstName = "", lastName = "";
        public string FullName => (firstName + " " + lastName).Trim();
        public bool Excluded;
        public bool preventEliminatorRemoval;
        public int Stat = 40;
        public Il2CppSystem.Collections.Generic.List<Rogue.Talent> powerups = new();
        public string ability;
        public int TalentCacheResets;
        public void ResetTalentCache() => TalentCacheResets++;
        public ForwardData Clone() => new() { id = id, name = name + "(Clone)", firstName = firstName, lastName = lastName, Stat = Stat, Excluded = Excluded,
            powerups = powerups, ability = ability, preventEliminatorRemoval = preventEliminatorRemoval };
    }
    public class TeamData : Harness.NativeObject
    {
        public Pool forwards = new(); public int NumPlayingForwards; public bool Room = true;
        public List<string> relics = new();
        public bool HasRoomOnBench() => Room;
        public Pool GetForwards(bool includeNull) => forwards;
        public int Adds;
        public void AddForwardToActiveLine(ForwardData player) { if (BaseGamePoolsRuntime.AddForwardPrefix(this, player)) { forwards.Add(player); Adds++; } }
        public void AddForwardToBench(ForwardData player) => AddForwardToActiveLine(player);
    }
    public class RunData { public TeamData teamData; public int CurrentAct = 1; }
}
namespace State
{
    public class RunSquadScriptableObject { public int m_MaxBenchwarmersOnBench = 3; public Pool m_CustomSwapPool = new(); }
    public class CampaignState
    {
        public RunSquadScriptableObject Squad = new(); public Pool freeAgents = new();
        public RunData runData = new() { teamData = new() };
        public Unlocks.UnlockSystem _unlockSystem = new();
        public RunSquadScriptableObject GetCurrentSquad() => Squad;
    }
}
namespace Unlocks
{
    public class UnlockSystem
    {
        public HashSet<string> Locked = new();
        public bool IsUnlocked(string id) { bool result = false; return BaseGamePoolsRuntime.UnlockPrefix(id, ref result) ? !Locked.Contains(id) : result; }
    }
}
namespace Tape2Tape
{
    public static class CampaignUtil { public static CampaignState CampaignState; public static int GetMaxActiveForwards(bool check) => 5; }
    public static class CampaignHelper { public static bool IsForwardExcludedBySquad(ForwardData player, RunSquadScriptableObject squad) => player.Excluded; }
}
namespace Rogue.NPC.GeneralManagerNPC
{
    public class GeneralManagerEvent
    {
        public int NumTimesToOfferChoices = 3; public Unlocks.UnlockSystem _unlockSystem;
        public HashSet<string> _previouslyOfferedNames = new(); public Pool eventChoices = new(); public List<string> descTermParams = new();
        public static int Rerolls; public static void RerollSkaterNodeRewards() => Rerolls++;
        public int Creates;
        public void CreatePlayerChoice(ForwardData player, TeamData team) { Creates++; eventChoices.Add(player.Clone()); }
    }
}
namespace Rogue { public class Talent { public string name; } }
namespace Cysharp.Threading.Tasks
{
    public static class UniTaskExtensions { public static void Forget(UniTask task) { } }
    // Actual IL2CPP interop represents the native UniTask value as a reference
    // wrapper. default(UniTask) is null and cannot be returned to the caller.
    public class UniTask
    {
        public bool IsCompleted;
        public static UniTask CompletedTask => new() { IsCompleted = true };
    }
}
namespace Tape2Tape.Hockey.UI
{
    public class BenchwarmerMenu { public CampaignState m_CampaignState; }
    public class OldSquadMenu : Harness.NativeObject
    {
        public const int SUPER_STAR = 914891186, RELIC = unchecked((int)2839592655);
        public UnityEngine.Animator m_StepAnimator = new();
        public bool m_IsExiting, isActiveAndEnabled = true, Destroyed;
        public bool RelicVisible => m_StepAnimator.State == "Relic";
        public int RevealCancellations, RelicStartFrame = -1;
        public bool ReenteredNativeCaller;
        public CampaignState m_CampaignState; public TeamData m_Team; public Unlocks.UnlockSystem m_UnlockSystem;
        public Il2CppSystem.Collections.Generic.IReadOnlyList<ForwardData> m_SuperStarSkaters = new Pool();
        public int m_CurrentSkaterChoice = -1, m_CurrentRelicChoice = -1, m_CurrentPowerUpChoice = 0;
        public int m_Mode, RewardGrants, Exits, Creates, RelicStages, StarterStages;
        public ForwardData m_AngusData;
        public string SelectedAbility = "Tornado", SelectedTalent, SelectedRelic = "Brick Wall";
        public Pool NativeRandomSkaters = new();
        public Cysharp.Threading.Tasks.UniTask RelicTask;
        public bool FailGrant;
        public ForwardData CreatePlayerChoice(ForwardData player, TeamData team) { Creates++; var clone = player.Clone(); clone.id = "acquired_" + Creates; return clone; }
        public void InitData()
        {
            BaseGamePoolsRuntime.ResetEmptyStarMenu(this);
            m_CurrentRelicChoice = m_CurrentSkaterChoice = -1;
            m_StepAnimator.Reset();
        }
        public void BehaviourDestroy() { BaseGamePoolsRuntime.ResetEmptyStarMenu(this); Destroyed = true; isActiveAndEnabled = false; }
        public UnityEngine.Coroutine StartCoroutine(System.Collections.IEnumerator routine) { Harness.Frames.Start(routine); return new(); }
        public void CancelReveal() { RevealCancellations++; if (Harness.Frames.NativeCallbackDepth != 0) ReenteredNativeCaller = true; }
        public Cysharp.Threading.Tasks.UniTask InstantiateRelics()
        {
            // Native FullSequence: power-up -> star -> relic -> random starters.
            // Reaching this stage does not choose or apply the relic for the user.
            RelicStages++;
            RelicStartFrame = Harness.Frames.Count;
            if (Harness.Frames.NativeCallbackDepth != 0) ReenteredNativeCaller = true;
            m_StepAnimator.SetTrigger(RELIC);
            return RelicTask = new();
        }
        public void ChooseRelicAndConfirmStarters()
        {
            if (!RelicVisible) throw new InvalidOperationException("Native relic choices are not visible while the step controller remains on PowerUp.");
            m_CurrentRelicChoice = 0;
            StarterStages++;
            ApplyAndExit();
            if (RelicTask != null) RelicTask.IsCompleted = true;
        }
        public void ApplyAndExit()
        {
            BaseGamePoolsRuntime.ApplySelectionPrefix(this, out var scope);
            try
            {
                // Actual native guards precede ALL grants, including the chosen
                // ability. Relic choice is still unset when the star stage opens.
                if (m_Mode != 1 && m_CurrentRelicChoice >= 0 && m_CurrentPowerUpChoice >= 0
                    && m_CurrentSkaterChoice >= 0 && m_CurrentSkaterChoice < m_SuperStarSkaters.Count)
                {
                    RewardGrants++;
                    m_Team.relics.Add(SelectedRelic);
                    m_Team.AddForwardToBench(m_SuperStarSkaters[m_CurrentSkaterChoice]);
                    foreach (var player in NativeRandomSkaters) m_Team.AddForwardToActiveLine(player);
                    var angus = m_Team.forwards.Single(player => player.id == m_AngusData.id);
                    if (SelectedAbility != null) angus.ability = SelectedAbility;
                    if (SelectedTalent != null) angus.powerups.Add(new Rogue.Talent { name = SelectedTalent });
                    if (FailGrant) throw new InvalidOperationException("native reward failure");
                }
                Exits++;
            }
            finally { BaseGamePoolsRuntime.ApplySelectionFinalizer(this, scope, null); }
        }
    }
}
namespace BepInEx.Unity.IL2CPP.Utils.Collections
{
    public static class CollectionExtensions
    {
        public static System.Collections.IEnumerator WrapToIl2Cpp(this System.Collections.IEnumerator routine) => routine;
    }
}
namespace EndlessMode
{
    internal static class NativeCompatibility { internal static void Load(string path) { } }
    public class PlayerConfig { public CampaignDocument Fields = new(""); public string Name, Ability; public List<string> Talents = new(); }
    internal static class Plugin
    {
        internal static bool IsDefaultMode = true; internal static string ModContentRoot;
        internal static Logger Log = new();
        internal static void LoadPlayerFile(string path, PlayerConfig config)
        {
            config.Fields = new(File.ReadAllText(path)); config.Name = config.Fields.Get("Name"); config.Ability = config.Fields.Get("Ability");
            config.Talents = config.Fields.Get("Talents").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }
    }
    public class Logger
    {
        public List<string> Errors = new();
        public void LogInfo(string message) { }
        public void LogError(string message) => Errors.Add(message);
    }
    internal static class VanillaCampaignRuntime
    {
        internal static ForwardData CloneCandidate(PlayerConfig config)
        {
            string id = config.Fields.Get(VanillaCopyData.PlayerId);
            if (id.Length == 0) return null;
            return UnityEngine.Resources.Players.First(p => p.id == id && p.name == config.Fields.Get(VanillaCopyData.PlayerAsset)).Clone();
        }
    }
    internal static class CampaignSquadSetup
    {
        internal static void ApplyFreshPlayerConfig(ForwardData player, PlayerConfig config)
        {
            player.firstName = config.Name; player.lastName = "";
            if (config.Fields.Get(VanillaCopyData.PlayerId).Length > 0 || config.Talents.Count > 0)
            { player.powerups = new(); player.ResetTalentCache(); }
            foreach (string name in config.Talents) player.powerups.Add(new Rogue.Talent { name = name });
            if (!string.IsNullOrEmpty(config.Ability)) player.ability = config.Ability == "None" ? null : config.Ability;
            else if (config.Fields.Get(VanillaCopyData.PlayerId).Length > 0) player.ability = null;
        }
    }
}
