using System;
using CampaignCore;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using State;

namespace EndlessMode;

internal static class SquadPresetData
{
    internal static void Apply(RunSquadScriptableObject squad, TeamConfig cfg)
    {
        bool goalOnly = SquadBehaviors.Enabled(cfg.SquadTemplate, "goal_only_mode", key => cfg.SquadBehaviors.TryGetValue(key, out var value) ? value : "");
        // Keep the current template order while inherited; explicit overrides
        // must not leave a hidden game-mode restriction on the clone.
        if (goalOnly) squad.m_SupportedGameModes = new Il2CppStructArray<GameRule>(new[] { GameRule.Maxgoal });
        else if (squad.m_SupportedGameModes == null || squad.m_SupportedGameModes.Length < 2)
            squad.m_SupportedGameModes = new Il2CppStructArray<GameRule>(new[] { GameRule.Maxgoal, GameRule.Timer });
    }
}
