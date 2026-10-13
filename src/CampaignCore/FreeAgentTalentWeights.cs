using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CampaignCore;

/// <summary>Campaign-wide multipliers for newly rolled free-agent talents only.</summary>
public sealed class FreeAgentTalentWeights
{
    public const string Key = "Free Agent Talent Multipliers";
    public const string Help = "1 keeps the native weight; 2 doubles that weight, not the final probability. 0 prevents direct rolls of that level; uncheck it to also block upgrades into it. Existing and template talents, eligibility, extra-talent chances and stats stay unchanged. Multipliers do not change upgrade chances.";
    private readonly Dictionary<string, float> values;
    private FreeAgentTalentWeights(Dictionary<string, float> values) => this.values = values;
    public IReadOnlyDictionary<string, float> Values => new System.Collections.ObjectModel.ReadOnlyDictionary<string, float>(values);
    public bool IsDefault => values.Values.All(v => v == 1f);
    public float Multiplier(string id) => values.TryGetValue(id ?? "", out float value) ? value : 1f;

    public static bool TryNumber(string text, out float value, out string error)
    {
        value = 1f; error = "";
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
            || double.IsNaN(number) || double.IsInfinity(number) || number < 0 || number > float.MaxValue)
        { error = "Use a finite multiplier of 0 or greater."; return false; }
        float narrowed = (float)number;
        string mantissa = (text ?? "").Trim().Split('e', 'E')[0];
        if ((number > 0 && narrowed == 0) || (number == 0 && mantissa.Any(c => c >= '1' && c <= '9')))
        { error = "That positive multiplier is too small. Only explicit 0 disables a talent."; return false; }
        value = narrowed == 0 ? 0f : narrowed;
        return true;
    }

    public static bool TryParse(string text, out FreeAgentTalentWeights result, out string error)
    {
        var values = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        result = new FreeAgentTalentWeights(values); error = "";
        if (string.IsNullOrWhiteSpace(text)) return true;
        foreach (string entry in text.Split(','))
        {
            string[] parts = entry.Split(':');
            string id = parts[0].Trim();
            if (parts.Length != 2 || !(Guid.TryParseExact(id, "D", out _) || Guid.TryParseExact(id, "N", out _)))
            { error = "Each free-agent talent weight needs a talent GUID followed by : and a multiplier."; return false; }
            if (!TryNumber(parts[1].Trim(), out float value, out error))
            { error = id + ": " + error; return false; }
            if (values.ContainsKey(id))
            { error = "Free-agent talent weight appears twice: " + id; return false; }
            values.Add(id, value); // Preserve authored GUIDs, including unknown future catalog IDs.
        }
        return true;
    }

    public string Serialize() => string.Join(", ", values.Select(p => p.Key + ":" + p.Value.ToString("R", CultureInfo.InvariantCulture)));

    /// <summary>Call only for a nondefault policy, after native pool/role/exclusion filtering.</summary>
    public List<T> Select<T>(IEnumerable<T> candidates, int requested, Func<T, string> id,
        Func<T, float> nativeWeight, Func<double> nextUnit)
    {
        var picked = new List<T>();
        if (requested <= 0) return picked;
        var remaining = new List<(T Value, double Weight)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (T candidate in candidates)
        {
            string key = id(candidate);
            float multiplier = Multiplier(key);
            if (string.IsNullOrEmpty(key) || multiplier == 0 || !seen.Add(key)) continue;
            float native = nativeWeight(candidate);
            if (native <= 0 || float.IsNaN(native) || float.IsInfinity(native)) continue;
            // Positive finite float products fit in double, including subnormals.
            remaining.Add((candidate, (double)native * multiplier));
        }
        int count = Math.Min(requested, remaining.Count);
        for (int n = 0; n < count; n++)
        {
            // Recompute after removal: subtraction can erase tiny remaining weights.
            double total = 0, correction = 0;
            foreach (var entry in remaining)
            {
                double adjusted = entry.Weight - correction, sum = total + adjusted;
                correction = (sum - total) - adjusted; total = sum;
            }
            if (total <= 0 || double.IsInfinity(total) || double.IsNaN(total))
                throw new InvalidOperationException("Invalid free-agent talent weight total.");
            double unit = nextUnit();
            if (unit < 0 || unit > 1 || double.IsNaN(unit))
                throw new InvalidOperationException("Free-agent talent random value is outside [0,1].");
            double target = unit * total, cumulative = 0;
            int chosen = remaining.Count - 1; // Unity's inclusive upper endpoint chooses the last candidate.
            for (int i = 0; i < remaining.Count; i++)
            {
                cumulative += remaining[i].Weight;
                if (target < cumulative) { chosen = i; break; }
            }
            picked.Add(remaining[chosen].Value);
            remaining.RemoveAt(chosen);
        }
        return picked;
    }
}
