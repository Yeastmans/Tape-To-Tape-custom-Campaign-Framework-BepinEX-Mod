using System;
using System.Linq;
using Data;
using HarmonyLib;
using I2.Loc;
using Il2CppInterop.Runtime;
using Rogue;
using Rogue.NPC.BlademasterNPC;
using State;
using STS.Map;

namespace EndlessMode;

// Native OldBlademasterEvent loops the requested ability count, even when the
// repository returns a shorter list. Custom pools deliberately never fill that
// gap with unchecked abilities. Preserve the native player selection, card data
// and callback while bounding the list reads by the actual returned count.
[HarmonyPatch(typeof(BlademasterEvent), "OldBlademasterEvent")]
internal static class CustomBlademasterPool
{
    [HarmonyPrefix]
    internal static bool Prefix(BlademasterEvent __instance, CampaignState __0)
    {
        // InitializeUpgradeChoices calls this fallback when fewer than three
        // upgrades exist, even for squads which disable new ability offers.
        // Leave any legitimate upgrades already in the native list intact.
        if (SquadBehaviorRuntime.TryEnabled(__0?.runData?.squadId, "no_ability_offers", out bool disabled) && disabled)
        {
            Plugin.Log.LogInfo($"[RewardPool] Blademaster new abilities disabled; retaining {__instance.eventChoices?.Count ?? 0} existing upgrade choices");
            return false;
        }
        var pool = CustomRewardPools.Read("Abilities");
        if (pool == null) return true;
        try
        {
            var eligible = new System.Collections.Generic.List<ForwardData>();
            foreach (var player in __instance.GetPlayersEligibleForNewAbility(__0)) eligible.Add(player);
            // Native uses OrderBy(Guid.NewGuid).Take(3), not the run's RNG.
            var players = eligible.OrderBy(_ => Guid.NewGuid()).Take(3).ToArray();
            if (players.Length == 0 || __instance.eventChoices.Count >= 3) return false;
            var excluded = new Il2CppSystem.Collections.Generic.List<Ability>();
            int requested = 4 - players.Length;
            int before = __instance.eventChoices.Count;
            foreach (var player in players)
            {
                var abilities = CustomRewardPools.SelectAbilities(pool, false, requested, excluded, true);
                if (abilities == null) continue;
                excluded.AddRange(abilities.Cast<Il2CppSystem.Collections.Generic.IEnumerable<Ability>>());
                for (int i = 0; i < abilities.Count && __instance.eventChoices.Count < 3; i++)
                {
                    var ability = abilities[i];
                    if (ability == null) continue;
                    AddChoice(__instance, CreateChoice(player, ability));
                }
                if (__instance.eventChoices.Count >= 3) break;
            }
            Plugin.Log.LogInfo($"[RewardPool] Blademaster custom abilities: {__instance.eventChoices.Count - before} new choices, {__instance.eventChoices.Count} total; no base-game fallback");
        }
        catch (Exception ex)
        {
            // Existing upgrades and previously completed cards stay valid;
            // CreateChoice finishes before Add, so there is no partial card.
            Plugin.Log.LogError($"[RewardPool] Blademaster custom ability choices failed; retaining {__instance.eventChoices?.Count ?? 0} complete choices without native fallback: " + ex);
        }
        return false;
    }

    internal static EventChoice CreateChoice(ForwardData player, Ability ability)
    {
        string name = ability.localizedPowerupName;
        return new EventChoice
        {
            translatedButtonName = UtilsMono.SafeStringFormatter.SafeFormat(
                LocalizationManager.GetTermTranslation("npc/blademaster/new_ability_card_title"),
                new Il2CppSystem.Object[] { name, player.FullName }),
            onSelection = (Action)(() => player.ability = ability),
            interactable = true,
            nodeType = NodeType.Blademaster,
            choicesTermParams = new Il2CppSystem.Collections.Generic.List<EventTermParam>(),
            ChoiceType = player.ability == null ? EEventChoiceType.PowerUp : EEventChoiceType.PowerUpSwap,
            Skater = player,
            PowerUpOrRelic = ability,
            PowerUpOrRelic2 = player.ability,
            CustomLocalizedTexts = new[] { name }
        };
    }

    internal static unsafe void AddChoice(BlademasterEvent node, EventChoice choice)
    {
        // EventChoice is a native value type represented by a managed wrapper.
        // This interop build's generic List<T>.Add passes the boxed object's
        // header, shifting every card field. Invoke the same native Add with
        // the unboxed payload; IL2CPP still owns copying and GC write barriers.
        var choices = node.eventChoices;
        IntPtr method = IL2CPP.il2cpp_class_get_method_from_name(
            Il2CppClassPointerStore<Il2CppSystem.Collections.Generic.List<EventChoice>>.NativeClassPtr, "Add", 1);
        if (method == IntPtr.Zero) throw new MissingMethodException("List<EventChoice>.Add");
        void** arguments = stackalloc void*[1];
        arguments[0] = (void*)IL2CPP.il2cpp_object_unbox(choice.Pointer);
        IntPtr exception = IntPtr.Zero;
        IL2CPP.il2cpp_runtime_invoke(method, choices.Pointer, arguments, ref exception);
        Il2CppException.RaiseExceptionIfNecessary(exception);
        GC.KeepAlive(choice);
        GC.KeepAlive(choices);
    }
}
