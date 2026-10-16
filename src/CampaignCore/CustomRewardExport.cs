using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CampaignCore;

// Companion files and exported library snapshots carry the complete rule and
// embedded PNG. They do not depend on the author's campaign or library path.
public static class CustomRewardExport
{
    public const string Prefix = "Export Custom Reward ";
    public static void AddSnapshot(IDictionary<string, CustomRewardDefinition> items, CustomRewardDefinition item)
    {
        if (items.TryGetValue(item.Id, out var previous) && previous.ToDocument().ToString() != item.ToDocument().ToString())
            throw new ArgumentException("Conflicting embedded definitions for custom reward " + item.Name + " (" + item.Id + "). Re-export the affected players or team from the same definition.");
        items[item.Id] = item;
    }
    public static void ValidatePlayNow(CustomRewardDefinition item)
    {
        if (item.NeedsRedesign) throw new ArgumentException(item.Name + " is excluded until redesigned.");
        if (item.Effect == CustomRewardEffects.AngusZero || item.Effect == CustomRewardEffects.FreezeOwnRelics || item.Effect == CustomRewardEffects.FragileBody || item.Effect == CustomRewardEffects.SpikedInjury || item.Effect == CustomRewardEffects.IgnoreInjuryPenalties || item.Effect == CustomRewardEffects.InjureArm || new[] { "While eye injured", "While shoulder injured", "While leg injured", "While arm injured" }.Contains(item.Trigger) || item.Trigger == "While injured" || item.Trigger == "While team has an injured skater" || item.Trigger == "Injured goal value calculation" || CustomRewardDefinition.IsPreviousMatchBasis(item.AmountBasis) || item.AmountBasis == "Per liquidated talent" || item.AmountBasis == "Per injured playing teammate" || CustomRewardEffects.IsCampaignAction(item.Effect) || CustomRewardTriggers.IsCompletion(item.Trigger)
            || CustomRewardTriggers.IsQuery(item.Trigger) && item.Trigger != "Native blue-line crossing" && item.Trigger != "Cultist incantation cycle" && item.Trigger != "Defensive possession teleport" && item.Trigger != "Knockout duration calculation" && item.Trigger != "Opposing breakaway shot" && item.Trigger != "Opening team shots" && item.Trigger != "Recent team shot" && item.Trigger != "Centre defender loans" && item.Trigger != "Controlled possession display" && item.Trigger != "Random fart timer" && item.Trigger != "Puck fart timer" && item.Trigger != "Goal-post collision" && item.Trigger != "Fart interaction" && item.Trigger != "Opposing possession timer" && item.Trigger != "Breakaway display" && item.Trigger != "Without puck timer" && item.Trigger != "Puck possession timer" && item.Trigger != "Bodycheck movement" && item.Trigger != "Faceoff setup" && item.Trigger != "Opponent goal reversal window" && item.Trigger != "Shared puck value" && item.Trigger != "Match winner calculation" && item.Trigger != "Final whistle" && item.Trigger != "Attribute bonus conversion" && item.Trigger != "Match setup" && item.Trigger != "Goal verification" && item.Trigger != "Enraged duration calculation" && item.Trigger != "Helmet equipment" && item.Trigger != "Helmet effect calculation" && !CustomRewardTriggers.IsGoalValueCalculation(item.Trigger) && !CustomRewardTriggers.IsShotCalculation(item.Trigger)
            || !CustomRewardTriggers.IsCondition(item.Trigger) && !CustomRewardTriggers.IsQuery(item.Trigger) && item.Effect == CustomRewardEffects.StatChange && item.Duration == "Rest of run"
            || item.Effect == CustomRewardEffects.Injure || item.Effect == CustomRewardEffects.Heal)
            throw new ArgumentException("Cannot export " + item.Name + " to Play Now: its rule requires campaign progress (" + item.Trigger + " / " + item.Effect + "). Choose a match-compatible rule or remove it from this export.");
    }
    public static void Embed(CampaignDocument document, IEnumerable<CustomRewardDefinition> items)
    {
        // Replace the snapshot as a unit. Re-exporting an exported team must not
        // retain old identities that are no longer referenced by its loadout.
        var snapshot = items.ToArray();
        foreach (var item in snapshot) item.Validate();
        foreach (var key in document.Fields.Keys.Where(k => k.StartsWith(Prefix, StringComparison.Ordinal)).ToArray()) document.Remove(key);
        foreach (var item in snapshot) {
            document.Set(Prefix + item.Id, Convert.ToBase64String(Encoding.UTF8.GetBytes(item.ToDocument().ToString())));
        }
    }
    public static IEnumerable<CustomRewardDefinition> Read(CampaignDocument document)
    {
        foreach (var pair in document.Fields.Where(p => p.Key.StartsWith(Prefix, StringComparison.Ordinal))) {
            if (pair.Value.Length > 7600000) throw new ArgumentException("Exported custom reward is too large.");
            var item = CustomRewardDefinition.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(pair.Value)));
            if (!pair.Key.Substring(Prefix.Length).Equals(item.Id, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Exported custom reward identity does not match its key.");
            yield return item;
        }
    }
}
