using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CampaignCore;
using Data;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using Rogue;
using Rogue.Powerups.Repository;
using Tape2Tape;
using NativeTalents = Il2CppSystem.Collections.Generic.List<Rogue.Talent>;

namespace EndlessMode;

// Synchronous recruitment scopes only. Native extra-roll probabilities, contract
// generation and fixed talents remain in the original methods. Stat scaling is
// independently optional for the specific template in a GM offer.
internal static class FreeAgentTalentWeightRuntime
{
    internal sealed class Scope : IDisposable
    {
        internal Scope Previous;
        internal string Campaign, Error, Purpose;
        internal FreeAgentTalentWeights Policy;
        internal FreeAgentTalentPool Pool;
        internal IntPtr State, Run;
        internal bool KeepStats;
        internal IntPtr StatsTemplate;
        internal int OfferCount, Speed, Accuracy, Power, Checking;
        internal Rogue.FreeAgents.PreGeneratedFreeAgentData StoredAgent;
        internal Il2CppSystem.Collections.Generic.List<string> StoredTalents;
        private bool disposed;
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try
            {
                if (StoredAgent != null) StoredAgent.talentIds = StoredTalents;
            }
            finally { current = Previous; }
        }

        internal void FilterStoredTalents(ForwardData template)
        {
            if (Campaign == null || template == null || Pool?.UseBaseGame == true) return;
            var agents = CampaignUtil.CampaignState?.runData?.preGeneratedFreeAgents;
            if (agents == null) return;
            var fixedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (template.powerups != null)
                foreach (var talent in template.powerups)
                    if (talent != null) fixedIds.Add(talent.id);
            foreach (var agent in agents)
            {
                if (agent == null || agent.templateFullName != template.FullName) continue;
                // CreatePlayerChoice inlines this lookup and loads stored IDs
                // without asking GetRandomTalents. Filter only this offer's
                // temporary view; never rewrite saved or already signed players.
                if (agent.talentIds == null) return;
                var filtered = new Il2CppSystem.Collections.Generic.List<string>();
                foreach (string id in agent.talentIds)
                    if (fixedIds.Contains(id) || Pool?.Selected.Contains(id) == true) filtered.Add(id);
                if (filtered.Count == agent.talentIds.Count) return;
                StoredAgent = agent;
                StoredTalents = agent.talentIds;
                agent.talentIds = filtered;
                return; // Match the native first-name-match lookup.
            }
        }
    }
    [ThreadStatic] private static Scope current;
    [ThreadStatic] private static bool readingNativeWeights;
    internal static bool ReadingNativeWeights => readingNativeWeights;
    internal static bool InRecruitmentScope => current?.Campaign != null && !Plugin.IsDefaultMode && !RewardPoolPicks.Probing;
    internal static bool KeepStats(ForwardData template, Scope scope)
    {
        if (!ReferenceEquals(current, scope) || !InRecruitmentScope || scope?.KeepStats != true || template == null
            || scope.StatsTemplate == IntPtr.Zero || scope.StatsTemplate != template.Pointer) return false;
        var state = CampaignUtil.CampaignState;
        return state != null && state.Pointer == scope.State && state.runData != null
            && state.runData.Pointer == scope.Run && !string.IsNullOrWhiteSpace(Plugin.ModFolder)
            && string.Equals(scope.Campaign, Path.GetFullPath(Plugin.ModFolder), StringComparison.OrdinalIgnoreCase);
    }
    internal static Scope Enter(string purpose)
    {
        var scope = new Scope { Previous = current, Purpose = purpose };
        current = scope; // A nested disabled/default campaign cannot inherit an outer policy.
        try
        {
            if (Plugin.IsDefaultMode || string.IsNullOrWhiteSpace(Plugin.ModFolder)) return scope;
            scope.Campaign = Path.GetFullPath(Plugin.ModFolder);
            var state = CampaignUtil.CampaignState;
            scope.State = state?.Pointer ?? IntPtr.Zero;
            scope.Run = state?.runData?.Pointer ?? IntPtr.Zero;
            string file = Path.Combine(scope.Campaign, "campaign.txt");
            var document = new CampaignDocument(File.Exists(file) ? File.ReadAllText(file) : "");
            scope.KeepStats = FreeAgentStats.Keep(document);
            scope.Pool = FreeAgentTalentPool.Read(document);
            string text = document.Get(FreeAgentTalentWeights.Key);
            if (!FreeAgentTalentWeights.TryParse(text, out var policy, out string error)) scope.Error = error;
            else scope.Policy = policy;
            if (scope.Error != null) Plugin.Log.LogError("[FreeAgentWeights] Invalid campaign settings; random additional talents disabled: " + scope.Error);
        }
        catch (Exception ex)
        {
            scope.Error = ex.Message;
            Plugin.Log.LogError("[FreeAgentWeights] Could not read recruitment weights; random additional talents disabled: " + ex);
        }
        return scope;
    }

    private static bool RecruitmentCategory(TalentCategory category) =>
        (category & (TalentCategory.Goalie | TalentCategory.UsedInCampaignGoaliePool | TalentCategory.Chaos)) == 0;

    // Default recruitment must also bypass the unrelated Regular reward allow-list.
    internal static bool UseNativePool(TalentCategory category) => !Plugin.IsDefaultMode && !RewardPoolPicks.Probing
        && current?.Policy?.IsDefault == true && current.Pool?.UseBaseGame == true && RecruitmentCategory(category);

    // Legacy imported player files can author their own random list. Template
    // construction precedes CreatePlayerChoice, so it needs a separate bounded
    // scope. Call only for configs from the recruitment pool, never the roster.
    internal static List<string> SelectTemplateTalents(TalentRepository repository, IEnumerable<string> authored,
        ForwardData forward, int requested)
    {
        var result = new List<string>();
        using var scope = Enter("random recruitment template talents");
        if (Plugin.IsDefaultMode || requested <= 0 || scope.Error != null || scope.Policy == null) return result;
        try
        {
            if (repository == null || forward == null) throw new InvalidOperationException("Recruitment template talent context is unavailable.");
            var candidates = new NativeTalents();
            foreach (string token in authored)
            {
                var talent = PatchBossLaunchMatch.FindTalent(token);
                if (talent == null || (talent.category & TalentCategory.Goalie) != 0) continue;
                if (scope.Pool.UseBaseGame ? NativeBaseOutcome(repository, talent) : scope.Pool.Selected.Contains(talent.id)) candidates.Add(talent);
            }
            var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddExcluded(repository, forward.powerups, excluded);
            AddExcluded(repository, CampaignUtil.Instance.GetExcludedTalentRewards(candidates.Cast<Il2CppSystem.Collections.Generic.IEnumerable<Talent>>()), excluded);
            int humans = repository._playersManager.Players.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<Player>>().Count;
            var eligible = new List<Talent>();
            foreach (var talent in candidates)
                if (!excluded.Contains(talent.id) && (humans <= 2 || (talent.category & TalentCategory.AIBehaviour) == 0)) eligible.Add(talent);
            var selected = scope.Policy.Select(eligible, requested, t => t.id, NativeWeightReader(forward), () => UnityEngine.Random.value);
            // These legacy entries already specify exact levels. Native added
            // talent upgrades still happen later in CreatePlayerChoice's picker.
            result.AddRange(selected.Select(t => t.id));
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError("[FreeAgentWeights] Random template selection failed without fallback: " + ex);
        }
        return result;
    }

    private static bool NativeBaseOutcome(TalentRepository repository, Talent talent)
    {
        var first = talent.level == 1 ? talent : repository.GetTalent(talent.GetIl2CppType(), 1, false);
        if (first == null || (first.category & TalentCategory.UsedInCampaignPool) == 0 || (first.category & TalentCategory.Goalie) != 0) return false;
        // Match the editor's native base choices plus reachable exact upgrades.
        // A missing intermediate level cannot be reached by TryUpgradeTalent.
        for (int level = 2; level <= talent.level; level++)
        {
            first = repository.GetTalent(talent.GetIl2CppType(), level, false);
            if (first == null) return false;
        }
        return string.Equals(first.id, talent.id, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool TrySelect(TalentRepository repository, TalentCategory category, NativeTalents excluded,
        bool clone, int requested, bool mustBeInPool, bool matchAnyCategory, ForwardData forward, out NativeTalents result)
    {
        result = null;
        var scope = current;
        if (scope == null || (scope.Policy == null && scope.Error == null) || Plugin.IsDefaultMode || RewardPoolPicks.Probing) return false;
        // This control cannot affect goalie/Chaos event requests nested inside unrelated callbacks.
        if (!RecruitmentCategory(category)) return false;
        result = new NativeTalents();
        try
        {
            var state = CampaignUtil.CampaignState;
            if (scope.Error != null || scope.Policy == null) return true;
            if (!string.Equals(scope.Campaign, Path.GetFullPath(Plugin.ModFolder), StringComparison.OrdinalIgnoreCase)
                || state == null || state.Pointer != scope.State || state.runData == null || state.runData.Pointer != scope.Run)
                throw new InvalidOperationException("Recruitment campaign/run changed inside the weight scope.");
            if (UseNativePool(category)) { result = null; return false; }
            if (requested <= 0) return true;

            var custom = scope.Pool.UseBaseGame ? null : scope.Pool;
            var native = new NativeTalents();
            if (custom == null)
            {
                // Use the native category predicates; do not infer catalog eligibility.
                var candidates = matchAnyCategory ? repository.GetTalentsMatchingAnyCategory(category) : repository.GetTalents(category);
                var enumerator = candidates.GetEnumerator();
                var cursor = enumerator.Cast<Il2CppSystem.Collections.IEnumerator>();
                try { while (cursor.MoveNext()) { var talent = enumerator.Current; if (talent != null) native.Add(talent); } }
                finally { enumerator.Cast<Il2CppSystem.IDisposable>().Dispose(); }
            }
            else
            {
                // Recruitment has its own selected IDs, independent of every reward pool.
                foreach (var talent in CustomRewardPools.Assets<Talent>(custom.Selected, t => t.id))
                    if ((talent.category & TalentCategory.Goalie) == 0
                        && ((category & TalentCategory.OnlyStats) == 0 || (talent.category & TalentCategory.OnlyStats) != 0)) native.Add(talent);
            }
            var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddExcluded(repository, excluded, skip);
            // Preserve current native unlock/run/squad eligibility, including conditional
            // CanBeRewarded checks. A multiplier must never unlock a selected entry.
            AddExcluded(repository, CampaignUtil.Instance.GetExcludedTalentRewards(native.Cast<Il2CppSystem.Collections.Generic.IEnumerable<Talent>>()), skip);
            int humans = repository._playersManager.Players.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<Player>>().Count;
            var eligible = new List<Talent>();
            foreach (var talent in native)
                if (!skip.Contains(talent.id)
                    && (humans <= 2 || (talent.category & TalentCategory.AIBehaviour) == 0)
                    && (custom != null || (talent.level == 1 && (!mustBeInPool || (talent.category & TalentCategory.UsedInCampaignPool) != 0))))
                    eligible.Add(talent);

            var weight = NativeWeightReader(forward);
            var evaluated = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            var chosen = scope.Policy.Select(eligible, requested, t => t.id,
                t => { float value = weight(t); evaluated[t.id] = value; return value; }, () => UnityEngine.Random.value);
            float upgradeChance = state.runData.CurrentAct == 2 ? 0.075f : state.runData.CurrentAct == 3 ? 0.15f : 0.025f;
            // Native TryUpgradeTalent executes the same recursive chance/level logic as
            // GetRandomTalents. Edited pools allow an upgrade only when its exact
            // resulting level is checked; otherwise keep the checked original.
            foreach (var selected in chosen)
            {
                bool upgrade = custom == null || (selected.level == 1 && (selected.category & TalentCategory.UsedInCampaignPool) != 0);
                var talent = upgrade ? (Talent)AccessTools.Method(typeof(TalentRepository), "TryUpgradeTalent")
                    .Invoke(repository, new object[] { selected, upgradeChance, false }) : selected;
                if (custom != null && (talent == null || !custom.Selected.Contains(talent.id))) talent = selected;
                result.Add(clone ? UnityEngine.Object.Instantiate(talent) : talent);
            }
            string details = string.Join(", ", chosen.Select(t => t.id + " native=" + evaluated[t.id].ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                + " multiplier=" + scope.Policy.Multiplier(t.id).ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
            Plugin.Log.LogInfo($"[FreeAgentWeights] {scope.Purpose}: {result.Count}/{requested} additional talents; {(custom == null ? "native" : "custom")} pool, no fallback; {details}");
        }
        catch (Exception ex)
        {
            result = new NativeTalents();
            Plugin.Log.LogError("[FreeAgentWeights] Additional talent selection failed without fallback: " + ex);
        }
        return true;
    }

    private static void AddExcluded(TalentRepository repository, NativeTalents talents, ISet<string> ids)
    {
        if (talents == null) return;
        foreach (var talent in talents) if (talent != null)
        {
            ids.Add(talent.id);
            if (talent.level > 1)
            {
                var first = repository.GetTalent(talent.GetIl2CppType(), 1, false);
                if (first != null) ids.Add(first.id);
            }
        }
    }

    private static Func<Talent, float> NativeWeightReader(ForwardData forward)
    {
        // Invoke the native closure rather than copying its Slapshot branch. Existing
        // custom squad comparison hooks and undiscovered/conditional weights still run.
        var matches = new List<MethodInfo>();
        foreach (var type in typeof(TalentRepository).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
        {
            var context = AccessTools.Property(type, "forwardData");
            // Il2CppInterop renames this native local function on the current game.
            // Bind the verified signature/context, never a guessed generated name.
            if (context == null || context.PropertyType != typeof(ForwardData) || !context.CanWrite
                || type.GetConstructor(Type.EmptyTypes) == null) continue;
            foreach (var methodInfo in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                if (methodInfo.ReturnType == typeof(float)
                    && methodInfo.GetParameters().Length == 1 && methodInfo.GetParameters()[0].ParameterType == typeof(Talent)) matches.Add(methodInfo);
        }
        if (matches.Count != 1) throw new MissingMethodException("Unique native talent-weight closure not found.");
        var method = matches[0];
        object closure = Activator.CreateInstance(method.DeclaringType);
        AccessTools.Property(method.DeclaringType, "forwardData").SetValue(closure, forward);
        return talent =>
        {
            bool previous = readingNativeWeights; readingNativeWeights = true;
            try { return (float)method.Invoke(closure, new object[] { talent }); }
            finally { readingNativeWeights = previous; }
        };
    }
}

[HarmonyPatch(typeof(Rogue.NPC.GeneralManagerNPC.GeneralManagerEvent), "CreatePlayerChoice")]
internal static class FreeAgentChoiceWeightScope
{
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static void Prefix(Rogue.NPC.GeneralManagerNPC.GeneralManagerEvent __instance, ForwardData __0,
        out FreeAgentTalentWeightRuntime.Scope __state)
    {
        __state = FreeAgentTalentWeightRuntime.Enter("recruit/contract replacement");
        __state.StatsTemplate = __0?.Pointer ?? IntPtr.Zero;
        __state.OfferCount = __instance.eventChoices?.Count ?? 0;
        if (__state.KeepStats && __0 != null)
        {
            __state.Speed = __0.speed;
            __state.Accuracy = __0.shotAccuracy;
            __state.Power = __0.shotPower;
            __state.Checking = __0.checking;
        }
        __state.FilterStoredTalents(__0);
    }
    [HarmonyPostfix]
    private static void Postfix(Rogue.NPC.GeneralManagerNPC.GeneralManagerEvent __instance, ForwardData __0,
        FreeAgentTalentWeightRuntime.Scope __state)
    {
        if (!FreeAgentTalentWeightRuntime.KeepStats(__0, __state)
            || __instance.eventChoices == null || __instance.eventChoices.Count != __state.OfferCount + 1) return;
        var player = __instance.eventChoices[__state.OfferCount]?.Skater?.TryCast<ForwardData>();
        if (player == null || player.Pointer == __state.StatsTemplate) return;
        // Complete the native offer first, then restore only its four base stats.
        // Detouring AdjustPlayerStatsToMatchTeam (which takes Nullable<float>)
        // crashes native GM calls on build 26001062, even with keep-stats off.
        player.speed = __state.Speed;
        player.shotAccuracy = __state.Accuracy;
        player.shotPower = __state.Power;
        player.checking = __state.Checking;
    }
    [HarmonyFinalizer]
    private static Exception Finalizer(FreeAgentTalentWeightRuntime.Scope __state, Exception __exception)
    { __state?.Dispose(); return __exception; }
}

[HarmonyPatch(typeof(State.CampaignState), "PreGenerateFreeAgents")]
internal static class FreeAgentContractWeightScope
{
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static void Prefix(out FreeAgentTalentWeightRuntime.Scope __state) => __state = FreeAgentTalentWeightRuntime.Enter("contract generation/waitlist");
    [HarmonyFinalizer]
    private static Exception Finalizer(FreeAgentTalentWeightRuntime.Scope __state, Exception __exception)
    { __state?.Dispose(); return __exception; }
}
