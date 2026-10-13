using System;
using System.Collections.Generic;
using HarmonyLib;
using Tape2Tape.Hockey.UI;
using UnityEngine;
using UnityEngine.UI;
using System.IO;
using CampaignCore;

namespace EndlessMode;

/// <summary>
/// Keep the game's native tile click/selection wiring, but put campaign squads
/// on their own page. The first custom tile doubles as the page entry while
/// closed; it is never allowed to select a squad in that state.
/// </summary>
internal static class CustomSquadMenu
{
    private static ChooseMetaMenu menu;
    private static readonly List<MetaTeamItem> custom = new();
    private static readonly List<MetaTeamItem> vanilla = new();
    private static readonly Dictionary<int, bool> originallyActive = new();
    private static bool open;
    private static bool customOnly;
    private static int gatewayInputFrame = -1;
    private static bool openingGateway;
    private static string title;
    private static TMPro.TextMeshProUGUI entryLabel;
    private static RectMask2D gatewayMask;
    private static Image gatewayBackground;
    private static readonly List<Graphic> gatewayGraphics = new();
    private static readonly List<Animator> gatewayAnimators = new();
    private static Vector3 pageOrigin;
    private static Vector2 pageCell;
    private static bool gridWasEnabled;
    private static Sprite cardSprite;
    private static readonly List<GameObject> zamboniLabels = new();
    private static Vector3 nativeScale;
    private static RectTransform customPage;
    private static GridLayoutGroup customGrid;
    private static Transform cardParent;
    private static BishopGames.UI.AbstractNavigable nativeDefaultNavigable;
    private static readonly List<GameObject> gatewayHidden = new();

    internal static void Install(Harmony harmony)
    {
        CampaignSquadSetup.Install(harmony);
        Patch(harmony, "OnMetaClicked", nameof(Click));
        Patch(harmony, "ConfirmSelections", nameof(Confirm));
        Patch(harmony, "SetState", nameof(ChangeState));
        harmony.Patch(AccessTools.Method(typeof(ChooseMetaMenu), "SetState"),
            postfix: new HarmonyMethod(typeof(CustomSquadMenu), nameof(StateChanged)));
        var highlight = AccessTools.Method(typeof(ChooseMetaMenu), "SetHighlightedMeta");
        if (highlight == null) throw new MissingMethodException(typeof(ChooseMetaMenu).FullName, "SetHighlightedMeta");
        harmony.Patch(highlight, prefix: new HarmonyMethod(typeof(CustomSquadMenu), nameof(BeforeHighlight)),
            postfix: new HarmonyMethod(typeof(CustomSquadMenu), nameof(Highlighted)));
        Patch(harmony, "BackOut", nameof(Back));
        Patch(harmony, "IsValidMetaSelection", nameof(Validate));
        // Keep page tiles registered when the native navigation grid rebuilds.
        try
        {
            harmony.Patch(AccessTools.Method(typeof(BishopGames.UI.NavigableParent), "RegisterChildren", new[] { typeof(bool) }),
                postfix: new HarmonyMethod(typeof(CustomSquadMenu), nameof(ParentRegistered)));
        }
        catch (Exception ex) { Plugin.Log.LogError("[CustomSquadMenu] Navigation hooks unavailable: " + ex.Message); }
    }
    private static bool registeringPage;
    private static bool IsPageGrid(BishopGames.UI.NavigableParent parent)
        => open && menu != null && parent != null && menu.m_MetaTeamsParent != null && parent.Pointer == menu.m_MetaTeamsParent.Pointer;
    private static MetaTeamItem TileFor(BishopGames.UI.AbstractNavigable nav)
    {
        if (nav == null) return null;
        foreach (var item in custom) if (item != null && item.Navigable != null && item.Navigable.Pointer == nav.Pointer) return item;
        return null;
    }
    public static void ParentRegistered(BishopGames.UI.NavigableParent __instance)
    {
        if (registeringPage || !IsPageGrid(__instance)) return;
        try { RegisterPageTiles(); }
        catch (Exception ex) { Plugin.Log.LogWarning("[CustomSquadMenu] Re-register page tiles: " + ex.Message); }
    }
    private static void RegisterPageTiles()
    {
        var parent = menu.m_MetaTeamsParent;
        registeringPage = true;
        try
        {
            foreach (var item in custom)
                if (item != null && item.Navigable != null && !parent.HasChild(item.Navigable)) parent.RegisterChild(item.Navigable, true);
            // The native neighbor search does not connect these nested page tiles.
            // Link them in layout order, using the page grid's column count.
            var navs = new List<BishopGames.UI.AbstractNavigable>();
            foreach (var item in custom) if (item != null && item.Navigable != null) navs.Add(item.Navigable);
            int columns = Math.Max(1, customGrid != null ? customGrid.constraintCount : navs.Count);
            for (int i = 0; i < navs.Count; i++)
            {
                var nav = navs[i];
                nav.SetNeighbor(BishopGames.UI.AbstractNavigable.EDirection.Right, i + 1 < navs.Count ? navs[i + 1] : null);
                nav.SetNeighbor(BishopGames.UI.AbstractNavigable.EDirection.Left, i > 0 ? navs[i - 1] : null);
                nav.SetNeighbor(BishopGames.UI.AbstractNavigable.EDirection.Down, i + columns < navs.Count ? navs[i + columns] : null);
                nav.SetNeighbor(BishopGames.UI.AbstractNavigable.EDirection.Up, i >= columns ? navs[i - columns] : null);
            }
        }
        finally { registeringPage = false; }
    }
    private static void Patch(Harmony h, string target, string method)
    {
        var original = AccessTools.Method(typeof(ChooseMetaMenu), target);
        if (original == null) throw new MissingMethodException(typeof(ChooseMetaMenu).FullName, target);
        h.Patch(original, prefix: new HarmonyMethod(typeof(CustomSquadMenu), method));
    }
    internal static void Build(ChooseMetaMenu instance)
    {
        RestoreSquadDetails();
        SetGatewayArt(false);
        if (cardParent != null) foreach (var item in custom) if (item != null) item.transform.SetParent(cardParent, false);
        if (customPage != null) UnityEngine.Object.Destroy(customPage.gameObject);
        if (menu != null && menu.m_MetaTeamsLayout != null) menu.m_MetaTeamsLayout.enabled = gridWasEnabled;
        RestoreDefaultBeforeBuild(instance);
        gatewayGraphics.Clear(); gatewayAnimators.Clear();
        menu = instance;
        nativeDefaultNavigable = menu.m_MetaTeamsParent?.m_DefaultNavigable;
        string campaignFile = Path.Combine(Plugin.ModFolder, "campaign.txt");
        customOnly = Plugin.PlayerTeamsEnabled && File.Exists(campaignFile) && new CampaignDocument(File.ReadAllText(campaignFile)).Get("Custom Squads Only").Equals("yes", StringComparison.OrdinalIgnoreCase);
        custom.Clear(); vanilla.Clear(); originallyActive.Clear(); open = false;
        gatewayInputFrame = -1; openingGateway = false;
        if (menu.m_MetaItems == null) return;
        foreach (var item in menu.m_MetaItems)
        {
            if (item == null) continue;
            originallyActive[item.GetInstanceID()] = item.gameObject.activeSelf;
            string id = item.CurrentSquad?.id ?? "";
            if (id.StartsWith("Custom_", StringComparison.Ordinal)
                && Plugin.PlayerTeamConfigs.ContainsKey(id.Substring(7))) custom.Add(item);
            else if (!id.StartsWith("Custom_", StringComparison.Ordinal)) vanilla.Add(item);
            else item.gameObject.SetActive(false);
        }
        if (custom.Count == 0)
        {
            if (customOnly)
            {
                foreach (var item in vanilla) item.gameObject.SetActive(false);
                if (menu.m_MetaFinalTeamItem != null) menu.m_MetaFinalTeamItem.gameObject.SetActive(false);
                menu.m_LockedInMeta = null; menu.m_HighlightedMeta = null;
                if (menu.m_Title != null) menu.m_Title.text = "CUSTOM SQUADS";
                bool configured = Plugin.PlayerTeamConfigs.Count > 0;
                if (menu.m_MetaName != null) menu.m_MetaName.text = configured ? "Custom squads could not load" : "No custom squads yet";
                if (menu.m_MetaDescription != null) menu.m_MetaDescription.text = configured
                    ? "Your campaign squads were found, but could not be loaded. Check their settings in Campaign Studio and the game log for details."
                    : "Add a custom squad to this campaign in Campaign Studio, then reopen squad selection.";
                if (menu.m_MetaTeamsParent != null) menu.m_MetaTeamsParent.RegisterChildren(true);
            }
            return;
        }
        foreach (var item in custom) RemoveUnlockEffects(item);
        Canvas.ForceUpdateCanvases();
        var nativeGrid = menu.m_MetaTeamsLayout;
        gridWasEnabled = nativeGrid != null && nativeGrid.enabled;
        var nativeCell = vanilla.Count > 0 ? vanilla[0].GetComponent<RectTransform>() : custom[0].GetComponent<RectTransform>();
        pageCell = nativeCell.sizeDelta;
        nativeScale = nativeCell.localScale;
        pageOrigin = nativeCell.localPosition;
        cardParent = custom[0].transform.parent;
        var pageObject = new GameObject("CustomSquadsPage");
        customPage = pageObject.AddComponent<RectTransform>();
        pageObject.AddComponent<LayoutElement>().ignoreLayout = true;
        var originalRoot = cardParent.GetComponent<RectTransform>();
        // The native NavigableParent is on TeamsGrid itself, verified from the
        // prefab. Keep page descendants inside it so controller registration
        // finds the custom squad tiles; ignore the outer layout here.
        customPage.SetParent(cardParent, false);
        customPage.anchorMin = Vector2.zero; customPage.anchorMax = Vector2.one;
        customPage.pivot = originalRoot.pivot; customPage.sizeDelta = Vector2.zero;
        customPage.anchoredPosition = Vector2.zero; customPage.localScale = Vector3.one;
        customGrid = pageObject.AddComponent<GridLayoutGroup>();
        if (nativeGrid != null)
        {
            customGrid.cellSize = nativeGrid.cellSize;
            customGrid.spacing = nativeGrid.spacing;
            customGrid.startCorner = nativeGrid.startCorner;
            customGrid.startAxis = nativeGrid.startAxis;
            customGrid.childAlignment = nativeGrid.childAlignment;
            customGrid.constraint = nativeGrid.constraint;
            customGrid.constraintCount = nativeGrid.constraintCount;
            customGrid.padding = new RectOffset(nativeGrid.padding.left, nativeGrid.padding.right,
                nativeGrid.padding.top, nativeGrid.padding.bottom);
            // Keep the first occupied slot at the native top-left position even
            // when this page has fewer rows than the centered base-game grid.
            var columns = new HashSet<int>();
            foreach (var item in vanilla)
                if (item.transform.parent == cardParent && item != menu.m_MetaFinalTeamItem)
                    columns.Add(Mathf.RoundToInt(item.transform.localPosition.x));
            customGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            customGrid.constraintCount = Math.Max(1, columns.Count);
            customGrid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            customGrid.startAxis = GridLayoutGroup.Axis.Horizontal;
            customGrid.childAlignment = TextAnchor.UpperLeft;
            customGrid.padding = new RectOffset(
                Mathf.RoundToInt(pageOrigin.x - originalRoot.rect.xMin - nativeGrid.cellSize.x * nativeCell.pivot.x), 0,
                Mathf.RoundToInt(originalRoot.rect.yMax - pageOrigin.y - nativeGrid.cellSize.y * (1 - nativeCell.pivot.y)), 0);
        }
        var pageGroup = pageObject.AddComponent<CanvasGroup>();
        pageGroup.alpha = 1; pageGroup.interactable = true; pageGroup.blocksRaycasts = true;
        pageGroup.ignoreParentGroups = true;
        pageObject.SetActive(false);
        foreach (var graphic in custom[0].GetComponentsInChildren<Graphic>(true))
            if (graphic.enabled) gatewayGraphics.Add(graphic);
        foreach (var animator in custom[0].GetComponentsInChildren<Animator>(true))
            if (animator.enabled) gatewayAnimators.Add(animator);
        if (gatewayBackground != null) UnityEngine.Object.Destroy(gatewayBackground.gameObject);
        gatewayBackground = MakeCard("CustomSquadsBackground", custom[0].transform);
        var backgroundRect = gatewayBackground.rectTransform;
        backgroundRect.anchorMin = Vector2.zero; backgroundRect.anchorMax = Vector2.one;
        backgroundRect.sizeDelta = Vector2.zero;
        if (gatewayMask != null) UnityEngine.Object.Destroy(gatewayMask);
        // Native squad cards have animated shine graphics which extend outside
        // their bounds. The separately positioned gateway must contain that art.
        gatewayMask = custom[0].gameObject.AddComponent<RectMask2D>();
        title = menu.m_Title?.text ?? "";
        if (entryLabel != null) UnityEngine.Object.Destroy(entryLabel.gameObject);
        var labelObject = new GameObject("CustomSquadsLabel");
        labelObject.transform.SetParent(custom[0].transform, false);
        entryLabel = labelObject.AddComponent<TMPro.TextMeshProUGUI>();
        if (menu.m_Title != null) entryLabel.font = menu.m_Title.font;
        entryLabel.text = "CUSTOM\nSQUADS";
        entryLabel.fontSize = 17;
        entryLabel.enableAutoSizing = true;
        entryLabel.fontSizeMin = 10;
        entryLabel.fontSizeMax = 17;
        entryLabel.alignment = TMPro.TextAlignmentOptions.Center;
        entryLabel.color = Color.white;
        entryLabel.raycastTarget = false;
        var labelRect = entryLabel.GetComponent<RectTransform>();
        // The gateway is text-only; the squad art belongs on the second page.
        labelRect.anchorMin = new Vector2(.06f, .06f); labelRect.anchorMax = new Vector2(.94f, .94f);
        labelRect.pivot = new Vector2(.5f, .5f); labelRect.anchoredPosition = Vector2.zero;
        labelRect.sizeDelta = Vector2.zero;
        if (customOnly) OpenGateway(); else Show(false);
        Plugin.Log.LogInfo($"[CustomSquadMenu] {custom.Count} campaign squads moved into Custom Squads menu");
    }
    private static bool IsEntry(BishopGames.UI.AbstractNavigable nav)
        => custom.Count > 0 && nav != null && custom[0].Navigable == nav;

    public static bool Click(ChooseMetaMenu __instance, BishopGames.UI.AbstractNavigable __0)
    {
        if (__instance == menu && customOnly && !custom.Exists(item => item != null && item.Navigable == __0)) return false;
        if (__instance == menu && ConsumingGatewayInput) return false;
        if (__instance != menu || custom.Count == 0 || open || !IsEntry(__0)) return true;
        OpenGateway();
        return false;
    }
    public static bool Confirm(ChooseMetaMenu __instance, ref bool __result)
    {
        if (__instance == menu && customOnly && !custom.Contains(menu.CurrentMeta)) { __result = false; return false; }
        if (__instance == menu && ConsumingGatewayInput) { __result = false; return false; }
        // Controller/keyboard confirmation bypasses OnMetaClicked entirely.
        if (__instance != menu || open || custom.Count == 0
            || menu.m_CurrentState != ChooseMetaMenu.EState.SelectMeta || menu.CurrentMeta != custom[0]) return true;
        OpenGateway();
        __result = false; // opening the browser must not advance to difficulty
        return false;
    }
    public static bool ChangeState(ChooseMetaMenu __instance, ChooseMetaMenu.EState __0)
    {
        if (__instance == menu && customOnly && menu.m_CurrentState == ChooseMetaMenu.EState.SelectMeta
            && __0 == ChooseMetaMenu.EState.SelectDifficulty && !custom.Contains(menu.CurrentMeta)) return false;
        if (__instance == menu && ConsumingGatewayInput
            && menu.m_CurrentState == ChooseMetaMenu.EState.SelectMeta && __0 != ChooseMetaMenu.EState.SelectMeta) return false;
        if (__instance != menu || open || custom.Count == 0 || menu.CurrentMeta != custom[0]
            || menu.m_CurrentState != ChooseMetaMenu.EState.SelectMeta || __0 == ChooseMetaMenu.EState.SelectMeta) return true;
        Plugin.Log.LogInfo($"[CustomSquadMenu] Intercepted gateway transition to {__0}; opening custom page");
        OpenGateway();
        return false;
    }
    private static bool ConsumingGatewayInput => openingGateway || gatewayInputFrame == Time.frameCount;
    private static void OpenGateway()
    {
        // Stamp before any native callbacks. A single mouse/controller gesture
        // can call Click, Confirm and SetState in the same frame; changing open
        // alone previously let the later callback advance the first real squad.
        gatewayInputFrame = Time.frameCount;
        openingGateway = true;
        try
        {
            // Use the game's release latch for a held confirm action. Only
            // latch inputs currently down, so the next fresh press is retained.
            if (menu.m_StartRunInput != null)
                foreach (var input in menu.m_StartRunInput)
                    if (input != null && input.m_IsButtonPressed) input.m_IsExpectingButtonRelease = true;
            Show(true);
            menu.m_LockedInMeta = null;
            custom[0].SetToggled(false);
            menu.m_HighlightedMeta = null;
            menu.SetHighlightedMeta(custom[0]);
            SelectTile(custom[0]);
            Plugin.Log.LogInfo($"[CustomSquadMenu] Gateway input consumed at frame {gatewayInputFrame}; waiting for a fresh squad selection");
        }
        finally { openingGateway = false; }
    }
    public static void StateChanged(ChooseMetaMenu __instance)
    {
        if (__instance != menu || customPage == null) return;
        // Native difficulty/lineup transitions hide the original grid. Our
        // independent page must follow the same state instead of lingering.
        bool visible = open && menu.m_CurrentState == ChooseMetaMenu.EState.SelectMeta;
        bool wasVisible = customPage.gameObject.activeSelf;
        customPage.gameObject.SetActive(visible);
        if (visible && !wasVisible && custom.Count > 0)
        {
            // Disabling the page resets native Animator state. Refresh after
            // reactivation so the existing Custom_* unlock postfix restores
            // the portrait instead of leaving the native locked silhouette.
            RefreshPageTiles();
            SelectTile(custom.Contains(menu.CurrentMeta) ? menu.CurrentMeta : custom[0]);
        }
    }
    private static void RefreshPageTiles()
    {
        foreach (var item in custom)
            if (item != null && item.CurrentSquad != null)
                item.Refresh(item.CurrentSquad, ProfileData.Instance.GetSquadTeamData(item.CurrentSquad.id));
    }
    private static void SelectTile(MetaTeamItem item)
    {
        if (item?.Navigable == null) return;
        // SetHighlightedMeta changes the details panel, not keyboard focus.
        // Native SetupMetas has already queued its previous/default tile; our
        // page must choose an active child after registration and layout.
        if (open && menu.m_MetaTeamsParent != null)
            menu.m_MetaTeamsParent.m_DefaultNavigable = item.Navigable;
        BishopGames.UI.AbstractNavigableUtils.SelectOnNextFrame(item.Navigable);
    }
    private static void RestoreDefaultBeforeBuild(ChooseMetaMenu instance)
    {
        if (menu == null || menu.m_MetaTeamsParent == null) return;
        // A repeated Build may still own the default. Native SetupMetas can
        // also have supplied a fresh one; retain that new native selection.
        if (menu != instance || TileFor(menu.m_MetaTeamsParent.m_DefaultNavigable) != null)
            menu.m_MetaTeamsParent.m_DefaultNavigable = nativeDefaultNavigable;
    }
    public static void Highlighted(ChooseMetaMenu __instance, MetaTeamItem __0)
    {
        if (__instance != menu || custom.Count == 0 || __0 == null) return;
        if (open)
        {
            string id = __0.CurrentSquad?.id ?? "";
            if (!Plugin.CustomSquadText.TryGetValue(id, out var squad)) return;
            if (menu.m_MetaName != null) menu.m_MetaName.text = squad.name;
            if (menu.m_MetaDescription != null)
                menu.m_MetaDescription.text = string.IsNullOrWhiteSpace(squad.desc)
                    ? $"Play as {squad.name} in {Plugin.ActiveCampaign}.\n\nSelect this custom squad to start your run with its campaign roster."
                    : squad.desc;
            return;
        }
        if (__0 != custom[0]) return;
        // This is a navigation card, not the first squad's roster or rewards.
        ShowGatewayZamboni();
        if (menu.m_RelicsLayout != null)
        {
            var root = menu.m_RelicsLayout.transform;
            for (int i = 0; i < root.childCount; i++) HideForGateway(root.GetChild(i).gameObject);
        }
        if (menu.m_ViewRelicText != null) HideForGateway(menu.m_ViewRelicText);
        if (menu.m_MetaName != null) menu.m_MetaName.text = "Custom Squad";
        if (menu.m_MetaDescription != null)
            menu.m_MetaDescription.text = $"Choose a custom squad for \"{Plugin.ActiveCampaign}\"";
    }
    public static void BeforeHighlight(ChooseMetaMenu __instance)
    {
        if (__instance == menu) RestoreSquadDetails();
    }
    private static void HideForGateway(GameObject item)
    {
        if (!item.activeSelf) return;
        gatewayHidden.Add(item);
        item.SetActive(false);
    }
    private static void RestoreSquadDetails()
    {
        foreach (var label in zamboniLabels) if (label != null) { label.SetActive(false); UnityEngine.Object.Destroy(label); }
        zamboniLabels.Clear();
        foreach (var item in gatewayHidden) if (item != null) item.SetActive(true);
        gatewayHidden.Clear();
    }
    public static bool Validate(ChooseMetaMenu __instance, MetaTeamItem __0, ref bool __result)
    {
        if (__instance == menu && customOnly && !custom.Contains(__0)) { __result = false; return false; }
        if (__instance != menu || open || custom.Count == 0 || __0 != custom[0]) return true;
        __result = false;
        return false;
    }
    public static bool Back(ChooseMetaMenu __instance)
    {
        // Only intercept the squad-grid page, not Back from difficulty/lineup.
        if (__instance != menu || !open || customOnly || menu.m_CurrentState != ChooseMetaMenu.EState.SelectMeta) return true;
        Show(false);
        menu.m_HighlightedMeta = null;
        menu.SetHighlightedMeta(custom[0]);
        SelectTile(custom[0]);
        return false;
    }
    private static void Show(bool showCustom)
    {
        RestoreSquadDetails();
        open = showCustom;
        if (!open && menu.m_MetaTeamsParent != null)
            menu.m_MetaTeamsParent.m_DefaultNavigable = nativeDefaultNavigable;
        if (customPage != null) customPage.gameObject.SetActive(open);
        foreach (var item in vanilla) item.gameObject.SetActive(!open && originallyActive[item.GetInstanceID()]);
        for (int i = 0; i < custom.Count; i++)
        {
            var item = custom[i];
            item.transform.SetParent(open ? customPage : cardParent, false);
            item.transform.SetSiblingIndex(i);
            var itemLayout = item.GetComponent<LayoutElement>();
            if (itemLayout != null) itemLayout.ignoreLayout = !open && i == 0;
            item.gameObject.SetActive(open || i == 0);
            if (item.Navigable != null) item.Navigable.IsNavigable = open || i == 0;
        }
        var first = custom[0];
        if (gatewayMask != null) gatewayMask.enabled = false;
        var layout = first.GetComponent<LayoutElement>() ?? first.gameObject.AddComponent<LayoutElement>();
        layout.ignoreLayout = !open;
        if (open) RefreshPageTiles();
        else first.Refresh(first.CurrentSquad, ProfileData.Instance.GetSquadTeamData(first.CurrentSquad.id));
        if (first.m_KeyPlayerHead != null)
            first.m_KeyPlayerHead.enabled = open;
        if (entryLabel != null) entryLabel.gameObject.SetActive(!open);
        if (!open)
        {
            if (first.m_MetaName != null) first.m_MetaName.text = ""; // use the in-card caption only
            first.SetToggled(false);
        }
        SetGatewayArt(!open);
        if (menu.m_MetaFinalTeamItem != null)
            menu.m_MetaFinalTeamItem.gameObject.SetActive(!open && (!originallyActive.TryGetValue(menu.m_MetaFinalTeamItem.GetInstanceID(), out var wasActive) || wasActive));
        if (menu.m_Title != null) menu.m_Title.text = open ? "CUSTOM SQUADS — " + Plugin.ActiveCampaign : title;
        Canvas.ForceUpdateCanvases();
        var grid = menu.m_MetaTeamsLayout;
        if (grid != null)
        {
            grid.enabled = open ? false : gridWasEnabled;
            if (open)
            {
                for (int i = 0; i < custom.Count; i++)
                {
                    var rt = custom[i].GetComponent<RectTransform>();
                    rt.localScale = nativeScale;
                }
                LayoutRebuilder.ForceRebuildLayoutImmediate(customPage);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(grid.GetComponent<RectTransform>());
            if (!open)
            {
                // Measure native cells after layout; place below the leftmost
                // column, never beside/under the separately positioned Gauntlet.
                float left = float.PositiveInfinity, bottom = float.PositiveInfinity;
                MetaTeamItem anchor = null;
                foreach (var item in vanilla)
                {
                    if (item == menu.m_MetaFinalTeamItem || !item.gameObject.activeInHierarchy) continue;
                    var rt = item.GetComponent<RectTransform>();
                    if (rt == null || rt.parent != first.transform.parent) continue;
                    if (rt.localPosition.x < left - 1) { left = rt.localPosition.x; bottom = rt.localPosition.y; anchor = item; }
                    else if (Mathf.Abs(rt.localPosition.x - left) < 1 && rt.localPosition.y < bottom)
                    { bottom = rt.localPosition.y; anchor = item; }
                }
                if (anchor != null)
                {
                    var rt = first.GetComponent<RectTransform>();
                    var source = anchor.GetComponent<RectTransform>();
                    rt.sizeDelta = source.sizeDelta;
                    rt.localScale = source.localScale;
                    rt.localPosition = new Vector3(source.localPosition.x, bottom - grid.cellSize.y - grid.spacing.y, source.localPosition.z);
                }
            }
        }
        if (menu.m_MetaTeamsParent != null)
        {
            registeringPage = true;
            try { menu.m_MetaTeamsParent.RegisterChildren(true); }
            finally { registeringPage = false; }
        }
        // TeamsGrid (NavigableParentGrid) registers only its direct children: the prefab has
        // m_RegisterChildrenDeeply = 0. The page's tiles sit one level down, so without this only
        // the programmatically highlighted first squad was reachable. Neighbors are found by
        // position among the registered siblings.
        if (open && menu.m_MetaTeamsParent != null) RegisterPageTiles();
    }
    private static void SetGatewayArt(bool gateway)
    {
        // Keep native card art and hover; replace only its player portrait.
        foreach (var animator in gatewayAnimators) if (animator != null) animator.enabled = true;
        foreach (var graphic in gatewayGraphics) if (graphic != null) graphic.enabled = true;
        if (custom.Count > 0 && custom[0] != null && custom[0].m_KeyPlayerHead != null)
        {
            custom[0].m_KeyPlayerHead.enabled = !gateway;
            custom[0].m_KeyPlayerHead.gameObject.SetActive(!gateway);
        }
        if (gatewayBackground != null) gatewayBackground.gameObject.SetActive(false);
    }
    private static void RemoveUnlockEffects(MetaTeamItem item)
    {
        // Verified in the native MetaTeamInfo prefab: these root-level reveal
        // effects extend 3–5x beyond the card. Hover lives under Container and
        // is deliberately retained. Custom cards have no unlock ceremony.
        foreach (string path in new[] { "screenmask", "twirl (1)", "twirl (2)", "vfx-reward", "Container/PlayerHead/rtrt" })
        {
            var effect = item.transform.Find(path);
            if (effect == null) continue;
            effect.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(effect.gameObject);
        }
    }
    private static void ShowGatewayZamboni()
    {
        var zamboni = menu.m_Zamboni;
        if (zamboni == null) return;
        // Leave the vehicle and its native presentation visible.
        if (zamboni.m_ZamboniSkaterHeadUI != null) ReplaceZamboniImage(zamboni.m_ZamboniSkaterHeadUI);
        if (zamboni.m_TeamLogosUI != null)
            foreach (var logo in zamboni.m_TeamLogosUI) if (logo != null) ReplaceZamboniImage(logo);
        if (zamboni.m_ZamboniSkaterHeadSprite != null) HideForGateway(zamboni.m_ZamboniSkaterHeadSprite.gameObject);
        if (zamboni.m_TeamLogosSprite != null)
            foreach (var logo in zamboni.m_TeamLogosSprite) if (logo != null) HideForGateway(logo.gameObject);
    }
    private static void ReplaceZamboniImage(Image image)
    {
        var source = image.rectTransform;
        var obj = new GameObject("CustomSquadZamboniText");
        obj.transform.SetParent(source.parent, false);
        var label = obj.AddComponent<TMPro.TextMeshProUGUI>();
        label.font = entryLabel.font;
        label.text = "CUSTOM\nSQUAD";
        label.alignment = TMPro.TextAlignmentOptions.Center;
        label.color = Color.white;
        label.enableAutoSizing = true; label.fontSizeMin = 6; label.fontSizeMax = 22;
        label.raycastTarget = false;
        var rect = label.rectTransform;
        rect.anchorMin = source.anchorMin; rect.anchorMax = source.anchorMax;
        rect.pivot = source.pivot; rect.sizeDelta = source.sizeDelta;
        rect.localPosition = source.localPosition; rect.localRotation = source.localRotation; rect.localScale = source.localScale;
        zamboniLabels.Add(obj);
        HideForGateway(image.gameObject);
    }
    private static Image MakeCard(string name, Transform parent)
    {
        if (cardSprite == null)
        {
            var texture = new Texture2D(100, 120, TextureFormat.RGBA32, false);
            for (int y = 0; y < 120; y++) for (int x = 0; x < 100; x++)
            {
                float bottom = Mathf.Abs(x - 49.5f) * .5f;
                bool inside = x >= 2 && x <= 97 && y >= bottom && y <= 117;
                bool border = x < 6 || x > 93 || y < bottom + 4 || y > 113;
                texture.SetPixel(x, y, !inside ? Color.clear : border ? Color.black : new Color(.17f, .49f, .70f, 1));
            }
            texture.Apply();
            cardSprite = Sprite.Create(texture, new Rect(0, 0, 100, 120), new Vector2(.5f, .5f), 100);
        }
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        var image = obj.AddComponent<Image>();
        image.sprite = cardSprite;
        return image;
    }
}

