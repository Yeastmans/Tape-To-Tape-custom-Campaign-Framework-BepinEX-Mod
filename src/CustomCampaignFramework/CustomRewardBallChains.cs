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

internal static class CustomRewardBallChains
{
    private sealed class Chain { internal Forward Player; internal BallAndChain Payload; internal BallAndChainController Controller; }
    private static readonly Dictionary<IntPtr, Chain> chains = new();
    internal static void Refresh(Forward player)
    {
        if (player == null) return;
        bool active = !player.HaveTalentCached<BallAndChain>() && CustomRewardRuntime.ActiveConditions(player).Any(d => d.Effect == CustomRewardEffects.PhysicalBallChain);
        if (!active) { Remove(player.Pointer); return; }
        if (chains.ContainsKey(player.Pointer)) return;
        var original = Resources.FindObjectsOfTypeAll<BallAndChain>().FirstOrDefault(t => t.name != null && !t.name.StartsWith("CCF_Chain_", StringComparison.Ordinal));
        if (original?.ballPrefab == null || original.chainPrefab == null) return;
        var payload = Object.Instantiate(original); payload.name = "CCF_Chain_" + player._forwardData.id;
        var chain = new Chain { Player = player, Payload = payload, Controller = new BallAndChainController() };
        chains.Add(player.Pointer, chain);
        // This controller owns one physical attachment. Its subscriptions only
        // maintain that attachment through faceoffs, puck drop and match reset;
        // none grant another reward or add an unrelated stat payoff.
        try { chain.Controller.Initialize(player, payload); }
        catch { Remove(player.Pointer); throw; }
    }
    internal static void Tick()
    {
        if (!CustomRewardRuntime.InMatch) { Clear(); return; }
        foreach (var pair in chains.ToArray()) {
            if (pair.Value.Player == null) { Remove(pair.Key); continue; }
            pair.Value.Controller.OnPlayingForwardsChanged();
            pair.Value.Controller.Update();
        }
    }
    private static void Remove(IntPtr key)
    {
        if (!chains.TryGetValue(key, out var chain)) return;
        chains.Remove(key);
        try { chain.Controller.Destroy(); }
        finally { if (chain.Payload != null) Object.Destroy(chain.Payload); }
    }
    internal static void Clear() { foreach (var key in chains.Keys.ToArray()) Remove(key); }
}
