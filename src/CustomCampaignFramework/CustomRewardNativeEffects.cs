using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CampaignCore;
using Events;
using HarmonyLib;
using Rogue;
using Rogue.Powerups.Components;
using Skaters;
using State;
using Tape2Tape;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EndlessMode;

// Native components execute the selected effect, but never subscribe to their
// original triggers. The custom rule owns activation and the action lifetime.
internal static class CustomRewardNativeEffects
{
    internal static bool DeflectLob(Skater actor)
    {
        var forward = actor?.TryCast<Forward>();
        if (forward == null || Puck.Instance == null || Puck.Instance.IsPossessed() || forward.SkaterAbility == null
            || !Puck.CanStickyPickup(forward)) return false;
        // Native level two will perform its own lob later in this same handler.
        if (forward.GetTalent<DefensiveDeflect>()?.IsMegaLob == true) return false;
        forward.RetrievePuck();
        if (!forward.HasPuck()) return false;
        forward.SkaterAbility.MegaLob();
        return true;
    }
    [ThreadStatic] internal static bool CreatingFlight;
    private static ShootingArg flight;
    private static bool ghost;
    private static CustomRewardDefinition impact;
    private static readonly List<MonoBehaviour> components = new();
    private static readonly List<PowerUp> payloads = new();
    private static readonly Dictionary<string, SmoothBoostComponent> boosts = new();
    private static readonly Dictionary<string, FartObject> waves = new();
    private static readonly Dictionary<string, Rogue.Powerups.VFX.VfxLoader> sonicVfx = new();
    internal static bool IsGhostShot => ghost && flight != null && GameManager.GameState == GameState.DuringShot
        && GameManager.CurrentShotInfo?.Pointer == flight.Pointer && Puck.Instance != null && !Puck.Instance.IsPossessed();
    internal static CustomRewardDefinition ImpactRule(Skater player) => player != null && impact != null && flight?.shootingSkater?.Pointer == player.Pointer
        && GameManager.GameState == GameState.DuringShot && GameManager.CurrentShotInfo?.Pointer == flight.Pointer
        && Puck.Instance != null && !Puck.Instance.IsPossessed() ? impact : null;
    internal static void BeginShot(ShootingArg shot) { ClearFlight(); flight = shot; }
    internal static void ClearFlight()
    {
        ghost = false; impact = null;
        foreach (var component in components) if (component != null) { component.enabled = false; component.StopAllCoroutines(); Object.Destroy(component); }
        components.Clear();
        foreach (var payload in payloads) {
            if (flight?.powerupsInShot != null) flight.powerupsInShot.Remove(payload);
            if (payload != null) Object.Destroy(payload);
        }
        payloads.Clear(); flight = null;
    }
    internal static void Contact()
    {
        // These native effects stop at the first physical collision. Spin follows
        // the native shot state instead. Ghost shots never collide with skaters.
        foreach (var component in components.ToArray()) {
            if (component == null) continue;
            if (component.TryCast<CurveBallComponent>() is { } curve) curve.isActivated = false;
            if (component.TryCast<SlowBallComponent>() is { } slow) slow.isActivated = false;
        }
    }
    internal static void Tick()
    {
        if (flight != null && (GameManager.GameState != GameState.DuringShot || GameManager.CurrentShotInfo?.Pointer != flight.Pointer || Puck.Instance == null || Puck.Instance.IsPossessed())) ClearFlight();
    }
    internal static void Clear()
    {
        ClearFlight();
        foreach (var boost in boosts.Values) if (boost != null) { boost.StopManually(); Object.Destroy(boost); }
        boosts.Clear();
        foreach (var wave in waves.Values) if (wave != null) Object.Destroy(wave);
        waves.Clear();
        foreach (var vfx in sonicVfx.Values) vfx.Dispose();
        sonicVfx.Clear();
    }
    private static T Component<T>(Forward actor) where T : MonoBehaviour
    {
        CreatingFlight = true;
        try { var component = actor.gameObject.AddComponent<T>(); components.Add(component); return component; }
        finally { CreatingFlight = false; }
    }
    private static void Payload<T>(CustomRewardDefinition d) where T : PowerUp
    {
        // This is shot metadata consumed by the native flight component. It is
        // never inserted into a player's talents, repositories, or saved run.
        var payload = ScriptableObject.CreateInstance<T>(); payload.id = d.Id; payload.name = "CCF_Shot_" + d.Id;
        flight.powerupsInShot ??= new Il2CppSystem.Collections.Generic.List<PowerUp>();
        flight.powerupsInShot.Add(payload); payloads.Add(payload);
    }
    internal static bool Flight(CustomRewardDefinition d, Skater actor)
    {
        if (flight == null || actor?.TryCast<Forward>() is not { } forward || flight.shootingSkater != forward || Puck.Instance == null || Puck.Instance.IsPossessed()) return false;
        switch (d.Effect) {
            case CustomRewardEffects.ImpactKnockout:
            case CustomRewardEffects.ImpactExplosion:
                // One native contact outcome. The upgraded explosion supersedes
                // the base hit, just as the native talent's upgraded level does.
                if (impact?.Effect == CustomRewardEffects.ImpactExplosion && d.Effect != CustomRewardEffects.ImpactExplosion) return false;
                impact = d; return true;
            case CustomRewardEffects.PuckGoalieGhost:
                var goalie = forward.Team?.otherTeam?.goaltender;
                if (goalie == null) return false;
                Puck.Instance.StartCoroutine(Puck.Instance.DisableGoalieCollision(goalie, .5f)); return true;
            case CustomRewardEffects.PuckGhost: ghost = true; return true;
            case CustomRewardEffects.PuckCurve:
                if (flight.headShot) return false; // Native curve excludes targeted headshots.
                Payload<CurveBall>(d);
                var curve = Component<CurveBallComponent>(forward); curve.Initialize(forward, d.Amount / 100f); curve.OnShot(flight);
                return curve.isActivated;
            case CustomRewardEffects.PuckSlow:
                Payload<SlowBall>(d);
                var slow = Component<SlowBallComponent>(forward); slow._forward = forward; slow.OnShot(flight); slow.baseForce *= d.Amount / 100f;
                return slow.isActivated;
            case CustomRewardEffects.PuckSpin:
                var spin = Component<TrickShotComponent>(forward); spin.Initialize(forward, d.Amount / 100f);
                spin.OnChangedState(GameState.PuckForGrabs, GameState.DuringShot); return spin._isActivated;
        }
        return false;
    }
    internal static bool Boost(CustomRewardDefinition d, Skater source, Forward target)
    {
        if (target == null || !target.IsVulnerable()) return false;
        string key = d.Id + "|" + source.Pointer + "|" + target.Pointer;
        if (!boosts.TryGetValue(key, out var boost) || boost == null) {
            boost = target.gameObject.AddComponent<SmoothBoostComponent>();
            boost.TriggerType = BoostTrigger.Manual; boosts[key] = boost;
        }
        boost.Initialize(target, d.Amount, d.Seconds, 0);
        if (d.Effect == CustomRewardEffects.ShotRecoil) {
            if (flight?.shootingSkater?.Pointer != source.Pointer || target.Pointer != source.Pointer) return false;
            var backwards = -flight.shotDirection; backwards.y = 0;
            if (backwards.sqrMagnitude <= .001f) return false;
            boost.StartBoost(new Il2CppSystem.Nullable<Vector3>(backwards));
        } else {
            // Interop nullable values are reference wrappers; null cannot be unboxed.
            boost.StartBoost(new Il2CppSystem.Nullable<Vector3>());
        }
        return boost.IsBoosting;
    }
    internal static bool Sonic(CustomRewardDefinition d, Skater actor)
    {
        if (actor?.SkaterAbility == null || actor._rigidbody == null || d.Target == CustomRewardEffects.Puck && Puck.Rigidbody == null) return false;
        var point = d.Target == CustomRewardEffects.Puck ? Puck.Rigidbody.position : actor._rigidbody.position;
        actor.SkaterAbility.SonicSlap(d.Radius, point, d.Target == CustomRewardEffects.Puck ? SonicType.Interception : SonicType.Pass);
        string key = d.Id + "|" + actor.Pointer;
        if (!sonicVfx.TryGetValue(key, out var vfx)) sonicVfx[key] = vfx = new Rogue.Powerups.VFX.VfxLoader();
        vfx.Play(point, d.Radius * 2, "SonicSlapVFX", null);
        return true;
    }
    internal static bool Shockwave(CustomRewardDefinition d, Skater actor)
    {
        if (actor == null || actor._rigidbody == null) return false;
        string key = d.Id + "|" + actor.Pointer;
        if (!waves.TryGetValue(key, out var wave) || wave == null) {
            var template = Resources.FindObjectsOfTypeAll<Windup>().FirstOrDefault();
            if (template == null) return false;
            wave = actor.gameObject.AddComponent<FartObject>(); wave.magnetoPlayer = actor;
            wave.Initialize(new FartConfiguration {
                ShouldPushForwards = d.Effect == CustomRewardEffects.PushSkaters,
                ShouldPushPucks = d.Effect == CustomRewardEffects.PushPuck,
                ShouldPushSticks = d.Effect == CustomRewardEffects.PushSticks,
                ShouldPropulse = false, ForceFactor = d.Amount / 100f,
                MaxRangeFactor = d.Radius / (d.Effect == CustomRewardEffects.PushSkaters ? 4.5f : 7f)
            }, template.fartParticle, actor);
            waves[key] = wave;
        }
        if (wave.IsActivated()) return false;
        wave.Activate(1, 1, 1); return true;
    }
}

[HarmonyPatch]
internal static class CustomRewardFlightNoNativeTriggers
{
    private static IEnumerable<MethodBase> TargetMethods() {
        foreach (var type in new[] { typeof(CurveBallComponent), typeof(SlowBallComponent), typeof(TrickShotComponent) })
            yield return AccessTools.Method(type, "Awake");
    }
    [HarmonyPrefix] private static bool Prefix() => !CustomRewardNativeEffects.CreatingFlight;
}
[HarmonyPatch(typeof(Puck), "IsDuringXRayShot")]
internal static class CustomRewardGhostShot
{
    [HarmonyPostfix] private static void Postfix(ref bool __result) { if (!__result) __result = CustomRewardNativeEffects.IsGhostShot; }
}

[HarmonyPatch(typeof(Puck), "IsDuringDestroyerShot")]
internal static class CustomRewardImpactShot
{
    [HarmonyPostfix] private static void Postfix(ref bool __result) {
        if (!__result) __result = CustomRewardNativeEffects.ImpactRule(GameManager.CurrentShotInfo?.shootingSkater) != null;
    }
}
