using CampaignCore;
using Data;
using Rogue;
using Skaters;
using Il2CppInterop.Runtime.InteropTypes;

namespace HarmonyLib {
    public class HarmonyPatch : Attribute { public HarmonyPatch() {} public HarmonyPatch(Type type, string method) {} }
    public class HarmonyPrefix : Attribute {}
    public class HarmonyPostfix : Attribute {}
    public class HarmonyFinalizer : Attribute {}
    public static class AccessTools {
        public static System.Reflection.MethodInfo Method(Type type, string method) => type.GetMethod(method);
    }
}
namespace Il2CppInterop.Runtime.InteropTypes {
    public class Il2CppObjectBase {
        private static int next;
        public IntPtr Pointer { get; } = new(++next);
        public T TryCast<T>() where T : class => this as T;
        public T Cast<T>() where T : class => (T)(object)this;
    }
}
namespace Il2CppSystem {
    public delegate TOut Func<TIn, TOut>(TIn input);
}
namespace Il2CppSystem.Collections.Generic {
    public class List<T> : System.Collections.Generic.List<T> {
        private static int next;
        private static readonly System.Reflection.FieldInfo version = typeof(System.Collections.Generic.List<T>)
            .GetField("_version", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        public IntPtr Pointer { get; } = new(++next);
        public int _version => (int)version.GetValue(this);
        public bool HasMatch(Predicate<T> predicate) => Exists(predicate);
    }
}
namespace UnityEngine {
    public class Object : Il2CppObjectBase { public static void Destroy(Object target) {} }
    public class ScriptableObject : Object {
        public string name;
        public static T CreateInstance<T>() where T : new() => new();
    }
    public class Transform { public Vector position; }
    public struct Vector {
        public float x;
        public float sqrMagnitude => x*x;
        public static Vector operator -(Vector a, Vector b) => new() { x = a.x - b.x };
    }
    public static class Random { public static int Range(int min, int max) => min; }
    public static class Time { public static float time; }
}
namespace Data {
    public class TeamData : Il2CppObjectBase {
        public string id;
        public Il2CppSystem.Collections.Generic.List<Relic> relics = new();
    }
    public class SkaterData : Il2CppObjectBase {
        public string id;
        public string FullName => id;
        public Il2CppSystem.Collections.Generic.List<Talent> powerups = new();
    }
    public class GoaltenderData : SkaterData {}
}
namespace Rogue {
    public class Talent : UnityEngine.ScriptableObject { public string id; public bool PayoffSuppressed; }
    public class Relic { public string id; }
    public class Gambler : Talent { public int minShotPower, minAccuracy; }
}
namespace Rogue.Powerups.Controllers.Talents {
    public class GamblerController : UnityEngine.Object { public Skater skater; public Talent Talent; }
}
namespace Skaters {
    public class Team : Il2CppObjectBase { public TeamData _teamData; public Team otherTeam; public bool isHomeTeam; }
    public class Skater : Il2CppObjectBase {
        public SkaterData _skaterData = new();
        public UnityEngine.Transform transform = new();
        public Team Side;
        public bool PowersEnabled = true, DefensiveZone;
        public Team Team { get { EndlessMode.Work.TeamReads++; return Side; } }
        public bool ArePowerUpsEnabled() { EndlessMode.Work.PowerChecks++; return PowersEnabled; }
    }
    public class Forward : Skater {
        public SkaterData _forwardData => _skaterData;
        public ForwardStats stats = new();
        public bool Benched;
        public bool IsBenched() => Benched;
        public bool IsReferee() { EndlessMode.Work.RefereeReads++; return false; }
    }
    public class ForwardStats {
        private bool balanced;
        private Forward forward;
        public bool _isBalanced {
            get { EndlessMode.Work.StatReads++; return balanced; }
            set { EndlessMode.Work.StatWrites++; balanced = value; }
        }
        public Forward _forward { get { EndlessMode.Work.StatReads++; return forward; } set => forward = value; }
        public void UpdateCachedStats() {}
    }
    public class Puck { public static Puck Instance = new(); public Skater skaterPossession; }
}
namespace Goaltender { public class Goaltender : Skater {} }
namespace ScriptableStates { public enum GameType { Quick_Game } }
public static class GameManager { public static ScriptableStates.GameType GameType; }
namespace Tape2Tape.Hockey.UI {
    public class RelicRewardMenu : Il2CppObjectBase { public TeamData m_Team; }
    public class SkaterPowerupRewardMenu : Il2CppObjectBase { public TeamData m_Team; }
}
namespace EndlessMode {
    internal static class Work {
        internal static int TeamReads, PowerChecks, Conditions, Observations, StatReads, StatWrites, RefereeReads;
        internal static void Reset() => TeamReads = PowerChecks = Conditions = Observations = StatReads = StatWrites = RefereeReads = 0;
    }
    internal static partial class CustomRewardRuntime {
        internal static bool InMatch = true;
        private static class StatRefresh { internal static void CheckAll() {} }
        private static bool inMatch => InMatch;
        private static bool completed => false;
        private static readonly Dictionary<IntPtr, Forward> forwards = new();
        private static readonly HashSet<string> dispatching = new();
        internal static readonly CustomRewardProgress progress = new();
        internal static readonly List<string> activations = new();
        private static readonly Dictionary<IntPtr, Skater> actors = new();
        private static readonly Dictionary<string, CustomRewardDefinition> Definitions = new(StringComparer.OrdinalIgnoreCase);
        internal static void Setup(IEnumerable<Skater> players, IEnumerable<CustomRewardDefinition> rules) {
            actors.Clear();
            boundLoadouts.Clear();
            forwards.Clear();
            progress.Clear();
            activations.Clear();
            Definitions.Clear();
            foreach (var rule in rules) Definitions.Add(rule.Id, rule);
            foreach (var player in players) {
                BindActor(player);
                if (player is Forward f) forwards.Add(f.Pointer, f);
            }
            inPlayStale = true;
            presented = true;
            _ = InPlay;
            Work.Reset();
        }
        // The game supplies these live signals. Keep the actual owner/target traversal in the linked runtime.
        internal static bool Condition(string trigger, Skater actor) {
            Work.Conditions++;
            return trigger switch {
                "While owned" => true,
                "While holding puck" => Puck.Instance.skaterPossession == actor,
                "While in defensive zone" => actor.DefensiveZone,
                _ => throw new ArgumentException(trigger)
            };
        }
        internal static void EnsureRun() {}
        private static void IndexActorData() {}
        internal static void Bind(Forward actor) => BindActor(actor);
        internal static void BindGoalie(Goaltender.Goaltender actor) => BindActor(actor);
        private static IReadOnlyList<string> StatRecipients(CustomRewardDefinition rule, Skater owner, Skater other)
            => [CustomRewardProgress.ScopedRecipient(Player(owner), Team(owner))];
        private static bool ApplyEffect(CustomRewardDefinition rule, Skater owner, Skater other) => true;
        internal static void Notify(CustomRewardDefinition rule, Skater owner, Skater other) => activations.Add(rule.Id);
        internal static void Save() {}
        internal static void ObserveOwnership(TeamData team) => Work.Observations++;
        internal static void Safe(Action action) => action();
    }
    internal static class Plugin { internal static class Log { internal static void LogInfo(string message) {} } }
    internal static class CustomRewardCapabilities { internal static bool Active(Skater player, string effect) => false; }
}
