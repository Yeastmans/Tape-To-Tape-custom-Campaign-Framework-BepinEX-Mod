using System;
using System.Collections.Generic;
using System.Linq;
using CampaignCore;
using Rogue;
using Rogue.Powerups.Controllers.Talents;
using Skaters;
using Tape2Tape;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EndlessMode;

internal static class CustomRewardLooseAttraction
{
    private sealed class Magnet { internal LightningRod Payload; internal LightningRodController Controller; }
    private static readonly Dictionary<IntPtr, Magnet> magnets = new();
    internal static void Tick(Forward player)
    {
        if (player == null) return;
        var rule = CustomRewardRuntime.InMatch && player.ArePowerUpsEnabled() && !player.IsBenched() && !player.HaveTalentCached<LightningRod>()
            ? CustomRewardRuntime.ActiveConditions(player).Where(d => d.Effect == CustomRewardEffects.LoosePuckAttraction).OrderByDescending(d => d.Radius).FirstOrDefault() : null;
        if (rule == null) { Remove(player.Pointer); return; }
        if (magnets.TryGetValue(player.Pointer, out var current) && Math.Abs(current.Payload.MaxAttractionDistance - rule.Radius) > .001f) Remove(player.Pointer);
        if (!magnets.TryGetValue(player.Pointer, out var magnet)) {
            var payload = ScriptableObject.CreateInstance<LightningRod>();
            payload.name = "CCF_LooseMagnet_" + rule.Id; payload.id = rule.Id;
            payload.MaxAttractionDistance = rule.Radius; payload.AttractionDuration = 100000000;
            payload.RangeForce = new Vector2(15, 40);
            magnet = new Magnet { Payload = payload, Controller = new LightningRodController() };
            magnets.Add(player.Pointer, magnet);
            try {
                magnet.Controller.Initialize(player, payload);
                // A condition can become active during an existing loose-puck
                // state, after its native state-change event has fired.
                magnet.Controller.OnChangeGameState(GameManager.GameState, GameManager.GameState);
            } catch { Remove(player.Pointer); throw; }
        }
        magnet.Controller.FixedUpdate();
    }
    private static void Remove(IntPtr key)
    {
        if (!magnets.Remove(key, out var magnet)) return;
        try { magnet.Controller.Destroy(); }
        finally { if (magnet.Payload != null) Object.Destroy(magnet.Payload); }
    }
    internal static void Clear() { foreach (var key in magnets.Keys.ToArray()) Remove(key); }
}
