// Native-shaped menu lifecycle doubles. These checks do not render Unity UI.
using System.Collections;
using Data;
using EndlessMode;
using BishopGames.UI;
using Tape2Tape.Hockey.UI;
using Tape2Tape.Customization.UI;

namespace UnityEngine
{
    public class MonoBehaviour : Harness.NativeObject
    {
        public void StartCoroutine(IEnumerator routine) { while (routine.MoveNext()) { } }
    }
    public class CanvasGroup { public float alpha = 1; public bool interactable = true, blocksRaycasts = true; }
    public static class Object
    {
        public static int Destroyed;
        public static TeamData Instantiate(TeamData source)
        {
            var clone = new TeamData { activeLineNumber = source.activeLineNumber, id = source.id, benchSize = source.benchSize };
            clone.forwards.AddRange(source.forwards);
            foreach (var l in source.lines) clone.lines.Add(new() { leftWinger = l.leftWinger, rightWinger = l.rightWinger, center = l.center, leftDefensemen = l.leftDefensemen, rightDefensemen = l.rightDefensemen });
            return clone;
        }
        public static void Destroy(TeamData value) => Destroyed++;
    }
}
namespace BepInEx.Unity.IL2CPP.Utils.Collections
{
    public static class Extensions { public static IEnumerator WrapToIl2Cpp(this IEnumerator value) => value; }
}
namespace Cysharp.Threading.Tasks
{
    public class UniTask<T>
    {
        public T Value;
        public Awaiter GetAwaiter() => new() { Value = Value };
        public struct Awaiter { public T Value; public bool IsCompleted => true; public T GetResult() => Value; }
    }
}
namespace BishopGames.Catalogs
{
    public class MenuDefinitionCatalogReference { public static MenuDefinitionCatalogReference Lineup = new(); }
}
namespace BishopGames.Module
{
    public static class BishopModule { public static T Get<T>() where T : class => T2TUIModule.Instance as T; }
}
namespace BishopGames.UI
{
    public class SingleEvent<T>
    {
        public List<Il2CppSystem.Action<T>> Actions = new();
        public void Register(Il2CppSystem.Action<T> value, string name) => Actions.Add(value);
        public void Unregister(Il2CppSystem.Action<T> value) => Actions.Remove(value);
        public void Invoke(T value) { foreach (var a in Actions.ToArray()) a.Invoke(value); }
    }
    public class AbstractNavigable : Harness.NativeObject { }
    public class Toggle { public bool IsSelected; public void SetSelected(bool on, bool invokeSelectionEvent = true) => IsSelected = on; }
    public class AbstractMenu : UnityEngine.MonoBehaviour
    {
        public bool IsHiding;
        public SingleEvent<AbstractMenu> OnDestroy = new(), OnHidingCompleted = new();
    }
    public class UIModule : Harness.NativeObject { }
}
namespace Tape2Tape.Customization.UI
{
    public enum ESkaterPosition { RW, RD, C, LD, LW, Goalie, Invalid }
    public struct SkaterDataForUI
    {
        public SkaterData Skater;
        public ESkaterPosition ActivePosition;
        public int BenchIndex;
        public bool IsBenched => ActivePosition == ESkaterPosition.Invalid;
    }
    public class SkaterCard : Harness.NativeObject
    {
        public SkaterDataForUI CurrentSkaterData;
        public Toggle Toggle = new();
        public bool Locked = true;
        public void SetLocked(bool locked, bool force) => Locked = locked;
    }
    public class SkaterCardList
    {
        public List<SkaterCard> m_SkaterCards = new();
        public bool ContainsSkaterCard(SkaterCard card) => m_SkaterCards.Contains(card);
    }
    public class TeamRosterData
    {
        public TeamData OriginalTeamData;
        public Il2CppSystem.Collections.Generic.List<ForwardData> m_BenchForwards = new();
        public bool HasChanged;
    }
    public class T2TUIModule : UIModule
    {
        public static T2TUIModule Instance = new();
        public static bool FailOpening;
        public Cysharp.Threading.Tasks.UniTask<T> ShowMenuAsync<T>(BishopGames.Catalogs.MenuDefinitionCatalogReference reference, LineupMenu.Metadata data) where T : class
        {
            if (FailOpening) throw new InvalidOperationException("Native menu could not load");
            var menu = new LineupMenu { Team = new() { OriginalTeamData = data.Team }, m_AreSkatersLocked = data.LockPlayers };
            LineupInitialRoster.Postfix(menu);
            TwoLineContractRosterInit.Postfix(menu);
            menu.LoadCards();
            return new() { Value = menu as T };
        }
    }
}
namespace Tape2Tape.Hockey.UI
{
    public class Text { public string text; }
    public class Header { public Text m_Title = new(); }
    public class LineupMenu : AbstractMenu
    {
        public class Metadata { public TeamData Team; public bool LockPlayers, IsHomeTeam; }
        public TeamRosterData Team;
        public bool m_HideBench, m_AreSkatersLocked;
        public SkaterCardList m_ActivePlayersList = new(), m_BenchPlayersList = new();
        public Header m_Header = new();
        public int FirstSelections;
        public bool DeferClose;
        public void SelectFirstPlayer() => FirstSelections++;
        public void TryLeaveMenu(bool fromBack)
        {
            if (Team.HasChanged) throw new Exception("Recruit selector dirtied the native lineup model");
            IsHiding = true;
            if (!DeferClose) OnHidingCompleted.Invoke(this);
        }
        public void LoadCards()
        {
            LineupPreviewRoster.Prefix(this);
            m_ActivePlayersList.m_SkaterCards.Clear(); m_BenchPlayersList.m_SkaterCards.Clear();
            var positions = new[] { ESkaterPosition.LW, ESkaterPosition.LD, ESkaterPosition.C, ESkaterPosition.RD, ESkaterPosition.RW };
            var offsets = new[] { 0, 3, 2, 4, 1 };
            for (int i = 0; i < 5; i++) m_ActivePlayersList.m_SkaterCards.Add(new() { CurrentSkaterData = new() { ActivePosition = positions[i], Skater = Team.OriginalTeamData.forwards[offsets[i]] } });
            m_ActivePlayersList.m_SkaterCards.Add(new() { CurrentSkaterData = new() { ActivePosition = ESkaterPosition.Goalie, Skater = new SkaterData { id = "goalie" } } });
            foreach (int b in new[] { 1, 3, 0, 4, 2 })
                m_BenchPlayersList.m_SkaterCards.Add(new() { CurrentSkaterData = new() { ActivePosition = ESkaterPosition.Invalid, BenchIndex = b, Skater = Team.m_BenchForwards[b] } });
            TwoLineContractRosterCards.Postfix(this);
        }
    }
    public class EventChoiceItem
    {
        public AbstractNavigable Navigable = new();
        public STS.Map.EventChoice ChoiceData;
    }
    public class EventChoiceMenu : AbstractMenu
    {
        public STS.Map.EventNode SourceNode;
        public List<EventChoiceItem> m_Items = new();
        public bool m_IsTakenOver, m_IsExiting, Visible = true, Busy;
        public AbstractMenu m_MenuOverSelf;
        public int m_ChoicesMade, Refreshes, Exits, DefaultSelections;
        public bool IgnoreInput => Busy || m_IsTakenOver || m_MenuOverSelf != null || m_IsExiting;
        public void SetVisibility(bool visible, bool instant) => Visible = visible;
        public void GoToNextSetOfChoices() { Refreshes++; LoadOffers(); }
        public void SelectDefaultItem() => DefaultSelections++;
        public void Exit() => Exits++;
        public void LoadOffers() { m_Items = SourceNode.eventChoices.Select(c => new EventChoiceItem { ChoiceData = c }).ToList(); }
    }
}

namespace EndlessMode { static class GoalieRosterMenu { internal static void EntryLabel(LineupMenu menu) {} } }
