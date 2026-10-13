using System;
using System.Collections.Generic;
using System.Linq;

namespace CampaignCore;

public sealed class RelicOpponentInteraction
{
    public string EffectKey { get; }
    public string SettingKey { get; }
    public string Label { get; }
    public string Help { get; }
    public string Classifier { get; }
    public string Consumer { get; }
    public RelicOpponentInteraction(string effectKey, string settingKey, string label, string help, string classifier, string consumer)
    {
        EffectKey = effectKey; SettingKey = settingKey; Label = label; Help = help;
        Classifier = classifier; Consumer = consumer;
    }
}

// These rules belong to a relic's native controller, not the team's global identity.
// Auto and On allow the same controller branch; neither grants or activates a relic.
public static class RelicOpponentInteractions
{
    public static readonly string[] Modes = { "Auto", "On", "Off" };
    public static readonly RelicOpponentInteraction[] All = {
        new RelicOpponentInteraction("referee-hands", "Referee Hands Interaction", "Referee Hands immunity",
            "Auto: when Referee Hands is equipped and active, prevent it from removing this opponent team's sticks. On: explicitly enable the same immunity. Off: let the relic remove sticks normally. Off disables only this special immunity, not the relic itself. This setting does not grant a relic.",
            "IsRefereeTeam", "Rogue.Relics.Controllers.RefHandsRelicController.AddEffect")
    };

    public static bool IsInteraction(string effectKey) => All.Any(i => i.EffectKey.Equals(effectKey, StringComparison.OrdinalIgnoreCase));

    public static string Resolve(string value, IEnumerable<string> legacyEffects, string effectKey)
    {
        string mode = Modes.FirstOrDefault(m => m.Equals((value ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
        if (mode != null) return mode;
        return (legacyEffects ?? Array.Empty<string>()).Any(k => k.Equals(effectKey, StringComparison.OrdinalIgnoreCase)) ? "On" : "Auto";
    }

    public static bool Allows(string effectKey, string mode, string consumer, string classifier) =>
        All.Any(i => i.EffectKey.Equals(effectKey, StringComparison.OrdinalIgnoreCase)
            && i.Consumer == consumer && i.Classifier == classifier)
        && (string.Equals(mode, "Auto", StringComparison.OrdinalIgnoreCase) || string.Equals(mode, "On", StringComparison.OrdinalIgnoreCase));
}
