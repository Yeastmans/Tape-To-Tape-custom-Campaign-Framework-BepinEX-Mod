using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CampaignCore;

// Rule bookkeeping is separate from native ownership. A signal never grants an item.
public sealed class CustomRewardProgress
{
    public sealed class Effect
    {
        public string Reward = "", Owner = "", Recipient = "", Stat = "", Duration = "";
        public int Amount, Stacks;
        public double Expires;
    }
    private sealed class Counter { public int Events, Activations; }
    private readonly Dictionary<string, Counter> counts = new Dictionary<string, Counter>();
    private readonly Dictionary<string, Counter> campaignCounts = new();
    public List<Effect> Effects { get; } = new List<Effect>();
    private readonly Dictionary<string, Dictionary<string, long>> acquisitions = new Dictionary<string, Dictionary<string, long>>();
    private long acquisitionSequence;
    private readonly Dictionary<string, Dictionary<string, int>> previousMatches = new();
    private readonly Dictionary<string, Dictionary<string, int>> spentCharges = new();
    public int ChargesSpent(string owner, string reward) => spentCharges.TryGetValue(owner, out var rewards) && rewards.TryGetValue(reward, out int count) ? count : 0;
    public bool SpendCharge(string owner, string reward, int capacity) {
        int spent = ChargesSpent(owner, reward);
        if (capacity < 1 || capacity > 999999 || spent >= capacity) return false;
        if (!spentCharges.TryGetValue(owner, out var rewards)) spentCharges[owner] = rewards = new(StringComparer.OrdinalIgnoreCase);
        rewards[reward] = spent + 1; return true;
    }
    public int PreviousMatchCount(string owner, string reward) => previousMatches.TryGetValue(owner, out var rewards) && rewards.TryGetValue(reward, out int count) ? count : 0;
    public void SetPreviousMatchCount(string owner, string reward, int count) {
        if (count < 0 || count > 999999) throw new ArgumentOutOfRangeException(nameof(count));
        if (!previousMatches.TryGetValue(owner, out var rewards)) previousMatches[owner] = rewards = new(StringComparer.OrdinalIgnoreCase);
        rewards[reward] = count;
    }
    public void ObserveOwnership(string owner, IEnumerable<string> rewards) {
        bool initial = !acquisitions.TryGetValue(owner, out var owned);
        if (initial) acquisitions[owner] = owned = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var current = rewards.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var id in owned.Keys.Where(id => !current.Contains(id, StringComparer.OrdinalIgnoreCase)).ToArray()) {
            owned.Remove(id);
            if (campaignCounts.Remove(Key(id, owner))) Effects.RemoveAll(e => e.Reward == id && e.Owner == owner);
            if (previousMatches.TryGetValue(owner, out var previous)) previous.Remove(id);
            if (spentCharges.TryGetValue(owner, out var spent)) spent.Remove(id);
        }
        // Existing saves retain native loadout order; subsequently acquired
        // items receive a sequence across all owners. Removing and reacquiring
        // an item makes it new again.
        for (int i = 0; i < current.Length; i++) if (!owned.ContainsKey(current[i])) owned[current[i]] = initial ? i - (long)current.Length : ++acquisitionSequence;
    }
    public long Acquired(string owner, string reward) => acquisitions.TryGetValue(owner, out var owned) && owned.TryGetValue(reward, out long value) ? value : 0;
    private static string Key(string reward, string actor) => reward + "|" + actor;
    public bool ResetBonus(CustomRewardDefinition definition, string signal, string owner, string recipient)
        => definition.ResetOn == signal && Effects.RemoveAll(e => e.Reward == definition.Id && e.Owner == owner && (recipient == null || e.Recipient == recipient)) > 0;
    public bool Signal(CustomRewardDefinition definition, string signal, string actor, string owner, string recipient, double time, Func<bool> apply = null, Func<bool> roll = null)
    {
        if (CustomRewardTriggers.IsCondition(definition.Trigger) || definition.Trigger != signal) return false;
        string key = Key(definition.Id, actor);
        var counters = CustomRewardTriggers.IsCampaignEvent(signal) ? campaignCounts : counts;
        if (!counters.TryGetValue(key, out var count)) counters[key] = count = new Counter();
        if (count.Activations >= definition.ActivationsPerMatch) return false;
        count.Events++;
        if (count.Events < definition.Threshold) return false;
        count.Events = 0;
        if (definition.Chance < 100 && (roll == null || !roll())) return false;
        // Reserve before calling native code: effects can emit another event.
        count.Activations++;
        if (definition.Effect != CustomRewardEffects.StatChange) {
            bool applied = apply != null && apply();
            if (!applied) count.Activations--;
            return applied;
        }
        Effects.RemoveAll(e => e.Duration == "Seconds" && e.Expires <= time);
        var existing = Effects.FirstOrDefault(e => e.Reward == definition.Id && e.Owner == owner && e.Recipient == recipient && e.Stat == definition.Stat && e.Duration == definition.Duration);
        if (existing == null) {
            existing = new Effect { Reward = definition.Id, Owner = owner, Recipient = recipient, Stat = definition.Stat, Duration = definition.Duration, Amount = definition.Amount };
            Effects.Add(existing);
        }
        bool changed = existing.Stacks < definition.MaxStacks || (existing.Duration == "Seconds" && existing.Expires < time + definition.Seconds);
        existing.Stacks = Math.Min(definition.MaxStacks, existing.Stacks + 1);
        existing.Expires = time + definition.Seconds;
        return changed;
    }
    public static string ScopedRecipient(string player, string team) => team + "|" + player;
    public static bool MatchesRecipient(string target, string player, string team) => target == player || target == team || target == ScopedRecipient(player, team);
    public int Bonus(string recipient, string team, string stat, double time, Func<string, string, bool> stillOwned)
    {
        long total = 0;
        foreach (var effect in Effects)
            if (MatchesRecipient(effect.Recipient, recipient, team) && (effect.Stat == stat || effect.Stat == "Overall" && CustomRewardDefinition.IsOverallStat(stat))
                && (!effect.Owner.StartsWith("team:", StringComparison.Ordinal) || effect.Owner.Contains("|player:") || effect.Owner == team || effect.Recipient == ScopedRecipient(recipient, team))
                && (effect.Duration != "Seconds" || effect.Expires > time) && stillOwned(effect.Reward, effect.Owner))
                total += (long)effect.Amount * effect.Stacks;
        return (int)Math.Max(-999000, Math.Min(999000, total));
    }
    public void EndPeriod() => Effects.RemoveAll(e => e.Duration == "Rest of period");
    public void BeginMatch() { counts.Clear(); Effects.RemoveAll(e => e.Duration != "Rest of run"); }
    public void Clear() { counts.Clear(); campaignCounts.Clear(); Effects.Clear(); acquisitions.Clear(); previousMatches.Clear(); spentCharges.Clear(); acquisitionSequence = 0; }
    private static string Encode(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
    private static string Decode(string text) => Encoding.UTF8.GetString(Convert.FromBase64String(text));
    public string Save()
    {
        var lines = new List<string> { "CCF custom reward progress 1" };
        foreach (var pair in campaignCounts) lines.Add(string.Join("\t", new[] { "T", Encode(pair.Key), pair.Value.Events.ToString(CultureInfo.InvariantCulture), pair.Value.Activations.ToString(CultureInfo.InvariantCulture) }));
        foreach (var owner in spentCharges) foreach (var reward in owner.Value)
            lines.Add(string.Join("\t", new[] { "U", Encode(owner.Key), Encode(reward.Key), reward.Value.ToString(CultureInfo.InvariantCulture) }));
        foreach (var owner in previousMatches) foreach (var reward in owner.Value)
            lines.Add(string.Join("\t", new[] { "P", Encode(owner.Key), Encode(reward.Key), reward.Value.ToString(CultureInfo.InvariantCulture) }));
        foreach (var owner in acquisitions) foreach (var reward in owner.Value)
            lines.Add(string.Join("\t", new[] { "A", Encode(owner.Key), Encode(reward.Key), reward.Value.ToString(CultureInfo.InvariantCulture) }));
        foreach (var e in Effects.Where(e => e.Duration == "Rest of run"))
            lines.Add(string.Join("\t", new[] { "E", Encode(e.Reward), Encode(e.Owner), Encode(e.Recipient), Encode(e.Stat), e.Amount.ToString(CultureInfo.InvariantCulture), e.Stacks.ToString(CultureInfo.InvariantCulture) }));
        return string.Join("\n", lines);
    }
    public void Load(string text)
    {
        var next = new CustomRewardProgress();
        var lines = text.Replace("\r", "").Split('\n');
        if (lines[0] != "CCF custom reward progress 1") throw new ArgumentException("Unsupported custom reward progress.");
        foreach (var line in lines.Skip(1).Where(l => l.Length > 0)) {
            var p = line.Split('\t');
            if (p.Length == 4 && p[0] == "T" && int.TryParse(p[2], out int events) && events >= 0 && events <= 999 && int.TryParse(p[3], out int activations) && activations >= 0 && activations <= 999) {
                string key = Decode(p[1]); if (next.campaignCounts.ContainsKey(key)) throw new ArgumentException("Duplicate campaign event count.");
                next.campaignCounts.Add(key, new Counter { Events = events, Activations = activations }); continue;
            }
            if (p.Length == 4 && p[0] == "U" && int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int spent) && spent >= 0 && spent <= 999999) {
                string owner = Decode(p[1]), reward = Decode(p[2]);
                if (!next.spentCharges.TryGetValue(owner, out var rewards)) next.spentCharges[owner] = rewards = new(StringComparer.OrdinalIgnoreCase);
                if (rewards.ContainsKey(reward)) throw new ArgumentException("Duplicate spent charge count.");
                rewards.Add(reward, spent); continue;
            }
            if (p.Length == 4 && p[0] == "P" && int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int previous) && previous >= 0 && previous <= 999999) {
                string owner = Decode(p[1]), reward = Decode(p[2]);
                if (next.previousMatches.TryGetValue(owner, out var rewards) && rewards.ContainsKey(reward)) throw new ArgumentException("Duplicate previous-match count.");
                next.SetPreviousMatchCount(owner, reward, previous);
                continue;
            }
            // Old preview countdown rows are safely ignored; curses no longer exist.
            if (p.Length == 4 && p[0] == "A" && long.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out long sequence) && sequence != 0 && sequence >= int.MinValue && sequence < long.MaxValue) {
                string owner = Decode(p[1]), reward = Decode(p[2]);
                if (!next.acquisitions.TryGetValue(owner, out var owned)) next.acquisitions[owner] = owned = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                if (owned.ContainsKey(reward)) throw new ArgumentException("Duplicate custom reward acquisition.");
                owned[reward] = sequence; next.acquisitionSequence = Math.Max(next.acquisitionSequence, sequence);
            }
            else if (p.Length == 3 && p[0] == "C" && int.TryParse(p[2], out int count) && count >= 0 && count <= 99) { Decode(p[1]); }
            else if (p.Length == 7 && p[0] == "E" && int.TryParse(p[5], out int amount) && Math.Abs((long)amount) <= 999
                && int.TryParse(p[6], out int stacks) && stacks >= 1 && stacks <= 999 && CustomRewardDefinition.Stats.Contains(Decode(p[4])))
                next.Effects.Add(new Effect { Reward = Decode(p[1]), Owner = Decode(p[2]), Recipient = Decode(p[3]), Stat = Decode(p[4]), Amount = amount, Stacks = stacks, Duration = "Rest of run" });
            else throw new ArgumentException("Invalid custom reward progress.");
        }
        Clear(); Effects.AddRange(next.Effects);
        foreach (var pair in next.campaignCounts) campaignCounts.Add(pair.Key, pair.Value);
        foreach (var pair in next.acquisitions) acquisitions.Add(pair.Key, pair.Value);
        foreach (var pair in next.previousMatches) previousMatches.Add(pair.Key, pair.Value);
        foreach (var pair in next.spentCharges) spentCharges.Add(pair.Key, pair.Value);
        acquisitionSequence = next.acquisitionSequence;
    }
}
