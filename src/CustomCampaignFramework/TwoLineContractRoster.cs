using System;
using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BishopGames.Module;
using BishopGames.UI;
using BishopGames.Catalogs;
using Data;
using HarmonyLib;
using Tape2Tape.Customization.UI;
using Tape2Tape.Hockey.UI;
using UnityEngine;

namespace EndlessMode;

// Recruitment is a position selection, not a normal lineup swap. Reuse the
// native two-row roster and its mouse/controller navigation, but keep its
// editable model detached from the actual team. Only Commit changes ownership.
internal static class TwoLineContractRoster
{
    internal sealed class Selection
    {
        internal TwoLineContracts.Visit Visit;
        internal TwoLineContracts.Offer Offer;
        internal int Revision;
        internal TeamData Preview;
        internal LineupMenu Menu;
        internal EventChoiceMenu Gm;
        internal EventChoicePanel Legacy;
        internal bool Ready, Closing, Finished, Committed;
        internal Il2CppSystem.Action<AbstractMenu> Closed;
    }

    internal static Selection Current;

    internal static bool Valid(Selection s) => s != null && ReferenceEquals(Current, s) && !s.Finished
        && TwoLineContracts.IsCurrent(s.Visit) && s.Visit.Pending == s.Offer
        && s.Visit.Revision == s.Revision && !s.Offer.Taken;

    internal static void Open(TwoLineContracts.Visit visit, EventChoiceMenu gm, EventChoicePanel legacy)
    {
        if (!TwoLineContracts.IsCurrent(visit) || visit.Pending == null || Current != null) return;
        var s = new Selection { Visit = visit, Offer = visit.Pending, Revision = visit.Revision, Gm = gm, Legacy = legacy };
        Current = s;
        visit.AdvancePending = false;
        try
        {
            TwoLineContracts.Reconcile(visit.Team);
            s.Preview = UnityEngine.Object.Instantiate(visit.Team);
            s.Preview.SetActiveLine(1); // Display Line 1 above Line 2, regardless of the live active line.
            if (gm != null) { gm.m_IsTakenOver = true; gm.SetVisibility(false, true); }
            if (legacy != null) SetLegacyVisible(legacy, false);
            MonoBehaviour host = gm != null ? gm : legacy;
            host.StartCoroutine(Show(s).WrapToIl2Cpp());
        }
        catch (Exception ex) { Fail(s, ex); }
    }

    private static IEnumerator Show(Selection s)
    {
        Cysharp.Threading.Tasks.UniTask<LineupMenu>.Awaiter awaiter;
        try
        {
            var ui = BishopModule.Get<UIModule>().Cast<T2TUIModule>();
            awaiter = ui.ShowMenuAsync<LineupMenu>(MenuDefinitionCatalogReference.Lineup,
                new LineupMenu.Metadata { Team = s.Preview, LockPlayers = true, IsHomeTeam = true }).GetAwaiter();
        }
        catch (Exception ex) { Fail(s, ex); yield break; }
        while (true)
        {
            bool ready;
            try { ready = awaiter.IsCompleted; }
            catch (Exception ex) { Fail(s, ex); yield break; }
            if (ready) break;
            yield return null;
        }
        try
        {
            var menu = awaiter.GetResult();
            if (menu == null) throw new InvalidOperationException("The roster menu did not open.");
            s.Menu = menu;
            WatchClose(s);
            if (!Valid(s)) { menu.TryLeaveMenu(false); yield break; }
            if (s.Gm != null) s.Gm.m_MenuOverSelf = menu;
            Configure(s, menu);
            s.Ready = true;
            menu.SelectFirstPlayer();
        }
        catch (Exception ex) { Fail(s, ex); }
    }

    internal static void Initialize(LineupMenu menu)
    {
        var s = Current;
        if (!Valid(s) || menu?.Team?.OriginalTeamData?.Pointer != s.Preview?.Pointer) return;
        s.Menu = menu;
        LineupPreviewRoster.FillInactiveLine(menu);
        menu.m_HideBench = false;
    }

    private static void WatchClose(Selection s)
    {
        if (s.Closed != null) return;
        s.Closed = (Action<AbstractMenu>)(_ => Finish(s));
        s.Menu.OnHidingCompleted.Register(s.Closed, "TwoLineContractRoster");
        s.Menu.OnDestroy.Register(s.Closed, "TwoLineContractRoster");
    }

    internal static int Slot(SkaterDataForUI data)
    {
        // GetBench returns C,LW,RW,LD,RD; BenchIndex retains this order even
        // though the native screen arranges those five cards horizontally.
        if (data.IsBenched)
            return data.BenchIndex switch { 0 => 7, 1 => 5, 2 => 6, 3 => 8, 4 => 9, _ => -1 };
        return data.ActivePosition switch
        {
            ESkaterPosition.LW => 0, ESkaterPosition.RW => 1, ESkaterPosition.C => 2,
            ESkaterPosition.LD => 3, ESkaterPosition.RD => 4, _ => -1
        };
    }

    internal static void Configure(Selection s, LineupMenu menu)
    {
        if (!Valid(s) || s.Menu?.Pointer != menu?.Pointer) return;
        foreach (var list in new[] { menu.m_ActivePlayersList, menu.m_BenchPlayersList })
            if (list?.m_SkaterCards != null)
                for (int i = 0; i < list.m_SkaterCards.Count; i++)
                {
                    var card = list.m_SkaterCards[i];
                    card.SetLocked(Slot(card.CurrentSkaterData) < 0, true);
                }
        if (menu.m_Header?.m_Title != null)
            menu.m_Header.m_Title.text = $"Choose a position for {s.Offer.Player.FullName}";
        // Keep HasChanged false: leaving an incomplete squad must not run the
        // ordinary lineup-validity prompt or apply a stale preview over a hire.
    }

    internal static bool Select(LineupMenu menu, SkaterCard card)
    {
        var s = Current;
        if (s?.Menu?.Pointer != menu?.Pointer) return false;
        if (!Valid(s) || !s.Ready || s.Closing || card == null || menu.IsHiding || card.Toggle?.IsSelected != true) return true;
        if (!menu.m_ActivePlayersList.ContainsSkaterCard(card) && !menu.m_BenchPlayersList.ContainsSkaterCard(card)) return true;
        int slot = Slot(card.CurrentSkaterData);
        if (slot < 0) return true;
        string shown = card.CurrentSkaterData.Skater?.id ?? "";
        if (shown != (TwoLineContracts.SlotId(s.Visit.Team, slot) ?? "")) return true;
        TwoLineContracts.Commit(s.Visit, s.Offer, slot);
        if (!s.Offer.Taken)
        {
            card.Toggle.SetSelected(false, false);
            if (menu.m_Header?.m_Title != null) menu.m_Header.m_Title.text = "Recruitment failed. Try again or go back.";
            return true;
        }
        s.Committed = true; s.Closing = true;
        menu.TryLeaveMenu(false);
        return true;
    }

    internal static void Finish(Selection s)
    {
        if (s == null || s.Finished) return;
        s.Finished = true;
        if (s.Menu != null && s.Closed != null)
        {
            s.Menu.OnHidingCompleted.Unregister(s.Closed);
            s.Menu.OnDestroy.Unregister(s.Closed);
        }
        if (ReferenceEquals(Current, s)) Current = null;
        bool currentVisit = TwoLineContracts.IsCurrent(s.Visit);
        if (!s.Committed && currentVisit) TwoLineContracts.CancelSelection(s.Visit);
        if (s.Gm != null)
        {
            s.Gm.m_IsTakenOver = false;
            if (s.Gm.m_MenuOverSelf?.Pointer == s.Menu?.Pointer) s.Gm.m_MenuOverSelf = null;
            if (currentVisit && !s.Gm.m_IsExiting)
            {
                s.Gm.SetVisibility(true, true);
                if (s.Committed)
                {
                    s.Gm.m_ChoicesMade = s.Visit.Hires;
                    if (s.Visit.Node.NextChoices(s.Visit.Hires)) s.Gm.GoToNextSetOfChoices();
                    else { s.Gm.m_IsExiting = true; s.Gm.Exit(); }
                }
                else s.Gm.SelectDefaultItem();
            }
        }
        if (s.Legacy != null && currentVisit)
        {
            SetLegacyVisible(s.Legacy, true);
            if (s.Committed)
            {
                s.Visit.AdvancePending = false;
                if (s.Visit.Node.NextChoices(s.Visit.Hires))
                { s.Legacy.ClearChoicesButtons(); s.Legacy.InitializeChoicesButtons(s.Visit.Node.eventChoices); }
                else s.Legacy.HideMenu();
            }
        }
        if (s.Preview != null) UnityEngine.Object.Destroy(s.Preview);
    }

    private static void SetLegacyVisible(EventChoicePanel panel, bool visible)
    {
        if (panel._canvasGroup == null) return;
        panel._canvasGroup.alpha = visible ? 1 : 0;
        panel._canvasGroup.interactable = visible;
        panel._canvasGroup.blocksRaycasts = visible;
    }

    private static void Fail(Selection s, Exception ex)
    {
        Plugin.Log.LogError("[TwoLineContracts] Could not open recruit position selection: " + ex);
        if (s.Menu != null)
        {
            WatchClose(s);
            if (!s.Menu.IsHiding) s.Menu.TryLeaveMenu(false);
        }
        else Finish(s);
    }
}

[HarmonyPatch(typeof(EventChoiceMenu), "OnItemClicked")]
internal static class TwoLineContractRecruitClick
{
    [HarmonyPrefix] internal static bool Prefix(EventChoiceMenu __instance, AbstractNavigable __0)
    {
        var visit = TwoLineContracts.Current;
        if (!TwoLineContracts.IsCurrent(visit) || __instance.SourceNode?.Pointer != visit.Node.Pointer) return true;
        if (__instance.IgnoreInput || TwoLineContractRoster.Current != null || __0 == null) return false;
        foreach (var item in __instance.m_Items)
            if (item.Navigable?.Pointer == __0.Pointer)
            {
                var choice = item.ChoiceData;
                if (!choice.interactable || choice.Skater?.TryCast<ForwardData>() == null) return true;
                choice.onSelection?.Invoke();
                TwoLineContractRoster.Open(visit, __instance, null);
                return false;
            }
        return false;
    }
}

[HarmonyPatch(typeof(LineupMenu), "InitData")]
internal static class TwoLineContractRosterInit
{
    [HarmonyPostfix] internal static void Postfix(LineupMenu __instance) => TwoLineContractRoster.Initialize(__instance);
}

[HarmonyPatch(typeof(LineupMenu), "LoadCurrentTeam")]
internal static class TwoLineContractRosterCards
{
    [HarmonyPostfix] internal static void Postfix(LineupMenu __instance) => TwoLineContractRoster.Configure(TwoLineContractRoster.Current, __instance);
}

[HarmonyPatch(typeof(LineupMenu), "OnTogglePlayerCard")]
internal static class TwoLineContractRosterPick
{
    [HarmonyPrefix] internal static bool Prefix(LineupMenu __instance, SkaterCard __0) => !TwoLineContractRoster.Select(__instance, __0);
}
