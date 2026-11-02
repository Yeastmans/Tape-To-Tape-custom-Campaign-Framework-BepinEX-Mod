using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using Data;
using HarmonyLib;
using Tape2Tape.Hockey.UI;
using Pool = Il2CppSystem.Collections.Generic.List<Data.ForwardData>;
using Scope = EndlessMode.BaseGamePoolSelectionRuntime.Scope;

namespace EndlessMode;

internal static partial class BaseGamePoolsRuntime
{
    private sealed class EmptyStarMenu
    {
        internal Pool Result;
        internal bool Continued;
    }
    private sealed class EmptyStarGrant
    {
        internal TeamData Team;
        internal ForwardData Existing;
        internal IntPtr Menu;
        internal EmptyStarMenu Entry;
        internal bool Skipped;
    }
    [ThreadStatic] private static EmptyStarGrant emptyStarGrant;
    private static readonly Dictionary<IntPtr, EmptyStarMenu> emptyStarMenus = new();

    private static void InstallEmptyStars(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(OldSquadMenu), "PopulateSuperStars"),
            prefix: new HarmonyMethod(typeof(BaseGamePoolsRuntime), nameof(PopulateStarsPrefix)));
        Patch(harmony, typeof(OldSquadMenu), "ApplyCurrentSelection", nameof(ApplySelectionPrefix), nameof(ApplySelectionFinalizer));
        harmony.Patch(AccessTools.Method(typeof(OldSquadMenu), "InitData"),
            prefix: new HarmonyMethod(typeof(BaseGamePoolsRuntime), nameof(ResetEmptyStarMenu)));
        harmony.Patch(AccessTools.Method(typeof(OldSquadMenu), "BehaviourDestroy"),
            prefix: new HarmonyMethod(typeof(BaseGamePoolsRuntime), nameof(ResetEmptyStarMenu)));
        foreach (string method in new[] { "AddForwardToActiveLine", "AddForwardToBench" })
            harmony.Patch(AccessTools.Method(typeof(TeamData), method),
                prefix: new HarmonyMethod(typeof(BaseGamePoolsRuntime), nameof(AddForwardPrefix)));
    }

    // Settings admit new results in StarsPrefix. Once admitted, a menu's native
    // choices must finish even if its settings change during the deferred stages.
    private static bool EmptyStars(OldSquadMenu menu) => Plugin.IsDefaultMode
        && menu?.m_SuperStarSkaters != null
        && emptyStarMenus.TryGetValue(menu.Pointer, out var entry) && entry.Result.Pointer == menu.m_SuperStarSkaters.Pointer
        && menu.m_SuperStarSkaters.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<ForwardData>>().Count == 0;

    private static void RememberEmptyStars(OldSquadMenu menu, Pool result)
        => emptyStarMenus[menu.Pointer] = new EmptyStarMenu { Result = result };

    internal static void ResetEmptyStarMenu(OldSquadMenu __instance) => emptyStarMenus.Remove(__instance.Pointer);

    private static void ForgetEmptyStars(OldSquadMenu menu, EmptyStarMenu entry)
    {
        if (emptyStarMenus.TryGetValue(menu.Pointer, out var current) && ReferenceEquals(current, entry))
            emptyStarMenus.Remove(menu.Pointer);
    }

    internal static bool PopulateStarsPrefix(OldSquadMenu __instance, ref Cysharp.Threading.Tasks.UniTask __result)
    {
        if (!EmptyStars(__instance)) return true;
        var entry = emptyStarMenus[__instance.Pointer];
        if (entry.Continued) { __result = Cysharp.Threading.Tasks.UniTask.CompletedTask; return false; }
        entry.Continued = true;
        __instance.m_CurrentSkaterChoice = -1;
        if ((int)__instance.m_Mode == 0)
        {
            // The native controller has only PowerUp -> SuperStar -> Relic.
            // Finish this caller before replaying the required star transition;
            // no candidate or selection UI is constructed for the empty stage.
            __result = Cysharp.Threading.Tasks.UniTask.CompletedTask;
            try { __instance.StartCoroutine(ContinueEmptyStarSequence(__instance, entry).WrapToIl2Cpp()); }
            catch { ForgetEmptyStars(__instance, entry); throw; }
            Plugin.Log.LogInfo("[BaseGamePools] No eligible superstar; scheduling native relic and starter choices after the star transition.");
        }
        else
        {
            // SuperStarOnly never generates the other reward stages.
            try { __instance.ApplyAndExit(); }
            finally { ForgetEmptyStars(__instance, entry); }
            __result = Cysharp.Threading.Tasks.UniTask.CompletedTask;
            Plugin.Log.LogInfo("[BaseGamePools] No eligible superstar; continuing without a recruit.");
        }
        return false;
    }

    private static bool ContinuingEmptyStars(OldSquadMenu menu, EmptyStarMenu entry)
        => menu != null && emptyStarMenus.TryGetValue(menu.Pointer, out var current) && ReferenceEquals(current, entry)
            && EmptyStars(menu) && !menu.m_IsExiting;

    private static IEnumerator ContinueEmptyStarSequence(OldSquadMenu menu, EmptyStarMenu entry)
    {
        bool handedOff = false;
        try
        {
            yield return null; // Let native InstantiateSuperStars finish its call.
            if (!ContinuingEmptyStars(menu, entry)) yield break;
            menu.CancelReveal();
            menu.m_StepAnimator.SetTrigger(OldSquadMenu.SUPER_STAR);
            float deadline = UnityEngine.Time.realtimeSinceStartup + 5f;
            yield return null; // Animator triggers are consumed by frame evaluation.
            while (ContinuingEmptyStars(menu, entry))
            {
                if (menu.m_StepAnimator.GetCurrentAnimatorStateInfo(0).IsName("SuperStar"))
                {
                    Plugin.Log.LogInfo("[BaseGamePools] Native SuperStar step observed; starting relic choices.");
                    Cysharp.Threading.Tasks.UniTaskExtensions.Forget(menu.InstantiateRelics());
                    handedOff = true;
                    yield break;
                }
                if (UnityEngine.Time.realtimeSinceStartup >= deadline)
                {
                    Plugin.Log.LogError("[BaseGamePools] Cannot continue starting rewards: native SuperStar step was not reached.");
                    yield break;
                }
                yield return null;
            }
        }
        finally { if (!handedOff) ForgetEmptyStars(menu, entry); }
    }

    internal static void ApplySelectionPrefix(OldSquadMenu __instance, out Scope __state)
    {
        __state = null;
        if (!EmptyStars(__instance) || (int)__instance.m_Mode == 1) return;
        // 26372689 validates relic, star and power-up choices before any grant.
        // Only after native relic/starter confirmation, borrow a forward for the
        // missing-star guard and suppress its one insertion. Native ability and
        // talent grants still resolve their own Angus recipient.
        ForwardData existing = null;
        foreach (var player in __instance.m_Team.GetForwards(false))
            if (player != null) { existing = player; break; }
        if (existing == null) throw new InvalidOperationException("Cannot preserve starting rewards without an existing team forward.");
        __state = new Scope();
        __state.Replace(() => emptyStarGrant, value => emptyStarGrant = value,
            new EmptyStarGrant { Team = __instance.m_Team, Existing = existing,
                Menu = __instance.Pointer, Entry = emptyStarMenus[__instance.Pointer] });
        var guard = new Pool(); guard.Add(existing);
        __state.Replace(() => __instance.m_SuperStarSkaters, value => __instance.m_SuperStarSkaters = value,
            guard.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<ForwardData>>());
        __state.Replace(() => __instance.m_CurrentSkaterChoice, value => __instance.m_CurrentSkaterChoice = value, 0);
    }

    internal static Exception ApplySelectionFinalizer(OldSquadMenu __instance, Scope __state, Exception __exception)
    {
        var grant = emptyStarGrant;
        try { return Restore(__state, __exception); }
        finally
        {
            if (__state != null && grant?.Menu == __instance.Pointer)
                ForgetEmptyStars(__instance, grant.Entry);
        }
    }

    internal static bool AddForwardPrefix(TeamData __instance, ForwardData __0)
    {
        var grant = emptyStarGrant;
        if (grant == null || grant.Skipped || __instance?.Pointer != grant.Team.Pointer || __0?.Pointer != grant.Existing.Pointer) return true;
        grant.Skipped = true;
        return false;
    }
}
