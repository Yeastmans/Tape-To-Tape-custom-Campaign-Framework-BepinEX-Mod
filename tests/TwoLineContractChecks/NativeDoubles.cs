// Source-linked checks, not native Unity execution or gameplay acceptance.
using EndlessMode;
using STS.Map;
namespace Harness
{
    public class NativeObject
    {
        static long next = 1000;
        public IntPtr Pointer = (IntPtr)Interlocked.Add(ref next, 256);
        public T TryCast<T>() where T : class => this as T;
        public T Cast<T>() where T : class => (T)(object)this;
    }
}
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)] public class HarmonyPatch : Attribute { public HarmonyPatch(Type t, string method) { } }
    public class HarmonyPrefix : Attribute { }
    public class HarmonyPostfix : Attribute { }
    public class HarmonyFinalizer : Attribute { }
}
namespace Il2CppSystem
{
    public sealed class Action
    {
        System.Action callback;
        public void Invoke() => callback();
        public static implicit operator Action(System.Action action) => new() { callback = action };
    }
    public sealed class Action<T>
    {
        System.Action<T> callback;
        public void Invoke(T value) => callback(value);
        public static implicit operator Action<T>(System.Action<T> action) => new() { callback = action };
    }
}
namespace Il2CppSystem.Collections.Generic
{
    public interface IReadOnlyList<T> : System.Collections.Generic.IReadOnlyList<T> { }
    public class List<T> : System.Collections.Generic.List<T>, IReadOnlyList<T>
    {
        public IntPtr Pointer => Il2CppInterop.Runtime.Memory.Register(this);
        public TCast Cast<TCast>() => (TCast)(object)this;
    }
}
namespace Il2CppInterop.Runtime
{
    public static class Memory
    {
        public static Dictionary<IntPtr, object> Values = new();
        static Dictionary<object, IntPtr> pointers = new(ReferenceEqualityComparer.Instance);
        public static IntPtr Register(object value)
        {
            if (!pointers.TryGetValue(value, out var p)) { p = (IntPtr)(100000 + pointers.Count * 256); pointers[value] = p; Values[p] = value; }
            return p;
        }
    }
    public static class Il2CppClassPointerStore<T> { public static IntPtr NativeClassPtr = (IntPtr)77; }
    public static unsafe class IL2CPP
    {
        public static int UnboxedAdds;
        public static IntPtr il2cpp_class_get_method_from_name(IntPtr type, string name, int count) => (IntPtr)88;
        public static IntPtr il2cpp_object_unbox(IntPtr box) => box + 16;
        public static void il2cpp_runtime_invoke(IntPtr method, IntPtr list, void** args, ref IntPtr error)
        {
            // Reject boxed-header arguments: production must unbox EventChoice.
            var card = (EventChoice)Memory.Values[(IntPtr)args[0] - 16];
            ((Il2CppSystem.Collections.Generic.List<EventChoice>)Memory.Values[list]).Add(card.Copy()); UnboxedAdds++;
        }
    }
    public static class Il2CppException { public static void RaiseExceptionIfNecessary(IntPtr error) { if (error != IntPtr.Zero) throw new Exception(); } }
}
namespace Data
{
    public class SkaterData : Harness.NativeObject { public string id, FullName; }
    public class ForwardData : SkaterData { public int Jersey, Stat = 40; public string Talent = "Original talent"; }
    public class Lineup { public string leftWinger, rightWinger, center, leftDefensemen, rightDefensemen; }
    public class TeamData : Harness.NativeObject
    {
        public Il2CppSystem.Collections.Generic.List<ForwardData> forwards = new();
        public Il2CppSystem.Collections.Generic.List<Lineup> lines = new();
        public int activeLineNumber = 1;
        public string id = "CCF_Test";
        public int benchSize;
        public bool isMultiLineup => lines.Count == 2;
        public void SetActiveLine(int number) => activeLineNumber = number;
        public int GetActiveLine() => activeLineNumber;
        public Il2CppSystem.Collections.Generic.List<ForwardData> benchPlayers
        {
            get
            {
                var result = new Il2CppSystem.Collections.Generic.List<ForwardData>();
                int offset = activeLineNumber == 1 ? 5 : 0;
                foreach (int i in new[] { 2, 0, 1, 3, 4 }) result.Add(forwards[offset + i]);
                return result;
            }
        }
        public void EnsureUniqueJerseyNumber(Il2CppSystem.Collections.Generic.IReadOnlyList<ForwardData> roster, SkaterData skater)
        {
            var p = (ForwardData)skater;
            while (roster.Any(x => x != null && x.Jersey == p.Jersey)) p.Jersey++;
        }
        public void AddForwardToActiveLine(ForwardData player)
        {
            if (!TwoLineContractPlacement.Prefix(this, player)) return;
            for (int i = 0; i < 5; i++) if (forwards[i] == null) { forwards[i] = player; break; }
        }
    }
}
namespace Data
{
    public class RunData : Harness.NativeObject
    {
        public Data.TeamData teamData;
        public string squadId = "Custom_Test";
        public Il2CppSystem.Collections.Generic.List<Rogue.FreeAgents.FreeAgent> freeAgents = new();
        public Dictionary<string, string> forwardInjuries = new();
    }
}
namespace State
{
    public class DeserializerV2 { }
    public class CampaignState { public Data.RunData runData; }
}
namespace Tape2Tape { public static class CampaignUtil { public static State.CampaignState CampaignState = new(); } }
namespace Rogue { }
namespace Rogue.FreeAgents
{
    public class FreeAgentManager { }
    public class FreeAgent { public string id; public int Matches = 4; }
}
namespace STS.Map
{
    public enum NodeType { GeneralManager = 8 }
    public enum EEventChoiceType { Skater = 1 }
    public class EventTermParam { }
    public class EventChoice
    {
        public string translatedButtonName;
        public Il2CppSystem.Action onSelection;
        public bool interactable, SuppressRewardTracking;
        public NodeType nodeType;
        public Il2CppSystem.Collections.Generic.List<EventTermParam> choicesTermParams;
        public EEventChoiceType ChoiceType;
        public Data.SkaterData Skater;
        public string[] CustomLocalizedTexts;
        public IntPtr Pointer => Il2CppInterop.Runtime.Memory.Register(this);
        public EventChoice Copy() => (EventChoice)MemberwiseClone();
    }
    public class EventNode : Harness.NativeObject { public Il2CppSystem.Collections.Generic.List<EventChoice> eventChoices = new(); }
}
namespace Rogue.NPC.GeneralManagerNPC
{
    public class GeneralManagerEvent : EventNode
    {
        public int NumTimesToOfferChoices = 2, Generations, NativeCallbacks, LastCount;
        public bool ThrowDuringGeneration, ThrowAfterInsertion, SkipInsertion;
        public bool NextChoices(int count)
        {
            bool result = false;
            bool native = TwoLineContractChoices.Prefix(this, ref count, ref result, out var scope);
            try
            {
                if (native)
                {
                    LastCount = count;
                    if (ThrowDuringGeneration) throw new InvalidOperationException("native generation failure");
                    if (count < NumTimesToOfferChoices)
                    {
                        Generations++;
                        var team = Tape2Tape.CampaignUtil.CampaignState.runData.teamData;
                        int room = 5;
                        TwoLineContractOfferRoom.Prefix(team, ref room);
                        eventChoices = new();
                        for (int i = 0; i < 3; i++)
                        {
                            var p = new Data.ForwardData { id = $"recruit-{Generations}-{i}", FullName = $"Recruit {Generations}-{i}", Jersey = 2 };
                            var action = (System.Action)(() =>
                            {
                                NativeCallbacks++;
                                if (room >= 5) throw new Exception("Full roster native error");
                                if (!SkipInsertion) team.AddForwardToActiveLine(p);
                                Tape2Tape.CampaignUtil.CampaignState.runData.freeAgents.Add(new() { id = p.id, Matches = 6 });
                                if (ThrowAfterInsertion) throw new Exception("Native contract callback failed");
                            });
                            eventChoices.Add(new() { Skater = p, onSelection = action, interactable = room < 5, ChoiceType = EEventChoiceType.Skater });
                        }
                        result = true;
                    }
                }
                TwoLineContractChoices.Postfix(result, scope);
                return result;
            }
            finally { TwoLineContractChoices.Finalizer(scope); }
        }
    }
}
public class EventChoicePanel : UnityEngine.MonoBehaviour
{
    public int Refreshes;
    public int Hides;
    public UnityEngine.CanvasGroup _canvasGroup = new();
    public void ClearChoicesButtons() { }
    public void InitializeChoicesButtons(Il2CppSystem.Collections.Generic.List<EventChoice> cards) => Refreshes++;
    public void HideMenu() { if (TwoLineContractLegacyAdvance.Prefix(this)) Hides++; }
}
namespace EndlessMode
{
    public static class SquadBehaviorRuntime
    {
        public static bool Two = true, Contracts = true, Default;
        public static bool Enabled(string squad, string flag, bool fallback) => !Default && squad == "Custom_Test" && (flag == "two_lines" ? Two : Contracts);
    }
    public class Logger
    {
        public List<string> Errors = new();
        public void LogError(string message) => Errors.Add(message);
        public void LogInfo(string message) { }
        public void LogWarning(string message) { }
    }
    public static class Plugin { public static Logger Log = new(); public static bool IsDefaultMode => SquadBehaviorRuntime.Default; }
    public static class CustomTeamRules { public static bool IsCustom(Data.TeamData team) => team?.id.StartsWith("CCF_") == true; }
    public static class VanillaCampaignRuntime { public static bool Owns(Data.TeamData team) => false; }
}
namespace EndlessMode { public static class PlayNowRosterRuntime { public static bool TryGet(Data.TeamData team,out object roster) { roster=null; return false; } } }
namespace EndlessMode { public static class PlayNowRosterContext { public static void RefreshLocked(Tape2Tape.Customization.UI.TeamRosterData roster) {} } }
// This fixture exercises skater recruitment; goalie-node routing is separate.
namespace EndlessMode { public static class GoalieRecruitment { public static bool IsGoalieNode => false; } }
