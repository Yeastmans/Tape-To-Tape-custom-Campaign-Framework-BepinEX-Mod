using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BishopGames.Module;
using BishopGames.UI;
using BishopGames.Catalogs;
using CampaignCore;
using Data;
using HarmonyLib;
using State;
using STS.Map;
using Tape2Tape;
using Tape2Tape.Customization.UI;
using Tape2Tape.Hockey.UI;
using UnityEngine;
using TMPro;

namespace EndlessMode;

// One native LineupMenu, with a detached preview and a one/two-card active row.
// SkaterCardList.Refresh owns click/toggle listeners and controller navigation.
// No TeamRosterData edit/apply operation is used for goalie ownership changes.
internal static class GoalieRosterMenu
{
    internal sealed class Selection
    {
        internal CampaignState Campaign;
        internal IntPtr Run;
        internal string Folder;
        internal long Revision;
        internal GoalieRosterState Snapshot;
        internal GoalieRecruitment.Offer Offer;
        internal bool? DeparturePrevention;
        internal GoaltenderData Active, Reserve;
        internal TeamData Preview;
        internal LineupMenu Menu, Parent;
        internal EventChoiceMenu Gm;
        internal EventChoicePanel Legacy;
        internal bool Ready, Saving, Finished, Committed, Unresolved, Destroyed;
        internal bool ParentPolicyCaptured, ParentHideBench, ParentBenchVisible;
        internal string Status, CardMessage;
        internal bool EmptyLabelUnavailable;
        internal readonly Dictionary<IntPtr, TextPresentation> Labels = new();
        internal readonly Dictionary<IntPtr, BadgePresentation> Badges = new();
        internal GoalieSlot? Confirming, AttemptedSlot;
        internal Il2CppSystem.Action<AbstractMenu> Closed, Destroying;
    }
    internal sealed class TextPresentation
    {
        internal TMP_Text Text;
        internal string Value;
        internal bool Auto, Wrap;
        internal float Size, Minimum, Maximum;
        internal int Lines;
        internal TextOverflowModes Overflow;
        internal Color Color;
    }
    internal sealed class BadgePresentation
    {
        internal SkaterCard Card;
        internal IntPtr Skater, Team;
        internal ESkaterPosition Position;
        internal GameObject Container;
        internal RectTransform ContainerRect, TextRect;
        internal Vector2 ContainerSize, ContainerPosition, TextSize;
        internal bool Visible;
        internal int SiblingIndex;
    }
    internal static Selection Current;
    private static readonly Dictionary<IntPtr, Selection> EntryPresentations = new();
    private static bool installed;
    internal static bool Enabled => installed && GoaliePersistenceBridge.Enabled;
    internal static void SetInstalled(bool value) => installed = value;
    internal const string Feature = "goalie-roster-menu";
    internal static readonly NamedManagedHookContract[] Required = {
        Hook("init", typeof(LineupMenu), "InitData", "System.Void", new[] { "Il2CppSystem.Object" }, "Initialize", false),
        Hook("cards", typeof(LineupMenu), "LoadCurrentTeam", "System.Void", Array.Empty<string>(), "Refresh", false),
        Hook("pick", typeof(LineupMenu), "OnTogglePlayerCard", "System.Void", new[] { "Tape2Tape.Customization.UI.SkaterCard" }, "Pick", true),
        Hook("back", typeof(LineupMenu), "TryLeaveMenu", "System.Void", new[] { "System.Boolean" }, "Leave", true),
        Hook("recruit", typeof(EventChoiceMenu), "OnItemClicked", "System.Void", new[] { "BishopGames.UI.AbstractNavigable" }, "RecruitClick", true),
        Hook("gm-init", typeof(EventChoiceMenu), "InitData", "System.Void", new[] { "Il2CppSystem.Object" }, "RecruitInitialized", false),
        Hook("gm-close", typeof(EventChoiceMenu), "OnClosed", "System.Void", Array.Empty<string>(), "RecruitClosed", false),
        Hook("gm-destroy", typeof(EventChoiceMenu), "BehaviourDestroy", "System.Void", Array.Empty<string>(), "RecruitClosed", true),
        Hook("legacy-open", typeof(EventChoicePanel), "ShowMenu", "System.Void", new[] { "STS.Map.MapNode", "STS.Map.EventNode" }, "LegacyOpening", true),
        Hook("legacy-close", typeof(EventChoicePanel), "HideMenu", "System.Void", Array.Empty<string>(), "LegacyLeave", true)
    };
    private static NamedManagedHookContract Hook(string id, Type type, string method, string result, string[] parameters, string patch, bool prefix)
        => new() { Feature = Feature, Id = "goalie-menu-" + id, Assembly = "MainScriptsAssembly", DeclaringType = type.FullName,
            Method = method, ReturnType = result, Parameters = parameters, Prefix = prefix ? patch : "", Postfix = prefix ? "" : patch };
    internal static (MethodInfo Native, MethodInfo Prefix, MethodInfo Postfix)[] Resolve()
        => Required.Select(c => (JerseyLogoManagedTargets.Resolve(c, AppDomain.CurrentDomain.GetAssemblies()),
            c.Prefix.Length == 0 ? null : AccessTools.Method(typeof(GoalieRosterMenu), c.Prefix) ?? throw new MissingMethodException(c.Prefix),
            c.Postfix.Length == 0 ? null : AccessTools.Method(typeof(GoalieRosterMenu), c.Postfix) ?? throw new MissingMethodException(c.Postfix))).ToArray();
    internal static bool Valid(Selection s) => s != null && !s.Finished && ReferenceEquals(Current, s)
        && s.Campaign?.runData?.Pointer == s.Run && s.Campaign.Pointer == CampaignUtil.CampaignState?.Pointer
        && string.Equals(s.Folder, Plugin.ModFolder, StringComparison.OrdinalIgnoreCase)
        && GoaliePersistenceBridge.Roster.CanEdit(s.Campaign)
        && (s.Offer == null || GoalieRecruitment.Valid(s.Offer) && ReferenceEquals(s.Offer.Visit.Pending, s.Offer));
    internal static void Open(CampaignState state)
    {
        var parent = UnityEngine.Object.FindObjectOfType<LineupMenu>();
        if (parent?.Team?.OriginalTeamData?.Pointer == state?.runData?.teamData?.Pointer) Open(state, null, null, null, parent);
    }
    internal static void Open(CampaignState state, GoalieRecruitment.Offer offer, EventChoiceMenu gm, EventChoicePanel legacy, LineupMenu parent = null)
    {
        if (!Enabled || Current != null || GoaliePersistenceBridge.Roster.Busy || state?.runData?.teamData == null) return;
        if (parent != null) RestoreEntry(parent);
        var s = new Selection { Campaign = state, Run = state.runData.Pointer, Folder = Plugin.ModFolder, Offer = offer, Gm = gm, Legacy = legacy, Parent = parent };
        Current = s;
        try {
            if (parent != null) {
                s.ParentHideBench = parent.m_HideBench;
                s.ParentBenchVisible = parent.m_BenchPlayersList.gameObject.activeSelf;
                s.ParentPolicyCaptured = true;
            }
            if (!GoaliePersistenceBridge.Roster.CanEdit(state)) { Finish(s); return; }
            s.Snapshot = GoaliePersistenceBridge.Roster.Capture(state.runData, Plugin.ModFolder); s.Revision = s.Snapshot.Revision;
            s.Active = GoalieNativeSnapshot.Restore(s.Snapshot.Active);
            s.Reserve = s.Snapshot.Reserve == null ? null : GoalieNativeSnapshot.Restore(s.Snapshot.Reserve);
            s.Preview = UnityEngine.Object.Instantiate(state.runData.teamData);
            s.Preview.goalie = s.Active;
            if (gm != null) { gm.m_IsTakenOver = true; gm.SetVisibility(false, true); }
            if (legacy != null) SetLegacyVisible(legacy, false);
            if (parent != null) {
                // Reuse the current native menu: UIModule can retain menus by
                // type, so opening a second LineupMenu is not an isolation boundary.
                s.Menu = parent; WatchClose(s); Configure(s); s.Ready = true; parent.SelectFirstPlayer(); return;
            }
            // UIModule is not a MonoBehaviour; a known active menu owns the coroutine.
            MonoBehaviour host = gm != null ? gm : legacy != null ? legacy : parent;
            if (host == null) host = UnityEngine.Object.FindObjectOfType<LineupMenu>();
            if (host == null) throw new InvalidOperationException("Open the campaign lineup to manage goalies.");
            host.StartCoroutine(Show(s).WrapToIl2Cpp());
        } catch (Exception ex) { Fail(s, ex); }
    }
    private static IEnumerator Show(Selection s)
    {
        Cysharp.Threading.Tasks.UniTask<LineupMenu>.Awaiter awaiter;
        try { awaiter = BishopModule.Get<UIModule>().Cast<T2TUIModule>().ShowMenuAsync<LineupMenu>(MenuDefinitionCatalogReference.Lineup,
            new LineupMenu.Metadata { Team = s.Preview, LockPlayers = true, IsHomeTeam = true }).GetAwaiter(); }
        catch (Exception ex) { Fail(s, ex); yield break; }
        while (true) {
            bool ready;
            try { ready = awaiter.IsCompleted; } catch (Exception ex) { Fail(s, ex); yield break; }
            if (ready) break; yield return null;
        }
        try {
            s.Menu = awaiter.GetResult() ?? throw new InvalidOperationException("The goalie menu did not open.");
            WatchClose(s);
            if (!Valid(s)) { s.Menu.TryLeaveMenu(false); yield break; }
            if (s.Gm != null) s.Gm.m_MenuOverSelf = s.Menu;
            Configure(s); s.Ready = true; s.Menu.SelectFirstPlayer();
        } catch (Exception ex) { Fail(s, ex); }
    }
    private static void WatchClose(Selection s)
    {
        s.Closed = (Action<AbstractMenu>)(_ => Finish(s));
        s.Destroying = (Action<AbstractMenu>)(_ => { s.Destroyed = true; Finish(s); });
        s.Menu.OnHidingCompleted.Register(s.Closed, "GoalieRosterMenu"); s.Menu.OnDestroy.Register(s.Destroying, "GoalieRosterMenu");
    }
    private static void Initialize(LineupMenu __instance)
    {
        var s = Current;
        if (s?.Preview?.Pointer != __instance?.Team?.OriginalTeamData?.Pointer) return;
        s.Menu = __instance; __instance.m_HideBench = true;
    }
    internal static void Refresh(LineupMenu __instance)
    {
        if (Current?.Menu?.Pointer == __instance?.Pointer && !Current.Finished) Configure(Current);
        else EntryLabel(__instance, nativeRefreshed: true);
    }
    internal static string ContractText(GoalieRecord record) => record == null ? "Empty" : record.Contract == null ? "Permanent"
        : record.Contract.AwaitingReplacement ? "Expired — awaiting replacement"
        : $"{Math.Max(0, record.Contract.Duration - record.Contract.PlayedMatches)} / {record.Contract.Duration} played matches remaining";
    private static void Configure(Selection s)
    {
        RestoreLabels(s);
        var menu = s.Menu;
        var cards = new Il2CppSystem.Collections.Generic.List<SkaterDataForUI>();
        // SkaterDataForUI is a native 48-byte struct represented by a managed
        // class. IList accepts its box; native code unboxes all 48 bytes. The
        // generated generic List.Add wrapper would pass the boxed header.
        var boxedCards = cards.Cast<Il2CppSystem.Collections.IList>();
        boxedCards.Add(new SkaterDataForUI(s.Preview, s.Active, ESkaterPosition.Goalie, null, null, EStatBonusesPreviewMode.None, null));
        if (s.Snapshot.ReserveEnabled) boxedCards.Add(new SkaterDataForUI(s.Preview, s.Reserve, ESkaterPosition.Goalie, null, null, EStatBonusesPreviewMode.None, null));
        menu.m_ActiveSkaters = cards; menu.m_BenchSkaters.Clear();
        menu.m_ActivePlayersList.Refresh(cards.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<SkaterDataForUI>>());
        menu.m_BenchPlayersList.Clear(); menu.m_HideBench = true;
        for (int i = 0; i < menu.m_ActivePlayersList.m_SkaterCards.Count; i++) {
            var card = menu.m_ActivePlayersList.m_SkaterCards[i];
            card.SetLocked(s.Saving || s.Unresolved || s.Offer == null && (i == 0 || s.Reserve == null), true);
            if (card.m_PositionLetter != null) card.m_PositionLetter.text = i == 0 ? "A" : "R";
        }
        Title(s, s.Status ?? (s.Offer == null ? "Goalies — select the reserve to swap" : "Choose a slot for " + s.Offer.Candidate.FullName));
    }
    private static void Title(Selection s, string title)
    { if (s.Finished) return; s.Status = title; if (s.Menu?.m_Header?.m_Title != null) s.Menu.m_Header.m_Title.text = title; LabelCards(s); }
    private static string ShortName(string name)
    {
        name = (name ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        return name.Length > 24 ? name.Substring(0, 23) + "…" : name;
    }
    private static void LabelCards(Selection s)
    {
        var cards = s.Menu?.m_ActivePlayersList?.m_SkaterCards;
        if (cards == null) return;
        for (int i = 0; i < cards.Count && i < 2; i++) {
            var card = cards[i]; var slot = i == 0 ? GoalieSlot.Active : GoalieSlot.Reserve;
            bool empty = i == 1 && s.Reserve == null;
            var record = i == 0 ? s.Snapshot.Active : s.Snapshot.Reserve;
            // Like skaters, show remaining matches in the native contract badge.
            // A pending hire previews the frozen offer only on its target card.
            bool incoming = s.Offer != null && (s.Confirming == slot || s.Saving && s.AttemptedSlot == slot || s.Unresolved && s.AttemptedSlot == slot);
            ContractBadge(s, card, incoming ? s.Offer.Contract : record?.Contract);
            string text = s.AttemptedSlot == slot
                ? s.Saving ? "Saving…" : s.Unresolved ? "Save unresolved — go back" : s.CardMessage
                : null;
            if (text == null) text = s.Confirming == slot
                ? s.Offer == null ? "Confirm swap" : "Confirm hire " + ShortName(s.Offer.Candidate.FullName)
                : empty ? s.Offer == null ? "Empty — recruit at GM" : "Empty — select to hire"
                : ShortName(i == 0 ? s.Active.FullName : s.Reserve.FullName);
            FitLabel(s, card.m_SkaterName, slot + ": " + text);
            if (empty) {
                // Both installed lineup card prefabs have four TMP descendants:
                // Name plus Swap/StatBoxSwap comparison labels. Address only Name.
                var label = card.m_EmptySeatContainer?.transform.Find("PlayerInfo/Name")?.GetComponent<TMP_Text>();
                if (label != null) {
                    FitLabel(s, label, slot + ": " + text);
                    // Native empty-seat text is half-transparent black. An
                    // offered goalie makes this a real hire target/status label.
                    if (s.Offer != null && card.m_SkaterName != null) label.color = card.m_SkaterName.color;
                }
                else if (!s.EmptyLabelUnavailable) {
                    s.EmptyLabelUnavailable = true;
                    Plugin.Log.LogWarning("[GoalieRoster] Empty-slot PlayerInfo/Name TMP label unavailable; retaining native label and header instructions.");
                }
            }
        }
    }
    private static void FitLabel(Selection s, TMP_Text text, string value)
    {
        if (text == null) return;
        CaptureLabel(s, text);
        var prior = s.Labels[text.Pointer];
        text.enableWordWrapping = false; text.enableAutoSizing = true;
        text.fontSizeMax = Math.Max(prior.Size, prior.Maximum); text.fontSizeMin = Math.Min(16, text.fontSizeMax);
        text.maxVisibleLines = 1; text.overflowMode = TextOverflowModes.Ellipsis; text.text = value;
    }
    private static void CaptureLabel(Selection s, TMP_Text text)
    {
        if (!s.Labels.ContainsKey(text.Pointer)) s.Labels[text.Pointer] = new TextPresentation { Text = text, Value = text.text,
            Auto = text.enableAutoSizing, Wrap = text.enableWordWrapping, Size = text.fontSize, Minimum = text.fontSizeMin,
            Maximum = text.fontSizeMax, Lines = text.maxVisibleLines, Overflow = text.overflowMode, Color = text.color };
    }
    private static void ContractBadge(Selection s, SkaterCard card, GoalieContractState contract)
    {
        var text = card.m_FreeAgentDuration; var container = card.m_FreeAgentDurationContainer;
        if (text == null || container == null) return;
        CaptureLabel(s, text);
        if (!s.Badges.TryGetValue(text.Pointer, out var prior)) {
            var containerRect = container.GetComponent<RectTransform>(); var textRect = text.rectTransform;
            prior = new BadgePresentation { Card = card, Skater = card.CurrentSkaterData.Skater?.Pointer ?? IntPtr.Zero,
                Team = card.CurrentSkaterData.ActiveTeam?.Pointer ?? IntPtr.Zero, Position = card.CurrentSkaterData.ActivePosition,
                Container = container, Visible = container.activeSelf, SiblingIndex = container.transform.GetSiblingIndex(),
                ContainerRect = containerRect, TextRect = textRect,
                ContainerSize = containerRect != null ? containerRect.sizeDelta : default,
                ContainerPosition = containerRect != null ? containerRect.anchoredPosition : default,
                TextSize = textRect != null ? textRect.sizeDelta : default };
            s.Badges[text.Pointer] = prior;
        }
        string value = contract == null ? "" : Math.Max(0, contract.Duration - contract.PlayedMatches).ToString(System.Globalization.CultureInfo.InvariantCulture);
        text.enableWordWrapping = false; text.maxVisibleLines = 1;
        text.enableAutoSizing = false; text.fontSize = s.Labels[text.Pointer].Size;
        text.text = value;
        if (prior.ContainerRect != null && prior.TextRect != null) {
            // Native layout keeps child width fixed. Grow horizontally for extra
            // digits while retaining the same left edge, height, font, and icon.
            float extra = Math.Max(0, text.GetPreferredValues(value).x - prior.TextSize.x);
            prior.TextRect.sizeDelta = new Vector2(prior.TextSize.x + extra, prior.TextSize.y);
            prior.ContainerRect.sizeDelta = new Vector2(prior.ContainerSize.x + extra, prior.ContainerSize.y);
            prior.ContainerRect.anchoredPosition = new Vector2(prior.ContainerPosition.x + extra / 2, prior.ContainerPosition.y);
        }
        // The native goalie portrait is a later sibling and covers the duration.
        // Keep the badge in its native place, above the portrait while owned.
        if (contract != null) container.transform.SetAsLastSibling();
        else container.transform.SetSiblingIndex(prior.SiblingIndex);
        container.SetActive(contract != null);
    }
    private static void RestoreLabels(Selection s, bool nativeRefreshed = false)
    {
        foreach (var prior in s.Labels.Values) if (prior.Text != null) {
            var text = prior.Text;
            if (!nativeRefreshed) text.text = prior.Value;
            text.enableAutoSizing = prior.Auto; text.enableWordWrapping = prior.Wrap;
            text.fontSize = prior.Size; text.fontSizeMin = prior.Minimum; text.fontSizeMax = prior.Maximum;
            text.maxVisibleLines = prior.Lines; text.overflowMode = prior.Overflow;
            text.color = prior.Color;
        }
        s.Labels.Clear();
        foreach (var prior in s.Badges.Values) {
            if (prior.Container != null) prior.Container.transform.SetSiblingIndex(prior.SiblingIndex);
            if (prior.ContainerRect != null) { prior.ContainerRect.sizeDelta = prior.ContainerSize; prior.ContainerRect.anchoredPosition = prior.ContainerPosition; }
            if (prior.TextRect != null) prior.TextRect.sizeDelta = prior.TextSize;
            if (!nativeRefreshed && prior.Container != null) prior.Container.SetActive(prior.Visible);
        }
        s.Badges.Clear();
    }
    internal static bool Pick(LineupMenu __instance, SkaterCard __0)
    {
        var s = Current;
        if (s?.Menu?.Pointer != __instance?.Pointer) return !Entry(__instance, __0);
        if (!Valid(s) || !s.Ready || s.Saving || s.Unresolved || __instance.IsHiding || __0?.Toggle?.IsSelected != true) return false;
        int index = -1;
        for (int i = 0; i < __instance.m_ActivePlayersList.m_SkaterCards.Count; i++)
            if (__instance.m_ActivePlayersList.m_SkaterCards[i].Pointer == __0.Pointer) index = i;
        if (index < 0 || index > 1 || index == 1 && !s.Snapshot.ReserveEnabled) return false;
        if (s.Offer == null && (index != 1 || s.Reserve == null)) return false;
        var slot = index == 0 ? GoalieSlot.Active : GoalieSlot.Reserve;
        s.CardMessage = null; s.AttemptedSlot = null;
        __0.Toggle.SetSelected(false, false);
        bool? prevention = null;
        if (s.Offer != null && slot == GoalieSlot.Reserve && GoalieContractPolicy.Expired(s.Snapshot.Active)) {
            if (!GoalieContractRuntime.TryDepartureProtection(s.Campaign, out bool prevented)) {
                s.Confirming = null; s.DeparturePrevention = null;
                Title(s, "Departure protection could not be verified. No signing made. Try again or go back.");
                return false;
            }
            prevention = prevented;
        }
        if (s.Confirming != slot || s.DeparturePrevention != prevention) {
            s.Confirming = slot;
            s.DeparturePrevention = prevention;
            string occupant = index == 0 ? s.Active.FullName : s.Reserve?.FullName;
            string result = prevention == false ? $" {s.Offer.Candidate.FullName} becomes active; expired {s.Active.FullName} leaves and Reserve is empty."
                : prevention == true ? $" Departure prevention applies: {s.Active.FullName} remains active; {s.Offer.Candidate.FullName} stays in Reserve." : "";
            Title(s, s.Offer == null ? $"Swap {s.Active.FullName} with {s.Reserve.FullName}? Select Reserve again to confirm; Back cancels."
                : $"{(occupant == null ? "Fill empty reserve" : "Replace " + occupant)} with {s.Offer.Candidate.FullName} ({GoalieContractRuntime.OfferText(s.Offer.Contract)})?{result} Select {slot} again to confirm; Back cancels.");
            return false;
        }
        s.AttemptedSlot = slot; s.Saving = true; Title(s, "Saving goalie roster…");
        void Completed(GoalieRosterRuntime.ChangeResult result) {
            Plugin.Log.LogInfo("[GoalieRoster] Selection completed; slot=" + slot + "; run=" + s.Run.ToString("X")
                + "; outcome=" + result.Outcome + "; message=" + result.Message + "; " + GoaliePersistenceBridge.DescribeReceipt(result.Receipt));
            s.Saving = false;
            if (result.Receipt?.Reconciled == true) { s.Committed = true; if (s.Offer != null) GoalieRecruitment.Consumed(s.Offer); }
            if (result.Outcome == GoalieRosterRuntime.ChangeOutcome.Committed) {
                s.Committed = true; if (s.Offer != null) GoalieRecruitment.Consumed(s.Offer);
                if (s.Finished) return;
                Title(s, result.Message); s.Menu.TryLeaveMenu(false);
            } else if (result.Outcome == GoalieRosterRuntime.ChangeOutcome.Unresolved) {
                s.Unresolved = true; if (s.Offer != null) s.Offer.Unresolved = true;
                Title(s, result.Message);
            } else { s.Confirming = null; s.CardMessage = "Save failed — select again"; Title(s, result.Message); }
        }
        bool accepted = s.Offer != null
            ? GoaliePersistenceBridge.Roster.TryRecruit(s.Campaign, s.Offer.Candidate, slot, s.Revision, Completed, s.Offer.Contract, s.Offer.Visit.Ledger, s.Offer.Id, s.DeparturePrevention, s.Offer.Visit.Selection.Index, s.Offer.Visit.Selection.Layer)
            : GoaliePersistenceBridge.Roster.TrySwap(s.Campaign, s.Revision, Completed);
        if (!accepted) { s.Saving = false; s.Confirming = null; s.CardMessage = "Unavailable — reopen"; Title(s, "Goalie selection changed or could not be prepared. Go back and reopen."); }
        return false;
    }
    internal static bool Leave(LineupMenu __instance)
    {
        var s = Current;
        if (s?.Menu?.Pointer != __instance?.Pointer) return true;
        if (s.Saving) return false;
        if (s.Parent != null) { Finish(s); return false; }
        return true;
    }
    private static bool Entry(LineupMenu menu, SkaterCard card)
    {
        var campaign = CampaignUtil.CampaignState;
        if (!Enabled || Current != null || menu?.Team?.OriginalTeamData?.Pointer != campaign?.runData?.teamData?.Pointer
            || card?.CurrentSkaterData.ActivePosition != ESkaterPosition.Goalie || card.Toggle?.IsSelected != true
            || !GoalieRosterRuntime.IsMapContext(CampaignUtil.CampaignState)) return false;
        card.Toggle.SetSelected(false, false);
        if (!GoaliePersistenceBridge.Roster.CanEdit(campaign)) {
            if (menu.m_Header?.m_Title != null) menu.m_Header.m_Title.text = GoaliePersistenceBridge.Roster.UnavailableReason ?? "Goalie roster unavailable.";
            return true;
        }
        Open(campaign, null, null, null, menu); return true;
    }
    internal static void EntryLabel(LineupMenu menu, bool nativeRefreshed = false)
    {
        // LoadCurrentTeam's caller marks pooled cards as freshly populated;
        // InitData can also request this entry presentation before that refresh.
        RestoreEntry(menu, nativeRefreshed);
        if (!Enabled || menu?.Team?.OriginalTeamData?.Pointer != CampaignUtil.CampaignState?.runData?.teamData?.Pointer
            || !GoalieRosterRuntime.IsMapContext(CampaignUtil.CampaignState)) return;
        if (menu.m_ActivePlayersList?.m_SkaterCards == null) return;
        Selection display = null;
        foreach (var card in menu.m_ActivePlayersList.m_SkaterCards)
            if (card.CurrentSkaterData.ActivePosition == ESkaterPosition.Goalie) {
                card.SetLocked(false, true);
                if (card.m_PositionLetter != null) card.m_PositionLetter.text = "G";
                if (GoaliePersistenceBridge.Roster.CanEdit(CampaignUtil.CampaignState)) {
                    if (display == null) {
                        display = new Selection { Menu = menu, Campaign = CampaignUtil.CampaignState, Run = CampaignUtil.CampaignState.runData.Pointer };
                        display.Closed = (Action<AbstractMenu>)(_ => RestoreEntry(menu));
                        display.Destroying = (Action<AbstractMenu>)(_ => RestoreEntry(menu));
                        EntryPresentations[menu.Pointer] = display;
                        menu.OnHidingCompleted.Register(display.Closed, "GoalieContractBadge");
                        menu.OnDestroy.Register(display.Destroying, "GoalieContractBadge");
                    }
                    ContractBadge(display, card, GoaliePersistenceBridge.Roster.Selection?.Active?.Contract);
                }
            }
    }
    private static void RestoreEntry(LineupMenu menu, bool nativeRefreshed = false)
    {
        if (menu == null || !EntryPresentations.TryGetValue(menu.Pointer, out var display)) return;
        EntryPresentations.Remove(menu.Pointer);
        // InitData and close can also arrive after a pooled card or run changes.
        // Never replace that new native occupant's text/visibility with a prior baseline.
        nativeRefreshed |= display.Run != CampaignUtil.CampaignState?.runData?.Pointer
            || display.Campaign?.Pointer != CampaignUtil.CampaignState?.Pointer
            || display.Badges.Values.Any(b => b.Card == null || b.Card.CurrentSkaterData.Skater?.Pointer != b.Skater
                || b.Card.CurrentSkaterData.ActiveTeam?.Pointer != b.Team || b.Card.CurrentSkaterData.ActivePosition != b.Position);
        RestoreLabels(display, nativeRefreshed);
        menu.OnHidingCompleted.Unregister(display.Closed); menu.OnDestroy.Unregister(display.Destroying);
    }
    internal static bool RecruitClick(EventChoiceMenu __instance, AbstractNavigable __0)
    {
        var visit = GoalieRecruitment.Current;
        if (!Enabled || !GoalieRecruitment.IsCurrent(visit) || __instance.SourceNode?.Pointer != visit.Node || visit.Selection.Menu?.Pointer != __instance.Pointer) return true;
        if (__instance.IgnoreInput || Current != null || __0 == null) return false;
        foreach (var item in __instance.m_Items) if (item.Navigable?.Pointer == __0.Pointer) {
            if (!item.ChoiceData.interactable) return false;
            var offer = visit.Offers.FirstOrDefault(o => o.Candidate.Pointer == item.ChoiceData.Skater?.Pointer);
            if (GoalieRecruitment.BeginSelection(offer)) Open(CampaignUtil.CampaignState, offer, __instance, null);
            return false;
        }
        return false;
    }
    internal static void RecruitInitialized(EventChoiceMenu __instance)
        => GoalieRecruitment.Selected(CampaignUtil.CampaignState, __instance?.mapNode, __instance?.SourceNode, __instance);
    private static void RecruitClosed(EventChoiceMenu __instance) => GoalieRecruitment.Closed(__instance);
    private static void LegacyOpening(EventChoicePanel __instance, MapNode __0, EventNode __1)
        => GoalieRecruitment.Selected(CampaignUtil.CampaignState, __0, __1, legacy: __instance);
    private static bool LegacyLeave(EventChoicePanel __instance)
    {
        var visit = GoalieRecruitment.Current;
        if (!GoalieRecruitment.IsCurrent(visit) || visit.Legacy?.Pointer != __instance.Pointer) return true;
        if (Current?.Legacy?.Pointer == __instance.Pointer) return false;
        if (visit.Pending == null) { GoalieRecruitment.Closed(__instance); return true; }
        Open(CampaignUtil.CampaignState, visit.Pending, null, __instance); return false;
    }
    internal static void Finish(Selection s)
    {
        if (s == null || s.Finished) return; s.Finished = true;
        RestoreLabels(s);
        if (s.Menu != null && s.Closed != null) { s.Menu.OnHidingCompleted.Unregister(s.Closed); s.Menu.OnDestroy.Unregister(s.Destroying); }
        if (ReferenceEquals(Current, s)) Current = null;
        if (s.Offer != null && !s.Saving) GoalieRecruitment.Cancel(s.Offer);
        bool currentVisit = s.Offer != null && GoalieRecruitment.IsCurrent(s.Offer.Visit);
        if (s.Gm != null) {
            s.Gm.m_IsTakenOver = false;
            if (s.Gm.m_MenuOverSelf?.Pointer == s.Menu?.Pointer) s.Gm.m_MenuOverSelf = null;
            if (currentVisit && !s.Gm.m_IsExiting) {
                s.Gm.SetVisibility(true, true);
                if (s.Committed) {
                    s.Gm.m_ChoicesMade = s.Offer.Visit.Hires;
                    if (s.Offer.Visit.Event.NextChoices(s.Offer.Visit.Hires)) s.Gm.GoToNextSetOfChoices();
                    else { s.Gm.m_IsExiting = true; s.Gm.Exit(); }
                } else s.Gm.SelectDefaultItem();
            }
        }
        if (s.Legacy != null && currentVisit) {
            SetLegacyVisible(s.Legacy, true);
            if (s.Committed) {
                if (s.Offer.Visit.Event.NextChoices(s.Offer.Visit.Hires)) { s.Legacy.ClearChoicesButtons(); s.Legacy.InitializeChoicesButtons(s.Offer.Visit.Event.eventChoices); }
                else s.Legacy.HideMenu();
            }
        }
        if (!s.Destroyed && s.Parent != null && s.Campaign?.runData?.Pointer == s.Run && !s.Parent.IsHiding) {
            // Parent owns canonical TeamData; refreshing renders the committed
            // active goalie and its binding, without reapplying authored config.
            s.Parent.Team.m_G = s.Campaign.runData.teamData.goalie;
            // A native refresh while the subview is open can deactivate the
            // bench object. LoadCurrentTeam's visible branch only repopulates
            // it; restore both prior policy and visibility before rebuilding.
            if (s.ParentPolicyCaptured) {
                s.Parent.m_HideBench = s.ParentHideBench;
                s.Parent.m_BenchPlayersList.gameObject.SetActive(s.ParentBenchVisible);
            }
            s.Parent.Team.UpdateTemporaryTeamData();
            s.Parent.LoadCurrentTeam(); s.Parent.SelectFirstPlayer();
        }
        if (s.Preview != null) UnityEngine.Object.Destroy(s.Preview);
        if (s.Active != null) UnityEngine.Object.Destroy(s.Active);
        if (s.Reserve != null) UnityEngine.Object.Destroy(s.Reserve);
    }
    private static void SetLegacyVisible(EventChoicePanel panel, bool visible)
    { if (panel._canvasGroup != null) { panel._canvasGroup.alpha = visible ? 1 : 0; panel._canvasGroup.interactable = visible; panel._canvasGroup.blocksRaycasts = visible; } }
    private static void Fail(Selection s, Exception ex)
    {
        Plugin.Log.LogError("[GoalieRoster] Menu preparation failed: " + ex);
        if (s.Menu != null && !s.Menu.IsHiding) s.Menu.TryLeaveMenu(false);
        Finish(s);
    }
}
