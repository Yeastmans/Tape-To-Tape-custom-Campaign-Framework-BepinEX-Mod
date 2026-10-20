using System;
using System.Collections.Generic;
using Data;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Rogue;
using Rogue.NPC.GeneralManagerNPC;
using STS.Map;

namespace EndlessMode;

// Native contracts compact a five-player roster and native GM hires only write
// indices 0..4. Double Decker instead resolves both lines by player ID. Keep the
// native contract lifecycle and generated offers, but reconcile those two views.
internal static class TwoLineContracts
{
    internal static bool Applies(TeamData team)
        => UsesTwoLines(team) && (SquadBehaviorRuntime.Enabled(Tape2Tape.CampaignUtil.CampaignState.runData.squadId, "gm_contracts", false)
            || Tape2Tape.CampaignUtil.CampaignState.runData.freeAgents?.Count > 0 || HasVacancy(team));

    internal static bool UsesTwoLines(TeamData team)
    {
        var run = Tape2Tape.CampaignUtil.CampaignState?.runData;
        return team != null && run?.teamData != null && team.Pointer == run.teamData.Pointer
            && team.lines?.Count == 2
            && SquadBehaviorRuntime.Enabled(run.squadId, "two_lines", false);
    }

    private static bool HasVacancy(TeamData team)
    {
        for (int slot = 0; slot < 10; slot++)
        {
            string id = SlotId(team, slot);
            bool present = false;
            if (team.forwards != null) foreach (var player in team.forwards)
                if (player != null && player.id == id) { present = true; break; }
            if (!present) return true;
        }
        return false;
    }

    internal static string SlotId(TeamData team, int slot)
    {
        var line = team.lines[slot / 5];
        return (slot % 5) switch { 0 => line.leftWinger, 1 => line.rightWinger,
            2 => line.center, 3 => line.leftDefensemen, _ => line.rightDefensemen };
    }

    internal static void SetSlot(TeamData team, int slot, string id)
    {
        var line = team.lines[slot / 5];
        switch (slot % 5)
        {
            case 0: line.leftWinger = id; break;
            case 1: line.rightWinger = id; break;
            case 2: line.center = id; break;
            case 3: line.leftDefensemen = id; break;
            case 4: line.rightDefensemen = id; break;
        }
    }

    internal static void Reconcile(TeamData team)
    {
        if (team?.lines?.Count != 2) return;
        if (team.forwards == null) team.forwards = new Il2CppSystem.Collections.Generic.List<ForwardData>();
        var available = new Dictionary<string, ForwardData>(StringComparer.Ordinal);
        var order = new List<ForwardData>();
        foreach (var player in team.forwards)
            if (player != null && !string.IsNullOrEmpty(player.id) && available.TryAdd(player.id, player)) order.Add(player);
        var assigned = new HashSet<string>(StringComparer.Ordinal);
        var slots = new ForwardData[10];
        // IDs, not the compacted native indices, preserve surviving positions.
        for (int i = 0; i < 10; i++)
            if (available.TryGetValue(SlotId(team, i) ?? "", out var player) && assigned.Add(player.id)) slots[i] = player;
        // Recover recruits already added by the old GM callback without an ID
        // in either line. Never recreate an expired player from a stale line ID.
        foreach (var player in order)
        {
            if (assigned.Contains(player.id)) continue;
            int vacant = Array.FindIndex(slots, p => p == null);
            if (vacant < 0) break;
            slots[vacant] = player; assigned.Add(player.id);
        }
        var roster = new Il2CppSystem.Collections.Generic.List<ForwardData>();
        for (int i = 0; i < 10; i++) { roster.Add(slots[i]); SetSlot(team, i, slots[i]?.id ?? ""); }
        // Preserve unusual extra reserves from older saves; reconciliation must
        // not silently delete owned players, talents or authored data.
        foreach (var player in order) if (!assigned.Contains(player.id)) roster.Add(player);
        team.forwards = roster;
    }

    internal static int Vacancy(TeamData team)
    {
        for (int i = 0; i < 10; i++) if (team.forwards[i] == null) return i;
        return -1;
    }

    internal sealed class Offer
    {
        internal EventChoice Card;
        internal ForwardData Player;
        internal Il2CppSystem.Action NativeSelection;
        internal bool Taken;
    }

    internal sealed class Visit
    {
        internal GeneralManagerEvent Node;
        internal TeamData Team;
        internal IntPtr Run;
        internal int Hires, Revision, OfferRevision;
        internal bool AdvancePending;
        internal Offer Pending;
        internal List<EventChoice> Offers = new();
        internal EventChoicePanel LegacyPanel;
    }

    internal sealed class Placement
    {
        internal TeamData Team;
        internal ForwardData Player;
        internal int Slot;
        internal bool Applied;
    }

    internal static Visit Current;
    [ThreadStatic] internal static TeamData Generating;
    [ThreadStatic] internal static Placement Inserting;

    internal static Visit GetVisit(GeneralManagerEvent node)
    {
        if (GoalieRecruitment.IsGoalieNode) return null;
        var run = Tape2Tape.CampaignUtil.CampaignState?.runData;
        if (!Applies(run?.teamData)) return null;
        if (Current == null || Current.Node.Pointer != node.Pointer || Current.Run != run.Pointer
            || Current.Team.Pointer != run.teamData.Pointer)
            Current = new Visit { Node = node, Team = run.teamData, Run = run.Pointer };
        return Current;
    }

    internal static bool IsCurrent(Visit visit) => visit != null && ReferenceEquals(Current, visit)
        && Tape2Tape.CampaignUtil.CampaignState?.runData?.Pointer == visit.Run && Applies(visit.Team);

    internal static void WrapOffers(Visit visit)
    {
        visit.Revision++;
        int revision = ++visit.OfferRevision;
        visit.Offers.Clear();
        foreach (var card in visit.Node.eventChoices)
        {
            var player = card.Skater?.TryCast<ForwardData>();
            if (player != null && card.onSelection != null)
            {
                var offer = new Offer { Card = card, Player = player, NativeSelection = card.onSelection };
                card.interactable = true;
                card.onSelection = (Action)(() =>
                {
                    if (!IsCurrent(visit) || visit.OfferRevision != revision || offer.Taken || visit.Pending != null) return;
                    Reconcile(visit.Team);
                    visit.Pending = offer; visit.Revision++; visit.AdvancePending = true;
                });
                // Every hire needs an explicit destination, including vacancies.
                card.SuppressRewardTracking = true;
            }
            visit.Offers.Add(card);
        }
        ReplaceChoices(visit.Node, visit.Offers);
    }

    internal static void CancelSelection(Visit visit)
    {
        if (!IsCurrent(visit)) return;
        visit.Pending = null; visit.AdvancePending = false; visit.Revision++;
    }

    internal static void Commit(Visit visit, Offer offer, int slot)
    {
        if (!IsCurrent(visit) || visit.Pending != offer || offer.Taken || slot < 0 || slot >= 10) return;
        var team = visit.Team;
        Reconcile(team);
        foreach (var owned in team.forwards)
            if (owned != null && owned.id == offer.Player.id) return;
        var run = Tape2Tape.CampaignUtil.CampaignState.runData;
        var oldRoster = team.forwards;
        var outgoing = oldRoster[slot];
        string oldId = SlotId(team, slot);
        var previous = Inserting;
        var placement = new Placement { Team = team, Player = offer.Player, Slot = slot };
        try
        {
            // All native recruit stats, talents, contract conditions and their
            // registration remain in the original callback. Only insertion is
            // redirected to the chosen slot on either line.
            Inserting = placement;
            offer.NativeSelection.Invoke();
            if (!placement.Applied) throw new InvalidOperationException("GM recruit did not reach the two-line placement hook.");
        }
        catch (Exception ex)
        {
            team.forwards = oldRoster; SetSlot(team, slot, oldId);
            RemoveContract(offer.Player.id);
            Plugin.Log.LogError("[TwoLineContracts] Recruitment rolled back: " + ex);
            return;
        }
        finally { Inserting = previous; }
        if (outgoing != null)
        {
            RemoveContract(outgoing.id);
            run.forwardInjuries?.Remove(outgoing.id);
        }
        offer.Taken = true; visit.Hires++; visit.Pending = null; visit.Offers.Clear(); visit.Revision++; visit.OfferRevision++;
        Plugin.Log.LogInfo($"[TwoLineContracts] Signed {offer.Player.id} into line {slot / 5 + 1}, slot {slot % 5}; replaced {outgoing?.id ?? "vacancy"}");
    }

    private static void RemoveContract(string id)
    {
        var agents = Tape2Tape.CampaignUtil.CampaignState?.runData?.freeAgents;
        if (agents == null) return;
        for (int i = agents.Count - 1; i >= 0; i--) if (agents[i]?.id == id) agents.RemoveAt(i);
    }

    internal static bool Insert(TeamData team, ForwardData player)
    {
        var placement = Inserting;
        if (placement == null || team?.Pointer != placement.Team.Pointer || player?.Pointer != placement.Player.Pointer) return false;
        if (placement.Applied) return true;
        var roster = new Il2CppSystem.Collections.Generic.List<ForwardData>();
        foreach (var existing in team.forwards) roster.Add(existing);
        // The outgoing jersey is available after replacement.
        roster[placement.Slot] = null;
        team.EnsureUniqueJerseyNumber(roster.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<ForwardData>>(), player);
        roster[placement.Slot] = player;
        team.forwards = roster; SetSlot(team, placement.Slot, player.id); placement.Applied = true;
        return true;
    }

    internal static unsafe void ReplaceChoices(EventNode node, List<EventChoice> cards)
    {
        // EventChoice is a boxed native value type. Generic interop Add copies
        // the object header in this interop build; invoke with the unboxed data.
        IntPtr method = IL2CPP.il2cpp_class_get_method_from_name(
            Il2CppClassPointerStore<Il2CppSystem.Collections.Generic.List<EventChoice>>.NativeClassPtr, "Add", 1);
        if (method == IntPtr.Zero) throw new MissingMethodException("List<EventChoice>.Add");
        var list = new Il2CppSystem.Collections.Generic.List<EventChoice>();
        void** args = stackalloc void*[1];
        foreach (var card in cards)
        {
            args[0] = (void*)IL2CPP.il2cpp_object_unbox(card.Pointer);
            IntPtr error = IntPtr.Zero;
            IL2CPP.il2cpp_runtime_invoke(method, list.Pointer, args, ref error);
            Il2CppException.RaiseExceptionIfNecessary(error);
            GC.KeepAlive(card);
        }
        node.eventChoices = list;
    }
}

[HarmonyPatch(typeof(Rogue.FreeAgents.FreeAgentManager), "HandleFreeAgentsLeaving")]
internal static class TwoLineContractExpiry
{
    [HarmonyPostfix] internal static void Postfix()
    {
        var team = Tape2Tape.CampaignUtil.CampaignState?.runData?.teamData;
        // The final custom contract may just have been removed, so the roster
        // shape (not the remaining contract count) controls expiry repair.
        if (TwoLineContracts.UsesTwoLines(team)) TwoLineContracts.Reconcile(team);
    }
}

[HarmonyPatch(typeof(State.DeserializerV2), "DeserializeFromModel")]
internal static class TwoLineContractLoadedRoster
{
    [HarmonyPostfix] internal static void Postfix(RunData __0)
    {
        // Deserialize may finish before CampaignState publishes this RunData.
        // Use its saved squad ID; never touch unrelated deserialized teams.
        if (__0?.teamData?.lines?.Count == 2
            && SquadBehaviorRuntime.Enabled(__0.squadId, "two_lines", false)
            && SquadBehaviorRuntime.Enabled(__0.squadId, "gm_contracts", false))
            TwoLineContracts.Reconcile(__0.teamData);
    }
}

[HarmonyPatch(typeof(GeneralManagerEvent), "Initialize")]
internal static class TwoLineContractVisitStart
{
    [HarmonyPrefix] internal static void Prefix() => TwoLineContracts.Current = null;
}

[HarmonyPatch(typeof(GeneralManagerEvent), "NextChoices")]
internal static class TwoLineContractChoices
{
    internal sealed class Scope { internal TeamData Previous; internal TwoLineContracts.Visit Visit; internal bool Generate; }
    [HarmonyPrefix] internal static bool Prefix(GeneralManagerEvent __instance, ref int __0, ref bool __result, out Scope __state)
    {
        __state = new Scope { Previous = TwoLineContracts.Generating };
        var visit = TwoLineContracts.GetVisit(__instance);
        if (visit == null) return true;
        __state.Visit = visit;
        visit.AdvancePending = false;
        TwoLineContracts.Reconcile(visit.Team);
        if (visit.Pending != null)
        {
            // A roster selector owns this choice until it commits or cancels.
            // Never generate new offers or consume signings while it is open.
            TwoLineContracts.ReplaceChoices(__instance, visit.Offers); __result = true; return false;
        }
        // Menu navigation does not consume a GM signing. Preserve Initialize's
        // native negative count for zero-choice nodes, otherwise count hires.
        __0 = visit.Hires == 0 && __0 < 0 ? __0 : visit.Hires;
        __state.Generate = true; TwoLineContracts.Generating = visit.Team;
        return true;
    }
    [HarmonyPostfix] internal static void Postfix(bool __result, Scope __state)
    {
        if (__result && __state?.Generate == true)
        {
            TwoLineContracts.WrapOffers(__state.Visit);
        }
    }
    [HarmonyFinalizer] internal static void Finalizer(Scope __state)
    { if (__state != null) TwoLineContracts.Generating = __state.Previous; }
}

[HarmonyPatch(typeof(TeamData), "get_NumPlayingForwards")]
internal static class TwoLineContractOfferRoom
{
    [HarmonyPrefix] internal static bool Prefix(TeamData __instance, ref int __result)
    {
        if (TwoLineContracts.Generating?.Pointer != __instance.Pointer) return true;
        __result = 0; return false; // Scoped only to native offer generation.
    }
}

[HarmonyPatch(typeof(TeamData), "AddForwardToActiveLine")]
internal static class TwoLineContractPlacement
{
    [HarmonyPrefix] internal static bool Prefix(TeamData __instance, ForwardData __0) => !TwoLineContracts.Insert(__instance, __0);
}

// The legacy panel closes after every button. Hand its pending recruit to the
// same ten-position selector instead; only a completed hire advances the node.
[HarmonyPatch(typeof(EventChoicePanel), "ShowMenu")]
internal static class TwoLineContractLegacyPanel
{
    [HarmonyPostfix] internal static void Postfix(EventChoicePanel __instance, EventNode __1)
    {
        var visit = TwoLineContracts.Current;
        if (TwoLineContracts.IsCurrent(visit) && visit.Node.Pointer == __1?.Pointer) visit.LegacyPanel = __instance;
    }
}

[HarmonyPatch(typeof(EventChoicePanel), "HideMenu")]
internal static class TwoLineContractLegacyAdvance
{
    [HarmonyPrefix] internal static bool Prefix(EventChoicePanel __instance)
    {
        var visit = TwoLineContracts.Current;
        if (!TwoLineContracts.IsCurrent(visit) || visit.LegacyPanel?.Pointer != __instance.Pointer) return true;
        if (TwoLineContractRoster.Current?.Legacy?.Pointer == __instance.Pointer) return false;
        // An external close/skip without a button selection must remain a close.
        if (!visit.AdvancePending || visit.Pending == null) { visit.LegacyPanel = null; return true; }
        TwoLineContractRoster.Open(visit, null, __instance);
        return false;
    }
}
