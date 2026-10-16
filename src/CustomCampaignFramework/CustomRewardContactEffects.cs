using System;
using System.Collections.Generic;
using System.Linq;
using CampaignCore;
using Rogue;
using Rogue.Powerups.Controllers.Talents;
using Skaters;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EndlessMode;

internal static class CustomRewardContactEffects
{
    private static readonly Dictionary<string, BoardBumperController> boards = new();
    [ThreadStatic] private static Forward contactPlayer;
    [ThreadStatic] private static Vector3 contactNormal;
    internal static void Contact(Forward player, Vector3 normal, Action dispatch)
    {
        var previous = contactPlayer; var previousNormal = contactNormal;
        contactPlayer = player; contactNormal = normal;
        try { dispatch(); } finally { contactPlayer = previous; contactNormal = previousNormal; }
    }
    internal static bool Bounce(CustomRewardDefinition rule, Skater actor)
    {
        if (contactPlayer == null || actor?.Pointer != contactPlayer.Pointer) return false;
        string key = rule.Id + "|" + actor.Pointer;
        if (!boards.TryGetValue(key, out var controller)) {
            var original = Resources.FindObjectsOfTypeAll<BoardBumper>().FirstOrDefault(t => t.level == 1 && t.name != null && !t.name.StartsWith("CCF_Board_", StringComparison.Ordinal));
            if (original == null) return false;
            var payload = Object.Instantiate(original); payload.name = "CCF_Board_" + rule.Id;
            payload.bonusSpeed *= rule.Amount / 100f; payload.minSpeed *= rule.Amount / 100f;
            // Do not Initialize or AddEffect: those subscribe the original
            // trigger. Only this selected contact invokes the native primitive.
            controller = new BoardBumperController { _forward = contactPlayer, skater = contactPlayer,
                _boardBumper = payload, Talent = CustomRewardRuntime.FindTalent(rule.Id, false), _lastBoardHitTime = float.NegativeInfinity };
            boards.Add(key, controller);
        }
        float before = controller._lastBoardHitTime;
        controller.OnSkaterHitBoard(contactPlayer, contactNormal);
        return controller._lastBoardHitTime != before;
    }
    internal static void Clear()
    {
        foreach (var controller in boards.Values) if (controller._boardBumper != null) Object.Destroy(controller._boardBumper);
        boards.Clear(); contactPlayer = null;
    }
}
