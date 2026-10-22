using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CampaignCore;
using HarmonyLib;
namespace EndlessMode;

// One startup owner for persistence, roster controls, signing, launch and completion.
// No runtime capability is published by the individual resolve-only adapters.
internal static class GoalieLifecycleActivation
{
    private const string Owner = "EndlessMode.GoalieLifecycle";
    private sealed class Hook
    {
        internal MethodInfo Native, Prefix, Postfix, Finalizer;
        internal bool Completion, Launch;
    }
    internal static void Install()
    {
        if (GoaliePersistenceBridge.Enabled) return;
        var harmony = new Harmony(Owner);
        var plan = new List<Hook>();
        try {
            GoalieActivationTransaction.Run(
                () => {
                    if (!FreshRunActivation.Installed) throw new InvalidOperationException("Verified fresh run boundary is unavailable.");
                    var profile = GoaliePersistenceContracts.Preflight();
                    foreach (var set in new[] { GoalieRosterMenu.Required, GoalieMatchLaunchGate.Required, GoalieMatchCompletion.Required })
                        NamedManagedHookContract.RequireExactSet(set, profile.NamedHooks, set[0].Feature);
                    foreach (var h in GoaliePersistenceContracts.Resolve()) plan.Add(new Hook { Native=h.Native, Prefix=h.Prefix, Postfix=h.Postfix });
                    foreach (var h in GoalieRosterMenu.Resolve()) plan.Add(new Hook { Native=h.Native, Prefix=h.Prefix, Postfix=h.Postfix });
                    foreach (var h in GoalieMatchLaunchGate.Resolve()) plan.Add(new Hook { Native=h.Native, Prefix=h.Prefix, Postfix=h.Postfix, Launch=true });
                    foreach (var h in GoalieMatchCompletion.Resolve()) plan.Add(new Hook { Native=h.Native, Prefix=h.Prefix, Finalizer=h.Finalizer, Completion=true });
                    if (plan.Count != 37 || plan.Select(h => h.Native).Distinct().Count() != 37)
                        throw new InvalidOperationException("Incomplete goalie lifecycle installation plan.");
                    // Reject pre-existing foreign patches on provenance targets; a
                    // later foreign skip remains unknown through __runOriginal.
                    foreach (var h in plan.Where(h => h.Completion || h.Launch)) {
                        var existing = Harmony.GetPatchInfo(h.Native);
                        if (existing != null && existing.Owners.Any(o => o != "com.mods.customcampaign" && o != Owner))
                            throw new InvalidOperationException("Foreign goalie lifecycle patch: " + h.Native);
                    }
                    var departure = plan.Single(h => h.Completion && h.Native.Name == "HandleFreeAgentsLeaving").Native;
                    var retention = typeof(CustomRewardContractRetention).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)
                        ?? throw new MissingMethodException("Custom retention decision observer.");
                    var departurePatches = Harmony.GetPatchInfo(departure);
                    if (departurePatches == null || departurePatches.Prefixes.Count(p => p.owner == "com.mods.customcampaign"
                        && p.PatchMethod == retention && p.priority < Priority.First) != 1)
                        throw new InvalidOperationException("The reviewed custom retention observer must follow the goalie departure prefix.");
                    MatchRuleRunner.Install();
                },
                GoalieMatchCompletion.Subscribe,
                () => {
                    foreach (var h in plan) {
                        HarmonyMethod Method(MethodInfo method, int priority) => method == null ? null : new HarmonyMethod(method) { priority = priority };
                        harmony.Patch(h.Native, Method(h.Prefix, h.Completion || h.Launch ? Priority.First : Priority.Normal),
                            Method(h.Postfix, h.Launch ? Priority.Last : Priority.Normal),
                            finalizer: Method(h.Finalizer, Priority.First));
                    }
                },
                () => {
                    foreach (var h in plan) {
                        var patches = Harmony.GetPatchInfo(h.Native) ?? throw new InvalidOperationException("Missing installed goalie target.");
                        void Require(MethodInfo method, IEnumerable<Patch> installed, int priority) {
                            if (method == null) return;
                            if (installed.Count(p => p.owner == Owner && p.PatchMethod == method && p.priority == priority) != 1)
                                throw new InvalidOperationException("Incomplete goalie patch: " + method);
                        }
                        Require(h.Prefix, patches.Prefixes, h.Completion || h.Launch ? Priority.First : Priority.Normal);
                        Require(h.Postfix, patches.Postfixes, h.Launch ? Priority.Last : Priority.Normal);
                        Require(h.Finalizer, patches.Finalizers, Priority.First);
                        if (h.Completion && patches.Finalizers.Any(p => p.owner != Owner && p.priority >= Priority.First))
                            throw new InvalidOperationException("Goalie finalizer exception observation order is not exclusive.");
                        if (h.Completion && patches.Prefixes.Any(p => p.owner != Owner && p.priority >= Priority.First))
                            throw new InvalidOperationException("Goalie prefix observation order is not exclusive.");
                    }
                },
                () => { GoalieRosterMenu.SetInstalled(true); GoaliePersistenceBridge.SetEnabled(true); },
                () => { GoaliePersistenceBridge.SetEnabled(false); GoalieRosterMenu.SetInstalled(false); },
                GoalieMatchCompletion.Unsubscribe,
                harmony.UnpatchSelf);
            Plugin.Log.LogInfo("[GoalieLifecycle] All 37 targets installed; persistence, menu, launch and completion enabled together.");
        } catch (Exception ex) { Plugin.Log.LogWarning("[GoalieLifecycle] Entire capability unavailable: " + ex); }
    }
}
