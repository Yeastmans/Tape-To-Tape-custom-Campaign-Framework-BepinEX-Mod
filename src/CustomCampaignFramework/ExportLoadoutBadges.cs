using System;
using System.Collections.Generic;
using HarmonyLib;
using Tape2Tape.Customization.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EndlessMode;

// A presentation marker, never a replacement talent/ability/relic asset.
internal static class ExportLoadoutBadges
{
    private const string BadgeName = "CampaignStudioCustomBadge";
    private static readonly Dictionary<IntPtr, bool> ExportedLists = new();
    private static readonly Dictionary<IntPtr, bool> GoalieLists = new();

    private static void PlayerInit(Component list, CustomizeSkaterData data)
    {
        ExportedLists[list.Pointer] = PlayNowTranslation.ReadPlayer(data?.SkaterData) != null;
        GoalieLists[list.Pointer] = data != null && data.IsGoalie;
    }
    public static void TalentInit(SkaterCustomizationTalentList __instance, CustomizeSkaterData skaterData) => PlayerInit(__instance, skaterData);
    public static void AbilityInit(SkaterCustomizationAbilityList __instance, CustomizeSkaterData skaterData) => PlayerInit(__instance, skaterData);
    public static void RelicInit(TeamCustomizationArtifactsList __instance, CustomizeTeamData teamData)
        => ExportedLists[__instance.Pointer] = teamData != null && PlayNowTranslation.Read(teamData.id, true) != null;

    private static bool Missing(Component list, string kind, string id)
    {
        if (list == null || !ExportedLists.TryGetValue(list.Pointer, out bool exported) || !exported) return false;
        bool goalie = GoalieLists.TryGetValue(list.Pointer, out bool value) && value;
        return NativeCustomizationOptions.Missing(kind, goalie, id);
    }
    public static void TalentRefresh(SkaterCustomizationTalentButton __instance, Rogue.Talent talent)
        => Update(__instance, talent != null && Missing(__instance.GetComponentInParent<SkaterCustomizationTalentList>(), "talent", talent.id));
    public static void AbilityRefresh(SkaterCustomizationAbilityButton __instance, Rogue.Ability ability)
        => Update(__instance, ability != null && Missing(__instance.GetComponentInParent<SkaterCustomizationAbilityList>(), "ability", ability.id));
    public static void RelicRefresh(TeamCustomizationArtifactButton __instance, Rogue.Relic relic)
        => Update(__instance, relic != null && Missing(__instance.GetComponentInParent<TeamCustomizationArtifactsList>(), "relic", relic.id));

    private static void Update(Component owner, bool visible)
    {
        try
        {
            var existing = owner.transform.Find(BadgeName);
            if (existing != null) { existing.gameObject.SetActive(visible); return; }
            if (!visible) return;
            var badge = new GameObject(BadgeName);
            var rect = badge.AddComponent<RectTransform>();
            rect.SetParent(owner.transform, false);
            rect.anchorMin = new Vector2(.05f, .02f); rect.anchorMax = new Vector2(.95f, .25f);
            rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
            var backing = badge.AddComponent<Image>();
            backing.color = new Color(.07f, .23f, .21f, .97f); backing.raycastTarget = false;
            var caption = new GameObject("Caption");
            var captionRect = caption.AddComponent<RectTransform>(); captionRect.SetParent(rect, false);
            captionRect.anchorMin = Vector2.zero; captionRect.anchorMax = Vector2.one;
            captionRect.offsetMin = new Vector2(2, 0); captionRect.offsetMax = new Vector2(-2, 0);
            var text = caption.AddComponent<TextMeshProUGUI>();
            text.font = TMP_Settings.defaultFontAsset;
            text.text = "CUSTOM"; text.fontStyle = FontStyles.Bold;
            text.color = new Color(.44f, .94f, .75f, 1);
            text.alignment = TextAlignmentOptions.Center;
            text.enableAutoSizing = true; text.fontSizeMin = 6; text.fontSizeMax = 15;
            text.raycastTarget = false;
            rect.SetAsLastSibling();
        }
        catch (Exception ex) { Plugin.Log.LogWarning("[PlayNow] Custom loadout badge: " + ex.Message); }
    }

    private static void Patch(Harmony harmony, Type owner, string name, Type argument, string callback, bool prefix)
    {
        try
        {
            // Init() is also inherited from SkaterTeamSubList. Name-only lookup can
            // select that shared base stub instead of Init(CustomizeSkaterData).
            var method = owner.GetMethod(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly,
                null, new[] { argument }, null);
            if (method == null || method.DeclaringType != owner)
                throw new MissingMethodException(owner.FullName, name + "(" + argument.Name + ")");
            var patch = new HarmonyMethod(typeof(ExportLoadoutBadges), callback);
            harmony.Patch(method, prefix: prefix ? patch : null, postfix: prefix ? null : patch);
        }
        catch (Exception ex)
        {
            // A cosmetic badge must never disable the export runtime or plugin.
            Plugin.Log.LogWarning("[PlayNow] Custom badge hook unavailable for " + owner.Name + "." + name + ": " + ex.Message);
        }
    }

    internal static void Install(Harmony harmony)
    {
        Patch(harmony, typeof(SkaterCustomizationTalentList), "Init", typeof(CustomizeSkaterData), nameof(TalentInit), true);
        Patch(harmony, typeof(SkaterCustomizationAbilityList), "Init", typeof(CustomizeSkaterData), nameof(AbilityInit), true);
        Patch(harmony, typeof(TeamCustomizationArtifactsList), "Init", typeof(CustomizeTeamData), nameof(RelicInit), true);
        Patch(harmony, typeof(SkaterCustomizationTalentButton), "Refresh", typeof(Rogue.Talent), nameof(TalentRefresh), false);
        Patch(harmony, typeof(SkaterCustomizationAbilityButton), "Refresh", typeof(Rogue.Ability), nameof(AbilityRefresh), false);
        Patch(harmony, typeof(TeamCustomizationArtifactButton), "RefreshRelic", typeof(Rogue.Relic), nameof(RelicRefresh), false);
    }
}
