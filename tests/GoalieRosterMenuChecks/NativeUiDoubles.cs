// Native-shaped menu lifecycle boundary; does not render Unity or execute IL2CPP.
using System.Collections;
using Data;
using EndlessMode;
using BishopGames.UI;
using Tape2Tape.Hockey.UI;
using Tape2Tape.Customization.UI;
namespace TMPro {
    public enum TextOverflowModes { Overflow, Ellipsis }
    public class TMP_Text : Il2CppSystem.Object {
        public string text; public bool enableAutoSizing, enableWordWrapping = true;
        public float fontSize = 30, fontSizeMin = 12, fontSizeMax = 30;
        public int maxVisibleLines = 100; public TextOverflowModes overflowMode;
    }
}
namespace UnityEngine {
    public class MonoBehaviour : Il2CppSystem.Object { public void StartCoroutine(IEnumerator routine) { while (routine.MoveNext()) {} } }
    public class CanvasGroup { public float alpha = 1; public bool interactable = true, blocksRaycasts = true; }
    public class Transform {
        public Dictionary<string, Transform> Children = new(); public object[] Components = Array.Empty<object>();
        public Transform Find(string path) { Transform current = this; foreach (var name in path.Split('/')) { if (!current.Children.TryGetValue(name, out var child)) return null; current = child; } return current; }
        public T GetComponent<T>() where T : class => Components.OfType<T>().SingleOrDefault();
    }
    public class GameObject { public bool Active = true; public bool activeSelf => Active; public object[] Children = Array.Empty<object>(); public Transform transform = new(); public void SetActive(bool value) => Active = value; public T[] GetComponentsInChildren<T>(bool inactive) => Children.OfType<T>().ToArray(); }
    public static class Object {
        public static LineupMenu CurrentMenu;
        public static int Destroyed;
        public static TeamData Instantiate(TeamData source) => new() { goalie = source.goalie };
        public static void Destroy(object value) => Destroyed++;
        public static T FindObjectOfType<T>() where T : class => CurrentMenu as T;
    }
}
namespace BepInEx.Unity.IL2CPP.Utils.Collections { public static class Extensions { public static IEnumerator WrapToIl2Cpp(this IEnumerator value) => value; } }
namespace Cysharp.Threading.Tasks { public class UniTask<T> {
    public T Value; public Awaiter GetAwaiter() => new() { Value = Value };
    public struct Awaiter { public T Value; public bool IsCompleted => true; public T GetResult() => Value; }
} }
namespace BishopGames.Catalogs { public class MenuDefinitionCatalogReference { public static MenuDefinitionCatalogReference Lineup = new(); } }
namespace BishopGames.Module { public static class BishopModule { public static T Get<T>() where T : class => T2TUIModule.Instance as T; } }
namespace BishopGames.UI {
    public class SingleEvent<T> {
        public List<Il2CppSystem.Action<T>> Actions = new();
        public void Register(Il2CppSystem.Action<T> value, string name) => Actions.Add(value);
        public void Unregister(Il2CppSystem.Action<T> value) => Actions.Remove(value);
        public void Invoke(T value) { foreach (var a in Actions.ToArray()) a.Invoke(value); }
    }
    public class AbstractNavigable : Il2CppSystem.Object {
        public event System.Action Clicked;
        public void Click() => Clicked?.Invoke();
    }
    public class Toggle {
        public bool IsSelected, CanToggle = true;
        public event System.Action SelectionChanged;
        public void SetSelected(bool on, bool invokeSelectionEvent = true) {
            if (!CanToggle) return;
            IsSelected = on; if (invokeSelectionEvent) SelectionChanged?.Invoke();
        }
        public void OnNavigableClicked() { if (CanToggle) SetSelected(!IsSelected); }
    }
    public class AbstractMenu : UnityEngine.MonoBehaviour { public bool IsHiding; public SingleEvent<AbstractMenu> OnDestroy = new(), OnHidingCompleted = new(); }
    public class UIModule : Il2CppSystem.Object { }
}
namespace Tape2Tape.Customization.UI {
    public enum ESkaterPosition { Goalie, C }
    public enum EStatBonusesPreviewMode { None }
    public class SkaterDataForUI : Il2CppSystem.Object {
        public SkaterData Skater; public TeamData ActiveTeam; public ESkaterPosition ActivePosition;
        public SkaterDataForUI(TeamData team, SkaterData skater, ESkaterPosition position, object injuries, object power, EStatBonusesPreviewMode mode, object bonuses)
        { ActiveTeam = team; Skater = skater; ActivePosition = position; }
    }
    public class SkaterCard : Il2CppSystem.Object {
        public SkaterDataForUI CurrentSkaterData; public Toggle Toggle = new(); public bool Locked;
        public AbstractNavigable Navigable = new();
        public Text m_PositionLetter = new(), m_SkaterName = new(), m_FreeAgentDuration = new();
        public UnityEngine.GameObject m_FreeAgentDurationContainer = new();
        public Text EmptySeatLabel = new() { text = "Empty Seat" };
        public Text EmptySwapLabel = new() { text = "Swap" }, EmptyStatNum = new() { text = "20" }, EmptyStatLabel = new() { text = "OVR" };
        public UnityEngine.GameObject m_EmptySeatContainer = new(), m_FilledSeatContainer = new();
        public SkaterCard() {
            Navigable.Clicked += Toggle.OnNavigableClicked;
            // Installed lineup prefab includes inactive comparison labels as well as Name.
            m_EmptySeatContainer.Children = new object[] { EmptySeatLabel, EmptySwapLabel, EmptyStatNum, EmptyStatLabel };
            var info = new UnityEngine.Transform(); m_EmptySeatContainer.transform.Children["PlayerInfo"] = info;
            info.Children["Name"] = new() { Components = new object[] { EmptySeatLabel } };
            info.Children["Swap"] = new() { Components = new object[] { EmptySwapLabel } };
            info.Children["StatBoxSwap"] = new() { Children = new() {
                ["StatNum"] = new() { Components = new object[] { EmptyStatNum } },
                ["Label"] = new() { Components = new object[] { EmptyStatLabel } } } };
        }
        public void SetLocked(bool value, bool force) { Locked = value; Toggle.CanToggle = !value; }
    }
    public class SkaterCardList {
        public List<SkaterCard> m_SkaterCards = new(); public List<AbstractNavigable> Navigables = new();
        public UnityEngine.GameObject gameObject = new();
        public event System.Action<SkaterCard> OnSkaterCardToggled;
        private readonly Dictionary<SkaterCard, System.Action> subscriptions = new();
        public void Clear() {
            foreach (var pair in subscriptions) pair.Key.Toggle.SelectionChanged -= pair.Value;
            subscriptions.Clear(); m_SkaterCards.Clear(); Navigables.Clear();
        }
        public void Refresh(Il2CppSystem.Collections.Generic.IReadOnlyList<SkaterDataForUI> cards) {
            Clear(); foreach (var item in cards) {
                var card = new SkaterCard { CurrentSkaterData = item };
                card.m_PositionLetter.text = item.ActivePosition == ESkaterPosition.Goalie ? "G" : "C";
                card.m_SkaterName.text = (item.Skater as GoaltenderData)?.FullName ?? "Empty";
                card.m_EmptySeatContainer.SetActive(item.Skater == null); card.m_FilledSeatContainer.SetActive(item.Skater != null);
                card.m_FreeAgentDurationContainer.SetActive(false); card.m_FreeAgentDuration.text = "";
                System.Action handler = () => OnSkaterCardToggled?.Invoke(card);
                subscriptions.Add(card, handler); card.Toggle.SelectionChanged += handler;
                m_SkaterCards.Add(card); Navigables.Add(card.Navigable);
            }
        }
    }
    public class TeamRosterData {
        public TeamData OriginalTeamData; public TeamData TemporaryTeamData = new(); public GoaltenderData m_G;
        public List<SkaterData> PendingForwards = new(), BenchPlayers = new();
        public bool HasChanged; public void UpdateTemporaryTeamData() { TemporaryTeamData.goalie = m_G; }
    }
    public class T2TUIModule : UIModule {
        public static T2TUIModule Instance = new(); public static bool FailOpening;
        public Cysharp.Threading.Tasks.UniTask<T> ShowMenuAsync<T>(BishopGames.Catalogs.MenuDefinitionCatalogReference reference, LineupMenu.Metadata data) where T : class {
            if (FailOpening) throw new InvalidOperationException("native menu failed to open");
            var menu = new LineupMenu { Team = new() { OriginalTeamData = data.Team }, m_AreSkatersLocked = data.LockPlayers };
            typeof(GoalieRosterMenu).GetMethod("Initialize", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).Invoke(null, new object[] { menu });
            menu.LoadCurrentTeam(); return new() { Value = menu as T };
        }
    }
}
namespace Tape2Tape.Hockey.UI {
    public class Text : TMPro.TMP_Text { } public class Header { public Text m_Title = new(); }
    public class LineupMenu : AbstractMenu {
        public class Metadata { public TeamData Team; public bool LockPlayers, IsHomeTeam; }
        public TeamRosterData Team; public bool m_HideBench, m_AreSkatersLocked;
        public SkaterCardList m_ActivePlayersList = new(), m_BenchPlayersList = new();
        public Il2CppSystem.Collections.Generic.List<SkaterDataForUI> m_ActiveSkaters = new(), m_BenchSkaters = new();
        public Header m_Header = new(); public int FirstSelections, Refreshes, ToggleCallbacks, NativeMutations;
        public bool FailNextFocus;
        public AbstractNavigable Focused;
        public LineupMenu() { m_ActivePlayersList.OnSkaterCardToggled += TogglePlayerCard; m_BenchPlayersList.OnSkaterCardToggled += TogglePlayerCard; }
        private void TogglePlayerCard(SkaterCard card) { ToggleCallbacks++; if (GoalieRosterMenu.Pick(this, card)) NativeMutations++; }
        public void Focus(AbstractNavigable navigable) { Focused = navigable; } // Native selection-change refreshes comparison only.
        public void SelectFirstPlayer() {
            FirstSelections++;
            if (FailNextFocus) { FailNextFocus = false; throw new InvalidOperationException("native focus preparation failed"); }
            Focus(m_ActivePlayersList.Navigables.FirstOrDefault());
        }
        public void TryLeaveMenu(bool fromBack) { if (!GoalieRosterMenu.Leave(this)) return; IsHiding = true; OnHidingCompleted.Invoke(this); }
        public void LoadCurrentTeam() {
            Refreshes++; m_Header.m_Title.text = "Lineup";
            m_ActiveSkaters.Clear(); m_BenchSkaters.Clear();
            m_ActiveSkaters.Add(new(Team.OriginalTeamData, Team.m_G ?? Team.OriginalTeamData.goalie, ESkaterPosition.Goalie, null, null, EStatBonusesPreviewMode.None, null));
            foreach (var forward in Team.PendingForwards) m_ActiveSkaters.Add(new(Team.OriginalTeamData, forward, ESkaterPosition.C, null, null, EStatBonusesPreviewMode.None, null));
            m_ActivePlayersList.Refresh(m_ActiveSkaters);
            // Native 0x43894ad policy branch skips bench population; 0x4389e0f
            // deactivates the object. The visible branch refreshes, not SetActive(true).
            if (m_HideBench) { m_BenchPlayersList.Clear(); m_BenchPlayersList.gameObject.SetActive(false); }
            else {
                foreach (var forward in Team.BenchPlayers) m_BenchSkaters.Add(new(Team.OriginalTeamData, forward, ESkaterPosition.C, null, null, EStatBonusesPreviewMode.None, null));
                m_BenchPlayersList.Refresh(m_BenchSkaters);
            }
            foreach (var card in m_ActivePlayersList.m_SkaterCards.Concat(m_BenchPlayersList.m_SkaterCards))
                card.SetLocked(m_AreSkatersLocked || card.CurrentSkaterData.ActivePosition == ESkaterPosition.Goalie, true);
            GoalieRosterMenu.Refresh(this);
        }
    }
    public class EventChoiceItem { public AbstractNavigable Navigable = new(); public STS.Map.EventChoice ChoiceData; }
    public class EventChoiceMenu : AbstractMenu {
        public STS.Map.MapNode mapNode; public STS.Map.EventNode SourceNode; public List<EventChoiceItem> m_Items = new();
        public bool m_IsTakenOver, m_IsExiting, Visible = true, Busy; public AbstractMenu m_MenuOverSelf;
        public int m_ChoicesMade, Refreshes, Exits, DefaultSelections;
        public bool IgnoreInput => Busy || m_IsTakenOver || m_MenuOverSelf != null || m_IsExiting;
        public void SetVisibility(bool value, bool instant) => Visible = value;
        public void GoToNextSetOfChoices() => Refreshes++;
        public void SelectDefaultItem() => DefaultSelections++;
        public void Exit() => Exits++;
    }
}
public class EventChoicePanel : UnityEngine.MonoBehaviour {
    public UnityEngine.CanvasGroup _canvasGroup = new();
    public void ClearChoicesButtons() {} public void InitializeChoicesButtons(object choices) {} public void HideMenu() {}
}
namespace STS.Map {
    public enum NodeType { GeneralManager }
    public class NodeBlueprint : Il2CppSystem.Object { public NodeType nodeType; }
    public class EventNode : Il2CppSystem.Object { public List<EventChoice> eventChoices = new(); }
    public enum EEventChoiceType { Skater }
    public class EventTermParam {}
    public class EventChoice { public bool interactable = true; public SkaterData Skater; public string translatedButtonName; public NodeType nodeType; public EEventChoiceType ChoiceType; public Il2CppSystem.Collections.Generic.List<EventTermParam> choicesTermParams; public Il2CppSystem.Action onSelection; }
    public class MapNode : Il2CppSystem.Object { public Node Node = new(); public Il2CppSystem.Object Blueprint = new(); }
    public class Node { public int NodeIndex, LayerIndex; }
}
namespace Rogue.NPC.GeneralManagerNPC {
    public class GeneralManagerEvent : STS.Map.EventNode { public int NextCalls, LastCount; public int NumTimesToOfferChoices = 2; public bool NextChoices(int count) { NextCalls++; LastCount = count; return count < 2; } }
}
namespace State { public partial class CampaignState { public STS.Map.NodeBlueprint currentNode; public Squad GetCurrentSquad() => new() { startingTeam = runData.teamData }; } public class Squad { public TeamData startingTeam; } }

namespace Il2CppSystem {
    public class Action<T> { System.Action<T> callback; public void Invoke(T value) => callback(value); public static implicit operator Action<T>(System.Action<T> value) => new() { callback = value }; }
}
namespace Il2CppSystem.Collections.Generic {
    public interface IReadOnlyList<T> : System.Collections.Generic.IReadOnlyList<T> {}
    public class List<T> : System.Collections.Generic.List<T>, IReadOnlyList<T>, Il2CppSystem.Collections.IList {
        public TCast Cast<TCast>() => (TCast)(object)this;
        int Il2CppSystem.Collections.IList.Add(Il2CppSystem.Object item) { Add((T)(object)item); return Count - 1; }
    }
}
namespace Il2CppSystem.Collections { public interface IList { int Add(Il2CppSystem.Object item); } }

namespace Il2CppSystem { public class Action { System.Action callback; public void Invoke() => callback(); public static implicit operator Action(System.Action value) => new() { callback = value }; } }
namespace EndlessMode {
    static partial class Plugin { internal static void LoadPlayerFile(string file, PlayerConfig config) { config.Fields = new CampaignCore.CampaignDocument(System.IO.File.ReadAllText(file)); } }

    static partial class SquadPoolRuntime { public static object Team(State.Squad squad) => null; }
    static class CustomMapLayout { public static string RecruitRoleFor(STS.Map.MapNode node) => CampaignCore.GoalieFreeAgents.Goalies; }
    static class TwoLineContracts { public static void ReplaceChoices(STS.Map.EventNode node, System.Collections.Generic.List<STS.Map.EventChoice> choices) { node.eventChoices = choices; } }
}
